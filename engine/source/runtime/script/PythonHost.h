#pragma once

#include "Core.h"
#include <string>
#include <vector>
#include <memory>
#include <unordered_map>
#include <functional>

namespace NcmaEngine
{
    /**
     * @brief Python interpreter host using Python C-API.
     *
     * Manages Python runtime initialization, script loading, and
     * provides interfaces for Python <-> C++ communication.
     */
    class PythonHost
    {
    public:
        PythonHost();
        ~PythonHost();

        // Singleton access
        static PythonHost& Get();
        static void ReleaseInstance();

        // Initialize Python interpreter
        bool Initialize();
        void Shutdown();

        // Script execution
        bool RunScript(const std::string& script);
        bool RunFile(const std::string& filePath);

        // Module management
        bool ImportModule(const std::string& moduleName);
        void AddSearchPath(const std::string& path);

        // Get Python object from module
        void* GetModuleAttribute(const std::string& moduleName, const std::string& attrName);
        void* CallFunction(void* module, const std::string& funcName, void* args = nullptr);

        // Check if initialized
        bool IsInitialized() const { return m_Initialized; }

        // Error handling
        std::string GetLastError();
        void ClearError();

    private:
        bool m_Initialized = false;
        std::vector<std::string> m_SearchPaths;
        std::string m_LastError;

        static PythonHost* s_Instance;
    };

} // namespace NcmaEngine