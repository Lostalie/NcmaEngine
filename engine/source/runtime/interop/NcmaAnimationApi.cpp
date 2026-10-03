#include "interop/NcmaAnimationApi.h"
#include "animation/ActionAnimationWorkspace.h"

#include <mutex>
#include <unordered_map>

namespace
{
    std::mutex SessionsMutex;
    std::unordered_map<std::uint64_t, std::unique_ptr<NcmaEngine::Animation::ActionAnimationWorkspace>> Sessions;
    std::uint64_t NextSession = 1;
    thread_local std::string AnimationError, AnimationReply;

    auto& RequireSession(std::uint64_t id)
    {
        const auto it = Sessions.find(id);
        if (it == Sessions.end()) throw std::invalid_argument("Unknown or disposed animation session");
        return *it->second;
    }
}

extern "C"
{
    std::uint32_t NCMA_NATIVE_CALL ncma_animation_abi_version() { return 1; }

    std::uint64_t NCMA_NATIVE_CALL ncma_animation_create(std::uint32_t version)
    {
        try
        {
            std::scoped_lock lock(SessionsMutex);
            if (version != 1) throw std::invalid_argument("Unsupported animation ABI version");
            if (Sessions.size() >= 64) throw std::invalid_argument("Animation session limit reached");
            auto workspace = std::make_unique<NcmaEngine::Animation::ActionAnimationWorkspace>();
            const auto id = NextSession++;
            Sessions.emplace(id, std::move(workspace));
            AnimationError.clear();
            return id;
        }
        catch (const std::exception& error) { AnimationError = error.what(); }
        catch (...) { AnimationError = "Unknown native animation error"; }
        return 0;
    }

    void NCMA_NATIVE_CALL ncma_animation_destroy(std::uint64_t session)
    {
        try
        {
            std::scoped_lock lock(SessionsMutex);
            Sessions.erase(session);
            AnimationError.clear();
        }
        catch (...) { /* Finalization must never propagate exceptions across the ABI. */ }
    }

    std::uint8_t NCMA_NATIVE_CALL ncma_animation_command(
        std::uint64_t session, std::uint32_t command, double value, const char* text)
    {
        try
        {
            std::scoped_lock lock(SessionsMutex);
            RequireSession(session).Execute(static_cast<NcmaEngine::Animation::AnimationCommand>(command),
                value, text ? text : "");
            AnimationError.clear();
            return 1;
        }
        catch (const std::exception& error) { AnimationError = error.what(); }
        catch (...) { AnimationError = "Unknown native animation error"; }
        return 0;
    }

    const char* NCMA_NATIVE_CALL ncma_animation_inspect(std::uint64_t session)
    {
        try
        {
            std::scoped_lock lock(SessionsMutex);
            AnimationReply = RequireSession(session).InspectJson();
            AnimationError.clear();
            return AnimationReply.c_str();
        }
        catch (const std::exception& error) { AnimationError = error.what(); }
        catch (...) { AnimationError = "Unknown native animation error"; }
        return nullptr;
    }

    const char* NCMA_NATIVE_CALL ncma_animation_last_error() { return AnimationError.c_str(); }
}
