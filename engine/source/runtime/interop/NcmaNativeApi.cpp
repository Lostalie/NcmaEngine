#include "interop/NcmaNativeApi.h"

#include "scene/SceneWorld.h"

#include <exception>
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
        return static_cast<NcmaEngine::SceneWorld*>(handle);
    }

    void CaptureError(const std::exception& error) { LastError = error.what(); }
}

extern "C"
{
    std::uint32_t NCMA_NATIVE_CALL ncma_get_abi_version() { return 1; }

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

    std::uint64_t NCMA_NATIVE_CALL ncma_world_create_node(
        NcmaWorldHandle world, const char* name, std::uint64_t parent)
    {
        try
        {
            LastError.clear();
            return AsWorld(world)->CreateNode(name == nullptr ? "Node" : name, parent);
        }
        catch (const std::exception& error)
        {
            CaptureError(error);
            return NcmaEngine::InvalidNodeId;
        }
    }

    std::uint8_t NCMA_NATIVE_CALL ncma_world_destroy_node(
        NcmaWorldHandle world, std::uint64_t node, std::uint8_t recursive)
    {
        try
        {
            LastError.clear();
            return AsWorld(world)->DestroyNode(node, recursive
                ? NcmaEngine::DestroyPolicy::Recursive
                : NcmaEngine::DestroyPolicy::ReparentChildren);
        }
        catch (const std::exception& error)
        {
            CaptureError(error);
            return false;
        }
    }

    std::uint8_t NCMA_NATIVE_CALL ncma_world_set_parent(
        NcmaWorldHandle world, std::uint64_t node, std::uint64_t parent)
    {
        try
        {
            LastError.clear();
            return AsWorld(world)->SetParent(node, parent);
        }
        catch (const std::exception& error)
        {
            CaptureError(error);
            return false;
        }
    }

    std::uint8_t NCMA_NATIVE_CALL ncma_world_get_local_transform(
        NcmaWorldHandle world, std::uint64_t node, NcmaTransform* output)
    {
        if (output == nullptr)
        {
            LastError = "Output transform is null";
            return false;
        }
        try
        {
            const auto& transform = AsWorld(world)->GetLocalTransform(node);
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
        NcmaWorldHandle world, std::uint64_t node, const NcmaTransform* value)
    {
        if (value == nullptr)
        {
            LastError = "Input transform is null";
            return false;
        }
        try
        {
            auto& transform = AsWorld(world)->GetLocalTransform(node);
        transform.Position = NcmaEngine::Vector3(value->Position.X, value->Position.Y, value->Position.Z);
        transform.Rotation = NcmaEngine::Quaternion(
                value->RotationW, value->RotationX, value->RotationY, value->RotationZ).normalized();
        transform.Scale = NcmaEngine::Vector3(value->Scale.X, value->Scale.Y, value->Scale.Z);
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
