#pragma once

#include <cstdint>
#include <optional>
#include <string>
#include <string_view>

namespace NcmaEngine
{
    // Persistent scene identity. Runtime GameObjectId values are deliberately never serialized.
    struct SceneUuid final
    {
        std::uint64_t High = 0;
        std::uint64_t Low = 0;

        [[nodiscard]] bool IsValid() const noexcept { return High != 0 || Low != 0; }
        [[nodiscard]] std::string ToString() const;

        [[nodiscard]] static SceneUuid New();
        [[nodiscard]] static std::optional<SceneUuid> Parse(std::string_view value);

        friend bool operator==(const SceneUuid&, const SceneUuid&) = default;
    };
}
