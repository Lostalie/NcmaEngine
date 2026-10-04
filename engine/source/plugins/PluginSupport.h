#pragma once
#include "contracts/NcmaPlugin.h"
#include <algorithm>
#include <cstring>
#include <exception>
#include <string_view>
#include <thread>
namespace NcmaPlugin {
inline uint32_t Error(NcmaErrorV1* output, uint32_t code, std::string_view message = {}) noexcept
{
    if (output) {
        *output = {}; output->code = code;
        auto count = std::min(message.size(), sizeof(output->message));
        if (count < message.size())
            while (count > 0 && (static_cast<unsigned char>(message[count]) & 0xC0u) == 0x80u) --count;
        if (count) std::memcpy(output->message, message.data(), count);
        output->message_length = static_cast<uint32_t>(count);
    }
    return code;
}
template<class F> uint32_t Guard(NcmaErrorV1* output, F&& action) noexcept
{
    if (!output) return NCMA_INVALID_ARGUMENT;
    *output = {};
    try { return action(); }
    catch (const std::exception& e) { return Error(output, NCMA_INTERNAL_ERROR, e.what()); }
    catch (...) { return Error(output, NCMA_INTERNAL_ERROR, "Unknown native failure."); }
}
inline uint32_t Diagnostic(uint64_t, uint8_t* output, uint32_t capacity, uint32_t* required, NcmaErrorV1* error) noexcept
{
    return Guard(error, [&]() -> uint32_t {
        if (!required || (capacity && !output)) return Error(error, NCMA_INVALID_ARGUMENT);
        *required = 0; return NCMA_OK;
    });
}
template<class T> uint32_t CopyApi(uint32_t major, uint32_t minor, void* output, uint32_t capacity,
    NcmaErrorV1* error, const T& table, uint32_t supportedMinor = 0) noexcept
{
    return Guard(error, [&]() -> uint32_t {
        if (major != 1 || minor > supportedMinor) return Error(error, NCMA_ABI_MISMATCH);
        if (!output || capacity < sizeof(T)) { Error(error, NCMA_BUFFER_TOO_SMALL); error->required_bytes = sizeof(T); return NCMA_BUFFER_TOO_SMALL; }
        std::memcpy(output, &table, sizeof(T)); return NCMA_OK;
    });
}
}
