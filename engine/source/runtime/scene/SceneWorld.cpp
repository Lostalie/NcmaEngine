#include "scene/SceneWorld.h"

#include <algorithm>
#include <cmath>
#include <limits>
#include <unordered_set>

namespace NcmaEngine
{
    namespace
    {
        Transform ValidatedTransform(Transform value)
        {
            const float norm = value.Rotation.squaredNorm();
            if (!value.Position.allFinite() || !value.Scale.allFinite() ||
                !value.Rotation.coeffs().allFinite() || !std::isfinite(norm) || norm < 0.000001F)
                throw std::invalid_argument("Transform must be finite with a nonzero quaternion");
            value.Rotation.normalize();
            return value;
        }
    }

    SceneWorld::SceneWorld(std::string name) : m_Name(std::move(name)),
        m_WorldIdentity(SceneUuid::New()), m_OwnerThread(std::this_thread::get_id()) {}

    void SceneWorld::VerifyAccess() const
    {
        if (std::this_thread::get_id() != m_OwnerThread)
            throw std::logic_error("World access must run on its owner thread");
    }

    ObjectReference SceneWorld::GetReference(GameObjectId object) const
    {
        VerifyAccess();
        (void)Require(object);
        return {m_WorldIdentity, object};
    }

    GameObjectId SceneWorld::Resolve(ObjectReference object) const
    {
        VerifyAccess();
        if (object.World != m_WorldIdentity || !Contains(object.Id))
            throw std::out_of_range("Stale or foreign GameObject reference");
        return object.Id;
    }

    void SceneWorld::ReadTransforms(std::span<const ObjectReference> objects, std::span<Transform> output) const
    {
        VerifyAccess();
        if (objects.size() > AccessCapacity || output.size() != objects.size())
            throw std::invalid_argument("Invalid transform batch size (maximum 4096)");
        // Validate the entire request before modifying any caller-owned output.
        for (const auto object : objects) (void)Resolve(object);
        for (std::size_t i = 0; i < objects.size(); ++i)
            output[i] = GetLocalTransform(objects[i].Id);
    }

    void SceneWorld::WriteTransforms(std::span<const TransformWrite> writes)
    {
        VerifyAccess();
        if (writes.size() > AccessCapacity)
            throw std::invalid_argument("Invalid transform batch size (maximum 4096)");
        m_BatchTargets.clear();
        m_BatchTargets.reserve(AccessCapacity);
        std::size_t additions = 0;
        for (const auto& write : writes)
        {
            (void)Resolve(write.Object);
            (void)ValidatedTransform(write.Value);
            m_BatchTargets.push_back(write.Object.Id);
            if (!Require(write.Object.Id).PendingTransform) ++additions;
        }
        std::sort(m_BatchTargets.begin(), m_BatchTargets.end());
        if (std::adjacent_find(m_BatchTargets.begin(), m_BatchTargets.end()) != m_BatchTargets.end())
            throw std::invalid_argument("Duplicate transform target in one batch");
        if (m_PhaseActive && m_WrittenObjects.size() + additions > AccessCapacity)
            throw std::length_error("Gameplay transform queue is full");
        // All validation/allocation precedes mutation. Staging reuses bounded buffers.
        for (const auto& write : writes) WriteGameplayTransform(write.Object.Id, write.Value);
    }

    Transform SceneWorld::ReadGameplayTransform(GameObjectId object) const
    {
        VerifyAccess();
        return GetLocalTransform(object);
    }

    void SceneWorld::WriteGameplayTransform(GameObjectId object, const Transform& value)
    {
        VerifyAccess();
        (void)Require(object);
        const auto validated = ValidatedTransform(value);
        if (!m_PhaseActive) { GetLocalTransform(object) = validated; return; }
        auto& pending = Require(object).PendingTransform;
        if (!pending && m_WrittenObjects.size() == AccessCapacity)
            throw std::length_error("Gameplay transform queue is full");
        if (!pending) m_WrittenObjects.push_back(object);
        pending = validated;
    }

    void SceneWorld::BeginGameplayPhase()
    {
        VerifyAccess();
        if (m_PhaseActive) throw std::logic_error("Gameplay phase is already active");
        m_PendingSignals.reserve(AccessCapacity);
        m_ReadySignals.reserve(AccessCapacity);
        m_WrittenObjects.reserve(AccessCapacity);
        m_PhaseActive = true;
    }

