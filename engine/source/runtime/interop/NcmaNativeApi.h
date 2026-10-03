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

// ABI 2 intentionally removes all native scene/world exports. Rebuild old managed consumers.
extern "C"
{
    NCMA_NATIVE_API std::uint32_t NCMA_NATIVE_CALL ncma_get_abi_version();
    NCMA_NATIVE_API const char* NCMA_NATIVE_CALL ncma_get_last_error();
}
