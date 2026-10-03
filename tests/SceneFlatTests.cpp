#include "scene/SceneCommandStack.h"
#include "scene/SceneSerializer.h"

#include <cassert>
#include <cmath>
#include <fstream>
#include <iomanip>
#include <limits>
#include <sstream>

namespace
{
    using namespace NcmaEngine;

    std::string LegacyFile(const SceneSnapshot& scene, std::uint32_t version,
        const std::vector<std::string>& parents)
    {
        std::ostringstream output;
        output << "NCMA_SCENE " << version << "\nname " << std::quoted(scene.Name)
            << (version < 4 ? "\nnodes " : "\nobjects ") << scene.Objects.size() << '\n';
        output << std::setprecision(std::numeric_limits<float>::max_digits10);
        for (std::size_t index = 0; index < scene.Objects.size(); ++index)
        {
            const auto& object = scene.Objects[index];
            const auto& t = object.LocalTransform;
            output << (version < 4 ? "node " : "object ") << std::quoted(object.PersistentId.ToString()) << ' ';
            if (version < 4) output << std::quoted(parents[index]) << ' ';
            output << std::quoted(object.Name) << ' ' << t.Position.x() << ' ' << t.Position.y() << ' ' << t.Position.z() << ' '
                << t.Rotation.x() << ' ' << t.Rotation.y() << ' ' << t.Rotation.z() << ' ' << t.Rotation.w() << ' '
                << t.Scale.x() << ' ' << t.Scale.y() << ' ' << t.Scale.z() << '\n';
            if (version < 2)
                continue;
            output << "behaviours " << object.Behaviours.size() << '\n';
            for (const auto& binding : object.Behaviours)
            {
                output << "behaviour " << std::quoted(binding.Id.ToString()) << ' ' << std::quoted(binding.TypeName);
                if (version >= 3)
                    output << ' ' << static_cast<std::uint32_t>(binding.Language);
                output << ' ' << binding.Enabled << ' ' << binding.Properties.size() << '\n';
                for (const auto& property : binding.Properties)
                    output << "property " << std::quoted(property.Name) << ' '
                        << static_cast<std::uint32_t>(property.Kind) << ' ' << property.Value << '\n';
            }
        }
        return output.str();
    }

    std::string ReadFile(const std::filesystem::path& path)
    {
        std::ifstream input(path, std::ios::binary);
        assert(input);
        std::ostringstream output;
        output << input.rdbuf();
        return output.str();
    }
}

