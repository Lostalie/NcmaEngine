#pragma once

#include "scene/SceneWorld.h"

#include <filesystem>
#include <string>
#include <string_view>

namespace NcmaEngine
{
    // Deterministic, versioned text serialization for .ncscene assets.
    class SceneSerializer final
    {
    public:
        [[nodiscard]] static std::string Serialize(const SceneSnapshot& snapshot);
        [[nodiscard]] static bool Deserialize(
            std::string_view serialized, SceneSnapshot& snapshot, std::string& error);
        [[nodiscard]] static bool Save(
            const std::filesystem::path& path, const SceneSnapshot& snapshot, std::string& error);
        [[nodiscard]] static bool Load(
            const std::filesystem::path& path, SceneSnapshot& snapshot, std::string& error);
    };
}
