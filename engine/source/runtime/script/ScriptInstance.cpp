#include "ScriptInstance.h"
#include "PythonHost.h"
#include "ScriptBindings.h"
#include "Core.h"
#include "core\log\Log.h"
#include <sstream>

#define PY_SSIZE_T_CLEAN
#include <Python.h>

namespace NcmaEngine
{
    ScriptInstance::ScriptInstance()
    {
    }

    ScriptInstance::~ScriptInstance()
    {
        if (m_Instance != nullptr)
        {
            Py_DECREF(m_Instance);
            m_Instance = nullptr;
        }
        if (m_Class != nullptr)
        {
            Py_DECREF(m_Class);
            m_Class = nullptr;
        }
    }

    bool ScriptInstance::LoadFromFile(const std::string& filePath, const std::string& className)
    {
        // Store the script path
        m_ScriptPath = filePath;

        // TODO: Use Python importlib to dynamically load the script
        // For now, we'll use a simpler approach via PythonHost
        PythonHost& host = PythonHost::Get();

        // Build Python code to import the module
        std::ostringstream ss;
        ss << "import importlib.util, sys, os\n";
        ss << "spec = importlib.util.spec_from_file_location('script_module', r'" << filePath << "')\n";
        ss << "module = importlib.util.module_from_spec(spec)\n";
        ss << "sys.modules['script_module'] = module\n";
        ss << "spec.loader.exec_module(module)\n";

        if (!host.RunScript(ss.str()))
        {
            ENGINE_LOG_ERROR("Failed to load script file: {}", filePath);
            ENGINE_LOG_ERROR("Error: {}", host.GetLastError());
            return false;
        }

        // Find the class - if className is empty, find the first class inheriting from InxComponent
        std::string searchClass = className;
        if (searchClass.empty())
        {
            // Look for InxComponent subclass
            std::string findClass = R"(
import inspect
for name, obj in inspect.getmembers(script_module, inspect.isclass):
    if hasattr(obj, '__bases__'):
        for base in obj.__bases__:
            if 'InxComponent' in str(base) or 'ScriptComponent' in str(base):
                print(name)
                break
)";
            // For now, just try to get a class called 'Script' or use the filename
            searchClass = "Script";
        }

        // Get the class from the module
        m_ClassName = searchClass;
        m_Class = static_cast<PyObject*>(host.GetModuleAttribute("script_module", searchClass));

        if (m_Class == nullptr)
        {
            ENGINE_LOG_ERROR("Failed to find class '{}' in script: {}", searchClass, filePath);
            return false;
        }

        // Instantiate the class
        m_Instance = PyObject_CallObject(m_Class, nullptr);
        if (m_Instance == nullptr)
        {
            ENGINE_LOG_ERROR("Failed to instantiate script class: {}", searchClass);
            return false;
        }

