#include "contracts/NcmaImport.h"
#include "PluginSupport.h"
#include "assets/FbxCharacterImporter.h"
#include <atomic>
#include <cmath>
#include <cstring>
#include <mutex>
#include <unordered_map>

namespace
{
using namespace NcmaEngine;
struct Failure final : std::exception
{
    explicit Failure(uint32_t value) : Code(value) {}
    uint32_t Code;
    const char* what() const noexcept override { return "Import context, thread, range or state rejected."; }
};
struct Context
{
    const std::thread::id Owner = std::this_thread::get_id();
    std::atomic_bool Busy{false}, Cancelled{false};
    std::atomic<uint32_t> Phase{0};
    std::atomic<uint64_t> BytesRead{0}, BytesTotal{0};
    std::mutex Decision;
    std::shared_ptr<const Assets::ImportedCharacter> Character;
};
std::mutex RegistryGate;
std::unordered_map<uint64_t, std::shared_ptr<Context>> Registry;
uint64_t NextContext = 1;
template<class F> uint32_t Run(NcmaErrorV1* error, F&& action) noexcept
{
    if (!error) return NCMA_INVALID_ARGUMENT;
    *error = {};
    try { return action(); }
    catch (const Failure& e) { return NcmaPlugin::Error(error, e.Code, e.what()); }
    catch (const Assets::FbxImportCancelled& e) { return NcmaPlugin::Error(error, NCMA_IMPORT_CANCELLED, e.what()); }
    catch (const std::exception& e) { return NcmaPlugin::Error(error, NCMA_INTERNAL_ERROR, e.what()); }
    catch (...) { return NcmaPlugin::Error(error, NCMA_INTERNAL_ERROR, "Unknown numerical import failure."); }
}
void Require(bool valid, uint32_t code = NCMA_INVALID_ARGUMENT) { if (!valid) throw Failure(code); }
std::shared_ptr<Context> Find(uint64_t id, bool owner = true, bool ready = false)
{
    std::shared_ptr<Context> result;
    { std::scoped_lock lock(RegistryGate); auto it = Registry.find(id); Require(it != Registry.end(), NCMA_INVALID_HANDLE); result = it->second; }
    if (owner) Require(result->Owner == std::this_thread::get_id(), NCMA_WRONG_THREAD);
    if (owner) Require(!result->Busy.load(), NCMA_BUSY);
    if (ready) Require(result->Character != nullptr, NCMA_BUSY);
    return result;
}
template<class T> void Output(T* value) { Require(value && value->struct_size == sizeof(T)); }
NcmaImportTransformV1 TransformData(const Transform& source)
{
    NcmaImportTransformV1 result{};
    for (int i = 0; i < 3; ++i) { result.position[i] = source.Position[i]; result.scale[i] = source.Scale[i]; }
    for (int i = 0; i < 4; ++i) result.rotation_xyzw[i] = source.Rotation.coeffs()[i];
    return result;
}
NcmaImportInfoV1 Describe(const Assets::ImportedCharacter& character)
{
    NcmaImportInfoV1 result{}; result.struct_size = sizeof(result);
    result.fbx_version = character.FbxVersion; result.binary = character.Binary ? 1u : 0u;
    result.bones = static_cast<uint32_t>(character.Animations->GetSkeleton().Bones.size());
    result.meshes = static_cast<uint32_t>(character.Meshes.size());
    result.clips = static_cast<uint32_t>(character.Animations->GetClips().size());
    result.warnings = static_cast<uint32_t>(character.Warnings.size());
    result.source_unit_metres = character.SourceUnitMeters; result.sample_rate = character.SampleRate;
    for (const auto& mesh : character.Meshes) { result.vertices += mesh.Vertices.size(); result.indices += mesh.Indices.size(); }
    for (const auto& clip : character.Animations->GetClips()) for (const auto& track : clip.Tracks) result.keys += track.Keys.size();
    return result;
}
uint32_t NCMA_CALL Create(uint64_t* id, NcmaErrorV1* error)
{
    return Run(error, [&]() -> uint32_t {
        Require(id); *id = 0;
        auto context = std::make_shared<Context>();
        std::scoped_lock lock(RegistryGate); Require(Registry.size() < 4 && NextContext != UINT64_MAX, NCMA_BUSY);
        const auto value = NextContext++; Registry.emplace(value, std::move(context)); *id = value; return NCMA_OK;
    });
}
uint32_t NCMA_CALL Close(uint64_t id, NcmaErrorV1* error)
{
    return Run(error, [&]() -> uint32_t { auto context = Find(id); std::scoped_lock lock(RegistryGate); Registry.erase(id); return NCMA_OK; });
}
uint32_t NCMA_CALL LoadMode(uint64_t id, const uint8_t* path, uint32_t bytes, double rate, uint32_t mode, NcmaImportInfoV1* info, NcmaErrorV1* error)
{
    return Run(error, [&]() -> uint32_t {
        Output(info); Require(mode <= 1 && path && bytes > 0 && bytes <= 32768 && !std::memchr(path, 0, bytes) && std::isfinite(rate) && rate >= 1 && rate <= 120);
        auto context = Find(id); Require(!context->Character, NCMA_BUSY);
        std::u8string source(bytes, u8'\0'); std::memcpy(source.data(), path, bytes);
        *info = {}; info->struct_size = sizeof(*info);
        context->Busy.store(true);
        struct BusyExit { Context& Value; ~BusyExit() { Value.Busy.store(false); } } busy{*context};
        try
        {
            Assets::FbxImportOptions options;
            options.AssetId = {1, 1}; // Private adapter IDs are never exported or used as persistent identities.
            options.SampleRate = rate; options.Cancellation = &context->Cancelled; options.Phase = &context->Phase;
            options.StaticOnly = mode == 1;
            options.RejectAmbiguousNames = true;
            options.BytesRead = &context->BytesRead; options.BytesTotal = &context->BytesTotal;
            auto candidate = Assets::FbxCharacterImporter::Import(std::filesystem::path(source), options);
            auto description = Describe(*candidate);
            std::scoped_lock decision(context->Decision);
            if (context->Cancelled.load()) throw Assets::FbxImportCancelled();
            context->Character = std::move(candidate); context->Phase.store(4); *info = description; return NCMA_OK;
        }
        catch (const Assets::FbxImportCancelled&) { context->Phase.store(5); throw; }
        catch (...) { context->Phase.store(6); throw; }
    });
}
uint32_t NCMA_CALL Info(uint64_t id, NcmaImportInfoV1* info, NcmaErrorV1* error)
{
    return Run(error, [&]() -> uint32_t { Output(info); auto context = Find(id, true, true); *info = Describe(*context->Character); return NCMA_OK; });
}
uint32_t NCMA_CALL Load(uint64_t id, const uint8_t* path, uint32_t bytes, double rate, NcmaImportInfoV1* info, NcmaErrorV1* error)
{
    return LoadMode(id, path, bytes, rate, 0, info, error);
}
uint32_t NCMA_CALL MeshInfo(uint64_t id, uint32_t mesh, NcmaImportMeshInfoV1* info, NcmaErrorV1* error)
{
    return Run(error, [&]() -> uint32_t {
        Output(info); auto context = Find(id, true, true); Require(mesh < context->Character->Meshes.size());
        const auto& value = context->Character->Meshes[mesh];
        NcmaImportMeshInfoV1 result{}; result.struct_size = sizeof(result);
        result.vertices = static_cast<uint32_t>(value.Vertices.size()); result.indices = static_cast<uint32_t>(value.Indices.size());
        result.triangles = static_cast<uint32_t>(value.TriangleMaterials.size()); result.bindings = static_cast<uint32_t>(value.Bindings.size());
        result.materials = static_cast<uint32_t>(value.Materials.size()); *info = result; return NCMA_OK;
    });
}
uint32_t NCMA_CALL ClipInfo(uint64_t id, uint32_t clip, NcmaImportClipInfoV1* info, NcmaErrorV1* error)
{
    return Run(error, [&]() -> uint32_t {
        Output(info); auto context = Find(id, true, true); const auto& clips = context->Character->Animations->GetClips(); Require(clip < clips.size());
        NcmaImportClipInfoV1 result{}; result.struct_size = sizeof(result); result.duration = clips[clip].Duration;
        result.tracks = static_cast<uint32_t>(clips[clip].Tracks.size());
        for (const auto& track : clips[clip].Tracks) result.keys += static_cast<uint32_t>(track.Keys.size());
        *info = result; return NCMA_OK;
    });
}
template<class T, class C, class F> uint32_t Page(const C& values, uint32_t first, uint32_t count, void* output,
    uint32_t capacity, uint32_t* written, NcmaErrorV1* error, F&& convert)
{
    Require(first <= values.size() && count <= values.size() - first);
    const auto bytes = static_cast<uint32_t>(count * sizeof(T));
    if (capacity < bytes) { NcmaPlugin::Error(error, NCMA_BUFFER_TOO_SMALL); error->required_bytes = bytes; return NCMA_BUFFER_TOO_SMALL; }
    Require(count == 0 || output);
    std::vector<T> result; result.reserve(count);
    for (uint32_t i = 0; i < count; ++i) result.push_back(convert(values[first + i]));
    if (bytes) std::memcpy(output, result.data(), bytes); *written = count; return NCMA_OK;
}
uint32_t NCMA_CALL ReadPage(uint64_t id, uint32_t stream, uint32_t object, uint32_t sub, uint32_t first,
    uint32_t count, void* output, uint32_t capacity, uint32_t* written, NcmaErrorV1* error)
{
    return Run(error, [&]() -> uint32_t {
        Require(written && count <= NCMA_IMPORT_MAX_PAGE && (capacity == 0 || output)); *written = 0;
        auto context = Find(id, true, true); const auto& value = *context->Character;
        if (stream == NCMA_IMPORT_BONES)
        {
            Require(object == 0 && sub == 0);
            return Page<NcmaImportBoneV1>(value.Animations->GetSkeleton().Bones, first, count, output, capacity, written, error,
                [](const auto& bone) { return NcmaImportBoneV1{bone.Parent, TransformData(bone.BindLocal)}; });
        }
        if (stream == NCMA_IMPORT_TRACKS || stream == NCMA_IMPORT_KEYS)
        {
            const auto& clips = value.Animations->GetClips(); Require(object < clips.size()); const auto& tracks = clips[object].Tracks;
            if (stream == NCMA_IMPORT_TRACKS)
            {
                Require(sub == 0);
                return Page<NcmaImportTrackV1>(tracks, first, count, output, capacity, written, error,
                    [](const auto& track) { return NcmaImportTrackV1{static_cast<uint32_t>(track.BoneIndex), static_cast<uint32_t>(track.Keys.size())}; });
            }
            Require(sub < tracks.size());
            return Page<NcmaImportKeyV1>(tracks[sub].Keys, first, count, output, capacity, written, error,
                [](const auto& key) { return NcmaImportKeyV1{key.Time, TransformData(key.Value)}; });
        }
        Require(object < value.Meshes.size() && sub == 0); const auto& mesh = value.Meshes[object];
        switch (stream)
        {
        case NCMA_IMPORT_VERTICES:
            return Page<NcmaImportVertexV1>(mesh.Vertices, first, count, output, capacity, written, error, [](const auto& vertex) {
                NcmaImportVertexV1 result{};
                for (int i = 0; i < 3; ++i) { result.position[i] = vertex.Position[i]; result.normal[i] = vertex.Normal[i]; }
                result.uv[0] = vertex.UV[0]; result.uv[1] = 1.0f - vertex.UV[1];
                for (int i = 0; i < 4; ++i) { result.joints[i] = vertex.Joints[i]; result.weights[i] = vertex.Weights[i]; }
                return result;
            });
        case NCMA_IMPORT_INDICES:
            return Page<uint32_t>(mesh.Indices, first, count, output, capacity, written, error, [](auto index) { return index; });
        case NCMA_IMPORT_TRIANGLE_MATERIALS:
            return Page<uint32_t>(mesh.TriangleMaterials, first, count, output, capacity, written, error, [](auto slot) { return slot; });
        case NCMA_IMPORT_BINDINGS:
            return Page<NcmaImportBindingV1>(mesh.Bindings, first, count, output, capacity, written, error, [](const auto& binding) {
                NcmaImportBindingV1 result{}; result.bone = static_cast<uint32_t>(binding.BoneIndex);
                for (int i = 0; i < 16; ++i) result.geometry_to_bone[i] = binding.GeometryToBone.data()[i]; return result;
            });
        default: return NcmaPlugin::Error(error, NCMA_UNSUPPORTED_FEATURE);
        }
    });
}
uint32_t NCMA_CALL ReadText(uint64_t id, uint32_t kind, uint32_t object, uint32_t sub, uint8_t* output,
    uint32_t capacity, uint32_t* required, NcmaErrorV1* error)
{
    return Run(error, [&]() -> uint32_t {
        Require(required && (capacity == 0 || output)); *required = 0;
        auto context = Find(id, true, true); const auto& value = *context->Character; const std::string* text = nullptr;
        switch (kind)
        {
        case NCMA_IMPORT_MESH_NAME: Require(sub == 0 && object < value.Meshes.size()); text = &value.Meshes[object].Name; break;
        case NCMA_IMPORT_BONE_NAME: Require(sub == 0 && object < value.Animations->GetSkeleton().Bones.size()); text = &value.Animations->GetSkeleton().Bones[object].Name; break;
        case NCMA_IMPORT_CLIP_NAME: Require(sub == 0 && object < value.Animations->GetClips().size()); text = &value.Animations->GetClips()[object].Name; break;
        case NCMA_IMPORT_MATERIAL_NAME: Require(object < value.Meshes.size() && sub < value.Meshes[object].Materials.size()); text = &value.Meshes[object].Materials[sub]; break;
        case NCMA_IMPORT_WARNING: Require(sub == 0 && object < value.Warnings.size()); text = &value.Warnings[object]; break;
        default: return NcmaPlugin::Error(error, NCMA_UNSUPPORTED_FEATURE);
        }
        Require(text->size() <= 65536); *required = static_cast<uint32_t>(text->size());
        if (capacity < *required) { NcmaPlugin::Error(error, NCMA_BUFFER_TOO_SMALL); error->required_bytes = *required; return NCMA_BUFFER_TOO_SMALL; }
        if (*required) std::memcpy(output, text->data(), *required); return NCMA_OK;
    });
}
uint32_t NCMA_CALL Cancel(uint64_t id, NcmaErrorV1* error)
{
    return Run(error, [&]() -> uint32_t {
        auto context = Find(id, false); std::scoped_lock decision(context->Decision);
        Require(context->Phase.load() != 4, NCMA_BUSY); context->Cancelled.store(true); return NCMA_OK;
    });
}
uint32_t NCMA_CALL Status(uint64_t id, NcmaImportStatusV1* status, NcmaErrorV1* error)
{
    return Run(error, [&]() -> uint32_t {
        Output(status); auto context = Find(id, false); NcmaImportStatusV1 result{}; result.struct_size = sizeof(result);
        result.phase = context->Phase.load(); result.busy = context->Busy.load() ? 1u : 0u; result.cancellation_requested = context->Cancelled.load() ? 1u : 0u;
        result.bytes_total = context->BytesTotal.load();
        // Independently published progress atomics are phase hints, not one coherent parse snapshot.
        result.bytes_read = std::min(context->BytesRead.load(), result.bytes_total); *status = result; return NCMA_OK;
    });
}
}
extern "C" NCMA_IMPORT_API uint32_t NCMA_CALL ncma_import_get_api(uint32_t major, uint32_t minor, void* output, uint32_t capacity, NcmaErrorV1* error)
{
    const NcmaImportApiV1 api{sizeof(NcmaImportApiV1), 1, 0, 0, &Create, &Close, &Load, &Info, &MeshInfo, &ClipInfo, &ReadPage, &ReadText, &Cancel, &Status};
    if (minor == 0) return NcmaPlugin::CopyApi(major, minor, output, capacity, error, api);
    auto extendedBase = api; extendedBase.struct_size = sizeof(NcmaImportApiV1_1); extendedBase.minor = 1;
    const NcmaImportApiV1_1 extended{extendedBase, &LoadMode};
    return NcmaPlugin::CopyApi(major, minor, output, capacity, error, extended, 1);
}
