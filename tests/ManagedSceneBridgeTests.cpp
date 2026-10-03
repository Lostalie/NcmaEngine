#include "scene/ManagedSceneClient.h"
#include "script/runtime/ManagedHost.h"
#include <Windows.h>
#include <algorithm>
#include <array>
#include <cassert>
#include <future>
#include <filesystem>
#include <stdexcept>

void TestManagedSceneBridge()
{
    using namespace NcmaEngine;
    ManagedSceneClient scene("Bridge regression");
    const auto a = scene.CreateObject("A"), b = scene.CreateObject("B");
    const auto uuid = scene.GetPersistentId(a);
    Transform value; value.Position = {1, 2, 3}; scene.SetLocalTransform(a, value);
    const auto copied = scene.GetLocalTransform(a);
    Transform changed = copied; changed.Position.x() = 99; // Read result is never writable scene memory.
    assert(scene.GetLocalTransform(a).Position.x() == 1);
    const auto empty = scene.CreateObject("Empty", false);
    const auto emptyUuid = scene.GetPersistentId(empty);
    const auto snapshot = scene.CaptureDocument();
    std::string error;
    assert(scene.RestoreDocument(snapshot, error));
    const auto current = scene.FindObject(uuid);
    assert(current != a && !scene.Contains(a) && !scene.Contains(b));
    assert(!scene.HasTransform(scene.FindObject(emptyUuid)));
    const auto before = scene.CaptureDocument();
    auto invalid = before; invalid.Bytes.pop_back();
    assert(!scene.RestoreDocument(invalid, error));
    assert(scene.CaptureDocument() == before);

    using Exchange = int(__cdecl*)(std::uint64_t, int, const std::byte*, int, std::byte*, int, char*, int);
    const auto exchange = reinterpret_cast<Exchange>(Scripting::ManagedHost::Resolve(L"SceneCall"));
    std::vector<std::byte> output(4 * 1024 * 1024, std::byte{0x55});
    std::array<char, 2048> message{};
    const auto reject = [&](std::uint64_t handle, int operation, const std::byte* input, int count, int outputCount) {
        assert(exchange(handle, operation, input, count, output.data(), outputCount, message.data(), static_cast<int>(message.size())) < 0);
        assert(message[0] && output[0] == std::byte{0x55});
        assert(scene.CaptureDocument() == before);
    };
    reject(scene.Handle(), 2, nullptr, -1, static_cast<int>(output.size()));
    reject(scene.Handle(), 2, nullptr, 1, static_cast<int>(output.size()));
    reject(scene.Handle(), 2, nullptr, 0, 1);
    reject(scene.Handle(), 4, nullptr, 0, static_cast<int>(output.size()));
    reject(scene.Handle(), 14, nullptr, 0, static_cast<int>(output.size()));
    reject(scene.Handle(), 16, nullptr, 0, static_cast<int>(output.size()));
    reject(scene.Handle(), 17, nullptr, 0, static_cast<int>(output.size()));
    reject(scene.Handle(), 18, nullptr, 0, static_cast<int>(output.size()));
    reject(scene.Handle(), 900, nullptr, 0, static_cast<int>(output.size()));
    reject(0, 5, nullptr, 0, static_cast<int>(output.size()));
    std::array<std::byte, 4> truncated{}; truncated[0] = std::byte{8};
    reject(scene.Handle(), 2, truncated.data(), 4, static_cast<int>(output.size()));
    auto crossThread = std::async(std::launch::async, [&] {
        std::array<char, 2048> threadError{};
        return exchange(scene.Handle(), 5, nullptr, 0, output.data(), static_cast<int>(output.size()), threadError.data(), static_cast<int>(threadError.size()));
    });
    assert(crossThread.get() < 0);
    std::uint64_t released;
    { ManagedSceneClient temporary; released = temporary.Handle(); }
    reject(released, 5, nullptr, 0, static_cast<int>(output.size()));
    using Version = std::uint32_t(__cdecl*)();
    const auto version = reinterpret_cast<Version>(Scripting::ManagedHost::Resolve(L"GetSceneBridgeVersion"));
    assert(version() == 6);
    // Complete opaque document snapshots drive history and play clones, not UI projections.
    {
        ManagedSceneClient document("Complete document");
        const auto hero = document.CreateObject("Hero");
        const auto heroId = document.GetPersistentId(hero);
        const auto revision = document.GetDocumentRevision();
        BehaviourBinding binding{SceneUuid::New(), "Missing.Behaviour", false, {
            {"Float", ExportKind::Float, 0.25}, {"Double", ExportKind::Double, 1e100},
            {"Integer", ExportKind::Integer, 23}, {"Boolean", ExportKind::Boolean, 1}}};
        document.AddBehaviour(hero, binding);
        assert(document.GetDocumentRevision() == revision + 1);
        const auto full = document.CaptureDocument();
        assert(!full.Bytes.empty());
        document.EnableEditing(); document.SelectEditorObject(heroId);
        document.RenameEditorObject(heroId, "Changed");
        assert(document.UndoEditor(false, error));
        assert(document.GetObjectName(document.FindObject(heroId)) == "Hero");
        const auto bindings = document.GetBehaviours(document.FindObject(heroId));
        assert(bindings.size() == 1 && bindings[0].Id == binding.Id && !bindings[0].Enabled && bindings[0].Properties.size() == 4);
        assert(!document.Contains(hero) && document.GetEditorState().Selection == heroId);
        assert(document.CaptureDocument() == full);
        assert(document.UndoEditor(true, error));
        ManagedSceneClient play;
        assert(play.RestoreDocument(document.CaptureDocument(), error));
        play.SetObjectName(play.FindObject(heroId), "Play");
        assert(document.GetObjectName(document.FindObject(heroId)) == "Changed");
        const auto safe = document.CaptureDocument();
        auto bad = safe; bad.Bytes.pop_back();
        const auto rev = document.GetDocumentRevision();
        assert(!document.RestoreDocument(bad, error) && document.GetDocumentRevision() == rev && document.CaptureDocument() == safe);
        assert(!document.RestoreDocument(full, error));
        assert(document.CaptureDocument() == safe);
    }
    {
        ManagedSceneClient editor("Editor commands"); const auto initial = editor.CreateObject("Hero");
        const auto id = editor.GetPersistentId(initial); editor.EnableEditing(); editor.SelectEditorObject(id);
        const auto editBefore = editor.CaptureDocument(); const auto revision = editor.GetDocumentRevision();
        const auto draft = editor.BeginInteraction(id, "Drag");
        for (int i = 0; i < 10; ++i) { Transform t; t.Position.x() = static_cast<float>(i); editor.PreviewTransform(draft, id, t); }
        assert(editor.CaptureDocument() == editBefore && editor.GetDocumentRevision() == revision && editor.GetEditorState().UndoCount == 0);
        editor.CommitInteraction(draft); assert(editor.GetEditorState().UndoCount == 1);
        assert(editor.GetLocalTransform(editor.FindObject(id)).Position.x() == 9);
        assert(editor.UndoEditor(false, error) && editor.CaptureDocument() == editBefore);
        assert(editor.UndoEditor(true, error));
        const auto cancel = editor.BeginInteraction(id, "Cancel"); editor.PreviewName(cancel, id, "Discard"); editor.CancelInteraction(cancel);
        assert(editor.GetObjectName(editor.FindObject(id)) == "Hero" && editor.GetEditorState().UndoCount == 1);
        bool denied = false; try { editor.SetObjectName(editor.FindObject(id), "Bypass"); } catch (const std::exception&) { denied = true; }
        assert(denied && !editor.RestoreDocument(editBefore, error));
        editor.FreezeEditing(true); denied = false;
        try { (void)editor.CreateEditorObject("Frozen"); } catch (const std::exception&) { denied = true; }
        assert(denied && !editor.UndoEditor(false, error)); editor.FreezeEditing(false);
        const auto created = editor.CreateEditorObject("Logic", false);
        assert(!editor.HasTransform(created) && editor.GetEditorState().UndoCount == 2);
        const auto createdId = editor.GetPersistentId(created);
        BehaviourBinding binding{SceneUuid::New(), "Missing.Type", false, {{"Speed", ExportKind::Double, 3}}};
        editor.SetEditorBindings(id, {binding}, "Attach Behaviour");
        const auto exportBefore = editor.CaptureDocument();
        const auto exportDraft = editor.BeginInteraction(id, "Export drag");
        for (int i = 0; i < 10; ++i) { binding.Properties[0].Value = i; editor.PreviewBindings(exportDraft, id, {binding}); }
        assert(editor.CaptureDocument() == exportBefore); editor.CommitInteraction(exportDraft);
        assert(editor.GetEditorState().UndoCount == 4 && editor.GetBehaviours(editor.FindObject(id))[0].Properties[0].Value == 9);
        assert(editor.UndoEditor(false, error) && editor.CaptureDocument() == exportBefore);
        assert(editor.UndoEditor(true, error));
        editor.SelectEditorObject(id);
        const auto deleteBefore = editor.CaptureDocument(); editor.DeleteEditorObject(id);
        assert(!editor.GetEditorState().Selection && editor.Contains(editor.FindObject(createdId)));
        assert(editor.UndoEditor(false, error) && editor.CaptureDocument() == deleteBefore && editor.GetEditorState().Selection == id);
        editor.RemoveEditorComponent(id, "ncma.transform");
        assert(!editor.HasTransform(editor.FindObject(id)) && editor.UndoEditor(false, error));
        const auto path = std::filesystem::current_path().parent_path() / "tests" / "editor-bridge" / (SceneUuid::New().ToString() + ".ncmascene");
        editor.SaveEditorDocument(path); const auto saved = editor.CaptureDocument(); const auto state = editor.GetEditorState();
        assert(!state.Dirty && !state.FilePath.empty()); editor.NewEditorDocument();
        assert(editor.Size() == 0 && editor.GetEditorState().Dirty && editor.GetEditorState().FilePath.empty());
        assert(editor.UndoEditor(false, error) && editor.CaptureDocument() == saved && editor.GetEditorState().FilePath == state.FilePath && !editor.GetEditorState().Dirty);
        editor.OpenEditorDocument(path); assert(!editor.GetEditorState().Dirty);
        const auto safe = editor.CaptureDocument(); const auto historyCount = editor.GetEditorState().UndoCount;
        bool fileDenied = false; try { editor.OpenEditorDocument(path.parent_path() / "missing.ncmascene"); } catch (const std::exception&) { fileDenied = true; }
        assert(fileDenied && editor.CaptureDocument() == safe && editor.GetEditorState().UndoCount == historyCount);
        editor.FreezeEditing(true); fileDenied = false; try { editor.SaveEditorDocument(path); } catch (const std::exception&) { fileDenied = true; }
        assert(fileDenied && editor.CaptureDocument() == safe); editor.FreezeEditing(false);
    }
    // A native plugin must not silently bring back SceneWorld exports.
    HMODULE native = LoadLibraryW((std::filesystem::current_path() / "NcmaNative.dll").c_str());
    assert(native);
    assert(!GetProcAddress(native, "ncma_world_create") && !GetProcAddress(native, "ncma_get_game_object_api_version"));
    (void)FreeLibrary(native);
}
