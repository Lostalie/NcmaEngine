#pragma once

#include "entity\Entity.h"
#include "ScriptInstance.h"
#include <string>
#include <vector>
#include <memory>

namespace NcmaEngine
{
    /**
     * @brief Script component - attaches Python scripts to entities.
     *
     * This component holds references to one or more Python script instances.
     * Scripts are loaded from .py files and instantiated when the component
     * is created.
     *
     * Usage:
     *   auto* script = entity.AddComponent<ScriptComponent>();
     *   script->LoadScript("scripts/components/PlayerController.py");
     */
    class ScriptComponent : public Component
    {
    public:
        ScriptComponent();
        ~ScriptComponent();

        // Load a script from file
        bool LoadScript(const std::string& filePath, const std::string& className = "");

        // Load a script from module
        bool LoadScriptFromModule(const std::string& moduleName, const std::string& className);

        // Get the primary script instance
        ScriptInstance* GetScript() { return m_Scripts.empty() ? nullptr : m_Scripts[0].get(); }

        // Get all script instances
        const std::vector<std::unique_ptr<ScriptInstance>>& GetAllScripts() const { return m_Scripts; }

        // Get script count
        size_t GetScriptCount() const { return m_Scripts.size(); }

        // Check if a script is loaded
        bool HasScript() const { return !m_Scripts.empty(); }

        // Script enabled/disabled
        bool IsEnabled() const { return m_Enabled; }
        void SetEnabled(bool enabled);

        // Remove a script at index
        void RemoveScript(size_t index);

        // Clear all scripts
        void ClearScripts();

        // Get the script path
        const std::string& GetScriptPath() const { return m_ScriptPath; }

        // Get the class name
        const std::string& GetClassName() const { return m_ClassName; }

    private:
        std::vector<std::unique_ptr<ScriptInstance>> m_Scripts;
        std::string m_ScriptPath;
        std::string m_ClassName;
        bool m_Enabled = true;

        friend class Scene;
    };

} // namespace NcmaEngine