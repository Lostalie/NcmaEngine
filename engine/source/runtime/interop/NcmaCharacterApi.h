#pragma once

#include "interop/NcmaNativeApi.h"

extern "C"
{
    NCMA_NATIVE_API std::uint32_t NCMA_NATIVE_CALL ncma_character_abi_version();
    // Read-only import/inspection, no retained session or file writes.
    // UTF-8 result/error are thread-local and must be copied before the next character API call.
    NCMA_NATIVE_API const char* NCMA_NATIVE_CALL ncma_character_inspect_fbx(
        std::uint32_t version, const char* path_utf8, double sample_rate);
    NCMA_NATIVE_API const char* NCMA_NATIVE_CALL ncma_character_last_error();
}
