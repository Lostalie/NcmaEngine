#pragma once

#include "foundation/MathTypes.h"
#include "scene/SceneUuid.h"
#include "scene/BehaviourBinding.h"

#include <cstdint>
#include <memory>
#include <optional>
#include <stdexcept>
#include <string>
#include <typeindex>
#include <unordered_map>
#include <utility>
#include <vector>

namespace NcmaEngine
{
    using NodeId = std::uint64_t;
    inline constexpr NodeId InvalidNodeId = 0;

    enum class DestroyPolicy
    {
        Recursive,
        ReparentChildren
    };

    inline constexpr std::uint32_t SceneSnapshotVersion = 3;

    struct SceneNodeSnapshot final
    {
        SceneUuid PersistentId;
        std::optional<SceneUuid> ParentId;
        std::string Name;
        Transform LocalTransform{};
        std::vector<BehaviourBinding> Behaviours;
    };

    struct SceneSnapshot final
    {
        std::uint32_t Version = SceneSnapshotVersion;
        std::string Name = "Untitled";
        std::vector<SceneNodeSnapshot> Nodes;
    };

    // Godot-like nodes own hierarchy/lifecycle. Unity-like components add behavior and data.
    class SceneWorld final
    {
    public:
        explicit SceneWorld(std::string name = "Untitled");

        [[nodiscard]] NodeId CreateNode(std::string name, NodeId parent = InvalidNodeId);
        [[nodiscard]] bool DestroyNode(NodeId node, DestroyPolicy policy = DestroyPolicy::Recursive);
        [[nodiscard]] bool SetParent(NodeId node, NodeId parent, bool keepWorldTransform = false);

        [[nodiscard]] bool Contains(NodeId node) const;
        [[nodiscard]] std::size_t Size() const noexcept { return m_Nodes.size(); }
        [[nodiscard]] const std::string& GetName() const noexcept { return m_Name; }
        void SetName(std::string name) { m_Name = std::move(name); }
        [[nodiscard]] const std::string& GetNodeName(NodeId node) const;
        void SetNodeName(NodeId node, std::string name);
        [[nodiscard]] SceneUuid GetPersistentId(NodeId node) const;
        [[nodiscard]] NodeId FindNode(SceneUuid persistentId) const noexcept;
        [[nodiscard]] std::vector<BehaviourBinding>& GetBehaviours(NodeId node);
        [[nodiscard]] const std::vector<BehaviourBinding>& GetBehaviours(NodeId node) const;

        [[nodiscard]] SceneSnapshot CaptureSnapshot() const;
        [[nodiscard]] bool RestoreSnapshot(const SceneSnapshot& snapshot, std::string& error);

        [[nodiscard]] NodeId GetParent(NodeId node) const;
        [[nodiscard]] const std::vector<NodeId>& GetChildren(NodeId node) const;
        [[nodiscard]] std::vector<NodeId> GetRoots() const;

        [[nodiscard]] Transform& GetLocalTransform(NodeId node);
        [[nodiscard]] const Transform& GetLocalTransform(NodeId node) const;
        [[nodiscard]] Transform GetWorldTransform(NodeId node) const;

        template<typename T, typename... Args>
        T& AddComponent(NodeId node, Args&&... args)
        {
            NodeRecord& record = Require(node);
            const std::type_index key(typeid(T));
            if (record.Components.contains(key))
                throw std::logic_error("Node already owns this component type");
            auto component = std::make_shared<T>(std::forward<Args>(args)...);
            T& result = *component;
            record.Components.emplace(key, std::move(component));
            return result;
        }

        template<typename T>
        [[nodiscard]] T* GetComponent(NodeId node)
        {
            NodeRecord& record = Require(node);
            const auto it = record.Components.find(std::type_index(typeid(T)));
            return it == record.Components.end() ? nullptr : static_cast<T*>(it->second.get());
        }

        template<typename T>
        [[nodiscard]] const T* GetComponent(NodeId node) const
        {
            const NodeRecord& record = Require(node);
            const auto it = record.Components.find(std::type_index(typeid(T)));
            return it == record.Components.end() ? nullptr : static_cast<const T*>(it->second.get());
        }

        template<typename T>
        [[nodiscard]] bool RemoveComponent(NodeId node)
        {
            return Require(node).Components.erase(std::type_index(typeid(T))) != 0;
        }

    private:
        struct NodeRecord final
        {
            NodeId Id = InvalidNodeId;
            SceneUuid PersistentId;
            std::string Name;
            NodeId Parent = InvalidNodeId;
            std::vector<NodeId> Children;
            Transform LocalTransform{};
            std::vector<BehaviourBinding> Behaviours;
            std::unordered_map<std::type_index, std::shared_ptr<void>> Components;
        };

        [[nodiscard]] NodeRecord& Require(NodeId node);
        [[nodiscard]] const NodeRecord& Require(NodeId node) const;
        [[nodiscard]] bool WouldCreateCycle(NodeId node, NodeId newParent) const;
        void RemoveFromParent(NodeId node);
        void DestroyRecursive(NodeId node);
        [[nodiscard]] NodeId CreateNodeWithUuid(std::string name, NodeId parent, SceneUuid persistentId);
        void CaptureNode(NodeId node, SceneSnapshot& snapshot) const;

        std::string m_Name;
        NodeId m_NextId = 1;
        std::unordered_map<NodeId, NodeRecord> m_Nodes;
    };
}