void TestFlatSceneMigration()
{
    using namespace NcmaEngine;
    SceneSnapshot legacy;
    legacy.Name = "Legacy hierarchy";
    SceneObjectSnapshot root, child, grandchild;
    root.PersistentId = *SceneUuid::Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
    child.PersistentId = SceneUuid::New();
    grandchild.PersistentId = SceneUuid::New();
    root.Name = child.Name = grandchild.Name = "Same name";
    root.LocalTransform.Position = {10, 20, 30};
    root.LocalTransform.Rotation = Quaternion(Eigen::AngleAxisf(1.1F, Vector3::UnitZ()));
    root.LocalTransform.Scale = {2, 3, -4};
    child.LocalTransform.Position = {1, 2, 3};
    child.LocalTransform.Rotation = Quaternion(Eigen::AngleAxisf(0.6F, Vector3::UnitY()));
    child.LocalTransform.Scale = {0.5F, 2, 1};
    grandchild.LocalTransform.Position = {-2, 1, 3};
    grandchild.LocalTransform.Rotation = Quaternion(Eigen::AngleAxisf(-0.4F, Vector3::UnitX()));
    grandchild.Behaviours.push_back({SceneUuid::New(), "Sample.Behaviour", false,
        {{"Speed", ExportKind::Double, 42.5}}});
    // Child records precede parents; file order survives flattening.
    legacy.Objects = {grandchild, root, child};
    const std::vector<std::string> parents = {child.PersistentId.ToString(), "",
        "AAAAAAAA-AAAA-4AAA-8AAA-AAAAAAAAAAAA"};
    const Transform expectedChild = Transform::Combine(root.LocalTransform, child.LocalTransform);
    const Transform expectedGrandchild = Transform::Combine(expectedChild, grandchild.LocalTransform);
    SceneSnapshot migrated;
    std::string error;
    for (const std::uint32_t version : {1U, 2U, 3U, 4U})
    {
        auto source = legacy;
        if (version == 4)
        {
            source.Objects[0].LocalTransform = expectedGrandchild;
            source.Objects[2].LocalTransform = expectedChild;
        }
        const auto input = LegacyFile(source, version, parents);
        assert(SceneSerializer::Deserialize(input, migrated, error));
        assert(migrated.Version == 5 && migrated.Objects.size() == 3);
        assert(migrated.Objects[0].PersistentId == grandchild.PersistentId);
        assert(migrated.Objects[1].PersistentId == root.PersistentId);
        assert(migrated.Objects[2].PersistentId == child.PersistentId);
        assert(migrated.Objects[0].LocalTransform.ToMatrix().isApprox(expectedGrandchild.ToMatrix(), 0.00001F));
        assert(migrated.Objects[2].LocalTransform.ToMatrix().isApprox(expectedChild.ToMatrix(), 0.00001F));
        if (version == 1)
            assert(migrated.Objects[0].Behaviours.empty());
        else
        {
            auto expectedBinding = grandchild.Behaviours.front();
            if (version == 2)
                expectedBinding.Language = BehaviourLanguage::CSharp;
            assert(migrated.Objects[0].Behaviours.front() == expectedBinding);
        }
        assert(migrated.Objects[0].LogicLanguage == BehaviourLanguage::CSharp);
        assert(migrated.Objects[1].LogicLanguage == BehaviourLanguage::CSharp);
        const auto flat = SceneSerializer::Serialize(migrated);
        assert(flat.starts_with("NCMA_SCENE 5\n"));
        assert(flat.find("\nobjects 3\n") != std::string::npos && flat.find("\nnode ") == std::string::npos);
        SceneSnapshot roundtrip;
        assert(SceneSerializer::Deserialize(flat, roundtrip, error));
        assert(SceneSerializer::Serialize(roundtrip) == flat);
    }
    const auto before = SceneSerializer::Serialize(migrated);
    auto mixed = legacy;
    mixed.Objects.front().Behaviours.front().Language = static_cast<BehaviourLanguage>(1);
    auto csharpBinding = grandchild.Behaviours.front();
    csharpBinding.Id = SceneUuid::New();
    csharpBinding.Language = BehaviourLanguage::CSharp;
    mixed.Objects.front().Behaviours.push_back(csharpBinding);
    for (const auto version : {3U, 4U})
    {
        assert(!SceneSerializer::Deserialize(LegacyFile(mixed, version, parents), migrated, error));
        assert(error.find("Python gameplay has been removed") != std::string::npos);
        assert(SceneSerializer::Serialize(migrated) == before);
    }
    auto invalidParents = parents;
    invalidParents[1] = grandchild.PersistentId.ToString();
    assert(!SceneSerializer::Deserialize(LegacyFile(legacy, 3, invalidParents), migrated, error));
    assert(error.find("cycle") != std::string::npos && SceneSerializer::Serialize(migrated) == before);
    invalidParents[1] = SceneUuid::New().ToString();
    assert(!SceneSerializer::Deserialize(LegacyFile(legacy, 3, invalidParents), migrated, error));
    assert(error.find("missing parent") != std::string::npos && SceneSerializer::Serialize(migrated) == before);
    auto duplicate = legacy;
    duplicate.Objects[0].PersistentId = duplicate.Objects[1].PersistentId;
    assert(!SceneSerializer::Deserialize(LegacyFile(duplicate, 3, parents), migrated, error));
    assert(SceneSerializer::Serialize(migrated) == before);
    auto overflow = legacy;
    overflow.Objects[1].LocalTransform.Scale.x() = 1e30F;
    overflow.Objects[2].LocalTransform.Scale.x() = 1e30F;
    assert(!SceneSerializer::Deserialize(LegacyFile(overflow, 3, parents), migrated, error));
    assert(SceneSerializer::Serialize(migrated) == before);
    auto invalidFlat = before;
    invalidFlat.replace(0, 12, "NCMA_SCENE 3");
    assert(!SceneSerializer::Deserialize(invalidFlat, migrated, error));
    assert(SceneSerializer::Serialize(migrated) == before);

    SceneWorld world;
    assert(world.RestoreSnapshot(migrated, error));
    const auto doomed = world.FindObject(root.PersistentId);
    SceneCommandStack history;
    const SceneCommandStack::State beforeDelete{world.CaptureSnapshot(), root.PersistentId};
    assert(world.DestroyObject(doomed));
    history.Push("Delete single object", beforeDelete, {world.CaptureSnapshot(), std::nullopt});
    assert(world.Size() == 2 && world.Contains(world.FindObject(grandchild.PersistentId)));
    assert(world.GetWorldTransform(world.FindObject(child.PersistentId)).ToMatrix().isApprox(expectedChild.ToMatrix()));
    std::optional<SceneUuid> selection;
    assert(history.Undo(world, selection, error));
    assert(world.Size() == 3 && selection == root.PersistentId);
    assert(history.Redo(world, selection, error));
    assert(world.Size() == 2 && !selection.has_value());

    const auto directory = std::filesystem::current_path() / "out" / "tests" / ("flat-" + SceneUuid::New().ToString());
    std::filesystem::create_directories(directory);
    const auto path = directory / "legacy.ncscene";
    const auto original = LegacyFile(legacy, 3, parents);
    { std::ofstream file(path, std::ios::binary); file << original; assert(file); }
    assert(SceneSerializer::Load(path, migrated, error));
    assert(ReadFile(path) == original); // Read-only load.
    assert(SceneSerializer::Save(path, migrated, error));
    assert(ReadFile(path).starts_with("NCMA_SCENE 5\n"));
    std::size_t backups = 0;
    for (const auto& entry : std::filesystem::directory_iterator(directory))
        if (entry.path().extension() == ".bak")
        {
            ++backups;
            assert(ReadFile(entry.path()) == original);
        }
    assert(backups == 1);
    assert(SceneSerializer::Load(path, migrated, error));
    assert(SceneSerializer::Save(path, migrated, error));
    backups = 0;
    for (const auto& entry : std::filesystem::directory_iterator(directory))
        if (entry.path().extension() == ".bak")
            ++backups;
    assert(backups == 1); // Current-format saves do not keep generating migration backups.

    const auto v4Path = directory / "flat-v4.ncscene";
    const auto v4Original = LegacyFile(legacy, 4, parents);
    { std::ofstream file(v4Path, std::ios::binary); file << v4Original; assert(file); }
    assert(SceneSerializer::Load(v4Path, migrated, error));
    assert(ReadFile(v4Path) == v4Original);
    assert(SceneSerializer::Save(v4Path, migrated, error));
    bool foundV4Backup = false;
    for (const auto& entry : std::filesystem::directory_iterator(directory))
        if (entry.path().filename().string().find("flat-v4.ncscene.v4.") == 0)
        {
            foundV4Backup = true;
            assert(ReadFile(entry.path()) == v4Original);
        }
    assert(foundV4Backup);
}

