#include "assets/FbxCharacterImporter.h"
#include "ufbx.h"

#include <algorithm>
#include <cctype>
#include <cmath>
#include <fstream>
#include <functional>
#include <iomanip>
#include <locale>
#include <limits>
#include <sstream>
#include <stdexcept>
#include <unordered_map>
#include <unordered_set>

namespace NcmaEngine::Assets
{
    namespace
    {
        using ScenePtr = std::unique_ptr<ufbx_scene, decltype(&ufbx_free_scene)>;
        void CheckCancelled(const FbxImportOptions& options)
        {
            if (options.Cancellation && options.Cancellation->load(std::memory_order_relaxed)) throw FbxImportCancelled();
        }
        ufbx_progress_result Progress(void* user, const ufbx_progress* progress) noexcept
        {
            const auto& options = *static_cast<const FbxImportOptions*>(user);
            if (options.BytesTotal) options.BytesTotal->store(progress->bytes_total, std::memory_order_relaxed);
            if (options.BytesRead) options.BytesRead->store(progress->bytes_read, std::memory_order_relaxed);
            return options.Cancellation && options.Cancellation->load(std::memory_order_relaxed) ? UFBX_PROGRESS_CANCEL : UFBX_PROGRESS_CONTINUE;
        }
        void Require(bool condition, const std::string& message)
        {
            if (!condition) throw std::runtime_error("FBX: " + message);
        }
        std::string Text(ufbx_string value) { return {value.data, value.length}; }
        std::string Utf8(const std::filesystem::path& path)
        {
            const auto text = path.u8string();
            return {reinterpret_cast<const char*>(text.data()), text.size()};
        }
        Matrix4 Matrix(const ufbx_matrix& value)
        {
            Matrix4 result = Matrix4::Identity();
            for (int column = 0; column < 4; ++column)
                for (int row = 0; row < 3; ++row) result(row, column) = static_cast<float>(value.cols[column].v[row]);
            Require(result.allFinite(), "non-finite transform matrix");
            return result;
        }
        Vector3 Vector(ufbx_vec3 value)
        {
            Vector3 result(static_cast<float>(value.x), static_cast<float>(value.y), static_cast<float>(value.z));
            Require(result.allFinite(), "non-finite vertex attribute");
            return result;
        }
        Transform Decompose(const Matrix4& matrix, const std::string& name)
        {
            Transform result;
            result.Position = matrix.block<3, 1>(0, 3);
            Eigen::Matrix3f rotation = matrix.block<3, 3>(0, 0);
            for (int column = 0; column < 3; ++column)
            {
                result.Scale[column] = rotation.col(column).norm();
                Require(std::isfinite(result.Scale[column]) && result.Scale[column] > 0.000001F,
                    "singular scale on " + name);
                rotation.col(column) /= result.Scale[column];
            }
            Require(rotation.determinant() > 0 && (rotation.transpose() * rotation).isApprox(Eigen::Matrix3f::Identity(), 0.001F),
                "reflected or sheared bone transform on " + name + "; apply transforms in the DCC exporter");
            result.Rotation = Quaternion(rotation).normalized();
            Require(result.ToMatrix().isApprox(matrix, 0.001F), "bone TRS decomposition failed on " + name);
            return result;
        }
        SceneUuid SubId(SceneUuid root, const std::string& key)
        {
            // Fixed byte-wise FNV-1a namespaces, independent of STL/platform hash implementations.
            auto hash = [&](std::uint64_t value) {
                for (const unsigned char ch : key) { value ^= ch; value *= 1099511628211ULL; }
                return value;
            };
            SceneUuid id{hash(root.High ^ 14695981039346656037ULL), hash(root.Low ^ 7809847782465536322ULL)};
            id.High = (id.High & ~0xF000ULL) | 0x5000ULL;
            id.Low = (id.Low & 0x3FFFFFFFFFFFFFFFULL) | 0x8000000000000000ULL;
            return id;
        }
        std::string UniqueName(std::string name, std::unordered_set<std::string>& used, bool rejectAmbiguous = false)
        {
            if (name.empty()) name = "Unnamed";
            const auto base = name;
            Require(!rejectAmbiguous || !used.contains(name), "ambiguous source name: " + name);
            for (std::size_t suffix = 2; !used.insert(name).second; ++suffix) name = base + "#" + std::to_string(suffix);
            return name;
        }
        std::string ErrorText(const ufbx_error& error)
        {
            std::array<char, 2048> text{};
            ufbx_format_error(text.data(), text.size(), &error);
            return text.data();
        }
        std::string Quote(const std::string& value)
        {
            std::ostringstream out;
            out << '"';
            for (const unsigned char ch : value)
            {
                if (ch == '"' || ch == '\\') out << '\\' << static_cast<char>(ch);
                else if (ch < 32) out << "\\u" << std::hex << std::setw(4) << std::setfill('0') << static_cast<int>(ch) << std::dec;
                else out << static_cast<char>(ch);
            }
            out << '"'; return out.str();
        }
    }

