#include "animation/AnimationGraph.h"

#include <algorithm>
#include <functional>
#include <unordered_set>

namespace NcmaEngine::Animation
{
    GraphNodeId AnimationGraph::AddNode(NodeKind kind, std::string name)
    {
        const GraphNodeId id = m_NextNode++;
        m_Nodes.emplace(id, GraphNode{id, kind, std::move(name)});
        return id;
    }

    GraphPinId AnimationGraph::AddPin(GraphNodeId node, std::string name, PinType type, PinDirection direction)
    {
        const auto nodeIt = m_Nodes.find(node);
        if (nodeIt == m_Nodes.end())
            return 0;
        const GraphPinId id = m_NextPin++;
        nodeIt->second.Pins.push_back({id, std::move(name), type, direction});
        m_PinLocations[id] = {node, nodeIt->second.Pins.size() - 1};
        return id;
    }

    bool AnimationGraph::Connect(GraphPinId from, GraphPinId to, std::string& error)
    {
        const GraphPin* source = FindPin(from);
        const GraphPin* target = FindPin(to);
        if (source == nullptr || target == nullptr)
        {
            error = "Link references an unknown pin";
            return false;
        }
        if (source->Direction != PinDirection::Output || target->Direction != PinDirection::Input)
        {
            error = "Links must connect output pins to input pins";
            return false;
        }
        if (source->Type != target->Type)
        {
            error = "Pin types are incompatible";
            return false;
        }
        if (std::any_of(m_Links.begin(), m_Links.end(), [to](const GraphLink& link) { return link.To == to; }))
        {
            error = "Input pin is already connected";
            return false;
        }
        const GraphNodeId sourceNode = m_PinLocations.at(from).Node;
        const GraphNodeId targetNode = m_PinLocations.at(to).Node;
        if (source->Type == PinType::Pose && (sourceNode == targetNode || HasPosePath(targetNode, sourceNode)))
        {
            error = "Pose links cannot create cycles";
            return false;
        }
        m_Links.push_back({from, to});
        error.clear();
        return true;
    }

    bool AnimationGraph::Disconnect(GraphPinId from, GraphPinId to)
    {
        const auto before = m_Links.size();
        std::erase_if(m_Links, [from, to](const GraphLink& link) { return link.From == from && link.To == to; });
        return before != m_Links.size();
    }

    bool AnimationGraph::Validate(std::vector<std::string>& errors) const
    {
        errors.clear();
        std::size_t outputCount = 0;
        for (const auto& [id, node] : m_Nodes)
        {
            (void)id;
            if (node.Kind == NodeKind::OutputPose)
                ++outputCount;
        }
        if (outputCount != 1)
            errors.emplace_back("Animation graph must contain exactly one output pose node");
        return errors.empty();
    }

    const GraphPin* AnimationGraph::FindPin(GraphPinId pin) const
    {
        const auto location = m_PinLocations.find(pin);
        if (location == m_PinLocations.end())
            return nullptr;
        return &m_Nodes.at(location->second.Node).Pins.at(location->second.Index);
    }

    bool AnimationGraph::HasPosePath(GraphNodeId from, GraphNodeId to) const
    {
        std::unordered_set<GraphNodeId> visited;
        std::function<bool(GraphNodeId)> visit = [&](GraphNodeId node) {
            if (!visited.insert(node).second)
                return false;
            if (node == to)
                return true;
            for (const auto& link : m_Links)
            {
                const GraphPin* source = FindPin(link.From);
                if (source == nullptr || source->Type != PinType::Pose || m_PinLocations.at(link.From).Node != node)
                    continue;
                if (visit(m_PinLocations.at(link.To).Node))
                    return true;
            }
            return false;
        };
        return visit(from);
    }
}

