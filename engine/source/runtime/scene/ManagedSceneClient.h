#pragma once
#include "scene/SceneView.h"
#include <span>
#include <cstddef>
#include <filesystem>
#include <optional>
namespace NcmaEngine
{
    struct EditorState final
    {
        SceneUuid SessionId;
        std::uint64_t Revision = 0;
        std::uint32_t UndoCount = 0, RedoCount = 0;
        std::string UndoLabel, RedoLabel;
        bool Dirty = false, HistoryInvalidated = false, EditBusy = false, Frozen = false;
        std::optional<SceneUuid> Selection;
        std::string FilePath;
    };
    struct McpConnection final
    {
        SceneUuid Id; std::string Name; bool Paired = false, Connected = false; std::uint32_t Pending = 0;
    };
    struct McpProposal final
    {
        SceneUuid Id, ConnectionId, DeleteTarget; std::string Capability, Risk, Summary; std::uint64_t Revision = 0; bool Destructive = false;
    };
    struct McpEndpointState final
    {
        bool Enabled = false; SceneUuid InstanceId; std::uint64_t DocumentGeneration = 0;
        std::string DescriptorPath, GrantSummary, AuditSummary; std::uint32_t QueueCount = 0; std::vector<McpConnection> Connections; std::vector<McpProposal> Proposals;
    };
    // Complete C#-encoded snapshot; native code never interprets it as an object store.
    struct SceneDocumentBlob final
    {
        std::vector<std::byte> Bytes;
        friend bool operator==(const SceneDocumentBlob&, const SceneDocumentBlob&) = default;
    };
    // Non-owning data access policy: only an opaque C# session token is stored here.
    // No native object/component map, UUID index, writable references or gameplay scheduler.
    class ManagedSceneClient final
    {
    public:
        explicit ManagedSceneClient(std::string name = "Untitled");
        ~ManagedSceneClient();
        ManagedSceneClient(const ManagedSceneClient&) = delete;
        ManagedSceneClient& operator=(const ManagedSceneClient&) = delete;
        std::uint64_t Handle() const noexcept { return m_Handle; }
        GameObjectId CreateObject(std::string name, bool spatial = true);
        bool DestroyObject(GameObjectId object);
        bool Contains(GameObjectId object) const;
        std::size_t Size() const { return GetObjects().size(); }
        std::string GetName() const;
        std::string GetObjectName(GameObjectId object) const;
        void SetObjectName(GameObjectId object, std::string name);
        SceneUuid GetPersistentId(GameObjectId object) const;
        GameObjectId FindObject(SceneUuid uuid) const;
        void AddBehaviour(GameObjectId object, BehaviourBinding binding);
        void UpdateBehaviour(GameObjectId object, BehaviourBinding binding);
        bool RemoveBehaviour(GameObjectId object, SceneUuid bindingId);
        void SetBehaviours(GameObjectId object, std::vector<BehaviourBinding> bindings);
        std::vector<BehaviourBinding> GetBehaviours(GameObjectId object) const;
        SceneView CaptureView() const; // Inspection projection; NOT a persistence/restore source.
        SceneDocumentBlob CaptureDocument() const;
        bool RestoreDocument(const SceneDocumentBlob& snapshot, std::string& error);
        std::uint64_t GetDocumentRevision() const;
        bool LoadDocument(const std::filesystem::path& path, std::string& error);
        bool SaveDocument(const std::filesystem::path& path, std::string& error) const;
        void ResetDocument(std::string name = "Untitled");
        std::vector<GameObjectId> GetObjects() const;
        bool HasTransform(GameObjectId object) const;
        Transform GetLocalTransform(GameObjectId object) const;
        void SetLocalTransform(GameObjectId object, const Transform& value);
        Transform GetWorldTransform(GameObjectId object) const { return GetLocalTransform(object); }
        void EnableEditing();
        EditorState GetEditorState() const;
        void SelectEditorObject(std::optional<SceneUuid> object);
        GameObjectId CreateEditorObject(std::string name, bool spatial = true);
        void RenameEditorObject(SceneUuid object, std::string name);
        void SetEditorTransform(SceneUuid object, const Transform& value);
        void NewEditorDocument();
        void OpenEditorDocument(const std::filesystem::path& path);
        void SaveEditorDocument(const std::filesystem::path& path = {});
        void DeleteEditorObject(SceneUuid object);
        void SetEditorBindings(SceneUuid object, const std::vector<BehaviourBinding>& values, std::string label = "Edit C# Behaviours");
        void RemoveEditorComponent(SceneUuid object, std::string typeId);
        void PreviewBindings(SceneUuid token, SceneUuid object, const std::vector<BehaviourBinding>& values);
        SceneUuid BeginInteraction(SceneUuid object, std::string label);
        void PreviewName(SceneUuid token, SceneUuid object, std::string name);
        void PreviewTransform(SceneUuid token, SceneUuid object, const Transform& value);
        void CommitInteraction(SceneUuid token);
        void CancelInteraction(SceneUuid token);
        bool UndoEditor(bool redo, std::string& error);
        void FreezeEditing(bool frozen);
        std::string ConfigureMcp(bool enabled, const std::filesystem::path& projectRoot);
        McpEndpointState PumpMcp();
        void PairMcp(SceneUuid connection, bool approve);
        void RevokeMcp(SceneUuid connection);
        void ApproveMcp(SceneUuid proposal, bool history, const SceneUuid* confirmedDelete);
        void RevokeMcpGrants(SceneUuid connection);
        std::string InvokeReadOnlyCapability(std::string request) const;
    private:
        std::vector<std::byte> Call(int operation, std::span<const std::byte> input = {}) const;
        std::uint64_t m_Handle = 0;
    };
}
