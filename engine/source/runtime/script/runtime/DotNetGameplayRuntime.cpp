#include "script/runtime/DotNetGameplayRuntime.h"
#include "interop/NcmaGameplayBridge.h"
#include "script/runtime/ManagedHost.h"

#include <Windows.h>

#include <algorithm>
#include <array>
#include <format>
#include <optional>
#include <tuple>
#include <system_error>
#include <vector>

namespace NcmaEngine::Scripting
{
    namespace
    {
        using LoadGameplay = int(__cdecl*)(const char*, char*, int);
        using AdvanceGameplay = int(__cdecl*)(std::uint64_t, std::uint64_t, std::uint64_t, double, NcmaPlayStatusV5*, char*, int);
        using UnloadGameplay = int(__cdecl*)(char*, int);
        std::string ToUtf8(const std::filesystem::path& path)
        {
            const auto value = path.u8string();
            return {reinterpret_cast<const char*>(value.data()), value.size()};
        }
    }

    struct DotNetGameplayRuntime::Implementation final
    {
        RuntimeDescriptor Descriptor{
            Language::CSharp, RuntimeRole::Gameplay, ".NET Gameplay", "8.0+", true, false};
        std::filesystem::path RuntimeConfig;
        std::filesystem::path HostAssembly;
        std::filesystem::path GameplayAssembly;
        LoadGameplay Load = nullptr;
        AdvanceGameplay Advance = nullptr;
        UnloadGameplay Unload = nullptr;
        std::uint64_t SceneHandle = 0;
        NcmaPlayStatusV5 Status;
        std::uint32_t(__cdecl* BridgeVersion)() = nullptr;
        int(__cdecl* TypeInfo)(int, char*, int, char*, int) = nullptr;
        int(__cdecl* PropertyInfo)(int, int, NcmaExportPropertyV2*, char*, int) = nullptr;
        int(__cdecl* BeginScene)(std::uint64_t, NcmaPlayStatusV5*, char*, int) = nullptr;
        int(__cdecl* ControlPlay)(std::uint64_t, std::uint64_t, std::uint64_t, std::uint32_t, NcmaPlayStatusV5*, char*, int) = nullptr;
        int(__cdecl* EndScene)(std::uint64_t, std::uint64_t, std::uint64_t, char*, int) = nullptr;
        int(__cdecl* SubmitPlayInput)(std::uint64_t, const NcmaInputFrameV1*, char*, int) = nullptr;
        int(__cdecl* GetRenderFrame)(std::uint64_t, std::uint64_t, std::uint64_t, NcmaRenderHeaderV1*, NcmaRenderObjectV1*, int, char*, int) = nullptr;
        int(__cdecl* ReloadGameplay)(const char*, std::uint64_t, std::uint64_t, std::uint64_t, NcmaPlayStatusV5*, char*, int) = nullptr;
        std::vector<BehaviourDescriptor> Types;
        bool SceneBound = false;
        bool Started = false;
        int BehaviourCount = 0;
        std::string LastError;

        bool InitializeHost(std::string& error)
        {
            try
            {
                ManagedHost::Configure(RuntimeConfig, HostAssembly);
                const auto resolve = [](const wchar_t* method, auto& output) {
                    output = reinterpret_cast<std::remove_reference_t<decltype(output)>>(ManagedHost::Resolve(method));
                };
                resolve(L"GetBridgeVersion", BridgeVersion);
                if (BridgeVersion() != NcmaGameplayBridgeVersion) throw std::runtime_error("Managed scene bridge ABI mismatch");
                resolve(L"LoadGameplay", Load); resolve(L"AdvanceFrame", Advance); resolve(L"UnloadGameplay", Unload);
                resolve(L"GetTypeInfo", TypeInfo);
                resolve(L"GetPropertyInfo", PropertyInfo); resolve(L"BeginScene", BeginScene);
                resolve(L"ControlPlay", ControlPlay); resolve(L"EndScene", EndScene);
                resolve(L"ReloadGameplay", ReloadGameplay);
                resolve(L"SubmitPlayInput", SubmitPlayInput); resolve(L"GetRenderFrame", GetRenderFrame);
                return true;
            }
            catch (const std::exception& exception) { error = exception.what(); return false; }
        }