    std::shared_ptr<const ImportedCharacter> FbxCharacterImporter::Import(
        const std::filesystem::path& path, const FbxImportOptions& options)
    {
        CheckCancelled(options);
        if (options.Phase) options.Phase->store(1);
        Require(std::isfinite(options.SampleRate) && options.SampleRate >= 1 && options.SampleRate <= 120,
            "sample rate must be within 1..120 Hz");
        auto extension = path.extension().string();
        std::transform(extension.begin(), extension.end(), extension.begin(), [](unsigned char ch) { return static_cast<char>(std::tolower(ch)); });
        Require(extension == ".fbx", "character source must be an .fbx file");
        Require(std::filesystem::is_regular_file(path), "source is not a regular file: " + Utf8(path));
        const auto bytes = std::filesystem::file_size(path);
        Require(bytes > 0 && bytes <= options.MaxFileBytes, "source exceeds file budget or is empty");
        std::vector<char> data(static_cast<std::size_t>(bytes));
        std::ifstream input(path, std::ios::binary);
        for (std::size_t offset = 0; offset < data.size();)
        {
            CheckCancelled(options);
            const auto count = std::min<std::size_t>(1024 * 1024, data.size() - offset);
            Require(input.read(data.data() + offset, static_cast<std::streamsize>(count)).good(), "cannot read source file");
            offset += count;
        }
        ufbx_load_opts load{};
        load.file_format = UFBX_FILE_FORMAT_FBX;
        load.target_axes = ufbx_axes_right_handed_y_up;
        load.target_unit_meters = 1;
        load.space_conversion = UFBX_SPACE_CONVERSION_MODIFY_GEOMETRY;
        load.geometry_transform_handling = UFBX_GEOMETRY_TRANSFORM_HANDLING_PRESERVE;
        load.inherit_mode_handling = UFBX_INHERIT_MODE_HANDLING_HELPER_NODES;
        load.generate_missing_normals = true;
        load.normalize_normals = true;
        load.ignore_embedded = true;
        load.load_external_files = false;
        load.node_depth_limit = 128;
        load.index_error_handling = UFBX_INDEX_ERROR_HANDLING_ABORT_LOADING;
        load.temp_allocator.memory_limit = 256ULL * 1024 * 1024;
        load.result_allocator.memory_limit = 512ULL * 1024 * 1024;
        load.progress_cb.fn = &Progress;
        load.progress_cb.user = const_cast<FbxImportOptions*>(&options);
        load.progress_interval_hint = 64 * 1024;
        ufbx_error error{};
        ScenePtr scene(ufbx_load_memory(data.data(), data.size(), &load, &error), &ufbx_free_scene);
        CheckCancelled(options);
        Require(scene != nullptr, ErrorText(error));
        if (options.Phase) options.Phase->store(2);
        auto result = std::make_shared<ImportedCharacter>();
        result->Id = options.AssetId.IsValid() ? options.AssetId : SceneUuid::New();
        result->Source = std::filesystem::weakly_canonical(path);
        result->SampleRate = options.SampleRate;
        result->FbxVersion = scene->metadata.version;
        result->Binary = !scene->metadata.ascii;
        result->SourceUnitMeters = scene->settings.unit_meters;
        for (const auto& warning : scene->metadata.warnings) result->Warnings.push_back(Text(warning.description));

        std::unordered_set<const ufbx_node*> selected;
        const auto includeParents = [&](const ufbx_node* node) {
            for (; node; node = node->parent) selected.insert(node);
        };
        bool hasSkin = false;
        for (const auto* node : scene->nodes)
        {
            CheckCancelled(options);
            if (node->bone) includeParents(node);
            if (!node->mesh || node->mesh->num_triangles == 0) continue;
            includeParents(node);
            for (const auto* skin : node->mesh->skin_deformers)
                for (const auto* cluster : skin->clusters)
                {
                    Require(cluster->bone_node != nullptr, "skin cluster has no bone");
                    includeParents(cluster->bone_node); hasSkin = true;
                }
        }
        Require(options.StaticOnly ? !hasSkin : hasSkin,
            options.StaticOnly ? "static import rejects skinned meshes" : "character has no skinned triangle mesh");
        Require(selected.size() <= 1024, "skeleton plus transform helpers exceeds 1024 nodes");
        std::vector<const ufbx_node*> nodes;
        std::unordered_map<const ufbx_node*, std::size_t> nodeIndices;
        Animation::Skeleton skeleton{SubId(result->Id, "skeleton"), {}};
        std::unordered_set<std::string> boneNames;
        std::function<void(const ufbx_node*)> visit = [&](const ufbx_node* node) {
            if (!selected.contains(node)) return;
            const int parent = node->parent ? static_cast<int>(nodeIndices.at(node->parent)) : -1;
            nodeIndices[node] = nodes.size(); nodes.push_back(node);
            auto name = UniqueName(node->is_root ? "FBXRoot" : Text(node->name), boneNames, options.RejectAmbiguousNames);
            const Matrix4 global = Matrix(node->node_to_world);
            const Matrix4 local = node->parent ? (Matrix(node->parent->node_to_world).inverse() * global).eval() : global;
            skeleton.Bones.push_back({name, parent, Decompose(local, name)});
            for (const auto* child : node->children) visit(child);
        };
        visit(scene->root_node);

        std::size_t vertexCount = 0;
        bool truncatedWeights = false, rigidFallback = false;
        for (const auto* node : nodes)
        {
            CheckCancelled(options);
            const auto* mesh = node->mesh;
            if (!mesh || !mesh->num_triangles) continue;
            Require(mesh->num_indices <= options.MaxVertices - vertexCount, "vertex budget exceeded");
            Require(mesh->skin_deformers.count <= 1, "multiple skin deformers on a mesh are unsupported");
            const auto* skin = mesh->skin_deformers.count ? mesh->skin_deformers[0] : nullptr;
            if (skin)
            {
                Require(mesh->instances.count == 1, "instanced skinned meshes are unsupported");
                Require(skin->skinning_method == UFBX_SKINNING_METHOD_LINEAR || skin->skinning_method == UFBX_SKINNING_METHOD_RIGID,
                    "dual-quaternion skinning requires conversion to linear skinning in the exporter");
            }
            CharacterMesh output;
            output.Id = SubId(result->Id, "mesh/" + std::to_string(nodeIndices.at(node)));
            output.Name = skeleton.Bones[nodeIndices.at(node)].Name;
            for (const auto* material : node->materials) output.Materials.push_back(Text(material->name));
            if (skin)
                for (const auto* cluster : skin->clusters)
                    output.Bindings.push_back({nodeIndices.at(cluster->bone_node), Matrix(cluster->geometry_to_bone)});
            Require(output.Bindings.size() < 65535, "skin palette exceeds 16-bit limit");
            const auto fallback = static_cast<std::uint16_t>(output.Bindings.size());
            output.Bindings.push_back({nodeIndices.at(node), Matrix(node->geometry_to_node)});
            output.Vertices.reserve(mesh->num_indices);
            for (std::size_t corner = 0; corner < mesh->num_indices; ++corner)
            {
                if ((corner & 1023) == 0) CheckCancelled(options);
                SkinnedVertex vertex;
                vertex.Position = Vector(ufbx_get_vertex_vec3(&mesh->vertex_position, corner));
                vertex.Normal = Vector(ufbx_get_vertex_vec3(&mesh->vertex_normal, corner));
                if (mesh->vertex_uv.exists)
                {
                    const auto uv = ufbx_get_vertex_vec2(&mesh->vertex_uv, corner);
                    vertex.UV = {static_cast<float>(uv.x), static_cast<float>(uv.y)};
                    Require(vertex.UV.allFinite(), "non-finite UV coordinates");
                }
                double total = 0;
                std::size_t lane = 0;
                if (skin)
                {
                    const auto logical = mesh->vertex_indices[corner];
                    const auto weights = skin->vertices[logical];
                    truncatedWeights = truncatedWeights || weights.num_weights > 4;
                    for (std::size_t w = 0; w < weights.num_weights; ++w)
                    {
                        const auto weight = skin->weights[weights.weight_begin + w];
                        Require(std::isfinite(weight.weight) && weight.weight >= 0 &&
                            weight.weight <= std::numeric_limits<float>::max() && weight.cluster_index < skin->clusters.count,
                            "invalid skin weight");
                        if (lane == 4 || weight.weight == 0) continue;
                        vertex.Joints[lane] = static_cast<std::uint16_t>(weight.cluster_index);
                        vertex.Weights[lane] = static_cast<float>(weight.weight);
                        total += weight.weight; ++lane;
                    }
                }
                if (total > 0)
                    for (auto& weight : vertex.Weights) weight = static_cast<float>(weight / total);
                else
                {
                    vertex.Joints[0] = fallback; vertex.Weights[0] = 1;
                    rigidFallback = rigidFallback || skin != nullptr;
                }
                output.Vertices.push_back(vertex);
            }
            std::vector<std::uint32_t> triangle(mesh->max_face_triangles * 3);
            for (std::size_t faceIndex = 0; faceIndex < mesh->faces.count; ++faceIndex)
            {
                if ((faceIndex & 1023) == 0) CheckCancelled(options);
                const auto count = ufbx_triangulate_face(triangle.data(), triangle.size(), mesh, mesh->faces[faceIndex]);
                output.Indices.insert(output.Indices.end(), triangle.begin(), triangle.begin() + count * 3);
                const auto material = mesh->face_material.count ? mesh->face_material[faceIndex] : 0;
                output.TriangleMaterials.insert(output.TriangleMaterials.end(), count, material);
            }
            vertexCount += output.Vertices.size(); result->Meshes.push_back(std::move(output));
        }
        Require(!result->Meshes.empty(), "no character triangle meshes imported");
        if (truncatedWeights) result->Warnings.emplace_back("Weights reduced to the strongest four influences and normalized.");
        if (rigidFallback) result->Warnings.emplace_back("Unweighted vertices follow their mesh node.");

        std::vector<Animation::AnimationClip> clips;
        std::unordered_set<std::string> clipNames{"BindPose"};
        clips.push_back({SubId(result->Id, "bindpose"), skeleton.Id, "BindPose", 1, true, false, {}, {}});
        std::size_t keyCount = 0;
        if (options.Phase) options.Phase->store(3);
        for (const auto* stack : scene->anim_stacks)
        {
            if (options.StaticOnly) break;
            const double duration = stack->time_end - stack->time_begin;
            Require(std::isfinite(duration) && duration >= 0 && duration <= 600 && std::isfinite(stack->time_begin),
                "animation duration exceeds 600 seconds or has invalid time range");
            if (duration < 0.001) { result->Warnings.emplace_back("Skipped zero-duration animation: " + Text(stack->name)); continue; }
            const auto intervals = static_cast<std::size_t>(std::ceil(duration * options.SampleRate));
            const auto keys = (intervals + 1) * nodes.size();
            Require(keys <= options.MaxAnimationKeys - keyCount, "animation key budget exceeded; reduce sample rate");
            keyCount += keys;
            auto name = UniqueName(Text(stack->name), clipNames, options.RejectAmbiguousNames);
            Animation::AnimationClip clip{SubId(result->Id, "clip/" + name), skeleton.Id, name, duration, true, false, {}, {}};
            for (std::size_t bone = 0; bone < nodes.size(); ++bone) clip.Tracks.push_back({bone, {}});
            for (auto& track : clip.Tracks) track.Keys.reserve(intervals + 1);
            ufbx_evaluate_opts evaluate{};
            evaluate.temp_allocator.memory_limit = 256ULL * 1024 * 1024;
            evaluate.result_allocator.memory_limit = 512ULL * 1024 * 1024;
            for (std::size_t frame = 0; frame <= intervals; ++frame)
            {
                CheckCancelled(options);
                const double time = frame == intervals ? duration : static_cast<double>(frame) / options.SampleRate;
                ScenePtr sampled(ufbx_evaluate_scene(scene.get(), stack->anim, stack->time_begin + time, &evaluate, &error), &ufbx_free_scene);
                Require(sampled != nullptr, "animation evaluation failed: " + ErrorText(error));
                for (std::size_t bone = 0; bone < nodes.size(); ++bone)
                {
                    const auto* node = sampled->nodes[nodes[bone]->typed_id];
                    const Matrix4 global = Matrix(node->node_to_world);
                    const Matrix4 local = node->parent ? (Matrix(node->parent->node_to_world).inverse() * global).eval() : global;
                    clip.Tracks[bone].Keys.push_back({time, Decompose(local, skeleton.Bones[bone].Name)});
                }
            }
            clips.push_back(std::move(clip));
        }
        result->Animations = std::make_shared<const Animation::AnimationLibrary>(std::move(skeleton), std::move(clips));
        Animation::AnimationPlayer bind(result->Animations);
        result->BoundsMin = Vector3::Constant(std::numeric_limits<float>::max());
        result->BoundsMax = Vector3::Constant(std::numeric_limits<float>::lowest());
        for (const auto& mesh : result->Meshes)
        {
            CheckCancelled(options);
            std::vector<Vector3> positions(mesh.Vertices.size());
            SkinPositions(mesh, bind.ModelMatrices(), positions);
            for (const auto& position : positions)
            {
                result->BoundsMin = result->BoundsMin.cwiseMin(position);
                result->BoundsMax = result->BoundsMax.cwiseMax(position);
            }
        }
        CheckCancelled(options);
        return result;
    }

