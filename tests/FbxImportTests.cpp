#include "assets/FbxCharacterImporter.h"
#include "interop/NcmaCharacterApi.h"
#include "ufbx.h"

#include <fstream>
#include <iostream>
#include <limits>

namespace
{
    using namespace NcmaEngine;
    using namespace NcmaEngine::Assets;
    void Check(bool condition, const char* message) { if (!condition) throw std::runtime_error(message); }
    template<class F> void Reject(F&& action)
    {
        bool failed = false;
        try { action(); } catch (const std::exception&) { failed = true; }
        Check(failed, "Invalid FBX input was accepted");
    }
    Vector3 Vector(ufbx_vec3 p) { return {static_cast<float>(p.x), static_cast<float>(p.y), static_cast<float>(p.z)}; }
    std::string Utf8(const std::filesystem::path& path)
    {
        const auto value = path.u8string(); return {reinterpret_cast<const char*>(value.data()), value.size()};
    }

    void CompareWithReference(const ImportedCharacter& character, const ufbx_scene& source, double time, const ufbx_anim* anim)
    {
        ufbx_evaluate_opts options{};
        options.evaluate_skinning = true;
        ufbx_error error{};
        std::unique_ptr<ufbx_scene, decltype(&ufbx_free_scene)> reference(
            anim ? ufbx_evaluate_scene(&source, anim, time, &options, &error) : nullptr, &ufbx_free_scene);
        const auto& scene = reference ? *reference : source;
        Check(!anim || reference != nullptr, "Reference evaluation failed");
        Animation::AnimationPlayer player(character.Animations);
        if (anim)
        {
            const auto* stack = source.anim_stacks[0];
            player.Play(character.Animations->GetClips()[1].Name, 0);
            double remaining = time - stack->time_begin;
            while (remaining > 0) { const double step = std::min(remaining, 0.5); player.Advance(step); remaining -= step; }
        }
        for (const auto& mesh : character.Meshes)
        {
            const ufbx_node* sourceNode = nullptr;
            for (const auto* node : scene.nodes)
                if (std::string(node->name.data, node->name.length) == mesh.Name && node->mesh) sourceNode = node;
            Check(sourceNode != nullptr, "Reference mesh not found");
            const auto* sourceMesh = sourceNode->mesh;
            Check(mesh.Vertices.size() == sourceMesh->num_indices, "Vertex corner mapping mismatch");
            std::vector<Vector3> positions(mesh.Vertices.size());
            SkinPositions(mesh, player.ModelMatrices(), positions);
            for (std::size_t corner = 0; corner < positions.size(); ++corner)
            {
                auto expected = ufbx_get_vertex_vec3(&sourceMesh->skinned_position, corner);
                if (sourceMesh->skinned_is_local) expected = ufbx_transform_position(&sourceNode->geometry_to_world, expected);
                Check((positions[corner] - Vector(expected)).norm() < 0.002F,
                    "CPU skin positions disagree with FBX reference (bind matrices/axes/weights/animation)");
            }
        }
    }

