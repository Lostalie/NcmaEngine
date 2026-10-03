#pragma once

#include "interop/NcmaNativeApi.h"

// Independent extension ABI. Handles are process-local, never serialized or shared across DLLs.
// Returned UTF-8 strings live until the next animation API call on this thread. Copy immediately.
extern "C"
{
    NCMA_NATIVE_API std::uint32_t NCMA_NATIVE_CALL ncma_animation_abi_version();
    NCMA_NATIVE_API std::uint64_t NCMA_NATIVE_CALL ncma_animation_create(std::uint32_t version);
    NCMA_NATIVE_API void NCMA_NATIVE_CALL ncma_animation_destroy(std::uint64_t session);
    NCMA_NATIVE_API std::uint8_t NCMA_NATIVE_CALL ncma_animation_command(
        std::uint64_t session, std::uint32_t command, double value, const char* text);
    NCMA_NATIVE_API const char* NCMA_NATIVE_CALL ncma_animation_inspect(std::uint64_t session);
    NCMA_NATIVE_API const char* NCMA_NATIVE_CALL ncma_animation_last_error();
}