        ENGINE_LOG_INFO("Loaded script: {} from {}", searchClass, filePath);
        return true;
    }

    bool ScriptInstance::LoadFromModule(const std::string& moduleName, const std::string& className)
    {
        m_ClassName = className;

        PythonHost& host = PythonHost::Get();
        if (!host.ImportModule(moduleName))
        {
            ENGINE_LOG_ERROR("Failed to import module: {}", moduleName);
            return false;
        }

        m_Class = static_cast<PyObject*>(host.GetModuleAttribute(moduleName, className));
        if (m_Class == nullptr)
        {
            ENGINE_LOG_ERROR("Failed to find class '{}' in module: {}", className, moduleName);
            return false;
        }

        // Instantiate
        m_Instance = PyObject_CallObject(m_Class, nullptr);
        if (m_Instance == nullptr)
        {
            ENGINE_LOG_ERROR("Failed to instantiate script class: {}", className);
            return false;
        }

        return true;
    }

    void ScriptInstance::BindToEntity(Entity entity)
    {
        if (!entity.IsValid())
            return;

        if (m_GameObject == nullptr)
        {
            m_GameObject = new GameObject(entity);
        }
        else
        {
            m_GameObject->Wrap(entity);
        }

        if (m_Instance != nullptr)
        {
            ScriptBindings::SetGameObject(m_Instance, m_GameObject);
        }
    }

    void ScriptInstance::BindToGameObject(GameObject* gameObject)
    {
        m_GameObject = gameObject;

        if (m_Instance != nullptr)
        {
            ScriptBindings::SetGameObject(m_Instance, m_GameObject);
        }
    }

    void ScriptInstance::OnAwake()
    {
        if (m_Instance == nullptr || m_AwakeCalled)
            return;

        PyObject* awake = PyObject_GetAttrString(m_Instance, "awake");
        if (awake != nullptr && PyCallable_Check(awake))
        {
            PyObject* result = PyObject_CallObject(awake, nullptr);
            if (result == nullptr)
            {
                PythonHost::Get().ClearError();
            }
            else
            {
                Py_DECREF(result);
            }
            Py_DECREF(awake);
        }
        else
        {
            Py_XDECREF(awake);
        }

        m_AwakeCalled = true;
    }

    void ScriptInstance::OnStart()
    {
        if (m_Instance == nullptr || m_StartCalled)
            return;

        PyObject* start = PyObject_GetAttrString(m_Instance, "start");
        if (start != nullptr && PyCallable_Check(start))
        {
            PyObject* result = PyObject_CallObject(start, nullptr);
            if (result == nullptr)
            {
                PythonHost::Get().ClearError();
            }
            else
            {
                Py_DECREF(result);
            }
            Py_DECREF(start);
        }
        else
        {
            Py_XDECREF(start);
        }

        m_StartCalled = true;
    }

    void ScriptInstance::OnUpdate(float deltaTime)
    {
        if (m_Instance == nullptr)
            return;

        PyObject* update = PyObject_GetAttrString(m_Instance, "update");
        if (update != nullptr && PyCallable_Check(update))
        {
            PyObject* args = Py_BuildValue("(f)", deltaTime);
            PyObject* result = PyObject_CallObject(update, args);
            Py_DECREF(args);

            if (result == nullptr)
            {
                PythonHost::Get().ClearError();
            }
            else
            {
                Py_DECREF(result);
            }
            Py_DECREF(update);
        }
        else
        {
            Py_XDECREF(update);
        }
    }

    void ScriptInstance::OnLateUpdate(float deltaTime)
    {
        if (m_Instance == nullptr)
            return;

        PyObject* lateUpdate = PyObject_GetAttrString(m_Instance, "late_update");
        if (lateUpdate != nullptr && PyCallable_Check(lateUpdate))
        {
            PyObject* args = Py_BuildValue("(f)", deltaTime);
            PyObject* result = PyObject_CallObject(lateUpdate, args);
            Py_DECREF(args);

            if (result == nullptr)
            {
                PythonHost::Get().ClearError();
            }
            else
            {
                Py_DECREF(result);
            }
            Py_DECREF(lateUpdate);
        }
        else
        {
            Py_XDECREF(lateUpdate);
        }
    }

    void ScriptInstance::OnDestroy()
    {
        if (m_Instance == nullptr)
            return;

        PyObject* onDestroy = PyObject_GetAttrString(m_Instance, "on_destroy");
        if (onDestroy != nullptr && PyCallable_Check(onDestroy))
        {
            PyObject* result = PyObject_CallObject(onDestroy, nullptr);
            if (result == nullptr)
            {
                PythonHost::Get().ClearError();
            }
            else
            {
                Py_DECREF(result);
            }
            Py_DECREF(onDestroy);
        }
        else
        {
            Py_XDECREF(onDestroy);
        }
    }

    void ScriptInstance::OnEnable()
    {
        if (m_Instance == nullptr)
            return;

        PyObject* onEnable = PyObject_GetAttrString(m_Instance, "on_enable");
        if (onEnable != nullptr && PyCallable_Check(onEnable))
        {
            PyObject* result = PyObject_CallObject(onEnable, nullptr);
            if (result == nullptr)
            {
                PythonHost::Get().ClearError();
            }
            else
            {
                Py_DECREF(result);
            }
            Py_DECREF(onEnable);
        }
        else
        {
            Py_XDECREF(onEnable);
        }
    }

    void ScriptInstance::OnDisable()
    {
        if (m_Instance == nullptr)
            return;

        PyObject* onDisable = PyObject_GetAttrString(m_Instance, "on_disable");
        if (onDisable != nullptr && PyCallable_Check(onDisable))
        {
            PyObject* result = PyObject_CallObject(onDisable, nullptr);
            if (result == nullptr)
            {
                PythonHost::Get().ClearError();
            }
            else
            {
                Py_DECREF(result);
            }
            Py_DECREF(onDisable);
        }
        else
        {
            Py_XDECREF(onDisable);
        }
    }

    bool ScriptInstance::CallMethod(const std::string& methodName)
    {
        if (m_Instance == nullptr)
            return false;

        PyObject* method = PyObject_GetAttrString(m_Instance, methodName.c_str());
        if (method == nullptr || !PyCallable_Check(method))
        {
            Py_XDECREF(method);
            return false;
        }

        PyObject* result = PyObject_CallObject(method, nullptr);
        Py_DECREF(method);

        if (result == nullptr)
        {
            PythonHost::Get().ClearError();
            return false;
        }

        Py_DECREF(result);
        return true;
    }

} // namespace NcmaEngine