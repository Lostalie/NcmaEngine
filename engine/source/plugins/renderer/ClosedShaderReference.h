#pragma once
#include "ClosedShaderSources.h"
#include "NcmaClosedShaderBytes.h"
#include <span>
namespace NcmaEngine::Rendering::ClosedShaderReference {
// Exact source/entry/profile/name binding rejects a mismatched or stale generated
// reference. Never accept project bytecode merely because a cache key matched.
inline std::span<const unsigned char> Find(std::string_view source, std::string_view entry,
    std::string_view target, std::string_view sourceName) noexcept {
    for (const auto& value : ClosedShaderBytes::Entries)
        if (value.source == source && value.entry == entry && value.target == target && value.sourceName == sourceName)
            return {value.bytes, value.size};
    return {};
}
}
