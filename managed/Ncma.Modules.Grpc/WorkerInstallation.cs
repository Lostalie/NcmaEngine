using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Ncma.Modules.Grpc;

public sealed record WorkerInstalledFile(string Path, long Bytes, string Sha256);

/// <summary>
/// Trusted local installation, never an RPC-supplied module path. Scan is explicit off-frame IO.
/// This is a content approval boundary, NOT an OS sandbox or a claim that approved code is safe.
/// Windows CPython 3.12 direct interpreter layout; no venv redirector or global site packages.
/// </summary>
public sealed class WorkerInstallation
{
    public const int MaximumFiles = 4096;
    public const long MaximumBytes = 256L * 1024 * 1024;
    public const string PythonPathFile = "python312.zip\nDLLs\nsite-packages\n";
    private readonly WorkerInstalledFile[] files;
    public string Root { get; }
    public string Fingerprint { get; }
    public int FileCount => files.Length;
    public long Bytes => files.Sum(f => f.Bytes);
    public WorkerInstalledFile[] CopyFiles() => (WorkerInstalledFile[])files.Clone();

    private WorkerInstallation(string root, WorkerInstalledFile[] files)
    {
        Root = root; this.files = files;
        Fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { version = 1, root, files })));
    }

    public static WorkerInstallation Inspect(string root, CancellationToken cancellation = default)
    {
        using var pin = WorkerInstallationPin.Open(root, cancellation);
        return new(pin.Root, pin.Files);
    }

    internal WorkerInstallationPin Acquire(CancellationToken cancellation)
    {
        var pin = WorkerInstallationPin.Open(Root, cancellation);
        try
        {
            if (!files.SequenceEqual(pin.Files)) throw new InvalidDataException("Approved worker installation changed.");
            return pin;
        }
        catch { pin.Dispose(); throw; }
    }
}

internal sealed class WorkerInstallationPin : IDisposable
{
    private readonly List<SafeFileHandle> handles = [];
    private readonly HashSet<string> heldDirectories = new(StringComparer.OrdinalIgnoreCase);
    internal string Root { get; }
    internal WorkerInstalledFile[] Files { get; private set; } = [];
    private WorkerInstallationPin(string root) => Root = root;