    void SceneWorld::CommitGameplayPhase()
    {
        VerifyAccess();
        if (!m_PhaseActive) throw std::logic_error("No gameplay phase is active");
        for (const auto id : m_WrittenObjects) GetLocalTransform(id) = *Require(id).PendingTransform;
        // Capacity was reserved before any script ran; publishing cannot allocate.
        for (auto signal : m_PendingSignals)
        {
            signal.Sequence = m_NextSignalSequence++;
            m_ReadySignals.push_back(signal);
        }
        AbortGameplayPhase();
    }

    void SceneWorld::AbortGameplayPhase()
    {
        VerifyAccess();
        for (const auto id : m_WrittenObjects) Require(id).PendingTransform.reset();
        m_WrittenObjects.clear();
        m_PendingSignals.clear();
        m_PhaseActive = false;
    }

    void SceneWorld::SendSignal(GameplaySignal signal)
    {
        VerifyAccess();
        if (!m_PhaseActive) throw std::logic_error("Signals require an active gameplay phase");
        (void)Resolve(signal.Source);
        (void)Resolve(signal.Target);
        if (signal.Code == 0 || !std::isfinite(signal.Value) || signal.Sequence != 0)
            throw std::invalid_argument("Signal requires a nonzero code, finite value and sequence=0");
        if (m_ReadySignals.size() + m_PendingSignals.size() >= AccessCapacity)
            throw std::length_error("Gameplay signal queue is full; consume signals before sending more");
        m_PendingSignals.push_back(signal);
    }

    std::size_t SceneWorld::ReceiveSignals(ObjectReference target, std::span<GameplaySignal> output)
    {
        (void)Resolve(target);
        if (output.size() > AccessCapacity) throw std::invalid_argument("Invalid signal buffer size");
        // Consumption is outside the update transaction: abort does not replay delivered events.
        std::size_t count = 0;
        std::erase_if(m_ReadySignals, [&](const GameplaySignal& signal)
        {
            if (signal.Target != target || count == output.size()) return false;
            output[count++] = signal;
            return true;
        });
        return count;
    }

    GameObjectId SceneWorld::CreateObject(std::string name)
    {
        return CreateObjectWithUuid(std::move(name), SceneUuid::New());
    }

    GameObjectId SceneWorld::CreateObjectWithUuid(std::string name, SceneUuid persistentId)
    {
        VerifyAccess();
        if (m_PhaseActive) throw std::logic_error("Object creation is not allowed inside a gameplay phase");
        if (m_NextId == std::numeric_limits<GameObjectId>::max()) throw std::overflow_error("Object ID space exhausted");
        if (!persistentId.IsValid() || FindObject(persistentId) != InvalidGameObjectId)
            throw std::invalid_argument("Scene gameObject UUID must be valid and unique");

        const GameObjectId id = m_NextId++;
        ObjectRecord record;
        record.Id = id;
        record.PersistentId = persistentId;
        record.LogicLanguage = BehaviourLanguage::CSharp;
        record.Name = name.empty() ? "GameObject_" + std::to_string(id) : std::move(name);
        record.Components.emplace(std::type_index(typeid(Transform)), std::make_shared<Transform>());
        m_Objects.emplace(id, std::move(record));
        try { m_UuidIndex.emplace(persistentId, id); }
        catch (...) { m_Objects.erase(id); throw; }
        return id;
    }

    bool SceneWorld::DestroyObject(GameObjectId gameObject)
    {
        VerifyAccess();
        if (m_PhaseActive) throw std::logic_error("Object destruction is not allowed inside a gameplay phase");
        if (!Contains(gameObject)) return false;
        m_UuidIndex.erase(GetPersistentId(gameObject));
        m_Objects.erase(gameObject);
        std::erase_if(m_ReadySignals, [&](const auto& signal) { return signal.Source.Id == gameObject || signal.Target.Id == gameObject; });
        return true;
    }

    bool SceneWorld::Contains(GameObjectId gameObject) const
    {
        return gameObject != InvalidGameObjectId && m_Objects.contains(gameObject);
    }