        bool LoadGameplayAssembly(std::string& error)
        {
            std::array<char, 2048> managedError{};
            const std::string path = ToUtf8(std::filesystem::absolute(GameplayAssembly));
            const int result = Load(path.c_str(), managedError.data(), static_cast<int>(managedError.size()));
            if (result < 0)
            {
                error = managedError[0] == '\0' ? "Managed gameplay load failed" : managedError.data();
                LastError = error;
                return false;
            }
            return ReadTypes(result, error);
        }
        bool ReadTypes(int result, std::string& error)
        {
            std::array<char, 2048> managedError{};
            std::vector<BehaviourDescriptor> candidate;
            Started = true; // Ensure partial metadata failures can unload the newly loaded context.
            for (int t = 0; t < result; ++t)
            {
                std::array<char, 512> typeName{};
                const int count = TypeInfo(t, typeName.data(), static_cast<int>(typeName.size()),
                    managedError.data(), static_cast<int>(managedError.size()));
                if (count < 0) { error = managedError.data(); Types.clear(); return false; }
                BehaviourDescriptor type{typeName.data(), {}};
                for (int p = 0; p < count; ++p)
                {
                    NcmaExportPropertyV2 property;
                    if (PropertyInfo(t, p, &property, managedError.data(), static_cast<int>(managedError.size())) < 0)
                    { error = managedError.data(); Types.clear(); return false; }
                    type.Properties.push_back({{property.Name, static_cast<ExportKind>(property.Kind),
                        property.DefaultValue}, property.DisplayName, property.Category});
                }
                candidate.push_back(std::move(type));
            }
            Types = std::move(candidate);
            Started = true;
            LastError.clear();
            error.clear();
            return true;
        }

    };

    DotNetGameplayRuntime::DotNetGameplayRuntime(
        std::filesystem::path runtimeConfig,
        std::filesystem::path hostAssembly,
        std::filesystem::path gameplayAssembly)
        : m_Implementation(std::make_unique<Implementation>())
    {
        m_Implementation->RuntimeConfig = std::filesystem::absolute(std::move(runtimeConfig));
        m_Implementation->HostAssembly = std::filesystem::absolute(std::move(hostAssembly));
        m_Implementation->GameplayAssembly = std::filesystem::absolute(std::move(gameplayAssembly));
    }

    DotNetGameplayRuntime::~DotNetGameplayRuntime()
    {
        Stop();
    }

    const RuntimeDescriptor& DotNetGameplayRuntime::GetDescriptor() const noexcept
    {
        return m_Implementation->Descriptor;
    }

    bool DotNetGameplayRuntime::Start(std::string& error)
    {
        if (m_Implementation->Started)
        {
            error.clear();
            return true;
        }
        try
        {
            if (m_Implementation->InitializeHost(error) && m_Implementation->LoadGameplayAssembly(error))
                return true;
        }
        catch (const std::exception& exception)
        {
            error = exception.what();
        }
        Stop();
        m_Implementation->LastError = error;
        return false;
    }

    void DotNetGameplayRuntime::Stop() noexcept
    {
        if (!m_Implementation->Started || m_Implementation->Unload == nullptr)
            return;
        EndScene();
        std::array<char, 2048> error{};
        if (m_Implementation->Unload(error.data(), static_cast<int>(error.size())) < 0)
            m_Implementation->LastError = error.data();
        m_Implementation->Started = false;
        m_Implementation->BehaviourCount = 0;
        m_Implementation->SceneBound = false;
        m_Implementation->SceneHandle = 0;
        m_Implementation->Status = {};
        m_Implementation->Types.clear();
    }

