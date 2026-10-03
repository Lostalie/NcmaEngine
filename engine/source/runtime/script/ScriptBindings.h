#pragma once

#include "Core.h"
#include "GameObject.h"
#include <string>
#include <vector>

struct _object;
typedef _object PyObject;

namespace NcmaEngine
{
    /**
     * @brief Handles Python <-> C++ bindings for the script system.
     *
     * This class provides utilities for:
     * - Injecting engine objects into Python (game_object, transform, etc.)
     * - Calling Python lifecycle methods with proper error handling
     * - Converting between C++ and Python types
     */
    class ScriptBindings
    {
    public:
        ScriptBindings() = default;
        ~ScriptBindings() = default;

        // ---- Object injection into Python ----

        /// @brief Set the game_object property on a Python component instance
        static void SetGameObject(PyObject* instance, GameObject* go);

        /// @brief Get the game_object from a Python component instance
        static GameObject* GetGameObject(PyObject* instance);

        // ---- Lifecycle method calling ----

        /// @brief Call awake() on a Python component
        static void CallAwake(PyObject* instance);

        /// @brief Call start() on a Python component
        static void CallStart(PyObject* instance);

        /// @brief Call update(delta_time) on a Python component
        static void CallUpdate(PyObject* instance, float deltaTime);

        /// @brief Call late_update(delta_time) on a Python component
        static void CallLateUpdate(PyObject* instance, float deltaTime);

        /// @brief Call on_destroy() on a Python component
        static void CallOnDestroy(PyObject* instance);

        /// @brief Call on_enable() on a Python component
        static void CallOnEnable(PyObject* instance);

        /// @brief Call on_disable() on a Python component
        static void CallOnDisable(PyObject* instance);

        // ---- Type conversion ----

        /// @brief Convert a Vector3 to a Python tuple (x, y, z)
        static PyObject* Vector3ToPython(float x, float y, float z);

        /// @brief Convert a Python tuple/list to Vector3 components
        static bool PythonToVector3(PyObject* pyObj, float& x, float& y, float& z);

    private:
        static PyObject* CallMethod(PyObject* instance, const char* methodName, const char* format, ...);
    };

} // namespace NcmaEngine