using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Ncma.Assets.Runtime;

// Shared read-only file boundary for runtime assets and shader packages. No authoring or renderer.
internal sealed class RuntimeReadPin : IDisposable
{
    private readonly List<SafeFileHandle> _directories = [];
    private SafeFileHandle? _file;
    internal static string Root(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Runtime asset file pinning currently requires Windows.");
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        for (DirectoryInfo? d = new(root); d is not null; d = d.Parent)
            if ((File.GetAttributes(d.FullName) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Asset root traverses a reparse point.");
        return root;
    }
    internal RuntimeReadPin(string root, string relative, bool derived = false, bool directory = false, bool deploymentManifest = false)
    {
        if (deploymentManifest) {
            if (directory || derived || relative != "deployment-manifest.json") throw new ArgumentException("Exact runtime deployment manifest required.");
        } else if (!(directory && relative == "assets")) AssetPaths.Validate(relative, derived ? "out/assets" : "assets");
        string full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            for (DirectoryInfo? d = new(directory ? full : Path.GetDirectoryName(full)!); d is not null; d = d.Parent)
                _directories.Add(Open(d.FullName, true));
            // Locked parent identities prevent rename/junction substitution during canonical path verification.
            string current = root;
            foreach (string part in relative.Split('/'))
            {
                var entries = Directory.EnumerateFileSystemEntries(current).Take(16385).ToArray();
                if (entries.Length > 16384) throw new ArgumentException("Asset directory entry budget.");
                var matches = entries.Where(e => string.Equals(Path.GetFileName(e), part, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
                if (matches.Length != 1) throw new FileNotFoundException("Missing or ambiguous runtime asset path.", full);
                if (Path.GetFileName(matches[0]) != part) throw new ArgumentException("Noncanonical runtime asset casing.");
                current = matches[0];
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Runtime assets reject reparse points.");
            }
            if (!directory) _file = Open(full, false);
        }
        catch { Dispose(); throw; }
    }
    internal byte[] Read(int maximum, CancellationToken cancellation)
    {
        ObjectDisposedException.ThrowIf(_file is null, this);
        long length = RandomAccess.GetLength(_file);
        if (length is <= 0 || length > maximum) throw new ArgumentException("Runtime asset byte budget.");
        byte[] bytes = new byte[(int)length]; int at = 0;
        while (at < bytes.Length)
        {
            cancellation.ThrowIfCancellationRequested();
            int n = RandomAccess.Read(_file, bytes.AsSpan(at, Math.Min(65536, bytes.Length - at)), at);
            if (n == 0) throw new EndOfStreamException(); at += n;
        }
        return bytes;
    }
    private static SafeFileHandle Open(string path, bool directory)
    {
        string full = Path.GetFullPath(path);
        if (full.StartsWith(@"\\.\", StringComparison.Ordinal)) throw new ArgumentException("Runtime asset device path.");
        string native = full.StartsWith(@"\\?\", StringComparison.Ordinal) ? full : full.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + full[2..] : @"\\?\" + full;
        var handle = CreateFileW(native, directory ? 0u : 0x80000000u, directory ? 3u : 1u, IntPtr.Zero, 3,
            0x00200000u | (directory ? 0x02000000u : 0), IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error(); handle.Dispose();
            if (error is 2 or 3) { if (directory) throw new DirectoryNotFoundException(path); throw new FileNotFoundException("Runtime asset missing.", path); }
            throw new IOException("Runtime asset read lease failed.", new Win32Exception(error));
        }
        if (!GetFileInformationByHandle(handle, out var info) || (info.Attributes & 0x400) != 0 ||
            ((info.Attributes & 0x10) != 0) != directory || !directory && info.Links != 1)
        { handle.Dispose(); throw new IOException("Runtime asset lease rejects links or unexpected file types."); }
        return handle;
    }
    public void Dispose()
    {
        _file?.Dispose(); _file = null;
        for (int i = _directories.Count - 1; i >= 0; --i) _directories[i].Dispose(); _directories.Clear();
    }
    [StructLayout(LayoutKind.Sequential)] private struct FileInfo
    { public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh, Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInfo info);
}
