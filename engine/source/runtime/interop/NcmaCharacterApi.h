#pragma once
#include "interop/NcmaNativeApi.h"
extern "C"
{
    NCMA_NATIVE_API std::uint32_t NCMA_NATIVE_CALL ncma_character_abi_version();
    // ABI 2 immutable resources, no clock, player, Undo or editor policy.
    // Handles are monotonic generations, never persistent asset identities. Maximum 16 live resources.
    NCMA_NATIVE_API std::uint64_t NCMA_NATIVE_CALL ncma_character_import(
        std::uint32_t version, const char* path_utf8, double sample_rate, const char* asset_uuid);
    NCMA_NATIVE_API std::uint8_t NCMA_NATIVE_CALL ncma_character_release(std::uint64_t resource);
    // Result: 0 error, 1 copied, 2 query/short buffer. Byte counts include UTF-8 NUL terminator.
    // Queries never sample/reimport/write the destination. Failed copies do not partially publish.
    NCMA_NATIVE_API std::uint8_t NCMA_NATIVE_CALL ncma_character_read_report(
        std::uint64_t resource, char* output, std::uint32_t capacity, std::uint32_t* required);
    NCMA_NATIVE_API std::uint8_t NCMA_NATIVE_CALL ncma_character_read_error(
        char* output, std::uint32_t capacity, std::uint32_t* required);
    // Numeric counts are elements, not bytes. Layout: column-major model matrices (16 floats/bone),
    // followed by CPU-skinned XYZ vertices in mesh order. Time is explicit; calls never advance state.
    NCMA_NATIVE_API std::uint8_t NCMA_NATIVE_CALL ncma_character_sample(
        std::uint64_t resource, std::uint32_t clip, double time,
        float* output, std::uint32_t capacity, std::uint32_t* required);
    NCMA_NATIVE_API std::uint8_t NCMA_NATIVE_CALL ncma_character_read_indices(
        std::uint64_t resource, std::uint32_t mesh,
        std::uint32_t* output, std::uint32_t capacity, std::uint32_t* required);
    // Legacy symbols retained until consolidated M2 cleanup. ABI 1 explicitly rejected.
    NCMA_NATIVE_API const char* NCMA_NATIVE_CALL ncma_character_inspect_fbx(
        std::uint32_t version, const char* path_utf8, double sample_rate);
    NCMA_NATIVE_API const char* NCMA_NATIVE_CALL ncma_character_last_error();
}
