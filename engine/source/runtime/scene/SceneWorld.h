#pragma once

#include "foundation/MathTypes.h"
#include "scene/SceneUuid.h"
#include "scene/BehaviourBinding.h"

#include <cstdint>
#include <memory>
#include <optional>
#include <span>
#include <thread>
#include <stdexcept>
#include <string>
#include <typeindex>
#include <type_traits>
#include <unordered_map>
#include <utility>
#include <vector>

namespace NcmaEngine
{
    using GameObjectId = std::uint64_t;
    inline constexpr GameObjectId InvalidGameObjectId = 0;

    struct ObjectReference final
    {
        SceneUuid World;
        GameObjectId Id = 0;
        friend bool operator==(const ObjectReference&, const ObjectReference&) = default;
    };

    struct TransformWrite final { ObjectReference Object; Transform Value; };
    // Application-defined signal code, not a script-method name or a network RPC.
    struct GameplaySignal final
    {
        ObjectReference Source;
        ObjectReference Target;
        std::uint32_t Code = 0;
        double Value = 0;
        std::uint64_t Sequence = 0;
    };

    struct SceneUuidHash final
    {
        std::size_t operator()(SceneUuid value) const noexcept
        {
            return std::hash<std::uint64_t>{}(value.High) ^ (std::hash<std::uint64_t>{}(value.Low) << 1);
        }
    };

    inline constexpr std::uint32_t SceneSnapshotVersion = 5;

    struct SceneObjectSnapshot final
    {
        SceneUuid PersistentId;
        std::string Name;
        Transform LocalTransform{};
        std::vector<BehaviourBinding> Behaviours;
        BehaviourLanguage LogicLanguage = BehaviourLanguage::CSharp;
    };

    struct SceneSnapshot final
    {
        std::uint32_t Version = SceneSnapshotVersion;
        std::string Name = "Untitled";
        std::vector<SceneObjectSnapshot> Objects;
    };

    // Composition-based object store. Systems own update scheduling, not spatial hierarchy.
    class SceneWorld final
    {
    public:
        explicit SceneWorld(std::string name = "Untitled");
        SceneWorld(const SceneWorld&) = delete;
        SceneWorld& operator=(const SceneWorld&) = delete;
        SceneWorld(SceneWorld&&) = delete;
        SceneWorld& operator=(SceneWorld&&) = delete;

        static constexpr std::size_t AccessCapacity = 4096;
        void VerifyAccess() const;
        [[nodiscard]] ObjectReference GetReference(GameObjectId object) const;
        [[nodiscard]] GameObjectId Resolve(ObjectReference object) const;
        void ReadTransforms(std::span<const ObjectReference> objects, std::span<Transform> output) const;
        void WriteTransforms(std::span<const TransformWrite> writes);
        [[nodiscard]] Transform ReadGameplayTransform(GameObjectId object) const;
        void WriteGameplayTransform(GameObjectId object, const Transform& value);
        // Host-owned frame boundary. C# behaviours read committed state; writes become
        // visible together at commit. Last submitted write wins in deterministic host order.
        void BeginGameplayPhase();
        void CommitGameplayPhase();
        void AbortGameplayPhase();
        [[nodiscard]] bool IsGameplayPhaseActive() const noexcept { return m_PhaseActive; }
        void SendSignal(GameplaySignal signal);
        [[nodiscard]] std::size_t ReceiveSignals(ObjectReference target, std::span<GameplaySignal> output);

        [[nodiscard]] GameObjectId CreateObject(std::string name);
        [[nodiscard]] bool DestroyObject(GameObjectId gameObject);

        [[nodiscard]] bool Contains(GameObjectId gameObject) const;
        [[nodiscard]] std::size_t Size() const noexcept { return m_Objects.size(); }
        [[nodiscard]] const std::string& GetName() const noexcept { return m_Name; }
        void SetName(std::string name) { m_Name = std::move(name); }
        [[nodiscard]] const std::string& GetObjectName(GameObjectId gameObject) const;
        void SetObjectName(GameObjectId gameObject, std::string name);
        [[nodiscard]] SceneUuid GetPersistentId(GameObjectId gameObject) const;
        [[nodiscard]] GameObjectId FindObject(SceneUuid persistentId) const noexcept;
        [[nodiscard]] BehaviourLanguage GetLogicLanguage(GameObjectId gameObject) const;
        void AddBehaviour(GameObjectId gameObject, BehaviourBinding binding);
        void UpdateBehaviour(GameObjectId gameObject, BehaviourBinding binding);
        [[nodiscard]] bool RemoveBehaviour(GameObjectId gameObject, SceneUuid bindingId);
        void SetBehaviours(GameObjectId gameObject, std::vector<BehaviourBinding> bindings);
        [[nodiscard]] const std::vector<BehaviourBinding>& GetBehaviours(GameObjectId gameObject) const;

