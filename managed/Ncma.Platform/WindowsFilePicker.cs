using System.Runtime.InteropServices;
using System.Text;
namespace Ncma.Platform;

public enum LocalFileKind { OpenScene, SaveScene, OpenFbx, OpenUi, SaveUi, OpenAnimationGraph, SaveAnimationGraph }

// Platform-only trusted local dialog adapter. It selects paths; never loads or mutates documents.
public sealed class WindowsFilePicker
{
    private readonly int _owner=Environment.CurrentManagedThreadId;
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    private struct Dialog {
        public uint Size; public nint Owner,Instance; public string Filter; public nint CustomFilter;
        public uint CustomMax,FilterIndex; public nint File; public uint FileMax; public nint FileTitle;
        public uint TitleMax; public nint InitialDirectory,Title; public uint Flags; public ushort FileOffset,ExtensionOffset;
        public string DefaultExtension; public nint Data,Hook,Template,Reserved; public uint ReservedCount,ExtendedFlags;
    }
    [DllImport("comdlg32.dll",CharSet=CharSet.Unicode,ExactSpelling=true,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOpenFileNameW(ref Dialog dialog);
    [DllImport("comdlg32.dll",CharSet=CharSet.Unicode,ExactSpelling=true,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSaveFileNameW(ref Dialog dialog);
    [DllImport("comdlg32.dll")] private static extern uint CommDlgExtendedError();
    [DllImport("user32.dll")] private static extern nint GetActiveWindow();
    public string? Choose(LocalFileKind kind) {
        if(Environment.CurrentManagedThreadId!=_owner)throw new InvalidOperationException("File picker owner thread required.");
        if(!OperatingSystem.IsWindows() || !Enum.IsDefined(kind))throw new NotSupportedException("Windows local file picker required.");
        nint buffer=Marshal.AllocHGlobal(32768*2);
        try {
            Marshal.Copy(new byte[32768*2],0,buffer,32768*2);
            bool fbx=kind==LocalFileKind.OpenFbx,graph=kind is LocalFileKind.OpenAnimationGraph or LocalFileKind.SaveAnimationGraph,ui=kind is LocalFileKind.OpenUi or LocalFileKind.SaveUi,save=kind is LocalFileKind.SaveScene or LocalFileKind.SaveUi or LocalFileKind.SaveAnimationGraph;
            var dialog=new Dialog {Size=(uint)Marshal.SizeOf<Dialog>(),Owner=GetActiveWindow(),Filter=graph?"Ncma animation graphs (*.ncmaanim)\0*.ncmaanim\0\0":fbx?"FBX characters (*.fbx)\0*.fbx\0\0":ui?"Ncma UI (*.ncmaui)\0*.ncmaui\0\0":"Ncma scene (*.ncmascene)\0*.ncmascene\0\0",
                File=buffer,FileMax=32768,FilterIndex=1,DefaultExtension=graph?"ncmaanim":fbx?"fbx":ui?"ncmaui":"ncmascene",Flags=0x80000|0x800|0x8|(save?0x2u:0x1000u)};
            bool selected=save?GetSaveFileNameW(ref dialog):GetOpenFileNameW(ref dialog);
            if(!selected) {uint error=CommDlgExtendedError();if(error!=0)throw new IOException($"File dialog failed: {error}.");return null;}
            string path=Marshal.PtrToStringUni(buffer)!;
            if(!Path.IsPathFullyQualified(path) || Encoding.UTF8.GetByteCount(path)>1023)throw new ArgumentException("Selected path exceeds the editor control budget.");
            return Path.GetFullPath(path);
        } finally {Marshal.FreeHGlobal(buffer);}
    }
}
