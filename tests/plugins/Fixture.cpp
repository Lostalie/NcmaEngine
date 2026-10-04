#include "PluginSupport.h"
#include <unordered_map>
#include <mutex>
#ifndef NCMA_FIXTURE_MODE
#define NCMA_FIXTURE_MODE 0
#endif
namespace {
#if NCMA_FIXTURE_MODE != 6
struct Context { std::thread::id owner; bool timed_out = false; };
std::unordered_map<uint64_t, Context> contexts;
std::mutex contextGate;
const auto loadingThread = std::this_thread::get_id();
uint64_t next = 1;
uint32_t NCMA_CALL Initialize(const uint8_t* input, uint32_t length, uint64_t* output, NcmaErrorV1* error) noexcept
{
    return NcmaPlugin::Guard(error, [&]() -> uint32_t {
        std::lock_guard lock(contextGate);
        if (std::this_thread::get_id() != loadingThread) return NcmaPlugin::Error(error, NCMA_WRONG_THREAD);
        if (!output || input || length) return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT);
        *output = 0;
        if constexpr (NCMA_FIXTURE_MODE == 3) {
            return NcmaPlugin::Error(error, NCMA_INTERNAL_ERROR, "fixture_initialize_failed");
        } else {
        if (contexts.size() >= 64) return NcmaPlugin::Error(error, NCMA_BUSY);
        const uint64_t handle = (static_cast<uint64_t>(NCMA_FIXTURE_MODE + 1) << 56u) | next++;
        contexts.emplace(handle, Context{std::this_thread::get_id()}); *output = handle; return NCMA_OK;
        }
    });
}
uint32_t Validate(uint64_t handle, NcmaErrorV1* error)
{
    auto found = contexts.find(handle);
    if (found == contexts.end()) return NcmaPlugin::Error(error, NCMA_INVALID_HANDLE);
    if (found->second.owner != std::this_thread::get_id()) return NcmaPlugin::Error(error, NCMA_WRONG_THREAD);
    return NCMA_OK;
}
uint32_t NCMA_CALL Shutdown(uint64_t handle, NcmaErrorV1* error) noexcept
{
    return NcmaPlugin::Guard(error, [&]() -> uint32_t {
        auto valid = Validate(handle, error); if (valid) return valid;
        if constexpr (NCMA_FIXTURE_MODE == 4) {
        if (!contexts.at(handle).timed_out) {
            contexts.at(handle).timed_out = true;
            return NcmaPlugin::Error(error, NCMA_SHUTDOWN_TIMEOUT, "fixture_job_timeout");
        }
        }
        contexts.erase(handle); return NCMA_OK;
    });
}
uint32_t NCMA_CALL Status(uint64_t handle, NcmaModuleStatusV1* output, NcmaErrorV1* error) noexcept
{
    return NcmaPlugin::Guard(error, [&]() -> uint32_t {
        if (!output) return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT);
        auto valid = Validate(handle, error); if (valid) return valid;
        *output = {sizeof(NcmaModuleStatusV1), 1, 0, 0, handle}; return NCMA_OK;
    });
}
uint32_t NCMA_CALL Diagnostic(uint64_t context, uint8_t* output, uint32_t capacity, uint32_t* required, NcmaErrorV1* error) noexcept
{
    return NcmaPlugin::Guard(error, [&]() -> uint32_t {
        std::lock_guard lock(contextGate);
        auto valid = Validate(context, error); if (valid) return valid;
        return NcmaPlugin::Diagnostic(context, output, capacity, required, error);
    });
}
#endif
}
#if NCMA_FIXTURE_MODE == 6
extern "C" NCMA_EXPORT void NCMA_CALL ncma_fixture_missing_api() {}
#else
extern "C" NCMA_EXPORT uint32_t NCMA_CALL ncma_plugin_get_api(uint32_t major, uint32_t minor, void* output, uint32_t capacity, NcmaErrorV1* error) noexcept
{
    const NcmaModuleApiV1 api = {
        NCMA_FIXTURE_MODE == 2 ? 40u : static_cast<uint32_t>(sizeof(NcmaModuleApiV1)),
        NCMA_FIXTURE_MODE == 1 ? 2u : 1u, 0, NCMA_FIXTURE, 0,
        Initialize, Shutdown, Status, Diagnostic};
    return NcmaPlugin::CopyApi(major, minor, output, capacity, error, api);
}
#endif
