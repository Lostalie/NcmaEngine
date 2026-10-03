#include "interop/NcmaNativeApi.h"
extern "C"
{
    std::uint32_t NCMA_NATIVE_CALL ncma_get_abi_version() { return 2; }
    const char* NCMA_NATIVE_CALL ncma_get_last_error() { return "Native scene API removed; use the C# Runtime.World"; }
}
