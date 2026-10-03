#include "scene/ManagedSceneClient.h"
#include <cassert>
#include <filesystem>
#include <fstream>
#include <iterator>
#include <stdexcept>

void TestSceneDocumentFiles()
{
    using namespace NcmaEngine;
    ManagedSceneClient source("Flat document");
    const auto spatial = source.CreateObject("Hero");
    const auto logic = source.CreateObject("Logic", false);
    const auto heroId = source.GetPersistentId(spatial), logicId = source.GetPersistentId(logic);
    BehaviourBinding binding{SceneUuid::New(), "Missing.Disabled", false, {{"Value", ExportKind::Integer, 17}}};
    source.AddBehaviour(logic, binding);
    const auto before = source.CaptureDocument();
    const auto directory = std::filesystem::current_path() / "out" / "tests" / ("document-" + SceneUuid::New().ToString());
    const auto path = directory / "Scene.ncmascene";
    std::string error;
    if (!source.SaveDocument(path, error)) throw std::runtime_error("Scene save failed: " + error);
    ManagedSceneClient clone;
    assert(clone.LoadDocument(path, error) && clone.CaptureDocument() == before);
    assert(clone.Size() == 2 && !clone.HasTransform(clone.FindObject(logicId)));
    assert(clone.GetBehaviours(clone.FindObject(logicId)) == std::vector<BehaviourBinding>{binding});
    source.SetObjectName(spatial, "Updated");
    if (!source.SaveDocument(path, error)) throw std::runtime_error("Scene save failed: " + error); // Existing supported document is atomically replaced.
    assert(clone.LoadDocument(path, error) && clone.GetObjectName(clone.FindObject(heroId)) == "Updated");
    const auto safe = clone.CaptureDocument();
    const auto revision = clone.GetDocumentRevision();
    const auto corrupt = directory / "Unsupported.ncmascene";
    { std::ofstream file(corrupt, std::ios::binary); file << "NCMA_SCENE 6\n"; }
    assert(!clone.LoadDocument(corrupt, error) && clone.CaptureDocument() == safe && clone.GetDocumentRevision() == revision);
    assert(!clone.SaveDocument(corrupt, error)); // Unsupported bytes are never overwritten.
    { std::ifstream file(corrupt, std::ios::binary); const std::string bytes{std::istreambuf_iterator<char>{file}, {}}; assert(bytes == "NCMA_SCENE 6\n"); }
    assert(!clone.SaveDocument(directory / "Unsupported.txt", error));
    assert(!std::filesystem::exists(directory / "Unsupported.txt"));
    clone.ResetDocument();
    assert(clone.Size() == 0 && clone.GetName() == "Untitled");
    assert(clone.RestoreDocument(safe, error) && clone.CaptureDocument() == safe);
}
