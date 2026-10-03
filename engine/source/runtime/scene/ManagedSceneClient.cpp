#include "scene/ManagedSceneClient.h"
#include "script/runtime/ManagedHost.h"
#include <algorithm>
#include <array>
#include <cstring>
#include <stdexcept>
#include <type_traits>
namespace NcmaEngine
{
    namespace
    {
        constexpr std::size_t Limit = 4 * 1024 * 1024;
        struct Buffer
        {
            std::vector<std::byte> Bytes;
            std::size_t Cursor = 0;
            template<class T> void Put(T value)
            {
                static_assert(std::is_trivially_copyable_v<T>);
                if (Bytes.size() + sizeof(T) > Limit) throw std::invalid_argument("Scene bridge input exceeds 4 MiB");
                const auto* data = reinterpret_cast<const std::byte*>(&value);
                Bytes.insert(Bytes.end(), data, data + sizeof(T));
            }
            template<class T> T Get()
            {
                if (Cursor + sizeof(T) > Bytes.size()) throw std::runtime_error("Truncated scene bridge response");
                T value; std::memcpy(&value, Bytes.data() + Cursor, sizeof(T)); Cursor += sizeof(T); return value;
            }
            void Text(const std::string& value)
            {
                Put(static_cast<std::uint32_t>(value.size()));
                if (Bytes.size() + value.size() > Limit) throw std::invalid_argument("Scene bridge string exceeds limit");
                const auto* data = reinterpret_cast<const std::byte*>(value.data());
                Bytes.insert(Bytes.end(), data, data + value.size());
            }
            std::string Text()
            {
                const auto size = Get<std::uint32_t>();
                if (Cursor + size > Bytes.size()) throw std::runtime_error("Truncated scene bridge string");
                std::string value(reinterpret_cast<const char*>(Bytes.data() + Cursor), size); Cursor += size; return value;
            }
            void Uuid(SceneUuid value) { Put(value.High); Put(value.Low); }
            SceneUuid Uuid() { const auto high = Get<std::uint64_t>(); return {high, Get<std::uint64_t>()}; }
            void TransformValue(const Transform& value)
            {
                for (const auto axis : {value.Position.x(), value.Position.y(), value.Position.z(), value.Rotation.x(), value.Rotation.y(), value.Rotation.z(), value.Rotation.w(), value.Scale.x(), value.Scale.y(), value.Scale.z()}) Put(axis);
            }
            Transform TransformValue()
            {
                std::array<float, 10> v{}; for (auto& value : v) value = Get<float>();
                return {{v[0], v[1], v[2]}, Quaternion(v[6], v[3], v[4], v[5]), {v[7], v[8], v[9]}};
            }
            void Bindings(const std::vector<BehaviourBinding>& values)
            {
                Put(static_cast<std::uint32_t>(values.size()));
                for (const auto& value : values)
                {
                    Uuid(value.Id); Text(value.TypeName); Put<std::uint8_t>(value.Enabled);
                    Put(static_cast<std::uint32_t>(value.Properties.size()));
                    for (const auto& property : value.Properties) { Text(property.Name); Put(static_cast<std::uint32_t>(property.Kind)); Put(property.Value); }
                }
            }
            std::vector<BehaviourBinding> Bindings()
            {
                const auto count = Get<std::uint32_t>();
                if (count > 1024) throw std::runtime_error("Invalid bridge binding count");
                std::vector<BehaviourBinding> values;
                for (std::uint32_t i = 0; i < count; ++i)
                {
                    BehaviourBinding value; value.Id = Uuid(); value.TypeName = Text(); value.Enabled = Get<std::uint8_t>() != 0;
                    const auto properties = Get<std::uint32_t>();
                    if (properties > 1024) throw std::runtime_error("Invalid bridge property count");
                    for (std::uint32_t p = 0; p < properties; ++p) { ExportValue property; property.Name = Text(); property.Kind = static_cast<ExportKind>(Get<std::uint32_t>()); property.Value = Get<double>(); value.Properties.push_back(std::move(property)); }
                    values.push_back(std::move(value));
                }
                return values;
            }
            EditorState Editing()
            {
                EditorState state; state.SessionId = Uuid(); state.Revision = Get<std::uint64_t>();
                state.UndoCount = Get<std::uint32_t>(); state.RedoCount = Get<std::uint32_t>();
                state.UndoLabel = Text(); state.RedoLabel = Text();
                state.Dirty = Get<std::uint8_t>() != 0; state.HistoryInvalidated = Get<std::uint8_t>() != 0;
                state.EditBusy = Get<std::uint8_t>() != 0; state.Frozen = Get<std::uint8_t>() != 0;
                if (Get<std::uint8_t>() != 0) state.Selection = Uuid();
                state.FilePath = Text();
                if (Cursor != Bytes.size()) throw std::runtime_error("Trailing editor state response");
                return state;
            }
            SceneView View()
            {
                SceneView value; value.Name = Text(); const auto count = Get<std::uint32_t>();
                if (count > 4096) throw std::runtime_error("Invalid bridge object count");
                for (std::uint32_t i = 0; i < count; ++i)
                { SceneObjectView object; object.PersistentId = Uuid(); object.Name = Text(); object.HasTransform = Get<std::uint8_t>() != 0; object.LocalTransform = TransformValue(); object.Behaviours = Bindings(); value.Objects.push_back(std::move(object)); }
                if (Cursor != Bytes.size()) throw std::runtime_error("Trailing scene bridge response");
                return value;
            }
        };
        Buffer ObjectInput(GameObjectId id) { Buffer input; input.Put(id); return input; }
    }
    ManagedSceneClient::ManagedSceneClient(std::string name)
    {
        using Create = std::uint64_t(__cdecl*)(const char*, char*, int);
        using Version = std::uint32_t(__cdecl*)();
        const auto version = reinterpret_cast<Version>(Scripting::ManagedHost::Resolve(L"GetSceneBridgeVersion"));
        if (version() != 6) throw std::runtime_error("Managed scene bridge version mismatch");
        const auto create = reinterpret_cast<Create>(Scripting::ManagedHost::Resolve(L"CreateScene"));
        std::array<char, 2048> error{}; m_Handle = create(name.c_str(), error.data(), static_cast<int>(error.size()));
        if (!m_Handle) throw std::runtime_error(error.data());
    }
    ManagedSceneClient::~ManagedSceneClient() { try { (void)Call(1); } catch (...) { /* CLR teardown must not escape destruction. */ } }
    std::vector<std::byte> ManagedSceneClient::Call(int operation, std::span<const std::byte> input) const
    {
        using Exchange = int(__cdecl*)(std::uint64_t, int, const std::byte*, int, std::byte*, int, char*, int);
        static const auto exchange = reinterpret_cast<Exchange>(Scripting::ManagedHost::Resolve(L"SceneCall"));
        if (input.size() > Limit) throw std::invalid_argument("Scene request exceeds 4 MiB");
        thread_local std::array<std::byte, Limit> output{};
        std::array<char, 2048> error{};
        const int count = exchange(m_Handle, operation, input.data(), static_cast<int>(input.size()), output.data(), static_cast<int>(output.size()), error.data(), static_cast<int>(error.size()));
        if (count < 0) throw std::invalid_argument(error.data());
        if (static_cast<std::size_t>(count) > output.size()) throw std::runtime_error("Invalid managed scene response size");
        return {output.begin(), output.begin() + count};
    }
    GameObjectId ManagedSceneClient::CreateObject(std::string name, bool spatial) { Buffer in; in.Text(name); in.Put<std::uint8_t>(spatial); Buffer out{Call(2, in.Bytes)}; return out.Get<GameObjectId>(); }
    bool ManagedSceneClient::DestroyObject(GameObjectId object) { Buffer out{Call(3, ObjectInput(object).Bytes)}; return out.Get<std::uint8_t>() != 0; }
    SceneView ManagedSceneClient::CaptureView() const { Buffer out{Call(23)}; return out.View(); }
    SceneDocumentBlob ManagedSceneClient::CaptureDocument() const { return {Call(21)}; }
    bool ManagedSceneClient::RestoreDocument(const SceneDocumentBlob& snapshot, std::string& error)
    {
        try { (void)Call(22, snapshot.Bytes); error.clear(); return true; }
        catch (const std::exception& e) { error = e.what(); return false; }
    }
    std::uint64_t ManagedSceneClient::GetDocumentRevision() const { Buffer out{Call(24)}; return out.Get<std::uint64_t>(); }
    std::vector<GameObjectId> ManagedSceneClient::GetObjects() const { Buffer out{Call(5)}; const auto count = out.Get<std::uint32_t>(); if (count > 4096) throw std::runtime_error("Invalid object count"); std::vector<GameObjectId> ids; for (std::uint32_t i = 0; i < count; ++i) ids.push_back(out.Get<GameObjectId>()); return ids; }
    bool ManagedSceneClient::Contains(GameObjectId object) const { Buffer out{Call(6, ObjectInput(object).Bytes)}; return out.Get<std::uint8_t>() != 0; }
    std::string ManagedSceneClient::GetName() const { Buffer out{Call(7)}; return out.Text(); }
    std::string ManagedSceneClient::GetObjectName(GameObjectId object) const { Buffer out{Call(8, ObjectInput(object).Bytes)}; return out.Text(); }
    void ManagedSceneClient::SetObjectName(GameObjectId object, std::string name) { auto in = ObjectInput(object); in.Text(name); (void)Call(9, in.Bytes); }
    SceneUuid ManagedSceneClient::GetPersistentId(GameObjectId object) const { Buffer out{Call(10, ObjectInput(object).Bytes)}; return out.Uuid(); }
    GameObjectId ManagedSceneClient::FindObject(SceneUuid uuid) const { Buffer in; in.Uuid(uuid); Buffer out{Call(11, in.Bytes)}; return out.Get<GameObjectId>(); }
    Transform ManagedSceneClient::GetLocalTransform(GameObjectId object) const { Buffer out{Call(12, ObjectInput(object).Bytes)}; return out.TransformValue(); }
    void ManagedSceneClient::SetLocalTransform(GameObjectId object, const Transform& value) { auto in = ObjectInput(object); in.TransformValue(value); (void)Call(13, in.Bytes); }
    bool ManagedSceneClient::LoadDocument(const std::filesystem::path& path, std::string& error)
    {
        try { Buffer in; const auto utf8 = path.u8string(); in.Text(std::string(utf8.begin(), utf8.end())); (void)Call(25, in.Bytes); error.clear(); return true; }
        catch (const std::exception& e) { error = e.what(); return false; }
    }
    bool ManagedSceneClient::SaveDocument(const std::filesystem::path& path, std::string& error) const
    {
        try { Buffer in; const auto utf8 = path.u8string(); in.Text(std::string(utf8.begin(), utf8.end())); (void)Call(26, in.Bytes); error.clear(); return true; }
        catch (const std::exception& e) { error = e.what(); return false; }
    }
    void ManagedSceneClient::ResetDocument(std::string name) { Buffer in; in.Text(name); (void)Call(27, in.Bytes); }
    bool ManagedSceneClient::HasTransform(GameObjectId object) const { Buffer out{Call(19, ObjectInput(object).Bytes)}; return out.Get<std::uint8_t>() != 0; }
    std::vector<BehaviourBinding> ManagedSceneClient::GetBehaviours(GameObjectId object) const { auto in = ObjectInput(object); Buffer out{Call(20, in.Bytes)}; return out.Bindings(); }
    void ManagedSceneClient::SetBehaviours(GameObjectId object, std::vector<BehaviourBinding> values) { auto in = ObjectInput(object); in.Bindings(values); (void)Call(15, in.Bytes); }
    void ManagedSceneClient::AddBehaviour(GameObjectId object, BehaviourBinding binding) { auto values = GetBehaviours(object); values.push_back(std::move(binding)); SetBehaviours(object, std::move(values)); }
    void ManagedSceneClient::UpdateBehaviour(GameObjectId object, BehaviourBinding binding) { auto values = GetBehaviours(object); auto found = std::find_if(values.begin(), values.end(), [&](const auto& v) { return v.Id == binding.Id; }); if (found == values.end()) throw std::invalid_argument("Unknown Behaviour"); *found = std::move(binding); SetBehaviours(object, std::move(values)); }
    bool ManagedSceneClient::RemoveBehaviour(GameObjectId object, SceneUuid id) { auto values = GetBehaviours(object); if (!std::erase_if(values, [&](const auto& v) { return v.Id == id; })) return false; SetBehaviours(object, std::move(values)); return true; }
    void ManagedSceneClient::EnableEditing() { (void)Call(30); }
    EditorState ManagedSceneClient::GetEditorState() const { Buffer out{Call(34)}; return out.Editing(); }
    void ManagedSceneClient::SelectEditorObject(std::optional<SceneUuid> object) { Buffer in; in.Put<std::uint8_t>(object.has_value()); if (object) in.Uuid(*object); (void)Call(33, in.Bytes); }
    GameObjectId ManagedSceneClient::CreateEditorObject(std::string name, bool spatial)
    {
        const auto id = SceneUuid::New(); Buffer in; in.Put<std::uint32_t>(0); in.Uuid(id); in.Put(GetDocumentRevision());
        in.Text("Create GameObject"); in.Text(name); in.Put<std::uint8_t>(spatial); (void)Call(32, in.Bytes); return FindObject(id);
    }
    void ManagedSceneClient::RenameEditorObject(SceneUuid object, std::string name)
    {
        Buffer in; in.Put<std::uint32_t>(1); in.Uuid(object); in.Put(GetDocumentRevision()); in.Text("Rename GameObject"); in.Text(name); (void)Call(32, in.Bytes);
    }
    void ManagedSceneClient::SetEditorTransform(SceneUuid object, const Transform& value)
    {
        Buffer in; in.Put<std::uint32_t>(2); in.Uuid(object); in.Put(GetDocumentRevision()); in.Text("Edit Transform"); in.TransformValue(value); (void)Call(32, in.Bytes);
    }
    void ManagedSceneClient::NewEditorDocument() { Buffer in; in.Put(GetDocumentRevision()); (void)Call(43, in.Bytes); }
    void ManagedSceneClient::OpenEditorDocument(const std::filesystem::path& path)
    {
        Buffer in; const auto utf8 = path.u8string(); in.Text(std::string(utf8.begin(), utf8.end())); in.Put(GetDocumentRevision()); (void)Call(44, in.Bytes);
    }
    void ManagedSceneClient::SaveEditorDocument(const std::filesystem::path& path)
    {
        Buffer in; const auto utf8 = path.u8string(); in.Text(std::string(utf8.begin(), utf8.end())); (void)Call(45, in.Bytes);
    }
    void ManagedSceneClient::DeleteEditorObject(SceneUuid object)
    {
        Buffer in; in.Uuid(object); in.Put(GetDocumentRevision()); (void)Call(46, in.Bytes);
    }
    void ManagedSceneClient::SetEditorBindings(SceneUuid object, const std::vector<BehaviourBinding>& values, std::string label)
    {
        Buffer in; in.Put<std::uint32_t>(3); in.Uuid(object); in.Put(GetDocumentRevision()); in.Text(label); in.Bindings(values); (void)Call(32, in.Bytes);
    }
    void ManagedSceneClient::RemoveEditorComponent(SceneUuid object, std::string typeId)
    {
        Buffer in; in.Put<std::uint32_t>(4); in.Uuid(object); in.Put(GetDocumentRevision()); in.Text("Remove Component"); in.Text(typeId); (void)Call(32, in.Bytes);
    }
    void ManagedSceneClient::PreviewBindings(SceneUuid token, SceneUuid object, const std::vector<BehaviourBinding>& values)
    {
        Buffer in; in.Uuid(token); in.Put<std::uint32_t>(3); in.Uuid(object); in.Bindings(values); (void)Call(37, in.Bytes);
    }
    SceneUuid ManagedSceneClient::BeginInteraction(SceneUuid object, std::string label)
    {
        Buffer in; in.Uuid(object); in.Put(GetDocumentRevision()); in.Text(label); Buffer out{Call(36, in.Bytes)}; return out.Uuid();
    }
    void ManagedSceneClient::PreviewName(SceneUuid token, SceneUuid object, std::string name)
    {
        Buffer in; in.Uuid(token); in.Put<std::uint32_t>(1); in.Uuid(object); in.Text(name); (void)Call(37, in.Bytes);
    }
    void ManagedSceneClient::PreviewTransform(SceneUuid token, SceneUuid object, const Transform& value)
    {
        Buffer in; in.Uuid(token); in.Put<std::uint32_t>(2); in.Uuid(object); in.TransformValue(value); (void)Call(37, in.Bytes);
    }
    void ManagedSceneClient::CommitInteraction(SceneUuid token) { Buffer in; in.Uuid(token); (void)Call(38, in.Bytes); }
    void ManagedSceneClient::CancelInteraction(SceneUuid token) { Buffer in; in.Uuid(token); (void)Call(39, in.Bytes); }
    bool ManagedSceneClient::UndoEditor(bool redo, std::string& error)
    {
        try { Buffer in; in.Put<std::uint8_t>(redo); (void)Call(35, in.Bytes); error.clear(); return true; }
        catch (const std::exception& e) { error = e.what(); return false; }
    }
    void ManagedSceneClient::FreezeEditing(bool frozen) { Buffer in; in.Put<std::uint8_t>(frozen); (void)Call(40, in.Bytes); }
    std::string ManagedSceneClient::ConfigureMcp(bool enabled, const std::filesystem::path& projectRoot)
    {
        Buffer input; input.Put<std::uint8_t>(enabled); const auto path = projectRoot.u8string(); input.Text(std::string(path.begin(), path.end()));
        Buffer output{Call(50, input.Bytes)}; return output.Text();
    }
    McpEndpointState ManagedSceneClient::PumpMcp()
    {
        Buffer output{Call(51)}; McpEndpointState state; state.Enabled = output.Get<std::uint8_t>() != 0;
        if (!state.Enabled) return state;
        state.InstanceId = output.Uuid(); state.DocumentGeneration = output.Get<std::uint64_t>(); state.DescriptorPath = output.Text();
        state.QueueCount = output.Get<std::uint32_t>(); const auto count = output.Get<std::uint32_t>();
        if (count > 4) throw std::runtime_error("Invalid MCP connection count");
        for (std::uint32_t i = 0; i < count; ++i)
        {
            McpConnection value; value.Id = output.Uuid(); value.Name = output.Text(); value.Paired = output.Get<std::uint8_t>() != 0;
            value.Connected = output.Get<std::uint8_t>() != 0; value.Pending = output.Get<std::uint32_t>(); state.Connections.push_back(std::move(value));
        }
        const auto proposalCount = output.Get<std::uint32_t>();
        if (proposalCount > 16) throw std::runtime_error("Invalid MCP proposal count");
        for (std::uint32_t i = 0; i < proposalCount; ++i)
        {
            McpProposal value; value.Id = output.Uuid(); value.ConnectionId = output.Uuid(); value.Capability = output.Text(); value.Risk = output.Text();
            value.Revision = output.Get<std::uint64_t>(); value.Summary = output.Text(); value.Destructive = output.Get<std::uint8_t>() != 0;
            if (value.Destructive) value.DeleteTarget = output.Uuid();
            state.Proposals.push_back(std::move(value));
        }
        state.GrantSummary = output.Text(); state.AuditSummary = output.Text();
        return state;
    }
    void ManagedSceneClient::PairMcp(SceneUuid connection, bool approve) { Buffer input; input.Uuid(connection); input.Put<std::uint8_t>(approve); (void)Call(52, input.Bytes); }
    void ManagedSceneClient::RevokeMcp(SceneUuid connection) { Buffer input; input.Uuid(connection); (void)Call(53, input.Bytes); }
    void ManagedSceneClient::ApproveMcp(SceneUuid proposal, bool history, const SceneUuid* confirmedDelete)
    {
        Buffer input; input.Uuid(proposal); input.Put<std::uint8_t>(history); input.Put<std::uint8_t>(confirmedDelete != nullptr);
        if (confirmedDelete) input.Uuid(*confirmedDelete);
        (void)Call(54, input.Bytes);
    }
    void ManagedSceneClient::RevokeMcpGrants(SceneUuid connection) { Buffer input; input.Uuid(connection); (void)Call(55, input.Bytes); }
    std::string ManagedSceneClient::InvokeReadOnlyCapability(std::string request) const { Buffer in; in.Text(request); Buffer out{Call(31, in.Bytes)}; return out.Text(); }
}
