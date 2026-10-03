#include "ScriptBindings.h"
#include "PythonHost.h"
#include "Core.h"
#include "core\log\Log.h"
#include <cstdarg>

#define PY_SSIZE_T_CLEAN
#include <Python.h>

namespace NcmaEngine
{
    // Key for storing GameObject pointer in Python object
    static const char* GAME_OBJECT_KEY = "_game_object_ptr";

    void ScriptBindings::SetGameObject(PyObject* instance, GameObject* go)
    {
        if (instance == nullptr)
            return;

        // Store the pointer as an attribute on the instance
        PyObject* ptr = PyLong_FromUnsignedLongLong((uintptr_t)go);
        if (ptr == nullptr)
            return;

        int result = PyObject_SetAttrString(instance, GAME_OBJECT_KEY, ptr);
        Py_DECREF(ptr);

        if (result != 0)
        {
            ENGINE_LOG_WARN("Failed to set game_object on Python instance");
        }
    }

    GameObject* ScriptBindings::GetGameObject(PyObject* instance)
    {
        if (instance == nullptr)
            return nullptr;

        PyObject* ptr = PyObject_GetAttrString(instance, GAME_OBJECT_KEY);
        if (ptr == nullptr)
            return nullptr;

        GameObject* go = (GameObject*)PyLong_AsUnsignedLongLong(ptr);
        Py_DECREF(ptr);

        return go;
    }

    void ScriptBindings::CallAwake(PyObject* instance)
    {
        if (instance == nullptr)
            return;

        if (!PyObject_HasAttrString(instance, "awake"))
            return;

        PyObject* method = PyObject_GetAttrString(instance, "awake");
        if (method == nullptr || !PyCallable_Check(method))
        {
            Py_XDECREF(method);
            return;
        }

        PyObject* result = PyObject_CallObject(method, nullptr);
        Py_DECREF(method);

        if (result == nullptr)
        {
            PythonHost::Get().ClearError();
        }
        else
        {
            Py_DECREF(result);
        }
    }

    void ScriptBindings::CallStart(PyObject* instance)
    {
        if (instance == nullptr)
            return;

        if (!PyObject_HasAttrString(instance, "start"))
            return;

        PyObject* method = PyObject_GetAttrString(instance, "start");
        if (method == nullptr || !PyCallable_Check(method))
        {
            Py_XDECREF(method);
            return;
        }

        PyObject* result = PyObject_CallObject(method, nullptr);
        Py_DECREF(method);

        if (result == nullptr)
        {
            PythonHost::Get().ClearError();
        }
        else
        {
            Py_DECREF(result);
        }
    }

    void ScriptBindings::CallUpdate(PyObject* instance, float deltaTime)
    {
        if (instance == nullptr)
            return;

        if (!PyObject_HasAttrString(instance, "update"))
            return;

        PyObject* method = PyObject_GetAttrString(instance, "update");
        if (method == nullptr || !PyCallable_Check(method))
        {
            Py_XDECREF(method);
            return;
        }

        PyObject* args = Py_BuildValue("(f)", deltaTime);
        PyObject* result = PyObject_CallObject(method, args);
        Py_DECREF(args);
        Py_DECREF(method);

        if (result == nullptr)
        {
            PythonHost::Get().ClearError();
        }
        else
        {
            Py_DECREF(result);
        }
    }

    void ScriptBindings::CallLateUpdate(PyObject* instance, float deltaTime)
    {
        if (instance == nullptr)
            return;

        if (!PyObject_HasAttrString(instance, "late_update"))
            return;

        PyObject* method = PyObject_GetAttrString(instance, "late_update");
        if (method == nullptr || !PyCallable_Check(method))
        {
            Py_XDECREF(method);
            return;
        }

        PyObject* args = Py_BuildValue("(f)", deltaTime);
        PyObject* result = PyObject_CallObject(method, args);
        Py_DECREF(args);
        Py_DECREF(method);

        if (result == nullptr)
        {
            PythonHost::Get().ClearError();
        }
        else
        {
            Py_DECREF(result);
        }
    }

