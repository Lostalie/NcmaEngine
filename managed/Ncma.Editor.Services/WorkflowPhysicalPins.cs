using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Ncma.Editor.Services;

// Tool/script boundary only. Captured source and stamps remain original C1's.
internal sealed class WorkflowPhysicalPins : IDisposable
{
    private readonly Dictionary<string, SafeFileHandle> directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<SafeFileHandle> files = [];
    private readonly Dictionary<string, (SafeFileHandle Handle, long Bytes, string Sha256)> observed = new(StringComparer.OrdinalIgnoreCase);
    private readonly int maximumFiles, maximumDirectories;
    private bool closed;
    internal WorkflowPhysicalPins(int maximumFiles = 32, int maximumDirectories = 128)
    {
        if (maximumFiles is < 1 or > 2048 || maximumDirectories is < 1 or > 4096) throw new IOException("workflow_pin_budget");
        this.maximumFiles = maximumFiles; this.maximumDirectories = maximumDirectories;
    }
    internal static string Absolute(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("workflow_windows_required");
        if (string.IsNullOrWhiteSpace(path) || path.Length < 3 || path.Length > 1024 || path.Any(char.IsControl) || path.Contains('"') || path.Contains('/') ||
            !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal) || path[1] != ':' || path[2] != Path.DirectorySeparatorChar || path[3..].Contains(':'))
            throw new IOException("workflow_local_path_required");
        foreach (string part in path[3..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            if (part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ')) throw new IOException("workflow_path_alias");
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (GetDriveTypeW(Path.GetPathRoot(full)!) != 3) throw new IOException("workflow_fixed_drive_required");
        return full;
    }
    internal void Directory(string path)
    {
        ObjectDisposedException.ThrowIf(closed, this); path = Absolute(path);
        if (directories.ContainsKey(path)) return;
        if (directories.Count >= maximumDirectories) throw new IOException("workflow_directory_budget");
        string? parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent)) Directory(parent);
        if (directories.Count >= maximumDirectories) throw new IOException("workflow_directory_budget");
        directories.Add(path, Open(path, true));
    }
    internal void File(string path, string expectedHash, long? expectedSize = null)
        => _ = PinFile(path, expectedHash, expectedSize);
    internal byte[] ReadFile(string path, string expectedHash, long expectedSize, int maximumBytes)
    {
        if (maximumBytes is < 1 or > 4194304 || expectedSize < 0 || expectedSize > maximumBytes) throw new IOException("workflow_input_read_budget");
        var handle = PinFile(path, expectedHash, expectedSize);
        byte[] result = new byte[(int)expectedSize]; int offset = 0;
        while (offset < result.Length) {
            int count = RandomAccess.Read(handle, result.AsSpan(offset), offset);
            if (count == 0) throw new IOException("workflow_input_short_read");
            offset += count;
        }
        return result; // bytes from the SAME retained, read-only pinned handle
    }
    private SafeFileHandle PinFile(string path, string expectedHash, long? expectedSize)
    {
        ObjectDisposedException.ThrowIf(closed, this); WorkflowEvidenceRules.Hash(expectedHash); path = Absolute(path);
        if (files.Count >= maximumFiles) throw new IOException("workflow_tool_file_budget");
        Directory(Path.GetDirectoryName(path)!); var handle = Open(path, false);
        try {
            long length = RandomAccess.GetLength(handle);
            if (length > 64 * 1024 * 1024 || expectedSize is { } size && length != size) throw new IOException("workflow_tool_size");
            if (Hash(handle, length) != expectedHash) throw new IOException("workflow_tool_bytes_changed");
            files.Add(handle); return handle;
        } catch { handle.Dispose(); throw; }
    }
    // Read-only observation of an unpinned file: the returned hash is measured from
    // the SAME retained handle. Not a process, SDK-selection or freshness proof.
    internal (long Bytes, string Sha256) Observe(string path, long maximumBytes, bool allowEmpty = false)
    {
        ObjectDisposedException.ThrowIf(closed, this); path = Absolute(path);
        if (maximumBytes is < 1 or > 64 * 1024 * 1024) throw new IOException("workflow_tool_size");
        if (files.Count >= maximumFiles) throw new IOException("workflow_tool_file_budget");
        Directory(Path.GetDirectoryName(path)!); var handle = Open(path, false);
        try {
            long length = RandomAccess.GetLength(handle);
            if (length < (allowEmpty ? 0 : 1) || length > maximumBytes) throw new IOException("workflow_tool_size");
            string sha256 = Hash(handle, length); files.Add(handle);
            observed.TryAdd(path, (handle, length, sha256)); return (length, sha256);
        } catch { handle.Dispose(); throw; }
    }
    // Recheck previously observed bytes on the SAME retained handles, without consuming new pin slots.
    // Directory membership is the caller's responsibility; a retained directory permits new children.
    internal void VerifyObserved()
    {
        ObjectDisposedException.ThrowIf(closed, this);
        foreach (var file in observed.Values) {
            ValidateHandle(file.Handle, false);
            if (RandomAccess.GetLength(file.Handle) != file.Bytes || Hash(file.Handle, file.Bytes) != file.Sha256)
                throw new IOException("workflow_observed_bytes_changed");
        }
    }
    internal void FileWithSha512(string path, string sha256, long bytes, string sha512)
    {
        Span<byte> expected = stackalloc byte[64];
        if (!Convert.TryFromBase64String(sha512, expected, out int count) || count != 64) throw new IOException("workflow_package_sha512");
        var handle = PinFile(path, sha256, bytes);
        // Same retained handle, no second path open; failed comparison remains owned.
        string actual = Hash(handle, bytes, HashAlgorithmName.SHA512);
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actual), expected)) throw new IOException("workflow_package_archive_changed");
    }
    // Copies ONLY a previously observed retained handle, never reopens the path.
    internal void CopyObserved(string path, string sha256, long bytes, Stream destination, byte[] buffer)
    {
        ObjectDisposedException.ThrowIf(closed, this); path = Absolute(path);
        if (buffer.Length != 65536 || !observed.TryGetValue(path, out var source) || source.Bytes != bytes || source.Sha256 != sha256)
            throw new IOException("workflow_copy_unobserved_input");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); long offset = 0;
        while (offset < bytes) {
            int count = RandomAccess.Read(source.Handle, buffer.AsSpan(0, (int)Math.Min(buffer.Length, bytes - offset)), offset);
            if (count == 0) throw new IOException("workflow_input_short_read");
            hash.AppendData(buffer, 0, count); destination.Write(buffer, 0, count); offset += count;
        }
        if (RandomAccess.GetLength(source.Handle) != bytes || Convert.ToHexString(hash.GetHashAndReset()) != sha256)
            throw new IOException("workflow_copy_bytes_changed");
    }
    internal void CopyDeclaredFile(string source, string destination, string sha256, long bytes)
    {
        var handle = PinFile(source, sha256, bytes);
        destination = Absolute(destination); Directory(Path.GetDirectoryName(destination)!);
        using (var file = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) {
            ValidateHandle(file.SafeFileHandle, false); byte[] buffer = new byte[65536]; long offset = 0;
            while (offset < bytes) {
                int count = RandomAccess.Read(handle, buffer.AsSpan(0, (int)Math.Min(buffer.Length, bytes - offset)), offset);
                if (count == 0) throw new IOException("workflow_input_short_read");
                file.Write(buffer, 0, count); offset += count;
            }
            file.Flush(true);
        }
        File(destination, sha256, bytes); // exact copied bytes retained through execution
    }
    private static string Hash(SafeFileHandle handle, long length, HashAlgorithmName? algorithm = null)
    {
        using var hash = IncrementalHash.CreateHash(algorithm ?? HashAlgorithmName.SHA256);
        byte[] buffer = new byte[65536]; long offset = 0;
        while (offset < length) {
            int count = RandomAccess.Read(handle, buffer.AsSpan(0, (int)Math.Min(buffer.Length, length - offset)), offset);
            if (count == 0) throw new IOException("workflow_tool_short_read");
            hash.AppendData(buffer, 0, count); offset += count;
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    internal string CreateScratch(string workspace, Guid id)
    {
        Directory(workspace); string path = workspace;
        foreach (string part in new[] { "out", "verification", "m8-6", "preparers" }) {
            path = Path.Combine(path, part);
            if (!CreateDirectoryW(@"\\?\" + path, 0) && Marshal.GetLastWin32Error() != 183) throw new IOException("workflow_scratch_parent");
            Directory(path);
        }
        path = Path.Combine(path, id.ToString("N"));
        if (!CreateDirectoryW(@"\\?\" + path, 0)) throw new IOException("workflow_scratch_collision");
        Directory(path); return path; // evidence only; never auto-delete
    }
    internal string CreateJobOutput(string workspace, Guid id, bool offlineRestore = false)
    {
        if (id == Guid.Empty) throw new IOException("workflow_output_identity");
        workspace = Absolute(workspace); Directory(workspace); string path = workspace;
        foreach (string part in new[] { "out", "verification", "m8-6", offlineRestore ? "restores" : "jobs" }) {
            path = Path.Combine(path, part);
            if (!CreateDirectoryW(@"\\?\" + path, 0) && Marshal.GetLastWin32Error() != 183) throw new IOException("workflow_output_parent");
            Directory(path); // existing parents must still have exact non-reparse identity
        }
        path = Path.Combine(path, id.ToString("N"));
        if (!CreateDirectoryW(@"\\?\" + path, 0)) throw new IOException("workflow_output_collision");
        Directory(path); return path;
    }
    internal string CreateOutputChild(string parent, string name)
    {
        ObjectDisposedException.ThrowIf(closed, this); parent = Absolute(parent);
        if (!directories.ContainsKey(parent) || name is not ("artifacts" or "logs" or "temp" or "cli-home" or "feed" or "packages" or "cases" or "native")) throw new IOException("workflow_output_child");
        string path = Path.Combine(parent, name);
        if (!CreateDirectoryW(@"\\?\" + path, 0)) throw new IOException("workflow_output_child_collision");
        Directory(path); return path;
    }
    internal string CreateStageChild(string parent, string name)
    {
        ObjectDisposedException.ThrowIf(closed, this); parent = Absolute(parent);
        // Same portable path contract as evidence files, restricted to ONE segment.
        _ = WorkflowEvidenceRules.Files([new WorkflowEvidenceFile(name, 0, new string('0', 64))]);
        if (!directories.ContainsKey(parent) || name.Contains('/') || name.Contains('\\')) throw new IOException("workflow_stage_directory");
        string path = Path.Combine(parent, name);
        if (!CreateDirectoryW(@"\\?\" + path, 0)) throw new IOException("workflow_stage_directory_collision");
        Directory(path); return path;
    }
    private static SafeFileHandle Open(string path, bool directory)
    {
        var handle = CreateFileW(@"\\?\" + path, directory ? 0u : 0x80000000u, directory ? 3u : 1u, 0, 3,
            0x00200000u | (directory ? 0x02000000u : 0), 0);
        try {
            ValidateHandle(handle, directory); var final = new StringBuilder(2048);
            uint count = GetFinalPathNameByHandleW(handle, final, (uint)final.Capacity, 0);
            if (count == 0 || count >= final.Capacity || !string.Equals(final.ToString(), @"\\?\" + path, StringComparison.OrdinalIgnoreCase))
                throw new IOException("workflow_path_identity");
            return handle;
        } catch { handle.Dispose(); throw; }
    }
    internal static void ValidateHandle(SafeFileHandle handle, bool? directory = null)
    {
        if (handle.IsInvalid || handle.IsClosed || GetFileType(handle) != 1 || !GetFileInformationByHandle(handle, out var info) ||
            (info.Attributes & 0x400) != 0 || (directory is { } expected && ((info.Attributes & 0x10) != 0) != expected) ||
            ((info.Attributes & 0x10) == 0 && info.Links != 1)) throw new IOException("workflow_file_identity");
    }
    public void Dispose()
    {
        if (closed) return;
        for (int i = files.Count - 1; i >= 0; i--) files[i].Dispose(); files.Clear(); observed.Clear();
        foreach (var handle in directories.Values.Reverse()) handle.Dispose(); directories.Clear(); closed = true;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Info { public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh, Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandle(SafeFileHandle file, out Info info);
    [DllImport("kernel32.dll")] private static extern uint GetFileType(SafeFileHandle file);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, StringBuilder path, uint length, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern uint GetDriveTypeW(string root);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateDirectoryW(string path, nint security);
}
