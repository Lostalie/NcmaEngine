#pragma once

#include <cstdint>
#include <string>
#include <string_view>

namespace NcmaEngine::Scripting
{
    enum class Language : std::uint8_t
    {
        CSharp
    };

    enum class RuntimeRole : std::uint8_t
    {
        Gameplay
    };

    struct RuntimeDescriptor final
    {
        Language ScriptLanguage = Language::CSharp;
        RuntimeRole Role = RuntimeRole::Gameplay;
        std::string Name;
        std::string Version;
        bool SupportsHotReload = false;
        bool IsSandboxed = false;
    };

    class IScriptRuntime
    {
    public:
        virtual ~IScriptRuntime() = default;
        [[nodiscard]] virtual const RuntimeDescriptor& GetDescriptor() const noexcept = 0;
        virtual bool Start(std::string& error) = 0;
        virtual void Stop() noexcept = 0;
        virtual void Tick(double deltaSeconds) = 0;
    };
}

