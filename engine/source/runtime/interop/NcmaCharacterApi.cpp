#include "interop/NcmaCharacterApi.h"
#include "core/log/NativeDiagnostics.h"
#include "assets/FbxCharacterImporter.h"
#include <algorithm>
#include <array>
#include <cstring>
#include <cmath>
#include <limits>
#include <mutex>
#include <stdexcept>
#include <string_view>
#include <unordered_map>

namespace
{
    using namespace NcmaEngine;
    thread_local std::array<char, 4096> CharacterError{};
    void SetError(const char* message) noexcept
    {
        std::size_t length = 0;
        while (length + 1 < CharacterError.size() && message[length]) ++length;
        // Never split a UTF-8 sequence at the bounded error buffer boundary.
        while (length && (static_cast<unsigned char>(message[length]) & 0xc0U) == 0x80U) --length;
        std::copy_n(message, length, CharacterError.data()); CharacterError[length] = 0;
    }
    std::mutex ResourcesGate;
    struct Resource final
    {
        std::shared_ptr<const Assets::ImportedCharacter> Character;
        std::string Report;
        std::uint32_t SampleFloats = 0;
    };
    std::unordered_map<std::uint64_t, Resource> Resources;
    std::uint64_t NextResource = 1;
    constexpr std::size_t MaxResources = 16, MaxReportBytes = 4 * 1024 * 1024;
    void Require(bool valid, const char* message) { if (!valid) throw std::invalid_argument(message); }
    const Resource& Get(std::uint64_t handle)
    {
        const auto item = Resources.find(handle);
        Require(item != Resources.end(), "Unknown or released character resource generation");
        return item->second;
    }
    template<class T> std::uint8_t Copy(const T* source, std::uint32_t count,
        T* output, std::uint32_t capacity, std::uint32_t* required)
    {
        Require(required != nullptr, "Required count pointer is null");
        Require(output != nullptr || capacity == 0, "Null output requires zero capacity");
        *required = count;
        if (!output || capacity < count) return 2;
        std::copy_n(source, count, output);
        return 1;
    }
    template<class F> std::uint8_t Guard(F&& operation, std::uint32_t* required = nullptr)
    {
        if (required) *required = 0;
        try { const auto result = operation(); CharacterError[0] = 0; return result; }
        catch (const std::exception& error) { SetError(error.what()); }
        catch (...) { SetError("Unknown character kernel error"); }
        return 0;
    }
}
extern "C"
{
    std::uint32_t NCMA_NATIVE_CALL ncma_character_abi_version() { return 2; }
    std::uint64_t NCMA_NATIVE_CALL ncma_character_import(std::uint32_t version,
        const char* path_utf8, double sample_rate, const char* asset_uuid)
    {
        try
        {
            Require(version == 2, "Unsupported character ABI version; ABI 2 required");
            Require(path_utf8 && *path_utf8, "FBX source path is empty");
            Assets::FbxImportOptions options;
            options.SampleRate = sample_rate;
            if (asset_uuid && *asset_uuid)
            {
                const auto id = SceneUuid::Parse(asset_uuid);
                Require(id.has_value() && id->IsValid(), "Invalid persistent character UUID");
                options.AssetId = *id;
            }
            std::lock_guard lock(ResourcesGate);
            Require(Resources.size() < MaxResources, "Character resource budget exceeded (16)");
            Require(NextResource != std::numeric_limits<std::uint64_t>::max(), "Resource generations exhausted");
            const std::filesystem::path path(std::u8string_view(reinterpret_cast<const char8_t*>(path_utf8)));
            auto character = Assets::FbxCharacterImporter::Import(path, options);
            auto report = character->InspectJson();
            Require(report.size() + 1 <= MaxReportBytes, "Character report budget exceeded");
            std::size_t count = character->Animations->GetSkeleton().Bones.size() * 16;
            for (const auto& mesh : character->Meshes) count += mesh.Vertices.size() * 3;
            Require(count <= 6'016'384, "Character sample buffer budget exceeded");
            const auto handle = NextResource;
            Resources.emplace(handle, Resource{std::move(character), std::move(report), static_cast<std::uint32_t>(count)});
            ++NextResource;
            NcmaEngine::NativeDiagnostic("character.import","Immutable FBX resource imported.");
            CharacterError[0] = 0; return handle;
        }
        catch (const std::exception& error) { SetError(error.what()); }
        catch (...) { SetError("Unknown character import error"); }
        return 0;
    }
    std::uint8_t NCMA_NATIVE_CALL ncma_character_release(std::uint64_t handle)
    {
        return Guard([&]() -> std::uint8_t {
            std::lock_guard lock(ResourcesGate); (void)Get(handle); Resources.erase(handle); return 1;
        });
    }
    std::uint8_t NCMA_NATIVE_CALL ncma_character_read_report(std::uint64_t handle,
        char* output, std::uint32_t capacity, std::uint32_t* required)
    {
        return Guard([&] {
            std::lock_guard lock(ResourcesGate); const auto& report = Get(handle).Report;
            return Copy(report.c_str(), static_cast<std::uint32_t>(report.size() + 1), output, capacity, required);
        }, required);
    }
    std::uint8_t NCMA_NATIVE_CALL ncma_character_read_error(char* output,
        std::uint32_t capacity, std::uint32_t* required)
    {
        // Must not clear or replace the error between query and copy.
        if (!required || (!output && capacity)) return 0;
        return Copy(CharacterError.data(), static_cast<std::uint32_t>(std::strlen(CharacterError.data()) + 1), output, capacity, required);
    }
    std::uint8_t NCMA_NATIVE_CALL ncma_character_read_indices(std::uint64_t handle, std::uint32_t mesh,
        std::uint32_t* output, std::uint32_t capacity, std::uint32_t* required)
    {
        return Guard([&] {
            std::lock_guard lock(ResourcesGate); const auto& indices = Get(handle).Character->Meshes.at(mesh).Indices;
            return Copy(indices.data(), static_cast<std::uint32_t>(indices.size()), output, capacity, required);
        }, required);
    }
    std::uint8_t NCMA_NATIVE_CALL ncma_character_sample(std::uint64_t handle, std::uint32_t clip, double time,
        float* output, std::uint32_t capacity, std::uint32_t* required)
    {
        return Guard([&]() -> std::uint8_t {
            std::lock_guard lock(ResourcesGate); const auto& resource = Get(handle);
            const auto& library = *resource.Character->Animations;
            Require(clip < library.GetClips().size() && std::isfinite(time) && time >= 0 && time <= 1.0e9,
                "Invalid character clip/time sample");
            Require(required && (output || capacity == 0), "Invalid character sample buffers");
            *required = resource.SampleFloats;
            if (!output || capacity < resource.SampleFloats) return 2;
            const auto bones = library.GetSkeleton().Bones.size();
            std::vector<Transform> local(bones);
            std::vector<Matrix4> model(bones), skin(bones);
            library.Sample(clip, time, local); library.BuildMatrices(local, model, skin);
            // Scratch ensures failures never partially publish to caller-owned memory.
            std::vector<float> result(resource.SampleFloats);
            std::size_t offset = 0;
            for (const auto& matrix : model)
            {
                Require(matrix.allFinite(), "Non-finite character pose");
                std::copy_n(matrix.data(), 16, result.data() + offset); offset += 16;
            }
            for (const auto& mesh : resource.Character->Meshes)
            {
                std::vector<Vector3> positions(mesh.Vertices.size());
                Assets::SkinPositions(mesh, model, positions);
                for (const auto& position : positions)
                {
                    std::copy_n(position.data(), 3, result.data() + offset); offset += 3;
                }
            }
            std::copy(result.begin(), result.end(), output); return 1;
        }, required);
    }
    const char* NCMA_NATIVE_CALL ncma_character_inspect_fbx(std::uint32_t, const char*, double)
    {
        SetError("Legacy character ABI 1 rejected; use immutable ABI 2 resource and caller-owned buffers");
        return nullptr;
    }
    const char* NCMA_NATIVE_CALL ncma_character_last_error() { return CharacterError.data(); }
}
