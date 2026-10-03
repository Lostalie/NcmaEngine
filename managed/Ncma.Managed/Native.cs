using System.Runtime.InteropServices;

namespace Ncma;

internal static partial class Native
{
    private const string Library = "NcmaNative";

    [LibraryImport(Library, EntryPoint = "ncma_get_abi_version")]
    internal static partial uint GetAbiVersion();

    [LibraryImport(Library, EntryPoint = "ncma_get_game_object_api_version")]
    internal static partial uint GetGameObjectApiVersion();

    [LibraryImport(Library, EntryPoint = "ncma_get_world_access_api_version")]
    internal static partial uint GetWorldAccessApiVersion();

    [LibraryImport(Library, EntryPoint = "ncma_world_get_object_reference")]
    internal static partial byte WorldGetObjectReference(nint world, ulong gameObject, out ObjectReference output);
    [LibraryImport(Library, EntryPoint = "ncma_world_get_object_uuid")]
    internal static partial byte WorldGetObjectUuid(nint world, ObjectReference gameObject, out ulong high, out ulong low);
    [LibraryImport(Library, EntryPoint = "ncma_world_find_object")]
    internal static partial byte WorldFindObject(nint world, ulong high, ulong low, out ObjectReference output);
    [LibraryImport(Library, EntryPoint = "ncma_world_read_transforms")]
    internal static unsafe partial byte WorldReadTransforms(nint world, ObjectReference* objects, Transform* output, uint count);
    [LibraryImport(Library, EntryPoint = "ncma_world_write_transforms")]
    internal static unsafe partial byte WorldWriteTransforms(nint world, TransformWrite* writes, uint count);
    [LibraryImport(Library, EntryPoint = "ncma_world_send_signal")]
    internal static partial byte WorldSendSignal(nint world, in GameplaySignal signal);
    [LibraryImport(Library, EntryPoint = "ncma_world_receive_signals")]
    internal static unsafe partial byte WorldReceiveSignals(nint world, ObjectReference target, GameplaySignal* output, uint capacity, out uint count);

    [LibraryImport(Library, EntryPoint = "ncma_world_create", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint WorldCreate(string name);

    [LibraryImport(Library, EntryPoint = "ncma_world_destroy")]
    internal static partial void WorldDestroy(nint world);

    [LibraryImport(Library, EntryPoint = "ncma_world_create_object_with_language", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial ulong WorldCreateObject(nint world, string name, uint language);

    [LibraryImport(Library, EntryPoint = "ncma_world_destroy_object")]
    internal static partial byte WorldDestroyObject(nint world, ulong gameObject, byte recursive);

    [LibraryImport(Library, EntryPoint = "ncma_world_get_local_transform")]
    internal static partial byte WorldGetLocalTransform(nint world, ulong gameObject, out Transform output);

    [LibraryImport(Library, EntryPoint = "ncma_world_set_local_transform")]
    internal static partial byte WorldSetLocalTransform(nint world, ulong gameObject, in Transform value);

    [LibraryImport(Library, EntryPoint = "ncma_get_last_error")]
    private static partial nint GetLastErrorPointer();

    internal static string GetLastError() => Marshal.PtrToStringUTF8(GetLastErrorPointer()) ?? "Native engine error";
}
