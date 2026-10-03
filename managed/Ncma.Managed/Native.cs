using System.Runtime.InteropServices;
namespace Ncma;
internal static partial class Native
{
    [LibraryImport("NcmaNative", EntryPoint = "ncma_get_abi_version")]
    internal static partial uint GetAbiVersion();
    [LibraryImport("NcmaNative", EntryPoint = "ncma_get_last_error")]
    private static partial nint GetLastErrorPointer();
    internal static string GetLastError() => Marshal.PtrToStringUTF8(GetLastErrorPointer()) ?? "Native plugin error";
}
