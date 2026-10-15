#pragma once
// Windows graphics plugins only. Runtime tokens are never persisted or used as authority.
// A DLL-local counter repeats after FreeLibrary. LUIDs remain unique on this machine
// until reboot, independently of module loads. Keep ALL 64 bits (no type-tag masking).
#include <Windows.h>
#include <cstdint>
#include <stdexcept>
namespace NcmaPlugin {
#ifdef NCMA_PLUGIN_HANDLE_TEST
// Compiled only into the white-box executable, consistently across its translation units.
inline bool failHandleAllocation = false;
#endif
inline uint64_t NewWindowsHandle() {
#ifdef NCMA_PLUGIN_HANDLE_TEST
    if (failHandleAllocation) throw std::runtime_error("Native runtime handle allocation failed.");
#endif
    LUID id{};
    if (!AllocateLocallyUniqueId(&id)) throw std::runtime_error("Native runtime handle allocation failed.");
    const uint64_t value=(uint64_t(static_cast<uint32_t>(id.HighPart))<<32)|id.LowPart;
    if (!value) throw std::runtime_error("Native runtime handle allocation returned zero.");
    return value;
}
}
