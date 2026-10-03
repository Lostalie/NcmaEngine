#pragma once

/**
 * @file ScriptSystem.h
 * @brief Main header for the NcmaEngine Script System.
 *
 * This is the main entry point for the script system.
 * Include this header to access all script-related functionality.
 */

// Core script classes
#include "PythonHost.h"
#include "ScriptInstance.h"
#include "ScriptComponent.h"
#include "ScriptModule.h"
#include "ScriptLayer.h"

namespace NcmaEngine
{
    /**
     * @brief Initialize the script system.
     *
     * Call this during engine startup to initialize Python.
     * The ScriptModule handles this automatically if added to the module list.
     */
    inline void InitializeScriptSystem()
    {
        ScriptModule::Get().StartupModule();
    }

    /**
     * @brief Shutdown the script system.
     *
     * Call this during engine shutdown.
     */
    inline void ShutdownScriptSystem()
    {
        ScriptModule::Get().ShutdownModule();
    }

} // namespace NcmaEngine