    void SkinPositions(const CharacterMesh& mesh, std::span<const Matrix4> modelPose, std::span<Vector3> output)
    {
        Require(output.size() == mesh.Vertices.size(), "skin output buffer size mismatch");
        std::vector<Matrix4> palette;
        palette.reserve(mesh.Bindings.size());
        for (const auto& binding : mesh.Bindings)
        {
            Require(binding.BoneIndex < modelPose.size(), "skin binding references unknown bone");
            palette.push_back(modelPose[binding.BoneIndex] * binding.GeometryToBone);
        }
        for (std::size_t i = 0; i < output.size(); ++i)
        {
            output[i].setZero();
            const auto& vertex = mesh.Vertices[i];
            const Vector4 position(vertex.Position.x(), vertex.Position.y(), vertex.Position.z(), 1);
            for (std::size_t lane = 0; lane < 4; ++lane)
            {
                if (vertex.Weights[lane] == 0) continue;
                Require(vertex.Joints[lane] < palette.size(), "skin vertex references unknown binding");
                output[i] += vertex.Weights[lane] * (palette[vertex.Joints[lane]] * position).head<3>();
            }
            Require(output[i].allFinite(), "skinning produced non-finite positions");
        }
    }

    std::string ImportedCharacter::InspectJson() const
    {
        std::ostringstream out;
        out.imbue(std::locale::classic()); out << std::setprecision(9) << std::boolalpha;
        out << "{\"schema_version\":1,\"asset_uuid\":" << Quote(Id.ToString()) << ",\"source\":" << Quote(Utf8(Source))
            << ",\"format\":\"FBX\",\"fbx_version\":" << FbxVersion << ",\"binary\":" << Binary
            << ",\"sample_rate\":" << SampleRate << ",\"source_unit_meters\":" << SourceUnitMeters
            << ",\"target_unit_meters\":1,\"target_axes\":\"right_handed_y_up\",\"skeleton_uuid\":"
            << Quote(Animations->GetSkeleton().Id.ToString()) << ",\"bones\":" << Animations->GetSkeleton().Bones.size()
            << ",\"bounds_min\":[" << BoundsMin.x() << ',' << BoundsMin.y() << ',' << BoundsMin.z()
            << "],\"bounds_max\":[" << BoundsMax.x() << ',' << BoundsMax.y() << ',' << BoundsMax.z() << "],\"meshes\":[";
        bool first = true;
        for (const auto& mesh : Meshes)
        {
            if (!first) out << ','; first = false;
            out << "{\"uuid\":" << Quote(mesh.Id.ToString()) << ",\"name\":" << Quote(mesh.Name)
                << ",\"vertices\":" << mesh.Vertices.size() << ",\"triangles\":" << mesh.Indices.size() / 3
                << ",\"bindings\":" << mesh.Bindings.size() << ",\"materials\":" << mesh.Materials.size() << '}';
        }
        out << "],\"clips\":["; first = true;
        for (const auto& clip : Animations->GetClips())
        {
            if (!first) out << ','; first = false;
            out << "{\"uuid\":" << Quote(clip.Id.ToString()) << ",\"name\":" << Quote(clip.Name)
                << ",\"duration\":" << clip.Duration << ",\"tracks\":" << clip.Tracks.size() << '}';
        }
        out << "],\"skeleton\":[";
        const auto& bones = Animations->GetSkeleton().Bones;
        for (std::size_t i = 0; i < bones.size(); ++i) {
            if (i) out << ',';
            out << "{\"name\":" << Quote(bones[i].Name) << ",\"parent\":" << bones[i].Parent << '}';
        }
        out << "],\"warnings\":["; first = true;
        for (const auto& warning : Warnings) { if (!first) out << ','; first = false; out << Quote(warning); }
        out << "]}"; return out.str();
    }
}
