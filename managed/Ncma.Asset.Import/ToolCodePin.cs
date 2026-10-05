using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Ncma.Asset.Import;

// Private Windows trusted-tool code boundary: pin ancestors against path substitution while loading.
internal sealed class ToolCodePin : IDisposable
{
    private readonly List<SafeFileHandle> _parents = [];
    private readonly FileStream _file;
    public Stream Stream => _file;
    public ToolCodePin(string path)
    {
        ImportKernel.ValidateAbsolute(path);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Tool code pins currently require Windows.");
        try
        {
            var directories = new List<string>();
            for (DirectoryInfo? directory = new System.IO.FileInfo(path).Directory; directory is not null; directory = directory.Parent) directories.Add(directory.FullName);
            foreach (string directory in directories.AsEnumerable().Reverse())
            {
                var handle = CreateFileW(directory, 0, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
                if (handle.IsInvalid) { handle.Dispose(); throw new IOException("Cannot pin code parent.", new Win32Exception(Marshal.GetLastWin32Error())); }
                _parents.Add(handle);
                if (!GetFileInformationByHandle(handle, out var info) || (info.Attributes & 0x410) != 0x10) throw new IOException("Unexpected code parent/reparse point.");
            }
            _file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!GetFileInformationByHandle(_file.SafeFileHandle, out var fileInfo) || (fileInfo.Attributes & 0x410) != 0 || fileInfo.Links != 1)
            { _file.Dispose(); throw new IOException("Unexpected tool file/reparse point/hard link."); }
            ImportKernel.ValidateAbsolute(path);
        }
        catch { foreach (var parent in _parents) parent.Dispose(); throw; }
    }
    public void Dispose() { _file.Dispose(); foreach (var parent in _parents.AsEnumerable().Reverse()) parent.Dispose(); }
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInfo { public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh, Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInfo info);
}
