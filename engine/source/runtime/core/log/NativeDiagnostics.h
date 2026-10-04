#pragma once
#include "interop/NcmaNativeApi.h"
namespace NcmaEngine {
void NativeDiagnostic(const char* code,const char* message) noexcept;
}
extern "C" NCMA_NATIVE_API std::uint8_t NCMA_NATIVE_CALL ncma_diagnostics_read_v1(
    std::uint64_t after,std::uint32_t maximum,char* output,std::uint32_t capacity,std::uint32_t* required);
