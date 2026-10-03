#pragma once

#include <array>
#include <cstdint>
#include <string>
#include <unordered_map>
#include <vector>

namespace NcmaEngine::UI
{
    using UiNodeId = std::uint64_t;

    enum class UiNodeKind : std::uint8_t
    {
        Frame,
        Group,
        Rectangle,
        Text,
        Image,
        Component,
        ComponentInstance
    };

    enum class LayoutMode : std::uint8_t { Free, Horizontal, Vertical };
    enum class SizingMode : std::uint8_t { Fixed, HugContents, FillContainer };

    struct DesignToken final
    {
        std::string Name;
        std::string Type;
        std::string Value;
    };

    struct UiStyle final
    {
        std::array<float, 4> Fill{1.0F, 1.0F, 1.0F, 1.0F};
        float CornerRadius = 0.0F;
        float Opacity = 1.0F;
        bool ClipContents = false;
    };

    struct UiLayout final
    {
        LayoutMode Mode = LayoutMode::Free;
        SizingMode Width = SizingMode::Fixed;
        SizingMode Height = SizingMode::Fixed;
        float Gap = 0.0F;
        std::array<float, 4> Padding{};
    };

    struct UiNode final
    {
        UiNodeId Id = 0;
        UiNodeId Parent = 0;
        std::string Name;
        UiNodeKind Kind = UiNodeKind::Frame;
        UiStyle Style;
        UiLayout Layout;
        std::vector<UiNodeId> Children;
    };

    // A serializable document model shared by editor canvas, runtime UI, undo/redo, and agents.
    class UiDocument final
    {
    public:
        [[nodiscard]] UiNodeId CreateNode(std::string name, UiNodeKind kind, UiNodeId parent = 0)
        {
            if (parent != 0 && !m_Nodes.contains(parent))
                return 0;
            const UiNodeId id = m_NextId++;
            m_Nodes.emplace(id, UiNode{id, parent, std::move(name), kind});
            if (parent != 0)
                m_Nodes.at(parent).Children.push_back(id);
            return id;
        }

        [[nodiscard]] UiNode* Find(UiNodeId id)
        {
            const auto it = m_Nodes.find(id);
            return it == m_Nodes.end() ? nullptr : &it->second;
        }

        void SetToken(DesignToken token) { m_Tokens[token.Name] = std::move(token); }
        [[nodiscard]] const auto& GetNodes() const noexcept { return m_Nodes; }
        [[nodiscard]] const auto& GetTokens() const noexcept { return m_Tokens; }

    private:
        UiNodeId m_NextId = 1;
        std::unordered_map<UiNodeId, UiNode> m_Nodes;
        std::unordered_map<std::string, DesignToken> m_Tokens;
    };
}
