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
        using TickGameplay = int(__cdecl*)(double, char*, int);
        using UnloadGameplay = int(__cdecl*)(char*, int);
        using ManagedGetTickCountFunction = std::int64_t(__cdecl*)();
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
        TickGameplay Tick = nullptr;
        UnloadGameplay Unload = nullptr;
        ManagedGetTickCountFunction ManagedTickCount = nullptr;
        std::uint32_t(__cdecl* BridgeVersion)() = nullptr;
        int(__cdecl* TypeInfo)(int, char*, int, char*, int) = nullptr;
        int(__cdecl* PropertyInfo)(int, int, NcmaExportPropertyV2*, char*, int) = nullptr;
        int(__cdecl* BeginScene)(std::uint64_t, char*, int) = nullptr;
        int(__cdecl* CreateBehaviour)(int, std::uint64_t, int, char*, int) = nullptr;
        int(__cdecl* SetProperty)(int, int, double, char*, int) = nullptr;
        UnloadGameplay ActivateScene = nullptr;
        UnloadGameplay EndScene = nullptr;
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
                resolve(L"LoadGameplay", Load); resolve(L"Tick", Tick); resolve(L"UnloadGameplay", Unload);
                resolve(L"GetTickCount", ManagedTickCount); resolve(L"GetTypeInfo", TypeInfo);
                resolve(L"GetPropertyInfo", PropertyInfo); resolve(L"BeginScene", BeginScene);
                resolve(L"CreateBehaviour", CreateBehaviour); resolve(L"SetProperty", SetProperty);
                resolve(L"ActivateScene", ActivateScene); resolve(L"EndScene", EndScene);
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
            Started = true; // Ensure partial metadata failures can unload the newly loaded context.
            Types.clear();
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
                Types.push_back(std::move(type));
            }
            BehaviourCount = 0;
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
        std::array<char, 2048> error{};
        if (m_Implementation->Unload(error.data(), static_cast<int>(error.size())) < 0)
            m_Implementation->LastError = error.data();
        m_Implementation->Started = false;
        m_Implementation->BehaviourCount = 0;
        m_Implementation->SceneBound = false;
        m_Implementation->Types.clear();
    }

    void DotNetGameplayRuntime::Tick(double deltaSeconds)
    {
        if (!m_Implementation->Started || !m_Implementation->SceneBound)
            return;
        std::array<char, 2048> error{};
        if (m_Implementation->Tick(deltaSeconds, error.data(), static_cast<int>(error.size())) < 0)
            m_Implementation->LastError = error.data();
        else
            m_Implementation->LastError.clear();
    }

    bool DotNetGameplayRuntime::Reload(std::string& error)
    {
        Stop();
        return Start(error);
    }

    bool DotNetGameplayRuntime::IsStarted() const noexcept { return m_Implementation->Started; }
    const std::vector<BehaviourDescriptor>& DotNetGameplayRuntime::GetTypes() const noexcept
    { return m_Implementation->Types; }

    void DotNetGameplayRuntime::EndScene() noexcept
    {
        auto& runtime = *m_Implementation;
        if (!runtime.Started || !runtime.SceneBound) return;
        std::array<char, 2048> message{};
        if (runtime.EndScene(message.data(), static_cast<int>(message.size())) < 0)
            runtime.LastError = message.data();
        runtime.SceneBound = false;
        runtime.BehaviourCount = 0;
    }

    bool DotNetGameplayRuntime::BindScene(ManagedSceneClient& world, std::string& error)
    {
        if (!Start(error)) return false;
        EndScene();
        auto& runtime = *m_Implementation;
        std::array<char, 2048> message{};
        constexpr int capacity = static_cast<int>(message.size());
        if (runtime.BeginScene(world.Handle(), message.data(), capacity) < 0)
        { error = message.data(); return false; }
        runtime.SceneBound = true;
        const auto fail = [&](std::string reason) {
            EndScene();
            error = std::move(reason);
            runtime.LastError = error;
            return false;
        };
        for (const auto& gameObject : world.CaptureView().Objects)
        {
            for (const auto& binding : gameObject.Behaviours)
            {
                const auto type = std::find_if(runtime.Types.begin(), runtime.Types.end(), [&](const auto& item) {
                    return item.TypeName == binding.TypeName;
                });
                if (type == runtime.Types.end()) return fail("Missing Behaviour type: " + binding.TypeName);
                const int instance = runtime.CreateBehaviour(static_cast<int>(type - runtime.Types.begin()),
                    world.FindObject(gameObject.PersistentId), binding.Enabled ? 1 : 0, message.data(), capacity);
                if (instance < 0) return fail(message.data());
                for (const auto& value : binding.Properties)
                {
                    const auto property = std::find_if(type->Properties.begin(), type->Properties.end(), [&](const auto& item) {
                        return item.Default.Name == value.Name && item.Default.Kind == value.Kind;
                    });
                    if (property == type->Properties.end()) return fail("Missing or changed Export: " + value.Name);
                    if (!ValidExportValue(value)) return fail("Invalid Export value: " + value.Name);
                    if (runtime.SetProperty(instance, static_cast<int>(property - type->Properties.begin()),
                        value.Value, message.data(), capacity) < 0) return fail(message.data());
                }
            }
        }
        const int count = runtime.ActivateScene(message.data(), capacity);
        if (count < 0) return fail(message.data());
        runtime.BehaviourCount = count;
        runtime.LastError.clear();
        error.clear();
        return true;
    }
    int DotNetGameplayRuntime::GetBehaviourCount() const noexcept { return m_Implementation->BehaviourCount; }

    std::int64_t DotNetGameplayRuntime::GetTickCount() const noexcept
    {
        return m_Implementation->ManagedTickCount == nullptr ? 0 : m_Implementation->ManagedTickCount();
    }

    const std::string& DotNetGameplayRuntime::GetLastError() const noexcept
    {
        return m_Implementation->LastError;
    }
}
