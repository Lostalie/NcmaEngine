#include "scene/SceneWorld.h"

#include <algorithm>
#include <unordered_set>

namespace NcmaEngine
{
    SceneWorld::SceneWorld(std::string name) : m_Name(std::move(name)) {}

    NodeId SceneWorld::CreateNode(std::string name, NodeId parent)
    {
        return CreateNodeWithUuid(std::move(name), parent, SceneUuid::New());
    }

    NodeId SceneWorld::CreateNodeWithUuid(std::string name, NodeId parent, SceneUuid persistentId)
    {
        if (parent != InvalidNodeId)
            (void)Require(parent);
        if (!persistentId.IsValid() || FindNode(persistentId) != InvalidNodeId)
            throw std::invalid_argument("Scene node UUID must be valid and unique");

        const NodeId id = m_NextId++;
        NodeRecord record;
        record.Id = id;
        record.PersistentId = persistentId;
        record.Name = name.empty() ? "Node_" + std::to_string(id) : std::move(name);
        record.Parent = parent;
        m_Nodes.emplace(id, std::move(record));
        if (parent != InvalidNodeId)
            m_Nodes.at(parent).Children.push_back(id);
        return id;
    }

    bool SceneWorld::DestroyNode(NodeId node, DestroyPolicy policy)
    {
        if (!Contains(node))
            return false;

        if (policy == DestroyPolicy::Recursive)
        {
            RemoveFromParent(node);
            DestroyRecursive(node);
            return true;
        }

        NodeRecord& record = Require(node);
        const NodeId parent = record.Parent;
        const std::vector<NodeId> children = record.Children;
        RemoveFromParent(node);
        for (const NodeId child : children)
        {
            m_Nodes.at(child).Parent = parent;
            if (parent != InvalidNodeId)
                m_Nodes.at(parent).Children.push_back(child);
        }
        m_Nodes.erase(node);
        return true;
    }

    bool SceneWorld::SetParent(NodeId node, NodeId parent, bool keepWorldTransform)
    {
        NodeRecord& record = Require(node);
        if (parent != InvalidNodeId)
            (void)Require(parent);
        if (node == parent || WouldCreateCycle(node, parent))
            return false;
        if (record.Parent == parent)
            return true;

        // World-preserving reparenting requires inverse TRS; it is deliberately rejected until
        // the math layer supports non-uniform-scale-safe decomposition.
        if (keepWorldTransform)
            return false;

        RemoveFromParent(node);
        record.Parent = parent;
        if (parent != InvalidNodeId)
            m_Nodes.at(parent).Children.push_back(node);
        return true;
    }

    bool SceneWorld::Contains(NodeId node) const
    {
        return node != InvalidNodeId && m_Nodes.contains(node);
    }

    const std::string& SceneWorld::GetNodeName(NodeId node) const { return Require(node).Name; }
    void SceneWorld::SetNodeName(NodeId node, std::string name) { Require(node).Name = std::move(name); }
    SceneUuid SceneWorld::GetPersistentId(NodeId node) const { return Require(node).PersistentId; }
    std::vector<BehaviourBinding>& SceneWorld::GetBehaviours(NodeId node) { return Require(node).Behaviours; }
    const std::vector<BehaviourBinding>& SceneWorld::GetBehaviours(NodeId node) const { return Require(node).Behaviours; }

    NodeId SceneWorld::FindNode(SceneUuid persistentId) const noexcept
    {
        if (!persistentId.IsValid())
            return InvalidNodeId;
        for (const auto& [id, record] : m_Nodes)
            if (record.PersistentId == persistentId)
                return id;
        return InvalidNodeId;
    }

    SceneSnapshot SceneWorld::CaptureSnapshot() const
    {
        SceneSnapshot snapshot;
        snapshot.Name = m_Name;
        snapshot.Nodes.reserve(m_Nodes.size());
        for (const NodeId root : GetRoots())
            CaptureNode(root, snapshot);
        return snapshot;
    }

