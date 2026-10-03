#include "ScriptModule.h"
#include "PythonHost.h"
#include "Core.h"
#include "core\log\Log.h"

namespace NcmaEngine
{
    ScriptModule* ScriptModule::s_Instance = nullptr;

    ScriptModule::ScriptModule()
    {
        s_Instance = this;
    }

    ScriptModule::~ScriptModule()
    {
        s_Instance = nullptr;
    }

    void ScriptModule::StartupModule()
    {
        ENGINE_LOG_INFO("Starting Script Module...");

        // Initialize Python
        if (!PythonHost::Get().Initialize())
        {
            ENGINE_LOG_ERROR("Failed to initialize Python interpreter!");
            return;
        }

        // Add default search paths
        AddScriptSearchPath("./scripts");
        AddScriptSearchPath("./scripts/NcmaEngine");
        AddScriptSearchPath("./scripts/components");

        // Initialize the NcmaEngine Python package
        PythonHost::Get().RunScript(
            "import sys\n"
            "sys.path.insert(0, './scripts')\n"
            "try:\n"
            "    import NcmaEngine\n"
            "    print('NcmaEngine Python package loaded')\n"
            "except ImportError as e:\n"
            "    print(f'Warning: NcmaEngine package not found: {e}')\n"
        );

        ENGINE_LOG_INFO("Script Module started successfully");
    }

    void ScriptModule::ShutdownModule()
    {
        ENGINE_LOG_INFO("Shutting down Script Module...");

        PythonHost::ReleaseInstance();

        ENGINE_LOG_INFO("Script Module shut down");
    }

    ScriptModule& ScriptModule::Get()
    {
        return *s_Instance;
    }

    void ScriptModule::AddScriptSearchPath(const std::string& path)
    {
        m_SearchPaths.push_back(path);
        PythonHost::Get().AddSearchPath(path);
        ENGINE_LOG_INFO("Added script search path: {}", path);
    }

    bool ScriptModule::IsPythonActive() const
    {
        return PythonHost::Get().IsInitialized();
    }

    void ScriptModule::ReloadAllScripts()
    {
        ENGINE_LOG_INFO("Reloading all scripts...");
        // TODO: Implement script hot-reload
        // This would require:
        // 1. Store references to all ScriptComponents
        // 2. For each ScriptComponent, store its script path/class
        // 3. Destroy current ScriptInstances
        // 4. Re-load scripts from disk
        // 5. Call OnAwake/OnStart again
    }

} // namespace NcmaEngine