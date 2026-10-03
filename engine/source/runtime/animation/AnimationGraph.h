#pragma once

#include <cstdint>
#include <optional>
#include <string>
#include <unordered_map>
#include <vector>

namespace NcmaEngine::Animation
{
    using GraphNodeId = std::uint64_t;
    using GraphPinId = std::uint64_t;

    enum class PinType : std::uint8_t
    {
        Pose,
        Float,
        Bool,
        Trigger
    };

    enum class PinDirection : std::uint8_t
    {
        Input,
        Output
    };

    enum class NodeKind : std::uint8_t
    {
        OutputPose,
        ClipPlayer,
        Blend,
        BlendSpace2D,
        StateMachine,
        LayeredBlendPerBone,
        InverseKinematics
    };

    struct GraphPin final
    {
        GraphPinId Id = 0;
        std::string Name;
        PinType Type = PinType::Pose;
        PinDirection Direction = PinDirection::Input;
    };

    struct GraphNode final
    {
        GraphNodeId Id = 0;
        NodeKind Kind = NodeKind::ClipPlayer;
        std::string Name;
        float EditorX = 0.0F;
        float EditorY = 0.0F;
        std::vector<GraphPin> Pins;
    };

    struct GraphLink final
    {
        GraphPinId From = 0;
        GraphPinId To = 0;
    };

    class AnimationGraph final
    {
    public:
        [[nodiscard]] GraphNodeId AddNode(NodeKind kind, std::string name);
        [[nodiscard]] GraphPinId AddPin(GraphNodeId node, std::string name, PinType type, PinDirection direction);
        [[nodiscard]] bool Connect(GraphPinId from, GraphPinId to, std::string& error);
        [[nodiscard]] bool Disconnect(GraphPinId from, GraphPinId to);
        [[nodiscard]] bool Validate(std::vector<std::string>& errors) const;

        [[nodiscard]] const std::unordered_map<GraphNodeId, GraphNode>& GetNodes() const noexcept { return m_Nodes; }
        [[nodiscard]] const std::vector<GraphLink>& GetLinks() const noexcept { return m_Links; }

    private:
        struct PinLocation final { GraphNodeId Node = 0; std::size_t Index = 0; };
        [[nodiscard]] const GraphPin* FindPin(GraphPinId pin) const;
        [[nodiscard]] bool HasPosePath(GraphNodeId from, GraphNodeId to) const;

        GraphNodeId m_NextNode = 1;
        GraphPinId m_NextPin = 1;
        std::unordered_map<GraphNodeId, GraphNode> m_Nodes;
        std::unordered_map<GraphPinId, PinLocation> m_PinLocations;
        std::vector<GraphLink> m_Links;
    };
}

