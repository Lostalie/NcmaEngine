#include "contracts/NcmaImport.h"
#include "assets/FbxCharacterImporter.h"
#include <array>
#include <atomic>
#include <cassert>
#include <cmath>
#include <chrono>
#include <cstring>
#include <iostream>
#include <thread>
#include <vector>

namespace
{
NcmaImportApiV1 Api{};
NcmaErrorV1 Error{};
template<class T> std::vector<T> Read(uint64_t context, uint32_t stream, uint32_t object, uint32_t sub, uint32_t total)
{
    std::vector<T> output(total);
    for (uint32_t first = 0; first < total;)
    {
        const auto count = std::min<uint32_t>(37, total - first); uint32_t written = UINT32_MAX;
        assert(Api.read_page(context, stream, object, sub, first, count, output.data() + first, static_cast<uint32_t>(count * sizeof(T)), &written, &Error) == NCMA_OK);
        assert(written == count); first += count;
    }
    return output;
}
void Fixture(const std::filesystem::path& path, bool binary)
{
    uint64_t context = 0; assert(Api.create(&context, &Error) == NCMA_OK && context != 0);
    auto utf8 = path.u8string(); NcmaImportInfoV1 info{}; info.struct_size = sizeof(info);
    assert(Api.load(context, reinterpret_cast<const uint8_t*>(utf8.data()), static_cast<uint32_t>(utf8.size()), 30, &info, &Error) == NCMA_OK);
    assert(info.binary == (binary ? 1u : 0u) && info.meshes > 0 && info.bones > 0 && info.clips > 0 && info.keys > 0);
    NcmaEngine::Assets::FbxImportOptions options; options.AssetId = {1, 1}; options.SampleRate = 30;
    auto reference = NcmaEngine::Assets::FbxCharacterImporter::Import(path, options);
    assert(reference->Meshes.size() == info.meshes);
    for (uint32_t i = 0; i < info.meshes; ++i)
    {
        NcmaImportMeshInfoV1 meshInfo{}; meshInfo.struct_size = sizeof(meshInfo);
        assert(Api.mesh_info(context, i, &meshInfo, &Error) == NCMA_OK && meshInfo.flags == 0);
        auto vertices = Read<NcmaImportVertexV1>(context, NCMA_IMPORT_VERTICES, i, 0, meshInfo.vertices);
        const auto& mesh = reference->Meshes[i];
        auto indices = Read<uint32_t>(context, NCMA_IMPORT_INDICES, i, 0, meshInfo.indices);
        auto materials = Read<uint32_t>(context, NCMA_IMPORT_TRIANGLE_MATERIALS, i, 0, meshInfo.triangles);
        auto bindings = Read<NcmaImportBindingV1>(context, NCMA_IMPORT_BINDINGS, i, 0, meshInfo.bindings);
        assert(indices == mesh.Indices && materials == mesh.TriangleMaterials);
        for (std::size_t v = 0; v < vertices.size(); ++v)
        {
            for (int lane = 0; lane < 3; ++lane) { assert(vertices[v].position[lane] == mesh.Vertices[v].Position[lane]); assert(vertices[v].normal[lane] == mesh.Vertices[v].Normal[lane]); }
            assert(vertices[v].uv[0] == mesh.Vertices[v].UV[0] && vertices[v].uv[1] == 1.0f - mesh.Vertices[v].UV[1]);
            for (int lane = 0; lane < 4; ++lane) { assert(vertices[v].joints[lane] == mesh.Vertices[v].Joints[lane]); assert(vertices[v].weights[lane] == mesh.Vertices[v].Weights[lane]); }
        }
        for (std::size_t b = 0; b < bindings.size(); ++b)
        {
            assert(bindings[b].bone == mesh.Bindings[b].BoneIndex);
            for (int lane = 0; lane < 16; ++lane) assert(bindings[b].geometry_to_bone[lane] == mesh.Bindings[b].GeometryToBone.data()[lane]);
        }
        uint32_t textBytes = 0;
        assert(Api.read_text(context, NCMA_IMPORT_MESH_NAME, i, 0, nullptr, 0, &textBytes, &Error) == NCMA_BUFFER_TOO_SMALL && textBytes > 0);
        std::vector<uint8_t> text(textBytes);
        assert(Api.read_text(context, NCMA_IMPORT_MESH_NAME, i, 0, text.data(), textBytes, &textBytes, &Error) == NCMA_OK);
        assert(std::string(text.begin(), text.end()) == mesh.Name);
    }
    auto bones = Read<NcmaImportBoneV1>(context, NCMA_IMPORT_BONES, 0, 0, info.bones);
    for (uint32_t i = 0; i < info.bones; ++i)
    {
        const auto& bone = reference->Animations->GetSkeleton().Bones[i]; assert(bones[i].parent == bone.Parent);
        for (int lane = 0; lane < 3; ++lane) { assert(bones[i].bind_local.position[lane] == bone.BindLocal.Position[lane]); assert(bones[i].bind_local.scale[lane] == bone.BindLocal.Scale[lane]); }
        for (int lane = 0; lane < 4; ++lane) assert(bones[i].bind_local.rotation_xyzw[lane] == bone.BindLocal.Rotation.coeffs()[lane]);
    }
    for (uint32_t i = 0; i < info.clips; ++i)
    {
        NcmaImportClipInfoV1 clipInfo{}; clipInfo.struct_size = sizeof(clipInfo); assert(Api.clip_info(context, i, &clipInfo, &Error) == NCMA_OK);
        auto tracks = Read<NcmaImportTrackV1>(context, NCMA_IMPORT_TRACKS, i, 0, clipInfo.tracks);
        for (uint32_t t = 0; t < tracks.size(); ++t)
        {
            auto keys = Read<NcmaImportKeyV1>(context, NCMA_IMPORT_KEYS, i, t, tracks[t].keys);
            const auto& original = reference->Animations->GetClips()[i].Tracks[t];
            assert(tracks[t].bone == original.BoneIndex && keys.size() == original.Keys.size());
            assert(keys.front().time == 0 && keys.back().time == clipInfo.duration);
            for (std::size_t k = 0; k < keys.size(); ++k)
            {
                assert(keys[k].time == original.Keys[k].Time);
                for (int lane = 0; lane < 3; ++lane) { assert(keys[k].value.position[lane] == original.Keys[k].Value.Position[lane]); assert(keys[k].value.scale[lane] == original.Keys[k].Value.Scale[lane]); }
                for (int lane = 0; lane < 4; ++lane) assert(keys[k].value.rotation_xyzw[lane] == original.Keys[k].Value.Rotation.coeffs()[lane]);
            }
        }
    }
    uint32_t written = 123; std::array<uint8_t, 56> sentinel{}; sentinel.fill(0xA5);
    assert(Api.read_page(context, NCMA_IMPORT_VERTICES, 0, 0, 0, 1, sentinel.data(), 1, &written, &Error) == NCMA_BUFFER_TOO_SMALL);
    assert(written == 0 && Error.required_bytes == sizeof(NcmaImportVertexV1) && sentinel[0] == 0xA5);
    assert(Api.read_page(context, NCMA_IMPORT_VERTICES, 0, 0, UINT32_MAX, 1, sentinel.data(), 56, &written, &Error) == NCMA_INVALID_ARGUMENT);
    assert(Api.read_page(context, NCMA_IMPORT_VERTICES, 0, 0, 0, 4097, sentinel.data(), 56, &written, &Error) == NCMA_INVALID_ARGUMENT);
    assert(Api.read_page(context, 999, 0, 0, 0, 0, nullptr, 0, &written, &Error) == NCMA_UNSUPPORTED_FEATURE);
    std::thread foreign([&] { NcmaErrorV1 local{}; NcmaImportInfoV1 other{}; other.struct_size = sizeof(other); assert(Api.info(context, &other, &local) == NCMA_WRONG_THREAD); }); foreign.join();
    NcmaImportStatusV1 status{}; status.struct_size = sizeof(status); assert(Api.status(context, &status, &Error) == NCMA_OK && status.phase == 4 && status.busy == 0);
    assert(Api.cancel(context, &Error) == NCMA_BUSY);
    assert(Api.close(context, &Error) == NCMA_OK); assert(Api.info(context, &info, &Error) == NCMA_INVALID_HANDLE);
    assert(std::isfinite(bones[0].bind_local.scale[0])); // Copied data remains valid after disposal.
}
}
int main(int argc, char** argv)
{
    assert(argc == 2);
    assert(ncma_import_get_api(2, 0, &Api, sizeof(Api), &Error) == NCMA_ABI_MISMATCH);
    assert(ncma_import_get_api(1, 2, &Api, sizeof(Api), &Error) == NCMA_ABI_MISMATCH);
    NcmaImportApiV1_1 extended{};
    assert(ncma_import_get_api(1, 1, &extended, sizeof(Api), &Error) == NCMA_BUFFER_TOO_SMALL && Error.required_bytes == sizeof(extended));
    assert(ncma_import_get_api(1, 1, &extended, sizeof(extended), &Error) == NCMA_OK && extended.base.minor == 1 && extended.load_mode);
    assert(ncma_import_get_api(1, 0, &Api, 1, &Error) == NCMA_BUFFER_TOO_SMALL && Error.required_bytes == sizeof(Api));
    assert(ncma_import_get_api(1, 0, &Api, sizeof(Api), &Error) == NCMA_OK);
    std::array<uint64_t, 4> contexts{}; for (auto& context : contexts) assert(Api.create(&context, &Error) == NCMA_OK);
    uint64_t excess = 123; assert(Api.create(&excess, &Error) == NCMA_BUSY && excess == 0);
    for (auto context : contexts) assert(Api.close(context, &Error) == NCMA_OK);
    const auto root = std::filesystem::path(argv[1]);
    Fixture(root / "tests/assets/fbx/blender_279_sausage_6100_ascii.fbx", false);
    Fixture(root / "tests/assets/fbx/blender_279_sausage_7400_binary.fbx", true);
    uint64_t context = 0; assert(Api.create(&context, &Error) == NCMA_OK);
    std::thread cancel([&] { NcmaErrorV1 local{}; assert(Api.cancel(context, &local) == NCMA_OK); }); cancel.join();
    auto path = (root / "tests/assets/fbx/blender_279_sausage_7400_binary.fbx").u8string();
    NcmaImportInfoV1 info{}; info.struct_size = sizeof(info);
    assert(Api.load(context, reinterpret_cast<const uint8_t*>(path.data()), static_cast<uint32_t>(path.size()), 30, &info, &Error) == NCMA_IMPORT_CANCELLED && info.meshes == 0);
    NcmaImportStatusV1 status{}; status.struct_size = sizeof(status); assert(Api.status(context, &status, &Error) == NCMA_OK && status.phase == 5 && status.busy == 0);
    assert(Api.close(context, &Error) == NCMA_OK);
    // Cancel each observed parser/conversion/sampling phase, not only an unstarted job.
    for (uint32_t targetPhase : {1U, 2U, 3U}) {
    std::atomic<uint64_t> parsingContext{0}; std::atomic<uint32_t> result{NCMA_INTERNAL_ERROR}; std::atomic_bool finished{false};
    std::thread worker([&] {
        NcmaErrorV1 local{}; uint64_t handle = 0; assert(Api.create(&handle, &local) == NCMA_OK); parsingContext.store(handle);
        NcmaImportInfoV1 loaded{}; loaded.struct_size = sizeof(loaded);
        result.store(Api.load(handle, reinterpret_cast<const uint8_t*>(path.data()), static_cast<uint32_t>(path.size()), 120, &loaded, &local));
        finished.store(true); while (parsingContext.load() != 0) std::this_thread::yield();
        assert(Api.close(handle, &local) == NCMA_OK);
    });
    bool requested = false;
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(10);
    while (!finished.load() && std::chrono::steady_clock::now() < deadline)
    {
        if (const auto handle = parsingContext.load())
        {
            NcmaImportStatusV1 progress{}; progress.struct_size = sizeof(progress); NcmaErrorV1 local{};
            assert(Api.status(handle, &progress, &local) == NCMA_OK);
            if (progress.phase == targetPhase) { requested = Api.cancel(handle, &local) == NCMA_OK; break; }
        }
        std::this_thread::yield();
    }
    parsingContext.store(0); worker.join(); assert(requested && result.load() == NCMA_IMPORT_CANCELLED);
    }
    std::cout << "Independent import ABI: ASCII/binary raw stream reference equality, exact keys/endpoints, pages, budgets, owner affinity, cancellation and cleanup passed.\n";
}
