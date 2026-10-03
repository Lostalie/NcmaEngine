#include "interop/NcmaNativeApi.h"

#include "scene/SceneWorld.h"

#include <exception>
#include <array>
#include <cstddef>
#include <memory>
#include <stdexcept>
#include <string>

namespace
{
    thread_local std::string LastError;

    NcmaEngine::SceneWorld* AsWorld(NcmaWorldHandle handle)
    {
        if (handle == nullptr)
            throw std::invalid_argument("World handle is null");
        auto* world = static_cast<NcmaEngine::SceneWorld*>(handle);
        world->VerifyAccess();
        return world;
    }

    void CaptureError(const std::exception& error) { LastError = error.what(); }

    NcmaEngine::ObjectReference FromNative(NcmaObjectReference value) { return {{value.WorldHigh, value.WorldLow}, value.Id}; }
    NcmaObjectReference ToNative(NcmaEngine::ObjectReference value) { return {value.World.High, value.World.Low, value.Id}; }
    NcmaEngine::Transform FromNative(const NcmaTransform& value)
    {
        return {NcmaEngine::Vector3(value.Position.X, value.Position.Y, value.Position.Z),
            NcmaEngine::Quaternion(value.RotationW, value.RotationX, value.RotationY, value.RotationZ),
            NcmaEngine::Vector3(value.Scale.X, value.Scale.Y, value.Scale.Z)};
    }
    NcmaTransform ToNative(const NcmaEngine::Transform& value)
    {
        return {{value.Position.x(), value.Position.y(), value.Position.z()},
            value.Rotation.x(), value.Rotation.y(), value.Rotation.z(), value.Rotation.w(),
            {value.Scale.x(), value.Scale.y(), value.Scale.z()}};
    }
    NcmaGameplaySignal ToNative(const NcmaEngine::GameplaySignal& value)
    {
        return {ToNative(value.Source), ToNative(value.Target), value.Code, value.Value, value.Sequence};
    }
    template<typename F> std::uint8_t Guard(F&& action)
    {
        try { LastError.clear(); action(); return 1; }
        catch (const std::exception& error) { CaptureError(error); return 0; }
        catch (...) { LastError = "Unknown native world-access error"; return 0; }
    }
    void ValidateBuffer(const void* buffer, std::uint32_t count)
    {
        if (count > NcmaEngine::SceneWorld::AccessCapacity || (count != 0 && buffer == nullptr))
            throw std::invalid_argument("Invalid batch buffer (maximum 4096)");
    }
    // Per-thread conversion scratch, warmed once; never exposes native object memory.
    struct AccessScratch
    {
        std::array<NcmaEngine::ObjectReference, 4096> References;
        std::array<NcmaEngine::Transform, 4096> Transforms;
        std::array<NcmaEngine::TransformWrite, 4096> Writes;
        std::array<NcmaEngine::GameplaySignal, 4096> Signals;
    };
    thread_local AccessScratch Scratch;
    static_assert(sizeof(NcmaObjectReference) == 24 && sizeof(NcmaTransformWrite) == 64);
    static_assert(sizeof(NcmaGameplaySignal) == 72 && offsetof(NcmaGameplaySignal, Value) == 56);
}

