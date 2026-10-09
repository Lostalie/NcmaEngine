using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
namespace Ncma.Editor.Transport;

// Platform security stays behind this transport module. Reject remote clients at the kernel boundary.
[SupportedOSPlatform("windows")]
internal static class WindowsPipe
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes { public int Length; public nint Descriptor; public int Inherit; }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string text, uint revision, out nint descriptor, out uint length);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafePipeHandle CreateNamedPipeW(string name, uint openMode, uint mode, uint instances,
        uint outputBytes, uint inputBytes, uint timeout, ref SecurityAttributes security);
    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint memory);
    internal static NamedPipeServerStream Create(string name, bool first)
    {
        using var identity = WindowsIdentity.GetCurrent();
        string sid = identity.User?.Value ?? throw new InvalidOperationException("No user SID.");
        // .NET CurrentUserOnly clients compare the pipe OWNER to WindowsIdentity.Owner, not User.
        // Elevated tokens may own objects as Administrators. DACL access remains ONLY the actual User SID.
        string owner = identity.Owner?.Value ?? throw new InvalidOperationException("No token owner SID.");
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW("O:" + owner + "D:P(A;;GA;;;" + sid + ")", 1, out nint descriptor, out _))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var security = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = descriptor };
            var handle = CreateNamedPipeW(@"\\.\pipe\" + name, 3U | 0x40000000U | (first ? 0x80000U : 0),
                0x8, 4, 65536, 65536, 0, ref security);
            if (handle.IsInvalid) { handle.Dispose(); throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
            try { return new NamedPipeServerStream(PipeDirection.InOut, true, false, handle); }
            catch { handle.Dispose(); throw; }
        }
        finally { _ = LocalFree(descriptor); }
    }
}
