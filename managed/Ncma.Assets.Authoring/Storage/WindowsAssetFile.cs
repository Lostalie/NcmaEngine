using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace Ncma.Assets.Authoring.Storage;

// Private Windows storage boundary. No Win32 handles escape authoring into assets, World or gameplay.
internal sealed class WindowsAssetFile : IDisposable
{
    private readonly SafeFileHandle _handle;
    private WindowsAssetFile(SafeFileHandle handle, bool directory)
    {
        _handle = handle;
        if (!GetFileInformationByHandle(handle, out var info)) { handle.Dispose(); Fail(); }
        if ((info.Attributes & 0x400) != 0 || ((info.Attributes & 0x10) != 0) != directory || (!directory && info.Links != 1))
        { handle.Dispose(); throw new IOException("Asset storage rejects reparse points, hard links and unexpected file types."); }
        Identity = $"{info.Volume:X8}:{info.IndexHigh:X8}{info.IndexLow:X8}";
    }
    public string Identity { get; }
    public long Length => RandomAccess.GetLength(_handle);
    public static WindowsAssetFile Open(string path, bool directory = false, bool create = false)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Authoring file transactions currently require Windows.");
        var handle = CreateFileW(NativePath(path), directory ? 0 : 0x80010000u | (create ? 0x40000000u : 0),
            directory ? 3u : 1u, IntPtr.Zero, create ? 1u : 3u, 0x00200000u | (directory ? 0x02000000u : 0), IntPtr.Zero);
        if (handle.IsInvalid) { handle.Dispose(); Fail(); }
        return new(handle, directory);
    }
    // Read-only import lease: forbids writes/deletes, but unlike transaction handles does not request
    // DELETE access, so native read-only fopen in the child can coexist without sharing violations.
    public static WindowsAssetFile OpenReadLease(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var handle = CreateFileW(NativePath(path), 0x80000000u, 1u, IntPtr.Zero, 3u, 0x00200000u, IntPtr.Zero);
        if (handle.IsInvalid) { handle.Dispose(); Fail(); }
        return new(handle, directory: false);
    }
    public byte[] Read(int maximum)
    {
        if (Length > maximum) throw new ArgumentException("Asset file exceeds byte budget.");
        byte[] bytes = new byte[checked((int)Length)]; int offset = 0;
        while (offset < bytes.Length)
        {
            int count = RandomAccess.Read(_handle, bytes.AsSpan(offset), offset);
            if (count == 0) throw new EndOfStreamException(); offset += count;
        }
        return bytes;
    }
    public string Hash(int maximum, CancellationToken cancellation = default)
    {
        if (Length > maximum) throw new ArgumentException("Asset source exceeds byte budget.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[64 * 1024]; long offset = 0;
        while (offset < Length)
        {
            cancellation.ThrowIfCancellationRequested();
            int count = RandomAccess.Read(_handle, buffer.AsSpan(0, (int)Math.Min(buffer.Length, Length - offset)), offset);
            if (count == 0) throw new EndOfStreamException(); hash.AppendData(buffer, 0, count); offset += count;
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    public void Write(ReadOnlySpan<byte> bytes) { RandomAccess.Write(_handle, bytes, 0); Flush(); }
    public void CopyTo(WindowsAssetFile target, int maximum)
    {
        if (Length > maximum) throw new ArgumentException("Asset source exceeds byte budget.");
        byte[] buffer = new byte[64 * 1024]; long offset = 0;
        while (offset < Length)
        {
            int count = RandomAccess.Read(_handle, buffer.AsSpan(0, (int)Math.Min(buffer.Length, Length - offset)), offset);
            if (count == 0) throw new EndOfStreamException(); RandomAccess.Write(target._handle, buffer.AsSpan(0, count), offset); offset += count;
        }
        target.Flush();
    }
    public void Rename(string destination)
    {
        // FILE_RENAME_INFO: DWORD union, aligned HANDLE, DWORD byte length, WCHAR[]; never replace an occupant.
        int rootOffset = IntPtr.Size == 8 ? 8 : 4, lengthOffset = rootOffset + IntPtr.Size, nameOffset = lengthOffset + 4;
        byte[] name = System.Text.Encoding.Unicode.GetBytes(NativePath(destination));
        byte[] buffer = new byte[nameOffset + name.Length + 2];
        BitConverter.GetBytes(name.Length).CopyTo(buffer, lengthOffset); name.CopyTo(buffer, nameOffset);
        SetInfo(3, buffer);
    }
    public void Delete() => SetInfo(4, [1]); // Exact owned file handle, not a pathname or recursive delete.
    private void SetInfo(int kind, byte[] bytes)
    {
        IntPtr buffer = Marshal.AllocHGlobal(bytes.Length);
        try { Marshal.Copy(bytes, 0, buffer, bytes.Length); if (!SetFileInformationByHandle(_handle, kind, buffer, (uint)bytes.Length)) Fail(); }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    private void Flush() { if (!FlushFileBuffers(_handle)) Fail(); }
    public void Dispose() => _handle.Dispose();
    // These are already validated private storage paths. Explicit extended-length addressing keeps
    // CreateFile/rename independent of whether the calling dotnet/apphost manifest opts into MAX_PATH.
    // Do not shorten paths, change UUID/hash identities, or weaken parent/reparse validation.
    private static string NativePath(string path)
    {
        string full = Path.GetFullPath(path);
        if (full.StartsWith(@"\\.\", StringComparison.Ordinal)) throw new ArgumentException("Device paths are not asset files.");
        if (full.StartsWith(@"\\?\", StringComparison.Ordinal)) return full;
        return full.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + full[2..] : @"\\?\" + full;
    }
    private static void Fail() => throw new IOException("Asset file operation failed.", new Win32Exception(Marshal.GetLastWin32Error()));
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInfo
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh,
            Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInfo info);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int kind, IntPtr info, uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlushFileBuffers(SafeFileHandle handle);
}

internal sealed class AssetDirectoryLease : IDisposable
{
    private readonly List<WindowsAssetFile> _handles = [];
    public AssetDirectoryLease(AssetProjectPaths paths, IEnumerable<string> relativePaths)
    {
        try
        {
            var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string relative in relativePaths)
            {
                string canonical = relative.StartsWith("out/asset-authoring/", StringComparison.Ordinal)
                    ? AssetPaths.Validate(relative, "out/asset-authoring") : relative.StartsWith("out/assets/", StringComparison.Ordinal)
                    ? AssetPaths.Validate(relative, "out/assets") : AssetPaths.Validate(relative);
                string full = Path.Combine(paths.Root, canonical.Replace('/', Path.DirectorySeparatorChar));
                for (DirectoryInfo? directory = new(Path.GetDirectoryName(full)!); directory is not null; directory = directory.Parent)
                    directories.Add(directory.FullName);
            }
            foreach (string directory in directories.OrderBy(p => p.Length)) _handles.Add(WindowsAssetFile.Open(directory, directory: true));
            foreach (string relative in relativePaths)
                if (relative.StartsWith("out/asset-authoring/", StringComparison.Ordinal)) paths.ResolveCache(relative);
                else if (relative.StartsWith("out/assets/", StringComparison.Ordinal)) paths.ResolveDerived(relative); else paths.Resolve(relative);
        }
        catch { Dispose(); throw; }
    }
    public void Dispose() { for (int i = _handles.Count - 1; i >= 0; i--) _handles[i].Dispose(); _handles.Clear(); }
}