extern "C"
{
    std::uint32_t NCMA_NATIVE_CALL ncma_get_abi_version() { return 1; }
    std::uint32_t NCMA_NATIVE_CALL ncma_get_game_object_api_version() { return 4; }
    std::uint32_t NCMA_NATIVE_CALL ncma_get_world_access_api_version() { return 1; }

    std::uint8_t NCMA_NATIVE_CALL ncma_world_get_object_reference(
        NcmaWorldHandle world, std::uint64_t object, NcmaObjectReference* output)
    {
        return Guard([&] { if (!output) throw std::invalid_argument("Output reference is null");
            *output = ToNative(AsWorld(world)->GetReference(object)); });
    }
    std::uint8_t NCMA_NATIVE_CALL ncma_world_get_object_uuid(
        NcmaWorldHandle world, NcmaObjectReference object, std::uint64_t* high, std::uint64_t* low)
    {
        return Guard([&] { if (!high || !low) throw std::invalid_argument("Output UUID is null");
            auto& scene = *AsWorld(world);
            const auto uuid = scene.GetPersistentId(scene.Resolve(FromNative(object)));
            *high = uuid.High; *low = uuid.Low; });
    }
    std::uint8_t NCMA_NATIVE_CALL ncma_world_find_object(
        NcmaWorldHandle world, std::uint64_t high, std::uint64_t low, NcmaObjectReference* output)
    {
        return Guard([&] { if (!output) throw std::invalid_argument("Output reference is null");
            auto& scene = *AsWorld(world);
            *output = ToNative(scene.GetReference(scene.FindObject({high, low}))); });
    }
    std::uint8_t NCMA_NATIVE_CALL ncma_world_read_transforms(
        NcmaWorldHandle world, const NcmaObjectReference* objects, NcmaTransform* output, std::uint32_t count)
    {
        return Guard([&] {
            ValidateBuffer(objects, count); ValidateBuffer(output, count);
            auto& scene = *AsWorld(world);
            for (std::uint32_t i = 0; i < count; ++i) Scratch.References[i] = FromNative(objects[i]);
            scene.ReadTransforms({Scratch.References.data(), count}, {Scratch.Transforms.data(), count});
            for (std::uint32_t i = 0; i < count; ++i) output[i] = ToNative(Scratch.Transforms[i]);
        });
    }
    std::uint8_t NCMA_NATIVE_CALL ncma_world_write_transforms(
        NcmaWorldHandle world, const NcmaTransformWrite* writes, std::uint32_t count)
    {
        return Guard([&] {
            ValidateBuffer(writes, count);
            auto& scene = *AsWorld(world);
            for (std::uint32_t i = 0; i < count; ++i)
                Scratch.Writes[i] = {FromNative(writes[i].Object), FromNative(writes[i].Value)};
            scene.WriteTransforms({Scratch.Writes.data(), count});
        });
    }
    std::uint8_t NCMA_NATIVE_CALL ncma_world_begin_gameplay_phase(NcmaWorldHandle world)
        { return Guard([&] { AsWorld(world)->BeginGameplayPhase(); }); }
    std::uint8_t NCMA_NATIVE_CALL ncma_world_commit_gameplay_phase(NcmaWorldHandle world)
        { return Guard([&] { AsWorld(world)->CommitGameplayPhase(); }); }
    std::uint8_t NCMA_NATIVE_CALL ncma_world_abort_gameplay_phase(NcmaWorldHandle world)
        { return Guard([&] { AsWorld(world)->AbortGameplayPhase(); }); }
    std::uint8_t NCMA_NATIVE_CALL ncma_world_send_signal(NcmaWorldHandle world, const NcmaGameplaySignal* signal)
    {
        return Guard([&] { if (!signal) throw std::invalid_argument("Input signal is null");
            AsWorld(world)->SendSignal({FromNative(signal->Source), FromNative(signal->Target),
                signal->Code, signal->Value, signal->Sequence}); });
    }
    std::uint8_t NCMA_NATIVE_CALL ncma_world_receive_signals(
        NcmaWorldHandle world, NcmaObjectReference target, NcmaGameplaySignal* output,
        std::uint32_t capacity, std::uint32_t* count)
    {
        return Guard([&] {
            ValidateBuffer(output, capacity);
            if (!count) throw std::invalid_argument("Output count is null");
            const auto received = AsWorld(world)->ReceiveSignals(FromNative(target), {Scratch.Signals.data(), capacity});
            for (std::size_t i = 0; i < received; ++i) output[i] = ToNative(Scratch.Signals[i]);
            *count = static_cast<std::uint32_t>(received);
        });
    }

    NcmaWorldHandle NCMA_NATIVE_CALL ncma_world_create(const char* name)
    {
        try
        {
            LastError.clear();
            return new NcmaEngine::SceneWorld(name == nullptr ? "Untitled" : name);
        }
        catch (const std::exception& error)
        {
            CaptureError(error);
            return nullptr;
        }
    }

    void NCMA_NATIVE_CALL ncma_world_destroy(NcmaWorldHandle world)
    {
        delete static_cast<NcmaEngine::SceneWorld*>(world);
    }

    std::uint64_t NCMA_NATIVE_CALL ncma_world_create_object(
        NcmaWorldHandle world, const char* name, std::uint64_t parent)
    {
        try
        {
            LastError.clear();
            if (parent != NcmaEngine::InvalidGameObjectId)
                throw std::invalid_argument("Flat scenes do not support parented object creation");
            return AsWorld(world)->CreateObject(name == nullptr ? "GameObject" : name);
        }
        catch (const std::exception& error)
        {
            CaptureError(error);
            return NcmaEngine::InvalidGameObjectId;
        }
    }

    std::uint64_t NCMA_NATIVE_CALL ncma_world_create_object_with_language(
        NcmaWorldHandle world, const char* name, std::uint32_t language)
    {
        try
        {
            LastError.clear();
            if (language != 0)
                throw std::invalid_argument("Only C# gameplay is supported; Python gameplay has been removed");
            return AsWorld(world)->CreateObject(name == nullptr ? "GameObject" : name);
        }
        catch (const std::exception& error)
        {
            CaptureError(error);
            return NcmaEngine::InvalidGameObjectId;
        }
    }

    std::uint8_t NCMA_NATIVE_CALL ncma_world_get_object_language(
        NcmaWorldHandle world, std::uint64_t gameObject, std::uint32_t* output)
    {
        try
        {
            LastError.clear();
            if (output == nullptr) throw std::invalid_argument("Output language is null");
            *output = static_cast<std::uint32_t>(AsWorld(world)->GetLogicLanguage(gameObject));
            return true;
        }
        catch (const std::exception& error)
        {
            CaptureError(error);
            return false;
        }
    }

    std::uint8_t NCMA_NATIVE_CALL ncma_world_destroy_object(
        NcmaWorldHandle world, std::uint64_t gameObject, std::uint8_t recursive)
    {
        try
        {
            LastError.clear();
            (void)recursive; // Legacy flag has no meaning for independent objects.
            return AsWorld(world)->DestroyObject(gameObject);
        }
        catch (const std::exception& error)
        {
            CaptureError(error);
            return false;
        }
    }

    std::uint64_t NCMA_NATIVE_CALL ncma_world_create_node(
        NcmaWorldHandle world, const char* name, std::uint64_t parent)
    {
        return ncma_world_create_object(world, name, parent);
    }

    std::uint8_t NCMA_NATIVE_CALL ncma_world_destroy_node(
        NcmaWorldHandle world, std::uint64_t gameObject, std::uint8_t recursive)
    {
        return ncma_world_destroy_object(world, gameObject, recursive);
    }

    std::uint8_t NCMA_NATIVE_CALL ncma_world_set_parent(
        NcmaWorldHandle world, std::uint64_t gameObject, std::uint64_t parent)
    {
        try
        {
            LastError.clear();
            if (!AsWorld(world)->Contains(gameObject))
                throw std::out_of_range("Unknown scene GameObject");
            if (parent != NcmaEngine::InvalidGameObjectId)
                throw std::invalid_argument("Flat scenes do not support parenting");
            return true; // Compatibility detach is already satisfied by every flat object.
        }
        catch (const std::exception& error)
        {
            CaptureError(error);
            return false;
        }
    }

    std::uint8_t NCMA_NATIVE_CALL ncma_world_get_local_transform(
        NcmaWorldHandle world, std::uint64_t gameObject, NcmaTransform* output)
    {
        if (output == nullptr)
        {
            LastError = "Output transform is null";
            return false;
        }
        try
        {
            const auto transform = AsWorld(world)->ReadGameplayTransform(gameObject);
            *output = {
                {transform.Position.x(), transform.Position.y(), transform.Position.z()},
                transform.Rotation.x(), transform.Rotation.y(), transform.Rotation.z(), transform.Rotation.w(),
                {transform.Scale.x(), transform.Scale.y(), transform.Scale.z()}
            };
            LastError.clear();
            return true;
        }
        catch (const std::exception& error)
        {
            CaptureError(error);
            return false;
        }
    }

    std::uint8_t NCMA_NATIVE_CALL ncma_world_set_local_transform(
        NcmaWorldHandle world, std::uint64_t gameObject, const NcmaTransform* value)
    {
        if (value == nullptr)
        {
            LastError = "Input transform is null";
            return false;
        }
        try
        {
            AsWorld(world)->WriteGameplayTransform(gameObject, FromNative(*value));
            LastError.clear();
            return true;
        }
        catch (const std::exception& error)
        {
            CaptureError(error);
            return false;
        }
    }

    const char* NCMA_NATIVE_CALL ncma_get_last_error() { return LastError.c_str(); }
}
