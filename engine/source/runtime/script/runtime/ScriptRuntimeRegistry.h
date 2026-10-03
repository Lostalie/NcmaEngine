#pragma once

#include "script/runtime/ScriptRuntime.h"

#include <memory>
#include <unordered_map>
#include <vector>

namespace NcmaEngine::Scripting
{
    class ScriptRuntimeRegistry final
    {
    public:
        ScriptRuntimeRegistry();

        void Register(std::unique_ptr<IScriptRuntime> runtime);
        [[nodiscard]] IScriptRuntime* Find(Language language) const;
        [[nodiscard]] std::vector<RuntimeDescriptor> Describe() const;
        [[nodiscard]] Language GetGameplayLanguage() const noexcept { return Language::CSharp; }
        [[nodiscard]] Language GetToolsLanguage() const noexcept { return Language::Python; }

    private:
        std::unordered_map<Language, std::unique_ptr<IScriptRuntime>> m_Runtimes;
    };
}

