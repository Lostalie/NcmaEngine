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

    NCMA_NATIVE_API std::uint32_t NCMA_NATIVE_CALL ncma_get_abi_version();
    NCMA_NATIVE_API NcmaWorldHandle NCMA_NATIVE_CALL ncma_world_create(const char* name);
    NCMA_NATIVE_API void NCMA_NATIVE_CALL ncma_world_destroy(NcmaWorldHandle world);
    NCMA_NATIVE_API std::uint64_t NCMA_NATIVE_CALL ncma_world_create_node(
        NcmaWorldHandle world, const char* name, std::uint64_t parent);
    NCMA_NATIVE_API std::uint8_t NCMA_NATIVE_CALL ncma_world_destroy_node(
        NcmaWorldHandle world, std::uint64_t node, std::uint8_t recursive);
    NCMA_NATIVE_API std::uint8_t NCMA_NATIVE_CALL ncma_world_set_parent(
        NcmaWorldHandle world, std::uint64_t node, std::uint64_t parent);
    NCMA_NATIVE_API std::uint8_t NCMA_NATIVE_CALL ncma_world_get_local_transform(
        NcmaWorldHandle world, std::uint64_t node, NcmaTransform* output);
    NCMA_NATIVE_API std::uint8_t NCMA_NATIVE_CALL ncma_world_set_local_transform(
        NcmaWorldHandle world, std::uint64_t node, const NcmaTransform* value);
    NCMA_NATIVE_API const char* NCMA_NATIVE_CALL ncma_get_last_error();
}
