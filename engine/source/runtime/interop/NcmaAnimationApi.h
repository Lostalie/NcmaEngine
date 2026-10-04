#pragma once

#include "interop/NcmaNativeApi.h"

// Animation ABI 2: immutable demo-library numerical resource, no clock/player/history/policy.
// Buffers are caller-owned. 0=error, 1=copied, 2=query/short capacity; required includes JSON NUL.
// Pose layout: 10 local floats (XYZ, XYZW, scale XYZ) then 16 column-major model floats PER BONE,
// in two contiguous arrays: bones*10 locals followed by bones*16 models. Never serialize handles.
extern "C"
{
    NCMA_NATIVE_API std::uint32_t NCMA_NATIVE_CALL ncma_animation_abi_version();
    NCMA_NATIVE_API std::uint64_t NCMA_NATIVE_CALL ncma_animation_create(std::uint32_t version);
    NCMA_NATIVE_API void NCMA_NATIVE_CALL ncma_animation_destroy(std::uint64_t session);
    NCMA_NATIVE_API std::uint8_t NCMA_NATIVE_CALL ncma_animation_read_library(std::uint64_t resource,
        char* output, std::uint32_t capacity, std::uint32_t* required);
    NCMA_NATIVE_API std::uint8_t NCMA_NATIVE_CALL ncma_animation_sample(std::uint64_t resource,
        std::uint32_t clip, double time, const float* from_pose, std::uint32_t from_count, float weight,
        float* output, std::uint32_t capacity, std::uint32_t* required);
    NCMA_NATIVE_API std::uint8_t NCMA_NATIVE_CALL ncma_animation_motion(std::uint64_t resource,
        std::uint32_t clip, double from, double to, float* output, std::uint32_t capacity, std::uint32_t* required);
    NCMA_NATIVE_API std::uint8_t NCMA_NATIVE_CALL ncma_animation_notifies(std::uint64_t resource,
        std::uint32_t clip, double from, double to, char* output, std::uint32_t capacity, std::uint32_t* required);
    NCMA_NATIVE_API std::uint8_t NCMA_NATIVE_CALL ncma_animation_read_error(char* output,
        std::uint32_t capacity, std::uint32_t* required);
    // Legacy policy symbols are rejection-only until consolidated M2 cleanup.
    NCMA_NATIVE_API std::uint8_t NCMA_NATIVE_CALL ncma_animation_command(
        std::uint64_t session, std::uint32_t command, double value, const char* text);
    NCMA_NATIVE_API const char* NCMA_NATIVE_CALL ncma_animation_inspect(std::uint64_t session);
    NCMA_NATIVE_API const char* NCMA_NATIVE_CALL ncma_animation_last_error();
}