    internal static WorkerInstallationPin Open(string root, CancellationToken cancellation)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Worker installation currently supports Windows only.");
        cancellation.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(root) || root.Length > 1024 || !Path.IsPathFullyQualified(root) || root.StartsWith("\\", StringComparison.Ordinal))
            throw new ArgumentException("An absolute local worker installation is required.");
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (full.Length <= 3 || full[1] != ':' || full[2] != '\\' || full[3..].Contains(':'))
            throw new ArgumentException("Worker installation must be a local directory, not a drive/device/stream.");
        var result = new WorkerInstallationPin(full);
        try
        {
            // Pin ancestors top down without DELETE sharing before walking the installation.
            var ancestors = new Stack<string>();
            for (var current = new DirectoryInfo(full); current is not null; current = current.Parent) ancestors.Push(current.FullName);
            while (ancestors.TryPop(out var directory)) result.Hold(directory, directory: true);
            var entries = new List<WorkerInstalledFile>();
            var directories = new Queue<string>(); directories.Enqueue(full);
            var directoryCount = 0; long total = 0;
            while (directories.TryDequeue(out var directory))
            {
                cancellation.ThrowIfCancellationRequested();
                // Enumerate incrementally: sorting an untrusted directory first would materialize
                // arbitrarily many entries before either budget could be checked.
                foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                {
                    cancellation.ThrowIfCancellationRequested();
                    var name = Path.GetFileName(path);
                    if (name.EndsWith('.') || name.EndsWith(' ') || name.Contains(':')) throw new InvalidDataException("Ambiguous worker path.");
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Worker reparse points are not allowed.");
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        if (++directoryCount > WorkerInstallation.MaximumFiles) throw new InvalidDataException("Worker directory budget.");
                        result.Hold(path, directory: true); directories.Enqueue(path);
                        continue;
                    }
                    if (entries.Count == WorkerInstallation.MaximumFiles) throw new InvalidDataException("Worker file budget.");
                    var handle = result.Hold(path, directory: false);
                    var length = RandomAccess.GetLength(handle);
                    total = checked(total + length);
                    if (length < 0 || total > WorkerInstallation.MaximumBytes) throw new InvalidDataException("Worker installation byte budget.");
                    using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var buffer = new byte[65536]; long position = 0;
                    while (position < length)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        var count = RandomAccess.Read(handle, buffer.AsSpan(0, (int)Math.Min(buffer.Length, length - position)), position);
                        if (count == 0) throw new EndOfStreamException();
                        sha.AppendData(buffer, 0, count); position += count;
                    }
                    entries.Add(new(Path.GetRelativePath(full, path).Replace('\\', '/'), length, Convert.ToHexString(sha.GetHashAndReset())));
                }
            }
            result.Files = entries.OrderBy(f => f.Path, StringComparer.Ordinal).ToArray();
            foreach (var required in new[] { "python.exe", "python312.dll", "python312.zip", "python312._pth",
                "ncma_module_worker.py", "site-packages/grpc/__init__.py", "site-packages/typing_extensions.py" })
                if (!result.Files.Any(f => f.Path == required)) throw new InvalidDataException("Incomplete worker installation.");
            if (result.Files.Any(f => f.Path.Equals("pyvenv.cfg", StringComparison.OrdinalIgnoreCase)) ||
                result.Files.Single(f => f.Path == "python312._pth").Bytes != Encoding.UTF8.GetByteCount(WorkerInstallation.PythonPathFile) ||
                File.ReadAllText(Path.Combine(full, "python312._pth"), new UTF8Encoding(false, true)) != WorkerInstallation.PythonPathFile)
                throw new InvalidDataException("Worker must use the exact isolated Python search path.");
            result.VerifyNames();
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    internal void VerifyNames()
    {
        // Files/ancestors already held; detect additions too. No symlink traversal on this walk.
        var names = new List<string>(); var pending = new Queue<string>(); pending.Enqueue(Root); var directories = 0;
        while (pending.TryDequeue(out var directory))
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Worker installation reparse point appeared.");
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (!heldDirectories.Contains(path)) throw new InvalidDataException("Worker directory set changed.");
                    if (++directories > WorkerInstallation.MaximumFiles) throw new InvalidDataException("Worker directory budget.");
                    pending.Enqueue(path);
                }
                else
                {
                    if (names.Count == WorkerInstallation.MaximumFiles) throw new InvalidDataException("Worker file budget.");
                    names.Add(Path.GetRelativePath(Root, path).Replace('\\', '/'));
                }
            }
        if (!names.Order(StringComparer.Ordinal).SequenceEqual(Files.Select(f => f.Path)))
            throw new InvalidDataException("Worker installation file set changed.");
    }

    private SafeFileHandle Hold(string path, bool directory)
    {
        var handle = CreateFileW("\\\\?\\" + path, directory ? 0u : 0x80000000u, directory ? 3u : 1u,
            0, 3, 0x00200000u | (directory ? 0x02000000u : 0), 0);
        try
        {
            if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info))
                throw new IOException("Cannot pin worker installation.", new Win32Exception(Marshal.GetLastWin32Error()));
            if ((info.Attributes & 0x400) != 0 || ((info.Attributes & 0x10) != 0) != directory || (!directory && info.Links != 1))
                throw new InvalidDataException("Worker installation rejects links and unexpected file types.");
            handles.Add(handle); if (directory) heldDirectories.Add(path); return handle;
        }
        catch { handle.Dispose(); throw; }
    }

    public void Dispose() { for (var i = handles.Count - 1; i >= 0; i--) handles[i].Dispose(); handles.Clear(); }
    [StructLayout(LayoutKind.Sequential)]
    private struct Info
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh,
            Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out Info info);
}