    void DotNetGameplayRuntime::Tick(double deltaSeconds) { AdvanceFrame(deltaSeconds); }
    void DotNetGameplayRuntime::AdvanceFrame(double deltaSeconds)
    {
        auto& runtime = *m_Implementation;
        if (!runtime.Started || !runtime.SceneBound) return;
        std::array<char, 2048> error{};
        if (runtime.Advance(runtime.SceneHandle, runtime.Status.SessionHigh, runtime.Status.SessionLow,
            deltaSeconds, &runtime.Status, error.data(), static_cast<int>(error.size())) < 0)
            runtime.LastError = error.data();
        else runtime.LastError.clear();
    }
    bool DotNetGameplayRuntime::SubmitInput(NcmaInputFrameV1 input, std::string& error)
    {
        auto& runtime = *m_Implementation;
        if (!runtime.SceneBound) { error = "No bound Play session"; return false; }
        input.SessionHigh = runtime.Status.SessionHigh; input.SessionLow = runtime.Status.SessionLow;
        std::array<char, 2048> message{};
        if (runtime.SubmitPlayInput(runtime.SceneHandle, &input, message.data(), static_cast<int>(message.size())) < 0)
        { error = message.data(); return false; }
        error.clear(); return true;
    }
    bool DotNetGameplayRuntime::ReadRenderFrame(std::vector<NcmaRenderObjectV1>& output, NcmaRenderHeaderV1& header, std::string& error)
    {
        auto& runtime = *m_Implementation;
        if (!runtime.SceneBound) { error = "No bound Play session"; return false; }
        std::vector<NcmaRenderObjectV1> candidate(4096);
        std::array<char, 2048> message{};
        const int count = runtime.GetRenderFrame(runtime.SceneHandle, runtime.Status.SessionHigh, runtime.Status.SessionLow,
            &header, candidate.data(), static_cast<int>(candidate.size()), message.data(), static_cast<int>(message.size()));
        if (count < 0) { error = message.data(); return false; }
        candidate.resize(static_cast<std::size_t>(count)); output = std::move(candidate); error.clear(); return true;
    }
    bool DotNetGameplayRuntime::Control(std::uint32_t command, std::string& error)
    {
        auto& runtime = *m_Implementation;
        if (!runtime.Started || !runtime.SceneBound) { error = "No bound Play session"; return false; }
        std::array<char, 2048> message{};
        if (runtime.ControlPlay(runtime.SceneHandle, runtime.Status.SessionHigh, runtime.Status.SessionLow,
            command, &runtime.Status, message.data(), static_cast<int>(message.size())) < 0)
        { error = message.data(); runtime.LastError = error; return false; }
        error.clear(); runtime.LastError.clear(); return true;
    }
    bool DotNetGameplayRuntime::Pause(std::string& error) { return Control(0, error); }
    bool DotNetGameplayRuntime::Resume(std::string& error) { return Control(1, error); }
    bool DotNetGameplayRuntime::Step(std::string& error) { return Control(2, error); }
    const NcmaPlayStatusV5& DotNetGameplayRuntime::GetPlayStatus() const noexcept { return m_Implementation->Status; }

    bool DotNetGameplayRuntime::Reload(std::string& error)
    {
        auto& runtime = *m_Implementation;
        if (!runtime.Started) return Start(error);
        const auto path = ToUtf8(runtime.GameplayAssembly);
        std::array<char, 2048> message{};
        const int count = runtime.ReloadGameplay(path.c_str(), runtime.SceneHandle, runtime.Status.SessionHigh, runtime.Status.SessionLow,
            &runtime.Status, message.data(), static_cast<int>(message.size()));
        if (count < 0) { error = message.data(); runtime.LastError = error; return false; }
        return runtime.ReadTypes(count, error);
    }

    bool DotNetGameplayRuntime::IsStarted() const noexcept { return m_Implementation->Started; }
    const std::vector<BehaviourDescriptor>& DotNetGameplayRuntime::GetTypes() const noexcept
    { return m_Implementation->Types; }

    void DotNetGameplayRuntime::EndScene() noexcept
    {
        auto& runtime = *m_Implementation;
        if (!runtime.Started || !runtime.SceneBound) return;
        std::array<char, 2048> message{};
        if (runtime.EndScene(runtime.SceneHandle, runtime.Status.SessionHigh, runtime.Status.SessionLow,
            message.data(), static_cast<int>(message.size())) < 0)
            runtime.LastError = message.data();
        runtime.SceneBound = false;
        runtime.SceneHandle = 0; runtime.Status = {};
        runtime.BehaviourCount = 0;
    }

    bool DotNetGameplayRuntime::BindScene(ManagedSceneClient& world, std::string& error)
    {
        if (!Start(error)) return false;
        EndScene();
        auto& runtime = *m_Implementation;
        std::array<char, 2048> message{};
        const int count = runtime.BeginScene(world.Handle(), &runtime.Status,
            message.data(), static_cast<int>(message.size()));
        if (count < 0)
        { error = message.data(); runtime.LastError = error; return false; }
        runtime.SceneHandle = world.Handle();
        runtime.SceneBound = true;
        runtime.BehaviourCount = count;
        runtime.LastError.clear(); error.clear();
        return true;
    }
    int DotNetGameplayRuntime::GetBehaviourCount() const noexcept { return m_Implementation->BehaviourCount; }

    std::int64_t DotNetGameplayRuntime::GetTickCount() const noexcept
    {
        return static_cast<std::int64_t>(m_Implementation->Status.Tick);
    }

    const std::string& DotNetGameplayRuntime::GetLastError() const noexcept
    {
        return m_Implementation->LastError;
    }
}