        [[nodiscard]] SceneSnapshot CaptureSnapshot() const;
        [[nodiscard]] bool RestoreSnapshot(const SceneSnapshot& snapshot, std::string& error);

        // Deterministic creation order; no parent/child ownership or transform inheritance.
        [[nodiscard]] std::vector<GameObjectId> GetObjects() const;

        [[nodiscard]] Transform& GetLocalTransform(GameObjectId gameObject);
        [[nodiscard]] const Transform& GetLocalTransform(GameObjectId gameObject) const;
        [[nodiscard]] Transform GetWorldTransform(GameObjectId gameObject) const;

        template<typename T, typename... Args>
        T& AddComponent(GameObjectId gameObject, Args&&... args)
        {
            ObjectRecord& record = Require(gameObject);
            const std::type_index key(typeid(T));
            if (record.Components.contains(key))
                throw std::logic_error("GameObject already owns this component type");
            auto component = std::make_shared<T>(std::forward<Args>(args)...);
            T& result = *component;
            record.Components.emplace(key, std::move(component));
            return result;
        }

        template<typename T>
        [[nodiscard]] T* GetComponent(GameObjectId gameObject)
        {
            ObjectRecord& record = Require(gameObject);
            const auto it = record.Components.find(std::type_index(typeid(T)));
            return it == record.Components.end() ? nullptr : static_cast<T*>(it->second.get());
        }

        template<typename T>
        [[nodiscard]] const T* GetComponent(GameObjectId gameObject) const
        {
            const ObjectRecord& record = Require(gameObject);
            const auto it = record.Components.find(std::type_index(typeid(T)));
            return it == record.Components.end() ? nullptr : static_cast<const T*>(it->second.get());
        }

        template<typename T>
        [[nodiscard]] bool RemoveComponent(GameObjectId gameObject)
        {
            // Current scene objects are spatial. Non-spatial objects are a later World feature.
            if constexpr (std::is_same_v<T, Transform>)
                throw std::logic_error("The default spatial Transform cannot be removed");
            return Require(gameObject).Components.erase(std::type_index(typeid(T))) != 0;
        }

    private:
        struct ObjectRecord final
        {
            GameObjectId Id = InvalidGameObjectId;
            SceneUuid PersistentId;
            std::string Name;
            BehaviourLanguage LogicLanguage = BehaviourLanguage::CSharp;
            std::optional<Transform> PendingTransform;
            std::vector<BehaviourBinding> Behaviours;
            std::unordered_map<std::type_index, std::shared_ptr<void>> Components;
        };

        [[nodiscard]] ObjectRecord& Require(GameObjectId gameObject);
        [[nodiscard]] const ObjectRecord& Require(GameObjectId gameObject) const;
        [[nodiscard]] GameObjectId CreateObjectWithUuid(std::string name, SceneUuid persistentId);
        void CaptureObject(GameObjectId gameObject, SceneSnapshot& snapshot) const;

        std::string m_Name;
        GameObjectId m_NextId = 1;
        std::unordered_map<GameObjectId, ObjectRecord> m_Objects;
        std::unordered_map<SceneUuid, GameObjectId, SceneUuidHash> m_UuidIndex;
        SceneUuid m_WorldIdentity;
        std::thread::id m_OwnerThread;
        bool m_PhaseActive = false;
        std::vector<GameObjectId> m_WrittenObjects;
        std::vector<GameObjectId> m_BatchTargets;
        std::vector<GameplaySignal> m_PendingSignals;
        std::vector<GameplaySignal> m_ReadySignals;
        std::uint64_t m_NextSignalSequence = 1;
    };
}