    const std::string& SceneWorld::GetObjectName(GameObjectId gameObject) const { return Require(gameObject).Name; }
    void SceneWorld::SetObjectName(GameObjectId gameObject, std::string name) { Require(gameObject).Name = std::move(name); }
    SceneUuid SceneWorld::GetPersistentId(GameObjectId gameObject) const { return Require(gameObject).PersistentId; }
    BehaviourLanguage SceneWorld::GetLogicLanguage(GameObjectId gameObject) const { return Require(gameObject).LogicLanguage; }

    void SceneWorld::SetBehaviours(GameObjectId gameObject, std::vector<BehaviourBinding> bindings)
    {
        auto& record = Require(gameObject);
        std::unordered_set<std::string> identities;
        for (const auto& [id, object] : m_Objects)
            if (id != gameObject)
                for (const auto& binding : object.Behaviours)
                    identities.insert(binding.Id.ToString());
        for (const auto& binding : bindings)
        {
            if (binding.Language != BehaviourLanguage::CSharp)
                throw std::invalid_argument("Only C# gameplay is supported; Python gameplay has been removed");
            if (!binding.Id.IsValid() || !identities.insert(binding.Id.ToString()).second ||
                binding.TypeName.empty() || binding.TypeName.find_first_of("\r\n") != std::string::npos)
                throw std::invalid_argument("Invalid or duplicate Behaviour identity");
            std::unordered_set<std::string> properties;
            for (const auto& property : binding.Properties)
                if (!ValidExportValue(property) || !properties.insert(property.Name).second)
                    throw std::invalid_argument("Invalid or duplicate exported property: " + property.Name);
        }
        record.Behaviours = std::move(bindings);
    }

    void SceneWorld::AddBehaviour(GameObjectId gameObject, BehaviourBinding binding)
    {
        auto bindings = GetBehaviours(gameObject);
        bindings.push_back(std::move(binding));
        SetBehaviours(gameObject, std::move(bindings));
    }

    void SceneWorld::UpdateBehaviour(GameObjectId gameObject, BehaviourBinding binding)
    {
        auto bindings = GetBehaviours(gameObject);
        const auto found = std::find_if(bindings.begin(), bindings.end(),
            [&](const auto& existing) { return existing.Id == binding.Id; });
        if (found == bindings.end())
            throw std::out_of_range("Unknown Behaviour binding");
        *found = std::move(binding);
        SetBehaviours(gameObject, std::move(bindings));
    }

    bool SceneWorld::RemoveBehaviour(GameObjectId gameObject, SceneUuid bindingId)
    {
        auto bindings = GetBehaviours(gameObject);
        const auto count = std::erase_if(bindings, [&](const auto& binding) { return binding.Id == bindingId; });
        if (count == 0) return false;
        SetBehaviours(gameObject, std::move(bindings));
        return true;
    }
    const std::vector<BehaviourBinding>& SceneWorld::GetBehaviours(GameObjectId gameObject) const { return Require(gameObject).Behaviours; }

    GameObjectId SceneWorld::FindObject(SceneUuid persistentId) const noexcept
    {
        if (!persistentId.IsValid())
            return InvalidGameObjectId;
        const auto found = m_UuidIndex.find(persistentId);
        return found == m_UuidIndex.end() ? InvalidGameObjectId : found->second;
    }

    SceneSnapshot SceneWorld::CaptureSnapshot() const
    {
        SceneSnapshot snapshot;
        snapshot.Name = m_Name;
        snapshot.Objects.reserve(m_Objects.size());
        for (const GameObjectId gameObject : GetObjects())
            CaptureObject(gameObject, snapshot);
        return snapshot;
    }