void TestGameObjectLogicLanguages()
{
    using namespace NcmaEngine;
    SceneWorld world;
    const auto object = world.CreateObject("C#");
    assert(world.GetLogicLanguage(object) == BehaviourLanguage::CSharp);
    BehaviourBinding binding{SceneUuid::New(), "Sample.Behaviour", true, {}};
    world.AddBehaviour(object, binding);
    const auto snapshot = world.CaptureSnapshot();
    const auto before = SceneSerializer::Serialize(snapshot);
    std::string error;
    SceneSnapshot decoded;
    assert(SceneSerializer::Deserialize(before, decoded, error));
    const auto decodedBefore = SceneSerializer::Serialize(decoded);
    for (const auto language : {1U, 99U})
    {
        auto invalid = binding;
        invalid.Language = static_cast<BehaviourLanguage>(language);
        bool rejected = false;
        try { world.UpdateBehaviour(object, invalid); }
        catch (const std::invalid_argument&) { rejected = true; }
        assert(rejected && world.GetBehaviours(object).front() == binding);
        rejected = false;
        invalid.Id = SceneUuid::New();
        try { world.AddBehaviour(object, invalid); }
        catch (const std::invalid_argument&) { rejected = true; }
        assert(rejected && world.GetBehaviours(object).size() == 1);
        rejected = false;
        try { world.SetBehaviours(object, {invalid}); }
        catch (const std::invalid_argument&) { rejected = true; }
        assert(rejected && SceneSerializer::Serialize(world.CaptureSnapshot()) == before);
        auto malformed = snapshot;
        malformed.Objects[0].LogicLanguage = static_cast<BehaviourLanguage>(language);
        malformed.Objects[0].Behaviours.clear(); // Even empty Python objects must be rejected.
        assert(!world.RestoreSnapshot(malformed, error));
        assert(SceneSerializer::Serialize(world.CaptureSnapshot()) == before);
        assert(!SceneSerializer::Deserialize(SceneSerializer::Serialize(malformed), decoded, error));
        assert(SceneSerializer::Serialize(decoded) == decodedBefore);
        malformed = snapshot;
        malformed.Objects[0].Behaviours[0].Language = static_cast<BehaviourLanguage>(language);
        malformed.Objects[0].Behaviours[0].Enabled = false;
        assert(!world.RestoreSnapshot(malformed, error));
        assert(!SceneSerializer::Deserialize(SceneSerializer::Serialize(malformed), decoded, error));
        assert(SceneSerializer::Serialize(decoded) == decodedBefore);
        for (const auto version : {3U, 4U})
            assert(!SceneSerializer::Deserialize(LegacyFile(malformed, version, {""}), decoded, error));
        const auto path = std::filesystem::current_path() / "out" / "tests" / ("reject-" + SceneUuid::New().ToString() + ".ncscene");
        std::filesystem::create_directories(path.parent_path());
        { std::ofstream file(path, std::ios::binary); file << before; assert(file); }
        assert(!SceneSerializer::Save(path, malformed, error));
        assert(ReadFile(path) == before);
        const auto legacy = LegacyFile(malformed, 3, {""});
        { std::ofstream file(path, std::ios::binary); file << legacy; assert(file); }
        assert(!SceneSerializer::Load(path, decoded, error));
        assert(ReadFile(path) == legacy && SceneSerializer::Serialize(decoded) == decodedBefore);
    }
    SceneCommandStack history;
    const auto beforeCreate = world.CaptureSnapshot();
    const auto added = world.CreateObject("Empty C#");
    const auto uuid = world.GetPersistentId(added);
    history.Push("Create object", {beforeCreate, std::nullopt}, {world.CaptureSnapshot(), uuid});
    std::optional<SceneUuid> selection;
    assert(history.Undo(world, selection, error));
    assert(world.FindObject(uuid) == InvalidGameObjectId);
    assert(history.Redo(world, selection, error));
    assert(world.GetLogicLanguage(world.FindObject(uuid)) == BehaviourLanguage::CSharp);
    assert(SceneSerializer::Deserialize(SceneSerializer::Serialize(world.CaptureSnapshot()), decoded, error));
    SceneWorld restored;
    assert(restored.RestoreSnapshot(decoded, error));
    assert(restored.GetLogicLanguage(restored.FindObject(uuid)) == BehaviourLanguage::CSharp);
}
