// Controlled old-bridge parity fixture, never deployed as an editor/player entry.
#include "scene/ManagedSceneClient.h"
#include <filesystem>
#include <iostream>
#include <stdexcept>
#include <string>

int main(int count, char** arguments)
{
    if (count != 2) return 2;
    try
    {
        using namespace NcmaEngine;
        const std::filesystem::path baseline(arguments[1]);
        if (!baseline.is_absolute() || baseline.extension() != ".ncmascene")
            throw std::invalid_argument("Expected an absolute test-owned .ncmascene");
        const auto id = SceneUuid::Parse("11111111-1111-1111-1111-111111111111").value();
        ManagedSceneClient scene("Parity");
        scene.EnableEditing();
        scene.OpenEditorDocument(baseline);
        scene.SelectEditorObject(id);
        std::cout << "NCMA_REFERENCE:[";
        bool first = true;
        const auto capture = [&]()
        {
            const auto bytes = scene.CaptureDocument();
            const auto state = scene.GetEditorState();
            if (!first) std::cout << ',';
            first = false;
            std::cout << "{\"document\":";
            std::cout.write(reinterpret_cast<const char*>(bytes.Bytes.data()), static_cast<std::streamsize>(bytes.Bytes.size()));
            std::cout << ",\"revision\":" << state.Revision << ",\"undo\":" << state.UndoCount
                << ",\"redo\":" << state.RedoCount << ",\"dirty\":" << (state.Dirty ? "true" : "false")
                << ",\"busy\":" << (state.EditBusy ? "true" : "false")
                << ",\"invalidated\":" << (state.HistoryInvalidated ? "true" : "false")
                << ",\"frozen\":" << (state.Frozen ? "true" : "false") << ",\"selection\":";
            if (state.Selection) std::cout << '"' << state.Selection->ToString() << '"';
            else std::cout << "null";
            std::cout << '}';
        };
        capture();
        scene.RenameEditorObject(id, "Renamed"); capture();
        const auto token = scene.BeginInteraction(id, "Discard draft");
        scene.PreviewName(token, id, "Discard me"); capture();
        scene.CancelInteraction(token); capture();
        Transform transform;
        transform.Position = Vector3(2, 3, 4);
        scene.SetEditorTransform(id, transform); capture();
        std::string error;
        const auto history = [&](bool redo) { if (!scene.UndoEditor(redo, error)) throw std::runtime_error(error); capture(); };
        history(false); history(true);
        scene.DeleteEditorObject(id); capture();
        history(false);
        auto saved = baseline;
        saved += ".saved.ncmascene";
        scene.SaveEditorDocument(saved); capture();
        scene.NewEditorDocument(); capture(); history(false);
        std::cout << "]\n";
        return 0;
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