    bool SceneWorld::RestoreSnapshot(const SceneSnapshot& snapshot, std::string& error)
    {
        if (snapshot.Version < 1 || snapshot.Version > SceneSnapshotVersion)
        {
            error = "Unsupported scene snapshot version " + std::to_string(snapshot.Version);
            return false;
        }

        std::unordered_set<std::string> bindingIds;
        for (std::size_t index = 0; index < snapshot.Nodes.size(); ++index)
        {
            const SceneNodeSnapshot& node = snapshot.Nodes[index];
            for (const auto& binding : node.Behaviours)
            {
                if (!binding.Id.IsValid() || !bindingIds.insert(binding.Id.ToString()).second ||
                    binding.TypeName.empty() || binding.TypeName.find_first_of("\r\n") != std::string::npos ||
                    (binding.Language != BehaviourLanguage::CSharp && binding.Language != BehaviourLanguage::Python))
                {
                    error = "Invalid or duplicate Behaviour identity";
                    return false;
                }
                std::unordered_set<std::string> propertyNames;
                for (const auto& property : binding.Properties)
                    if (!ValidExportValue(property) || !propertyNames.insert(property.Name).second)
                    {
                        error = "Invalid or duplicate exported property: " + property.Name;
                        return false;
                    }
            }
            if (!node.PersistentId.IsValid())
            {
                error = "Scene node has an invalid UUID";
                return false;
            }
            for (std::size_t other = 0; other < index; ++other)
            {
                if (snapshot.Nodes[other].PersistentId == node.PersistentId)
                {
                    error = "Scene contains a duplicate node UUID";
                    return false;
                }
            }
            if (node.ParentId.has_value())
            {
                const bool parentExists = std::any_of(
                    snapshot.Nodes.begin(), snapshot.Nodes.end(), [&](const SceneNodeSnapshot& candidate) {
                        return candidate.PersistentId == *node.ParentId;
                    });
                if (!parentExists)
                {
                    error = "Scene node references a missing parent UUID";
                    return false;
                }
            }
        }

        SceneWorld restored(snapshot.Name.empty() ? "Untitled" : snapshot.Name);
        std::vector<bool> loaded(snapshot.Nodes.size(), false);
        std::size_t loadedCount = 0;
        while (loadedCount < snapshot.Nodes.size())
        {
            bool madeProgress = false;
            for (std::size_t index = 0; index < snapshot.Nodes.size(); ++index)
            {
                if (loaded[index])
                    continue;
                const SceneNodeSnapshot& node = snapshot.Nodes[index];
                NodeId parent = InvalidNodeId;
                if (node.ParentId.has_value())
                {
                    parent = restored.FindNode(*node.ParentId);
                    if (parent == InvalidNodeId)
                        continue;
                }
                const NodeId runtimeId = restored.CreateNodeWithUuid(node.Name, parent, node.PersistentId);
                restored.GetLocalTransform(runtimeId) = node.LocalTransform;
                restored.GetBehaviours(runtimeId) = node.Behaviours;
                loaded[index] = true;
                ++loadedCount;
                madeProgress = true;
            }
            if (!madeProgress)
            {
                error = "Scene hierarchy contains a cycle";
                return false;
            }
        }

        *this = std::move(restored);
        error.clear();
        return true;
    }
    NodeId SceneWorld::GetParent(NodeId node) const { return Require(node).Parent; }
    const std::vector<NodeId>& SceneWorld::GetChildren(NodeId node) const { return Require(node).Children; }

    std::vector<NodeId> SceneWorld::GetRoots() const
    {
        std::vector<NodeId> roots;
        roots.reserve(m_Nodes.size());
        for (const auto& [id, record] : m_Nodes)
            if (record.Parent == InvalidNodeId)
                roots.push_back(id);
        std::sort(roots.begin(), roots.end());
        return roots;
    }

    Transform& SceneWorld::GetLocalTransform(NodeId node) { return Require(node).LocalTransform; }
    const Transform& SceneWorld::GetLocalTransform(NodeId node) const { return Require(node).LocalTransform; }

    Transform SceneWorld::GetWorldTransform(NodeId node) const
    {
        const NodeRecord& record = Require(node);
        return record.Parent == InvalidNodeId
            ? record.LocalTransform
            : Transform::Combine(GetWorldTransform(record.Parent), record.LocalTransform);
    }

    SceneWorld::NodeRecord& SceneWorld::Require(NodeId node)
    {
        const auto it = m_Nodes.find(node);
        if (it == m_Nodes.end())
            throw std::out_of_range("Unknown scene node");
        return it->second;
    }

    const SceneWorld::NodeRecord& SceneWorld::Require(NodeId node) const
    {
        const auto it = m_Nodes.find(node);
        if (it == m_Nodes.end())
            throw std::out_of_range("Unknown scene node");
        return it->second;
    }

    bool SceneWorld::WouldCreateCycle(NodeId node, NodeId newParent) const
    {
        NodeId cursor = newParent;
        while (cursor != InvalidNodeId)
        {
            if (cursor == node)
                return true;
            cursor = Require(cursor).Parent;
        }
        return false;
    }

    void SceneWorld::RemoveFromParent(NodeId node)
    {
        NodeRecord& record = Require(node);
        if (record.Parent == InvalidNodeId)
            return;
        auto& siblings = m_Nodes.at(record.Parent).Children;
        siblings.erase(std::remove(siblings.begin(), siblings.end(), node), siblings.end());
        record.Parent = InvalidNodeId;
    }

    void SceneWorld::DestroyRecursive(NodeId node)
    {
        const std::vector<NodeId> children = Require(node).Children;
        for (const NodeId child : children)
            DestroyRecursive(child);
        m_Nodes.erase(node);
    }

    void SceneWorld::CaptureNode(NodeId node, SceneSnapshot& snapshot) const
    {
        const NodeRecord& record = Require(node);
        SceneNodeSnapshot captured;
        captured.PersistentId = record.PersistentId;
        if (record.Parent != InvalidNodeId)
            captured.ParentId = Require(record.Parent).PersistentId;
        captured.Name = record.Name;
        captured.LocalTransform = record.LocalTransform;
        captured.Behaviours = record.Behaviours;
        snapshot.Nodes.push_back(std::move(captured));
        for (const NodeId child : record.Children)
            CaptureNode(child, snapshot);
    }
}
