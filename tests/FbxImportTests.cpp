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
        Check(ncma_character_abi_version() == 2, "Character ABI version mismatch");
        const auto identity = options.AssetId.ToString();
        const auto handle = ncma_character_import(2, filename.c_str(), 30, identity.c_str());
        Check(handle != 0, "Native immutable FBX import failed");
        std::uint32_t required = 0;
        Check(ncma_character_read_report(handle, nullptr, 0, &required) == 2 && required > 1, "Report size query failed");
        std::vector<char> report(required, '#');
        auto needed = required;
        Check(ncma_character_read_report(handle, report.data(), required - 1, &needed) == 2 &&
            report.front() == '#', "Short report buffer modified");
        Check(ncma_character_read_report(handle, report.data(), required, &needed) == 1 &&
            report.back() == '\0' && std::string(report.data()) == character->InspectJson(), "Caller-owned report copy mismatch");
        Check(ncma_character_read_report(handle, nullptr, 1, &needed) == 0, "Null output/nonzero capacity accepted");
        Check(ncma_character_sample(handle, 1, localTime, nullptr, 0, &needed) == 2, "Numerical size query failed");
        std::vector<float> sampled(needed, -123.0F);
        required = needed;
        Check(ncma_character_sample(handle, 1, localTime, sampled.data(), required - 1, &needed) == 2 &&
            sampled.front() == -123.0F, "Short numerical buffer modified");
        Check(ncma_character_sample(handle, 1, localTime, sampled.data(), required, &needed) == 1, "Numerical sample failed");
        const auto bones = character->Animations->GetSkeleton().Bones.size();
        std::vector<Transform> pose(bones);
        std::vector<Matrix4> model(bones), skin(bones);
        character->Animations->Sample(1, localTime, pose);
        character->Animations->BuildMatrices(pose, model, skin);
        std::size_t offset = 0;
        for (const auto& matrix : model)
            for (std::size_t lane = 0; lane < 16; ++lane)
                Check(std::abs(sampled[offset++] - matrix.data()[lane]) < 0.00001F, "ABI model matrix mismatch");
        for (std::size_t meshIndex = 0; meshIndex < character->Meshes.size(); ++meshIndex)
        {
            const auto& mesh = character->Meshes[meshIndex];
            std::vector<Vector3> positions(mesh.Vertices.size());
            SkinPositions(mesh, model, positions);
            for (const auto& position : positions)
                for (std::size_t lane = 0; lane < 3; ++lane)
                    Check(std::abs(sampled[offset++] - position.data()[lane]) < 0.00001F, "ABI CPU skin mismatch");
            Check(ncma_character_read_indices(handle, static_cast<std::uint32_t>(meshIndex), nullptr, 0, &needed) == 2, "Index query failed");
            std::vector<std::uint32_t> indices(needed);
            Check(ncma_character_read_indices(handle, static_cast<std::uint32_t>(meshIndex), indices.data(), needed, &required) == 1 &&
                indices == mesh.Indices, "Index copy mismatch");
        }
        const auto original = sampled;
        Check(ncma_character_sample(handle, 9999, 0, sampled.data(), static_cast<std::uint32_t>(sampled.size()), &needed) == 0 &&
            sampled == original, "Invalid clip changed destination");
        Check(ncma_character_sample(handle, 0, std::numeric_limits<double>::quiet_NaN(), nullptr, 0, &needed) == 0, "NaN sample accepted");
        Check(ncma_character_sample(handle, 0, -1, nullptr, 0, &needed) == 0, "Negative sample accepted");
        Check(ncma_character_read_indices(handle, 9999, nullptr, 0, &needed) == 0, "Invalid mesh accepted");
        Check(ncma_character_release(handle) == 1, "Native resource release failed");
        Check(ncma_character_read_report(handle, nullptr, 0, &needed) == 0 && needed == 0, "Released generation accepted");
        Check(ncma_character_release(handle) == 0, "Double release accepted");
        Check(std::string(report.data()) == character->InspectJson(), "Caller-owned report invalidated by release");
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
        Check(ncma_character_import(1, Utf8(valid).c_str(), 30, nullptr) == 0, "Old character ABI accepted");
        Check(ncma_character_import(99, Utf8(valid).c_str(), 30, nullptr) == 0, "Unknown character ABI accepted");
        Check(ncma_character_import(2, Utf8(valid).c_str(), 30, "invalid") == 0, "Invalid persistent UUID accepted");
        Check(ncma_character_import(2, nullptr, 30, nullptr) == 0, "Null import path accepted");
        Check(ncma_character_inspect_fbx(1, Utf8(valid).c_str(), 30) == nullptr, "Legacy inspector must reject ABI 1");
        const auto immutable = ncma_character_import(2, Utf8(unicode).c_str(), 30, nullptr);
        Check(immutable != 0, "Unicode ABI import failed");
        { std::ofstream replacement(unicode, std::ios::binary); replacement << "changed test-owned fixture"; }
        std::uint32_t count = 0;
        Check(ncma_character_read_report(immutable, nullptr, 0, &count) == 2, "Read unexpectedly reimported changed file");
        Check(ncma_character_sample(immutable, 1, 0.2, nullptr, 0, &count) == 2, "Sample unexpectedly read source file");
        Check(ncma_character_release(immutable) == 1, "Immutable release failed");
        std::vector<std::uint64_t> leases;
        for (std::size_t i = 0; i < 16; ++i)
        {
            const auto handle = ncma_character_import(2, Utf8(valid).c_str(), 30, nullptr);
            Check(handle != 0, "Resource budget rejected a valid slot");
            leases.push_back(handle);
        }
        Check(ncma_character_import(2, Utf8(valid).c_str(), 30, nullptr) == 0, "Live resource budget bypassed");
        Check(ncma_character_read_report(leases.front(), nullptr, 0, &count) == 2, "Budget failure damaged existing resource");
        for (const auto handle : leases) Check(ncma_character_release(handle) == 1, "Budget lease release failed");
        const auto next = ncma_character_import(2, Utf8(valid).c_str(), 30, nullptr);
        Check(next > leases.back(), "Resource generation reused");
        Check(ncma_character_read_report(leases.front(), nullptr, 0, &count) == 0, "Old generation alias accepted");
        Check(ncma_character_release(next) == 1, "Next generation release failed");
        Check(ncma_character_import(1, Utf8(valid).c_str(), 30, nullptr) == 0, "Old ABI accepted");
        Check(ncma_character_read_error(nullptr, 0, &count) == 2 && count > 1, "Copied error query failed");
        std::vector<char> errorCopy(count);
        Check(ncma_character_read_error(errorCopy.data(), count, &count) == 1 &&
            std::string(errorCopy.data()).find("ABI 2") != std::string::npos, "Caller-owned ABI error missing");
        Check(ncma_character_inspect_fbx(1, nullptr, 30) == nullptr, "Null source path accepted");
        std::cout << "FBX: ASCII/binary, skeleton, topology, weights, reference skinning, animation, UUIDs, Unicode and rejection checks passed\n";
        return 0;
    }
    catch (const std::exception& error) { std::cerr << error.what() << '\n'; return 1; }
}
