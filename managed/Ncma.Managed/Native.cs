using System.Runtime.InteropServices;

namespace Ncma;

internal static partial class Native
{
    private const string Library = "NcmaNative";

    [LibraryImport(Library, EntryPoint = "ncma_get_abi_version")]
    internal static partial uint GetAbiVersion();

    [LibraryImport(Library, EntryPoint = "ncma_world_create", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint WorldCreate(string name);

    [LibraryImport(Library, EntryPoint = "ncma_world_destroy")]
    internal static partial void WorldDestroy(nint world);

    [LibraryImport(Library, EntryPoint = "ncma_world_create_node", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial ulong WorldCreateNode(nint world, string name, ulong parent);

    [LibraryImport(Library, EntryPoint = "ncma_world_destroy_node")]
    internal static partial byte WorldDestroyNode(nint world, ulong node, byte recursive);

    [LibraryImport(Library, EntryPoint = "ncma_world_set_parent")]
    internal static partial byte WorldSetParent(nint world, ulong node, ulong parent);

    [LibraryImport(Library, EntryPoint = "ncma_world_get_local_transform")]
    internal static partial byte WorldGetLocalTransform(nint world, ulong node, out Transform output);

    [LibraryImport(Library, EntryPoint = "ncma_world_set_local_transform")]
    internal static partial byte WorldSetLocalTransform(nint world, ulong node, in Transform value);

    [LibraryImport(Library, EntryPoint = "ncma_get_last_error")]
    private static partial nint GetLastErrorPointer();

    internal static string GetLastError() => Marshal.PtrToStringUTF8(GetLastErrorPointer()) ?? "Native engine error";
}
