#include "script/runtime/ManagedHost.h"
#include <Windows.h>
#include <filesystem>
#include <stdexcept>
#include <array>
#include <format>
#include <optional>
#include <tuple>
#include <vector>
#include <thread>
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


    }


    namespace
    {
        struct Host
        {
            std::filesystem::path RuntimeConfig, HostAssembly;
            HMODULE HostFxrModule = nullptr;
            LoadAssemblyAndGetFunctionPointer LoadManagedFunction = nullptr;
            void ReleaseHostFxrModule() noexcept
            {
                if (HostFxrModule) { FreeLibrary(HostFxrModule); HostFxrModule = nullptr; }
            }
        bool InitializeHost(std::string& error)
        {
            if (LoadManagedFunction != nullptr)
                return true;
            for (const auto& required : {RuntimeConfig, HostAssembly})
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
            return true;
        }


        };
        Host& HostStorage() { static Host host; return host; }
        Host& GetHost()
        {
            // CLR and its loader live for the process lifetime, never unload below managed delegates.
            static const auto owner = std::this_thread::get_id();
            if (std::this_thread::get_id() != owner) throw std::runtime_error("Managed host bootstrap requires its owner thread");
            auto& host = HostStorage();
            if (host.HostAssembly.empty())
            {
                std::array<wchar_t, 32768> executable{};
                const auto length = GetModuleFileNameW(nullptr, executable.data(), static_cast<DWORD>(executable.size()));
                if (length == 0 || length >= executable.size()) throw std::runtime_error("Cannot locate process executable");
                for (auto root : {std::filesystem::path(executable.data()).parent_path(), std::filesystem::current_path()})
                    while (!root.empty())
                    {
                        auto assembly = root / "out" / "managed" / "Ncma.Managed.Host.dll";
                        if (std::filesystem::is_regular_file(assembly))
                        {
                            host.HostAssembly = assembly;
                            host.RuntimeConfig = assembly.parent_path() / "Ncma.Managed.Host.runtimeconfig.json";
                            return host;
                        }
                        const auto parent = root.parent_path();
                        if (parent == root) break;
                        root = parent;
                    }
                throw std::runtime_error("Managed scene host not deployed. Run Build.bat (without -SkipManaged).");
            }
            return host;
        }
    }
    void ManagedHost::Configure(const std::filesystem::path& runtimeConfig, const std::filesystem::path& hostAssembly)
    {
        auto& host = GetHost();
        const auto config = std::filesystem::weakly_canonical(runtimeConfig);
        const auto assembly = std::filesystem::weakly_canonical(hostAssembly);
        if (host.LoadManagedFunction && (config != std::filesystem::weakly_canonical(host.RuntimeConfig) || assembly != std::filesystem::weakly_canonical(host.HostAssembly)))
            throw std::runtime_error("A different managed host is already loaded");
        host.RuntimeConfig = config; host.HostAssembly = assembly;
    }
    void* ManagedHost::Resolve(const wchar_t* method)
    {
        auto& host = GetHost();
        std::string error;
        if (!host.InitializeHost(error)) throw std::runtime_error(error);
        void* output = nullptr;
        const int result = host.LoadManagedFunction(host.HostAssembly.c_str(),
            L"Ncma.ManagedHost.NativeEntry, Ncma.Managed.Host", method,
            reinterpret_cast<const wchar_t*>(static_cast<std::intptr_t>(-1)), nullptr, &output);
        if (result < 0 || !output) throw std::runtime_error(std::format("Managed host entry point unavailable (0x{:08x}); rebuild host", result));
        return output;
    }
}