    void ScriptBindings::CallOnDestroy(PyObject* instance)
    {
        if (instance == nullptr)
            return;

        if (!PyObject_HasAttrString(instance, "on_destroy"))
            return;

        PyObject* method = PyObject_GetAttrString(instance, "on_destroy");
        if (method == nullptr || !PyCallable_Check(method))
        {
            Py_XDECREF(method);
            return;
        }

        PyObject* result = PyObject_CallObject(method, nullptr);
        Py_DECREF(method);

        if (result == nullptr)
        {
            PythonHost::Get().ClearError();
        }
        else
        {
            Py_DECREF(result);
        }
    }

    void ScriptBindings::CallOnEnable(PyObject* instance)
    {
        if (instance == nullptr)
            return;

        if (!PyObject_HasAttrString(instance, "on_enable"))
            return;

        PyObject* method = PyObject_GetAttrString(instance, "on_enable");
        if (method == nullptr || !PyCallable_Check(method))
        {
            Py_XDECREF(method);
            return;
        }

        PyObject* result = PyObject_CallObject(method, nullptr);
        Py_DECREF(method);

        if (result == nullptr)
        {
            PythonHost::Get().ClearError();
        }
        else
        {
            Py_DECREF(result);
        }
    }

    void ScriptBindings::CallOnDisable(PyObject* instance)
    {
        if (instance == nullptr)
            return;

        if (!PyObject_HasAttrString(instance, "on_disable"))
            return;

        PyObject* method = PyObject_GetAttrString(instance, "on_disable");
        if (method == nullptr || !PyCallable_Check(method))
        {
            Py_XDECREF(method);
            return;
        }

        PyObject* result = PyObject_CallObject(method, nullptr);
        Py_DECREF(method);

        if (result == nullptr)
        {
            PythonHost::Get().ClearError();
        }
        else
        {
            Py_DECREF(result);
        }
    }

    PyObject* ScriptBindings::Vector3ToPython(float x, float y, float z)
    {
        return Py_BuildValue("(fff)", x, y, z);
    }

    bool ScriptBindings::PythonToVector3(PyObject* pyObj, float& x, float& y, float& z)
    {
        if (pyObj == nullptr || !PyTuple_Check(pyObj) && !PyList_Check(pyObj))
            return false;

        Py_ssize_t size = Py_Size(pyObj);
        if (size < 3)
            return false;

        PyObject* xObj = PySequence_ITEM(pyObj, 0);
        PyObject* yObj = PySequence_ITEM(pyObj, 1);
        PyObject* zObj = PySequence_ITEM(pyObj, 2);

        if (xObj == nullptr || yObj == nullptr || zObj == nullptr)
        {
            Py_XDECREF(xObj);
            Py_XDECREF(yObj);
            Py_XDECREF(zObj);
            return false;
        }

        x = (float)PyFloat_AsDouble(xObj);
        y = (float)PyFloat_AsDouble(yObj);
        z = (float)PyFloat_AsDouble(zObj);

        Py_DECREF(xObj);
        Py_DECREF(yObj);
        Py_DECREF(zObj);

        return true;
    }

    PyObject* ScriptBindings::CallMethod(PyObject* instance, const char* methodName, const char* format, ...)
    {
        if (instance == nullptr || methodName == nullptr)
            return nullptr;

        if (!PyObject_HasAttrString(instance, methodName))
            return nullptr;

        PyObject* method = PyObject_GetAttrString(instance, methodName);
        if (method == nullptr || !PyCallable_Check(method))
        {
            Py_XDECREF(method);
            return nullptr;
        }

        va_list args;
        va_start(args, format);
        PyObject* pyArgs = Py_VaBuildValue(format, args);
        va_end(args);

        PyObject* result = nullptr;
        if (pyArgs != nullptr)
        {
            result = PyObject_CallObject(method, pyArgs);
            Py_DECREF(pyArgs);
        }

        Py_DECREF(method);
        return result;
    }

} // namespace NcmaEngine