    void TestFile(const std::filesystem::path& path, bool binary)
    {
        FbxImportOptions options;
        options.AssetId = SceneUuid::New();
        const auto character = FbxCharacterImporter::Import(path, options);
        Check(character->Binary == binary && character->Meshes.size() > 0, "FBX format/mesh import failed");
        Check(character->Animations->GetSkeleton().Bones.size() >= 3, "Skeleton not imported");
        Check(character->Animations->GetClips().size() >= 2, "Animation stacks not imported");
        Check(character->BoundsMin.allFinite() && character->BoundsMax.allFinite(), "Invalid bounds");
        Check((character->BoundsMax - character->BoundsMin).norm() > 0.01F, "Model collapsed during unit conversion");
        for (const auto& mesh : character->Meshes)
        {
            Check(!mesh.Indices.empty() && mesh.Indices.size() % 3 == 0, "Mesh not triangulated");
            Check(mesh.TriangleMaterials.size() == mesh.Indices.size() / 3, "Material slots not preserved per triangle");
            for (const auto& vertex : mesh.Vertices)
            {
                float total = 0;
                for (std::size_t lane = 0; lane < 4; ++lane)
                {
                    Check(vertex.Joints[lane] < mesh.Bindings.size(), "Skin palette index is out of bounds");
                    total += vertex.Weights[lane];
                }
                Check(std::abs(total - 1) < 0.00001F, "Skin weights not normalized");
            }
            for (const auto index : mesh.Indices) Check(index < mesh.Vertices.size(), "Triangle vertex index is out of bounds");
        }
        const auto again = FbxCharacterImporter::Import(path, options);
        Check(again->Id == character->Id && again->Animations->GetSkeleton().Id == character->Animations->GetSkeleton().Id,
            "Reimport changed persistent identities");
        Check(again->Meshes[0].Id == character->Meshes[0].Id, "Mesh UUID changed during reimport");
        Check(again->Animations->GetClips()[1].Id == character->Animations->GetClips()[1].Id, "Clip UUID changed during reimport");

        ufbx_load_opts load{};
        load.target_axes = ufbx_axes_right_handed_y_up; load.target_unit_meters = 1;
        load.space_conversion = UFBX_SPACE_CONVERSION_MODIFY_GEOMETRY;
        load.geometry_transform_handling = UFBX_GEOMETRY_TRANSFORM_HANDLING_PRESERVE;
        load.inherit_mode_handling = UFBX_INHERIT_MODE_HANDLING_HELPER_NODES;
        load.generate_missing_normals = true; load.ignore_embedded = true;
        load.evaluate_skinning = true;
        const auto filename = Utf8(path);
        std::unique_ptr<ufbx_scene, decltype(&ufbx_free_scene)> source(ufbx_load_file(filename.c_str(), &load, nullptr), &ufbx_free_scene);
        Check(source != nullptr, "Reference file did not load");
        CompareWithReference(*character, *source, 0, nullptr);
        const auto* stack = source->anim_stacks[0];
        const double localTime = std::floor((stack->time_end - stack->time_begin) * 0.5 * character->SampleRate) / character->SampleRate;
        CompareWithReference(*character, *source, stack->time_begin + localTime, stack->anim);
        Animation::AnimationPlayer player(character->Animations);
        const auto initial = player.LocalPose();
        player.Play(character->Animations->GetClips()[1].Name, 0);
        player.Advance(0.25);
        bool moved = false;
        for (std::size_t i = 0; i < initial.size(); ++i)
            moved = moved || !initial[i].ToMatrix().isApprox(player.LocalPose()[i].ToMatrix(), 0.00001F);
        Check(moved, "Imported animation did not change bone poses");
        Check(ncma_character_abi_version() == 1, "Character ABI version mismatch");
        Check(ncma_character_inspect_fbx(1, filename.c_str(), 30) != nullptr, "Native FBX inspector failed");
        std::cout << character->InspectJson() << '\n';
    }
}

int main(int argc, char** argv)
{
    try
    {
        Check(argc == 3, "Expected fixture and generated-output directories");
        const std::filesystem::path fixtures(argv[1]), output(argv[2]);
        TestFile(fixtures / "blender_279_sausage_6100_ascii.fbx", false);
        TestFile(fixtures / "blender_279_sausage_7400_binary.fbx", true);
        std::filesystem::create_directories(output);
        const auto unicode = output / std::filesystem::path(u8"角色 test.FBX");
        std::filesystem::copy_file(fixtures / "blender_279_sausage_7400_binary.fbx", unicode, std::filesystem::copy_options::overwrite_existing);
        Check(FbxCharacterImporter::Import(unicode)->Binary, "Unicode/space/uppercase filename failed");
        const auto invalid = output / "invalid.fbx";
        { std::ofstream file(invalid, std::ios::binary); file << "This is not an FBX file"; }
        Reject([&] { (void)FbxCharacterImporter::Import(invalid); });
        Reject([&] { (void)FbxCharacterImporter::Import(fixtures / "missing.fbx"); });
        FbxImportOptions budget;
        budget.MaxVertices = 1;
        const auto valid = fixtures / "blender_279_sausage_7400_binary.fbx";
        Reject([&] { (void)FbxCharacterImporter::Import(valid, budget); });
        budget = {}; budget.MaxAnimationKeys = 1;
        Reject([&] { (void)FbxCharacterImporter::Import(valid, budget); });
        budget = {}; budget.SampleRate = std::numeric_limits<double>::quiet_NaN();
        Reject([&] { (void)FbxCharacterImporter::Import(valid, budget); });
        Check(ncma_character_inspect_fbx(99, Utf8(valid).c_str(), 30) == nullptr, "Unsupported character ABI accepted");
        Check(ncma_character_inspect_fbx(1, nullptr, 30) == nullptr, "Null source path accepted");
        std::cout << "FBX: ASCII/binary, skeleton, topology, weights, reference skinning, animation, UUIDs, Unicode and rejection checks passed\n";
        return 0;
    }
    catch (const std::exception& error) { std::cerr << error.what() << '\n'; return 1; }
}