    bool SceneWorld::RestoreSnapshot(const SceneSnapshot& snapshot, std::string& error)
    {
        VerifyAccess();
        if (m_PhaseActive) { error = "Cannot restore a world during a gameplay phase"; return false; }
        if (snapshot.Version != SceneSnapshotVersion)
        {
            error = "Unsupported scene snapshot version " + std::to_string(snapshot.Version);
            return false;
        }

        std::unordered_set<std::string> objectIds;
        std::unordered_set<std::string> bindingIds;
        for (std::size_t index = 0; index < snapshot.Objects.size(); ++index)
        {
            const SceneObjectSnapshot& gameObject = snapshot.Objects[index];
            if (gameObject.LogicLanguage != BehaviourLanguage::CSharp)
            {
                error = "Unsupported GameObject language; Python gameplay has been removed: " + gameObject.Name;
                return false;
            }
            for (const auto& binding : gameObject.Behaviours)
            {
                if (binding.Language != BehaviourLanguage::CSharp)
                {
                    error = "Unsupported Behaviour language; Python gameplay has been removed: " + gameObject.Name;
                    return false;
                }
                if (!binding.Id.IsValid() || !bindingIds.insert(binding.Id.ToString()).second ||
                    binding.TypeName.empty() || binding.TypeName.find_first_of("\r\n") != std::string::npos ||
                    binding.Language != BehaviourLanguage::CSharp)
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
            if (!gameObject.PersistentId.IsValid() ||
                !objectIds.insert(gameObject.PersistentId.ToString()).second)
            {
                error = "Scene contains an invalid or duplicate GameObject UUID";
                return false;
            }
            const auto& transform = gameObject.LocalTransform;
            if (!transform.Position.allFinite() || !transform.Scale.allFinite() ||
                !transform.Rotation.coeffs().allFinite() ||
                !std::isfinite(transform.Rotation.squaredNorm()) || transform.Rotation.squaredNorm() < 0.000001F)
            {
                error = "Scene contains an invalid transform";
                return false;
            }
        }

        SceneWorld restored(snapshot.Name.empty() ? "Untitled" : snapshot.Name);
        restored.m_NextId = m_NextId; // Old numeric handles must not alias restored objects.
        for (const SceneObjectSnapshot& gameObject : snapshot.Objects)
        {
            const GameObjectId runtimeId = restored.CreateObjectWithUuid(gameObject.Name, gameObject.PersistentId);
            restored.GetLocalTransform(runtimeId) = gameObject.LocalTransform;
            restored.GetLocalTransform(runtimeId).Rotation.normalize();
            restored.Require(runtimeId).Behaviours = gameObject.Behaviours;
        }

        m_Name.swap(restored.m_Name);
        m_Objects.swap(restored.m_Objects);
        m_UuidIndex.swap(restored.m_UuidIndex);
        m_NextId = restored.m_NextId;
        m_WorldIdentity = restored.m_WorldIdentity;
        m_ReadySignals.clear();
        m_NextSignalSequence = 1;
        error.clear();
        return true;
    }
    std::vector<GameObjectId> SceneWorld::GetObjects() const
    {
        std::vector<GameObjectId> objects;
        objects.reserve(m_Objects.size());
        for (const auto& entry : m_Objects)
            objects.push_back(entry.first);
        std::sort(objects.begin(), objects.end());
        return objects;
    }

    Transform& SceneWorld::GetLocalTransform(GameObjectId gameObject)
    {
        auto* transform = GetComponent<Transform>(gameObject);
        if (transform == nullptr)
            throw std::logic_error("GameObject has no spatial Transform");
        return *transform;
    }

    const Transform& SceneWorld::GetLocalTransform(GameObjectId gameObject) const
    {
        const auto* transform = GetComponent<Transform>(gameObject);
        if (transform == nullptr)
            throw std::logic_error("GameObject has no spatial Transform");
        return *transform;
    }

    Transform SceneWorld::GetWorldTransform(GameObjectId gameObject) const
    {
        return GetLocalTransform(gameObject);
    }

    SceneWorld::ObjectRecord& SceneWorld::Require(GameObjectId gameObject)
    {
        const auto it = m_Objects.find(gameObject);
        if (it == m_Objects.end())
            throw std::out_of_range("Unknown scene gameObject");
        return it->second;
    }

    const SceneWorld::ObjectRecord& SceneWorld::Require(GameObjectId gameObject) const
    {
        const auto it = m_Objects.find(gameObject);
        if (it == m_Objects.end())
            throw std::out_of_range("Unknown scene gameObject");
        return it->second;
    }

    void SceneWorld::CaptureObject(GameObjectId gameObject, SceneSnapshot& snapshot) const
    {
        const ObjectRecord& record = Require(gameObject);
        SceneObjectSnapshot captured;
        captured.PersistentId = record.PersistentId;
        captured.Name = record.Name;
        captured.LocalTransform = GetLocalTransform(gameObject);
        captured.Behaviours = record.Behaviours;
        captured.LogicLanguage = record.LogicLanguage;
        snapshot.Objects.push_back(std::move(captured));
    }
}
