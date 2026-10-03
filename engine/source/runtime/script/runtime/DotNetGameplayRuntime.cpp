#include "script/runtime/DotNetGameplayRuntime.h"
#include "interop/NcmaGameplayBridge.h"

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
        using HostContext = void*;
        enum class HostDelegateType : int { LoadAssemblyAndGetFunctionPointer = 5 };

        using InitializeForRuntimeConfig = int(__cdecl*)(const wchar_t*, const void*, HostContext*);
        using GetRuntimeDelegate = int(__cdecl*)(HostContext, HostDelegateType, void**);
        using CloseHostContext = int(__cdecl*)(HostContext);
        using LoadAssemblyAndGetFunctionPointer = int(__stdcall*)(
            const wchar_t*, const wchar_t*, const wchar_t*, const wchar_t*, void*, void**);
        using LoadGameplay = int(__cdecl*)(const char*, char*, int);
        using TickGameplay = int(__cdecl*)(double, char*, int);
        using UnloadGameplay = int(__cdecl*)(char*, int);
        using ManagedGetTickCountFunction = std::int64_t(__cdecl*)();

        std::optional<std::filesystem::path> ReadDotNetRootFromRegistry()
        {
            std::array<wchar_t, 32768> value{};
            DWORD size = static_cast<DWORD>(value.size() * sizeof(wchar_t));
            const LSTATUS result = RegGetValueW(
                HKEY_LOCAL_MACHINE,
                L"SOFTWARE\\dotnet\\Setup\\InstalledVersions\\x64",
                L"InstallLocation", RRF_RT_REG_SZ, nullptr, value.data(), &size);
            if (result != ERROR_SUCCESS || value[0] == L'\0')
                return std::nullopt;
            return std::filesystem::path(value.data());
        }

        std::optional<std::tuple<int, int, int>> ParseVersion(std::wstring_view name)
        {
            std::vector<int> result;
            std::size_t begin = 0;
            while (begin < name.size())
            {
                const std::size_t end = name.find(L'.', begin);
                const std::wstring_view part = name.substr(
                    begin, end == std::wstring_view::npos ? name.size() - begin : end - begin);
                if (part.empty())
                {
                    result.clear();
                    return std::nullopt;
                }
                int number = 0;
                for (const wchar_t character : part)
                {
                    if (character < L'0' || character > L'9')
                    {
                        result.clear();
                        return std::nullopt;
                    }
                    number = number * 10 + static_cast<int>(character - L'0');
                }
                result.push_back(number);
                if (end == std::wstring_view::npos)
                    break;
                begin = end + 1;
            }
            if (result.size() < 3)
                return std::nullopt;
            return std::tuple(result[0], result[1], result[2]);
        }

        std::optional<std::filesystem::path> FindHostFxr(std::string& error)
        {
            std::vector<std::filesystem::path> roots;
            std::array<wchar_t, 32768> environment{};
            const DWORD environmentLength = GetEnvironmentVariableW(
                L"DOTNET_ROOT", environment.data(), static_cast<DWORD>(environment.size()));
            if (environmentLength > 0 && environmentLength < environment.size())
                roots.emplace_back(environment.data());
            if (const auto registryRoot = ReadDotNetRootFromRegistry(); registryRoot.has_value())
                roots.push_back(*registryRoot);
            std::array<wchar_t, 32768> programFiles{};
            const DWORD programFilesLength = GetEnvironmentVariableW(
                L"ProgramFiles", programFiles.data(), static_cast<DWORD>(programFiles.size()));
            if (programFilesLength > 0 && programFilesLength < programFiles.size())
                roots.emplace_back(std::filesystem::path(programFiles.data()) / "dotnet");

            for (const std::filesystem::path& root : roots)
            {
                const std::filesystem::path fxrRoot = root / "host" / "fxr";
                std::error_code filesystemError;
                if (!std::filesystem::is_directory(fxrRoot, filesystemError))
                    continue;
                std::filesystem::path bestPath;
                std::optional<std::tuple<int, int, int>> bestVersion;
                for (const auto& entry : std::filesystem::directory_iterator(fxrRoot, filesystemError))
                {
                    if (filesystemError || !entry.is_directory())
                        continue;
                    const auto version = ParseVersion(entry.path().filename().wstring());
                    const std::filesystem::path candidate = entry.path() / "hostfxr.dll";
                    if (version.has_value() && std::get<0>(*version) >= 8 &&
                        std::filesystem::is_regular_file(candidate, filesystemError) &&
                        (!bestVersion.has_value() || *version > *bestVersion))
                    {
                        bestVersion = version;
                        bestPath = candidate;
                    }
                }
                if (!bestPath.empty())
                    return bestPath;
            }
            error = "Could not locate an x64 .NET hostfxr.dll. Install the .NET 8 Desktop Runtime or SDK.";
            return std::nullopt;
        }

        template<typename Function>
        Function GetExport(HMODULE module, const char* name, std::string& error)
        {
            const auto function = reinterpret_cast<Function>(GetProcAddress(module, name));
            if (function == nullptr)
                error = std::format("hostfxr.dll does not export {}", name);
            return function;
        }

        std::string ToUtf8(const std::filesystem::path& path)
        {
            const std::u8string value = path.u8string();
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
        HMODULE HostFxrModule = nullptr;
        LoadAssemblyAndGetFunctionPointer LoadManagedFunction = nullptr;
        LoadGameplay Load = nullptr;
        TickGameplay Tick = nullptr;
        UnloadGameplay Unload = nullptr;
        ManagedGetTickCountFunction ManagedTickCount = nullptr;
        std::uint32_t(__cdecl* BridgeVersion)() = nullptr;
        int(__cdecl* TypeInfo)(int, char*, int, char*, int) = nullptr;
        int(__cdecl* PropertyInfo)(int, int, NcmaExportPropertyV2*, char*, int) = nullptr;
        int(__cdecl* BeginScene)(void*, char*, int) = nullptr;
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
            if (LoadManagedFunction != nullptr)
                return ResolveEntrypoints(error);
            for (const auto& required : {RuntimeConfig, HostAssembly, GameplayAssembly})
            {
                if (!std::filesystem::is_regular_file(required))
                {
                    error = "Required managed file was not found: " + required.string();
                    return false;
                }
            }
            const auto hostFxrPath = FindHostFxr(error);
            if (!hostFxrPath.has_value())
                return false;
            ReleaseHostFxrModule();
            HostFxrModule = LoadLibraryW(hostFxrPath->c_str());
            if (HostFxrModule == nullptr)
            {
                error = "Could not load " + hostFxrPath->string();
                return false;
            }
            const auto initialize = GetExport<InitializeForRuntimeConfig>(
                HostFxrModule, "hostfxr_initialize_for_runtime_config", error);
            const auto getDelegate = GetExport<GetRuntimeDelegate>(
                HostFxrModule, "hostfxr_get_runtime_delegate", error);
            const auto close = GetExport<CloseHostContext>(HostFxrModule, "hostfxr_close", error);
            if (initialize == nullptr || getDelegate == nullptr || close == nullptr)
                return false;

            HostContext context = nullptr;
            const int initializeResult = initialize(RuntimeConfig.c_str(), nullptr, &context);
            if (initializeResult < 0 || context == nullptr)
            {
                error = std::format("hostfxr failed to initialize .NET (0x{:08x})", initializeResult);
                return false;
            }
            void* delegate = nullptr;
            const int delegateResult = getDelegate(
                context, HostDelegateType::LoadAssemblyAndGetFunctionPointer, &delegate);
            (void)close(context);
            if (delegateResult < 0 || delegate == nullptr)
            {
                error = std::format("hostfxr could not create the assembly loader (0x{:08x})", delegateResult);
                return false;
            }
            LoadManagedFunction = reinterpret_cast<LoadAssemblyAndGetFunctionPointer>(delegate);
            return ResolveEntrypoints(error);
        }

        bool ResolveEntrypoints(std::string& error)
        {
            constexpr const wchar_t* typeName = L"Ncma.ManagedHost.NativeEntry, Ncma.Managed.Host";
            const auto unmanagedCallersOnly = reinterpret_cast<const wchar_t*>(static_cast<std::intptr_t>(-1));
            const auto resolve = [&](const wchar_t* name, void** output) {
                const int result = LoadManagedFunction(
                    HostAssembly.c_str(), typeName, name, unmanagedCallersOnly, nullptr, output);
                if (result < 0 || *output == nullptr)
                {
                    error = std::format("Could not resolve managed entry point (0x{:08x})", result);
                    return false;
                }
                return true;
            };
            if (!resolve(L"GetBridgeVersion", reinterpret_cast<void**>(&BridgeVersion))) return false;
            if (BridgeVersion() != NcmaGameplayBridgeVersion)
            {
                error = "Gameplay bridge ABI mismatch; rebuild the managed host with Build.bat";
                return false;
            }
            return resolve(L"LoadGameplay", reinterpret_cast<void**>(&Load)) &&
                resolve(L"Tick", reinterpret_cast<void**>(&Tick)) &&
                resolve(L"UnloadGameplay", reinterpret_cast<void**>(&Unload)) &&
                resolve(L"GetTickCount", reinterpret_cast<void**>(&ManagedTickCount)) &&
                resolve(L"GetTypeInfo", reinterpret_cast<void**>(&TypeInfo)) &&
                resolve(L"GetPropertyInfo", reinterpret_cast<void**>(&PropertyInfo)) &&
                resolve(L"BeginScene", reinterpret_cast<void**>(&BeginScene)) &&
                resolve(L"CreateBehaviour", reinterpret_cast<void**>(&CreateBehaviour)) &&
                resolve(L"SetProperty", reinterpret_cast<void**>(&SetProperty)) &&
                resolve(L"ActivateScene", reinterpret_cast<void**>(&ActivateScene)) &&
                resolve(L"EndScene", reinterpret_cast<void**>(&EndScene));
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

        void ReleaseHostFxrModule() noexcept
        {
            if (HostFxrModule != nullptr)
            {
                FreeLibrary(HostFxrModule);
                HostFxrModule = nullptr;
            }
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
        m_Implementation->ReleaseHostFxrModule();
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

    bool DotNetGameplayRuntime::BindScene(SceneWorld& world, std::string& error)
    {
        if (!Start(error)) return false;
        EndScene();
        auto& runtime = *m_Implementation;
        std::array<char, 2048> message{};
        constexpr int capacity = static_cast<int>(message.size());
        if (runtime.BeginScene(&world, message.data(), capacity) < 0)
        { error = message.data(); return false; }
        runtime.SceneBound = true;
        const auto fail = [&](std::string reason) {
            EndScene();
            error = std::move(reason);
            runtime.LastError = error;
            return false;
        };
        for (const auto& node : world.CaptureSnapshot().Nodes)
        {
            for (const auto& binding : node.Behaviours)
            {
                if (binding.Language != BehaviourLanguage::CSharp) continue;
                const auto type = std::find_if(runtime.Types.begin(), runtime.Types.end(), [&](const auto& item) {
                    return item.TypeName == binding.TypeName;
                });
                if (type == runtime.Types.end()) return fail("Missing Behaviour type: " + binding.TypeName);
                const int instance = runtime.CreateBehaviour(static_cast<int>(type - runtime.Types.begin()),
                    world.FindNode(node.PersistentId), binding.Enabled ? 1 : 0, message.data(), capacity);
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
