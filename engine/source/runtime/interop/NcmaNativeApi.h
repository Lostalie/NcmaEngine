#pragma once

#include <cstdint>

#if defined(_WIN32)
    #if defined(NCMA_NATIVE_EXPORTS)
        #define NCMA_NATIVE_API __declspec(dllexport)
    #else
        #define NCMA_NATIVE_API __declspec(dllimport)
    #endif
    #define NCMA_NATIVE_CALL __cdecl
#else
    #define NCMA_NATIVE_API __attribute__((visibility("default")))
    #define NCMA_NATIVE_CALL
#endif

extern "C"
{
    using NcmaWorldHandle = void*;

    struct NcmaVector3
    {
        float X;
        float Y;
        float Z;
    };

    struct NcmaTransform
    {
        NcmaVector3 Position;
        float RotationX;
        float RotationY;
        float RotationZ;
        float RotationW;
        NcmaVector3 Scale;
    };

    struct NcmaObjectReference { std::uint64_t WorldHigh; std::uint64_t WorldLow; std::uint64_t Id; };
    struct NcmaTransformWrite { NcmaObjectReference Object; NcmaTransform Value; };
    struct NcmaGameplaySignal
    {
        NcmaObjectReference Source;
        NcmaObjectReference Target;
        std::uint32_t Code;
        double Value;
        std::uint64_t Sequence;
    };

    // Additive world-access contract v1. Maximum batch/queue size=4096, main thread only.
    // Caller owns buffers. Output stays untouched on validation failure. Signals are
    // committed FIFO and consumed explicitly; no synchronous foreign script invocation.
    NCMA_NATIVE_API std::uint32_t NCMA_NATIVE_CALL ncma_get_world_access_api_version();
    NCMA_NATIVE_API std::uint8_t NCMA_NATIVE_CALL ncma_world_get_object_reference(
        NcmaWorldHandle world, std::uint64_t object, NcmaObjectReference* output);
    NCMA_NATIVE_API std::uint8_t NCMA_NATIVE_CALL ncma_world_get_object_uuid(
        NcmaWorldHandle world, NcmaObjectReference object, std::uint64_t* high, std::uint64_t* low);
    NCMA_NATIVE_API std::uint8_t NCMA_NATIVE_CALL ncma_world_find_object(
        NcmaWorldHandle world, std::uint64_t high, std::uint64_t low, NcmaObjectReference* output);
    NCMA_NATIVE_API std::uint8_t NCMA_NATIVE_CALL ncma_world_read_transforms(
        NcmaWorldHandle world, const NcmaObjectReference* objects, NcmaTransform* output, std::uint32_t count);
    NCMA_NATIVE_API std::uint8_t NCMA_NATIVE_CALL ncma_world_write_transforms(
        NcmaWorldHandle world, const NcmaTransformWrite* writes, std::uint32_t count);
    // Host-only phase management. Nested phases/structural mutations fail explicitly.
    NCMA_NATIVE_API std::uint8_t NCMA_NATIVE_CALL ncma_world_begin_gameplay_phase(NcmaWorldHandle world);
    NCMA_NATIVE_API std::uint8_t NCMA_NATIVE_CALL ncma_world_commit_gameplay_phase(NcmaWorldHandle world);
    NCMA_NATIVE_API std::uint8_t NCMA_NATIVE_CALL ncma_world_abort_gameplay_phase(NcmaWorldHandle world);
    NCMA_NATIVE_API std::uint8_t NCMA_NATIVE_CALL ncma_world_send_signal(
        NcmaWorldHandle world, const NcmaGameplaySignal* signal);
    NCMA_NATIVE_API std::uint8_t NCMA_NATIVE_CALL ncma_world_receive_signals(
        NcmaWorldHandle world, NcmaObjectReference target, NcmaGameplaySignal* output,
        std::uint32_t capacity, std::uint32_t* count);

    NCMA_NATIVE_API std::uint32_t NCMA_NATIVE_CALL ncma_get_abi_version();
    // GameObject API semantic version 4: flat C#-only objects.
    // The base binary ABI stays v1; old creation exports default to C#.
    // Legacy argument layouts remain: parent must be zero, recursive is ignored.
    // ncma_world_set_parent accepts only zero (already detached); nonzero is an error.
    NCMA_NATIVE_API std::uint32_t NCMA_NATIVE_CALL ncma_get_game_object_api_version();
    NCMA_NATIVE_API NcmaWorldHandle NCMA_NATIVE_CALL ncma_world_create(const char* name);
    NCMA_NATIVE_API void NCMA_NATIVE_CALL ncma_world_destroy(NcmaWorldHandle world);
    NCMA_NATIVE_API std::uint64_t NCMA_NATIVE_CALL ncma_world_create_object(
        NcmaWorldHandle world, const char* name, std::uint64_t parent);
    // Compatibility symbol: only language 0 (C#) is accepted; all other values fail.
    NCMA_NATIVE_API std::uint64_t NCMA_NATIVE_CALL ncma_world_create_object_with_language(
        NcmaWorldHandle world, const char* name, std::uint32_t language);
    NCMA_NATIVE_API std::uint8_t NCMA_NATIVE_CALL ncma_world_get_object_language(
        NcmaWorldHandle world, std::uint64_t gameObject, std::uint32_t* output);
    NCMA_NATIVE_API std::uint8_t NCMA_NATIVE_CALL ncma_world_destroy_object(
        NcmaWorldHandle world, std::uint64_t gameObject, std::uint8_t recursive);
    // Legacy binary exports only; no Node type or new frontend API is retained.
    NCMA_NATIVE_API std::uint64_t NCMA_NATIVE_CALL ncma_world_create_node(
        NcmaWorldHandle world, const char* name, std::uint64_t parent);
    NCMA_NATIVE_API std::uint8_t NCMA_NATIVE_CALL ncma_world_destroy_node(
        NcmaWorldHandle world, std::uint64_t gameObject, std::uint8_t recursive);
    NCMA_NATIVE_API std::uint8_t NCMA_NATIVE_CALL ncma_world_set_parent(
        NcmaWorldHandle world, std::uint64_t gameObject, std::uint64_t parent);
    NCMA_NATIVE_API std::uint8_t NCMA_NATIVE_CALL ncma_world_get_local_transform(
        NcmaWorldHandle world, std::uint64_t gameObject, NcmaTransform* output);
    NCMA_NATIVE_API std::uint8_t NCMA_NATIVE_CALL ncma_world_set_local_transform(
        NcmaWorldHandle world, std::uint64_t gameObject, const NcmaTransform* value);
    NCMA_NATIVE_API const char* NCMA_NATIVE_CALL ncma_get_last_error();
}
