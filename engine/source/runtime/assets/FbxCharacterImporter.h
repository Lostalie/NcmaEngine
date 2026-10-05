#pragma once

#include "animation/AnimationRuntime.h"

#include <array>
#include <filesystem>
#include <atomic>
#include <stdexcept>

namespace NcmaEngine::Assets
{
    struct SkinBinding final
    {
        std::size_t BoneIndex = 0;
        Matrix4 GeometryToBone = Matrix4::Identity();
    };

    struct SkinnedVertex final
    {
        Vector3 Position = Vector3::Zero();
        Vector3 Normal = Vector3::UnitY();
        Vector2 UV = Vector2::Zero();
        std::array<std::uint16_t, 4> Joints{}; // Mesh-local binding palette, not serialized node handles.
        std::array<float, 4> Weights{};
    };

    struct CharacterMesh final
    {
        SceneUuid Id;
        std::string Name;
        std::vector<SkinnedVertex> Vertices;
        std::vector<std::uint32_t> Indices;
        std::vector<std::uint32_t> TriangleMaterials;
        std::vector<std::string> Materials;
        std::vector<SkinBinding> Bindings;
    };

    struct ImportedCharacter final
    {
        SceneUuid Id;
        std::filesystem::path Source;
        double SampleRate = 30;
        double SourceUnitMeters = 1;
        std::uint32_t FbxVersion = 0;
        bool Binary = false;
        std::shared_ptr<const Animation::AnimationLibrary> Animations;
        std::vector<CharacterMesh> Meshes;
        std::vector<std::string> Warnings;
        Vector3 BoundsMin = Vector3::Zero(), BoundsMax = Vector3::Zero();
        [[nodiscard]] std::string InspectJson() const;
    };

    struct FbxImportOptions final
    {
        SceneUuid AssetId; // Reuse on reimport to preserve asset/subasset UUIDs.
        double SampleRate = 30;
        std::size_t MaxFileBytes = 256ULL * 1024 * 1024;
        std::size_t MaxVertices = 2'000'000;
        std::size_t MaxAnimationKeys = 2'000'000;
        bool StaticOnly = false; // Separate tool mode; old character callers stay strict.
        bool RejectAmbiguousNames = false; // Persistent import must not infer identity from duplicate-name suffix indices.
        // Private numerical worker signals, never callbacks into CLR, GUI or World.
        const std::atomic_bool* Cancellation = nullptr;
        std::atomic<std::uint32_t>* Phase = nullptr;
        std::atomic<std::uint64_t>* BytesRead = nullptr;
        std::atomic<std::uint64_t>* BytesTotal = nullptr;
    };

    class FbxImportCancelled final : public std::runtime_error
    {
    public:
        FbxImportCancelled() : std::runtime_error("FBX import cancelled") {}
    };

    class FbxCharacterImporter final
    {
    public:
        // Reads one FBX; no external texture/cache reads, no disk writes. Throws with context.
        [[nodiscard]] static std::shared_ptr<const ImportedCharacter> Import(
            const std::filesystem::path& path, const FbxImportOptions& options = {});
    };

    // Reference CPU skinning, also used by the editor's wireframe preview.
    // FBX per-mesh cluster bindings are authoritative; skeleton inverse-bind matrices alone are insufficient.
    void SkinPositions(const CharacterMesh& mesh, std::span<const Matrix4> modelPose,
        std::span<Vector3> output);
}
