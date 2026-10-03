#include "ScriptComponent.h"
#include "PythonHost.h"
#include "Core.h"
#include "core\log\Log.h"

namespace NcmaEngine
{
    ScriptComponent::ScriptComponent()
    {
    }

    ScriptComponent::~ScriptComponent()
    {
        ClearScripts();
    }

    bool ScriptComponent::LoadScript(const std::string& filePath, const std::string& className)
    {
        if (!PythonHost::Get().IsInitialized())
        {
            ENGINE_LOG_ERROR("Cannot load script: Python not initialized");
            return false;
        }

        auto script = std::make_unique<ScriptInstance>();
        if (!script->LoadFromFile(filePath, className))
        {
            ENGINE_LOG_ERROR("Failed to load script: {}", filePath);
            return false;
        }

        m_Scripts.push_back(std::move(script));
        m_ScriptPath = filePath;
        m_ClassName = className.empty() ? script->GetClassName() : className;

        ENGINE_LOG_INFO("Loaded script component: {} ({})", m_ScriptPath, m_ClassName);
        return true;
    }

    bool ScriptComponent::LoadScriptFromModule(const std::string& moduleName, const std::string& className)
    {
        if (!PythonHost::Get().IsInitialized())
        {
            ENGINE_LOG_ERROR("Cannot load script: Python not initialized");
            return false;
        }

        auto script = std::make_unique<ScriptInstance>();
        if (!script->LoadFromModule(moduleName, className))
        {
            ENGINE_LOG_ERROR("Failed to load script from module: {}.{}", moduleName, className);
            return false;
        }

        m_Scripts.push_back(std::move(script));
        m_ScriptPath = moduleName + "." + className;
        m_ClassName = className;

        return true;
    }

    void ScriptComponent::SetEnabled(bool enabled)
    {
        if (m_Enabled == enabled)
            return;

        m_Enabled = enabled;

        for (auto& script : m_Scripts)
        {
            if (m_Enabled)
            {
                script->OnEnable();
            }
            else
            {
                script->OnDisable();
            }
        }
    }

    void ScriptComponent::RemoveScript(size_t index)
    {
        if (index >= m_Scripts.size())
            return;

        // Call on_destroy before removing
        if (m_Scripts[index]->IsValid())
        {
            m_Scripts[index]->OnDestroy();
        }

        m_Scripts.erase(m_Scripts.begin() + index);

        if (m_Scripts.empty())
        {
            m_ScriptPath.clear();
            m_ClassName.clear();
        }
    }

    void ScriptComponent::ClearScripts()
    {
        for (auto& script : m_Scripts)
        {
            if (script->IsValid())
            {
                script->OnDestroy();
            }
        }
        m_Scripts.clear();
        m_ScriptPath.clear();
        m_ClassName.clear();
    }

} // namespace NcmaEngine