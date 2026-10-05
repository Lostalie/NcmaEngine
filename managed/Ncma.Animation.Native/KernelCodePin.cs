using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace Ncma.Animation.Native;
// Pin ancestors against renaming/substitution; this is integrity checking for trusted code, not a sandbox.
internal sealed class KernelCodePin : IDisposable
{
    private readonly List<SafeFileHandle> _parents=[];
    private FileStream? _file;
    internal Stream Stream => _file ?? throw new ObjectDisposedException(nameof(KernelCodePin));
    internal KernelCodePin(string path)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Animation code pin currently requires Windows.");
        try{
            var directories=new List<string>();for(var directory=new System.IO.FileInfo(path).Directory;directory is not null;directory=directory.Parent)directories.Add(directory.FullName);
            foreach(string directory in directories.AsEnumerable().Reverse()){
                var handle=CreateFileW(directory,0,3,0,3,0x02200000,0);
                if(handle.IsInvalid){handle.Dispose();throw new IOException("Cannot pin animation code parent.",new Win32Exception(Marshal.GetLastWin32Error()));}
                _parents.Add(handle);if(!GetFileInformationByHandle(handle,out var info)||(info.Attributes&0x410)!=0x10)throw new IOException("Unexpected animation parent/reparse point.");
            }
            // Open the file itself, NOT a reparse target: a pre-open Attributes check is not a pin.
            var fileHandle=CreateFileW(path,0x80000000,1,0,3,0x00200000,0);
            if(fileHandle.IsInvalid){fileHandle.Dispose();throw new IOException("Cannot pin animation code file.",new Win32Exception(Marshal.GetLastWin32Error()));}
            try{
                if(!GetFileInformationByHandle(fileHandle,out var file)||(file.Attributes&0x410)!=0||file.Links!=1)throw new IOException("Unexpected animation file/reparse point/hard link.");
                _file=new(fileHandle,FileAccess.Read);
            }catch{fileHandle.Dispose();throw;}
        }catch{Dispose();throw;}
    }
    public void Dispose(){_file?.Dispose();_file=null;for(int i=_parents.Count-1;i>=0;i--)_parents[i].Dispose();_parents.Clear();}
    [StructLayout(LayoutKind.Sequential)] private struct FileInfo{public uint Attributes,CreationLow,CreationHigh,AccessLow,AccessHigh,WriteLow,WriteHigh,Volume,SizeHigh,SizeLow,Links,IndexHigh,IndexLow;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern SafeFileHandle CreateFileW(string path,uint access,uint share,nint security,uint disposition,uint flags,nint template);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetFileInformationByHandle(SafeFileHandle handle,out FileInfo info);
}
