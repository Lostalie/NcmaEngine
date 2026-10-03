#pragma once
#include <cstddef>
#include <cstdint>

// Managed NativeEntry mirrors this POD layout. All functions use cdecl and caller-owned buffers.
inline constexpr std::uint32_t NcmaGameplayBridgeVersion = 2;
struct NcmaExportPropertyV2
{
    std::uint32_t Kind = 0;
    std::uint32_t Reserved = 0;
    double DefaultValue = 0;
    char Name[128]{};
    char DisplayName[128]{};
    char Category[128]{};
};
static_assert(sizeof(NcmaExportPropertyV2) == 400);
static_assert(offsetof(NcmaExportPropertyV2, DefaultValue) == 8);
