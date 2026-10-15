using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Ncma.Platform;

// Local owner-thread UI, independent of the renderer/ImGui device. No command execution,
// automatic approval, external window enumeration, or callback from an Agent endpoint.
public sealed class WindowsConfirmation
{
    private readonly int _owner=Environment.CurrentManagedThreadId;
    private bool _showing;
    [DllImport("user32.dll",CharSet=CharSet.Unicode,ExactSpelling=true,SetLastError=true)]
    private static extern int MessageBoxW(nint owner,string text,string caption,uint type);
    [DllImport("user32.dll")] private static extern nint GetActiveWindow();
    public bool Confirm(string text,string caption)
    {
        if(Environment.CurrentManagedThreadId!=_owner||_showing)throw new InvalidOperationException("Confirmation requires a nonreentrant owner thread.");
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Windows local confirmation required.");
        if(string.IsNullOrWhiteSpace(text)||text.Length>4096||text.Contains('\0')||string.IsNullOrWhiteSpace(caption)||caption.Length>256||caption.Contains('\0'))
            throw new ArgumentException("Bounded confirmation text and caption required.");
        _showing=true;
        try {
            // YESNO | ICONWARNING | DEFBUTTON2 (No) | TASKMODAL. No foreground override.
            int result=MessageBoxW(GetActiveWindow(),text,caption,0x4|0x30|0x100|0x2000);
            return result switch {6=>true,7=>false,0=>throw new Win32Exception(Marshal.GetLastWin32Error()),_=>throw new InvalidOperationException("Unexpected confirmation result.")};
        }finally{_showing=false;}
    }
}
