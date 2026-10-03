#include "script/runtime/ScriptRuntimeRegistry.h"

#include <stdexcept>

namespace NcmaEngine::Scripting
{
    ScriptRuntimeRegistry::ScriptRuntimeRegistry() = default;

    void ScriptRuntimeRegistry::Register(std::unique_ptr<IScriptRuntime> runtime)
    {
        if (!runtime)
            throw std::invalid_argument("Script runtime cannot be null");
        const Language language = runtime->GetDescriptor().ScriptLanguage;
        if (language != Language::CSharp || runtime->GetDescriptor().Role != RuntimeRole::Gameplay)
            throw std::invalid_argument("Only C# gameplay runtimes may be registered");
        m_Runtimes[language] = std::move(runtime);
    }

    IScriptRuntime* ScriptRuntimeRegistry::Find(Language language) const
    {
        const auto it = m_Runtimes.find(language);
        return it == m_Runtimes.end() ? nullptr : it->second.get();
    }

    std::vector<RuntimeDescriptor> ScriptRuntimeRegistry::Describe() const
    {
        std::vector<RuntimeDescriptor> result;
        result.reserve(m_Runtimes.size());
        for (const auto& [language, runtime] : m_Runtimes)
        {
            (void)language;
            result.push_back(runtime->GetDescriptor());
        }
        return result;
    }
}

