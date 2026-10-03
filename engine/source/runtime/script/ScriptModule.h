#pragma once

#include "core\module\IModuleInterface.h"
#include <string>
#include <vector>

namespace NcmaEngine
{
    /**
     * @brief Script module - manages Python runtime and script lifecycle.
     *
     * Integrates with the engine's module system (IModuleInterface).
     * Handles:
     * - Python interpreter initialization/shutdown
     * - Search path configuration
     * - Script preloading
     * - Per-frame script updates
     */
    class ScriptModule : public IModuleInterface
    {
    public:
        ScriptModule();
        virtual ~ScriptModule();

        // IModuleInterface
        void StartupModule() override;
        void ShutdownModule() override;

        // Get singleton instance
        static ScriptModule& Get();

        // Add search paths for Python scripts
        void AddScriptSearchPath(const std::string& path);

        // Get all registered search paths
        const std::vector<std::string>& GetSearchPaths() const { return m_SearchPaths; }

        // Is the Python runtime active?
        bool IsPythonActive() const;

        // Reload all scripts (for hot-reload support)
        void ReloadAllScripts();

    private:
        std::vector<std::string> m_SearchPaths;
        static ScriptModule* s_Instance;
    };

} // namespace NcmaEngine