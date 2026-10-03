#include "PythonHost.h"
#include "Core.h"
#include "core\log\Log.h"
#include <sstream>

// Python C-API headers
#define PY_SSIZE_T_CLEAN
#include <Python.h>

namespace NcmaEngine
{
    PythonHost* PythonHost::s_Instance = nullptr;

    PythonHost::PythonHost()
    {
    }

    PythonHost::~PythonHost()
    {
        if (m_Initialized)
        {
            Shutdown();
        }
        s_Instance = nullptr;
    }

    PythonHost& PythonHost::Get()
    {
        if (s_Instance == nullptr)
        {
            s_Instance = new PythonHost();
        }
        return *s_Instance;
    }

    void PythonHost::ReleaseInstance()
    {
        delete s_Instance;
        s_Instance = nullptr;
    }

    bool PythonHost::Initialize()
    {
        if (m_Initialized)
        {
            return true;
        }

        // Initialize Python interpreter
        Py_Initialize();
        m_Initialized = true;

        // Add current directory to Python path
        PyObject* sysPath = PySys_GetObject("path");
        if (sysPath)
        {
            PyList_Append(sysPath, PyUnicode_FromString("."));
            PyList_Append(sysPath, PyUnicode_FromString("./scripts"));
        }

        // Add search paths
        for (const auto& path : m_SearchPaths)
        {
            PyObject* pathObj = PyUnicode_FromString(path.c_str());
            PyList_Append(sysPath, pathObj);
            Py_DECREF(pathObj);
        }

        ENGINE_LOG_INFO("Python interpreter initialized");
        return true;
    }

    void PythonHost::Shutdown()
    {
        if (!m_Initialized)
        {
            return;
        }

        Py_Finalize();
        m_Initialized = false;
        ENGINE_LOG_INFO("Python interpreter shutdown");
    }

    bool PythonHost::RunScript(const std::string& script)
    {
        if (!m_Initialized)
        {
            m_LastError = "Python not initialized";
            return false;
        }

        PyObject* result = PyRun_SimpleString(script.c_str());
        if (result == nullptr)
        {
            m_LastError = GetLastError();
            return false;
        }

        Py_DECREF(result);
        return true;
    }

    bool PythonHost::RunFile(const std::string& filePath)
    {
        if (!m_Initialized)
        {
            m_LastError = "Python not initialized";
            return false;
        }

        FILE* file = fopen(filePath.c_str(), "r");
        if (file == nullptr)
        {
            m_LastError = "Cannot open file: " + filePath;
            return false;
        }

        PyObject* result = PyRun_SimpleFile(file, filePath.c_str());
        fclose(file);

        if (result == nullptr)
        {
            m_LastError = GetLastError();
            return false;
        }

        Py_DECREF(result);
        return true;
    }

    bool PythonHost::ImportModule(const std::string& moduleName)
    {
        if (!m_Initialized)
        {
            m_LastError = "Python not initialized";
            return false;
        }

        PyObject* module = PyImport_ImportModule(moduleName.c_str());
        if (module == nullptr)
        {
            m_LastError = GetLastError();
            return false;
        }

        Py_DECREF(module);
        return true;
    }

    void PythonHost::AddSearchPath(const std::string& path)
    {
        m_SearchPaths.push_back(path);

        if (m_Initialized)
        {
            PyObject* sysPath = PySys_GetObject("path");
            if (sysPath)
            {
                PyObject* pathObj = PyUnicode_FromString(path.c_str());
                PyList_Append(sysPath, pathObj);
                Py_DECREF(pathObj);
            }
        }
    }

    void* PythonHost::GetModuleAttribute(const std::string& moduleName, const std::string& attrName)
    {
        if (!m_Initialized)
        {
            return nullptr;
        }

        PyObject* module = PyImport_ImportModule(moduleName.c_str());
        if (module == nullptr)
        {
            return nullptr;
        }

        PyObject* attr = PyObject_GetAttrString(module, attrName.c_str());
        Py_DECREF(module);

        return attr;
    }

    void* PythonHost::CallFunction(void* module, const std::string& funcName, void* args)
    {
        if (!m_Initialized || module == nullptr)
        {
            return nullptr;
        }

        PyObject* func = PyObject_GetAttrString(static_cast<PyObject*>(module), funcName.c_str());
        if (func == nullptr || !PyCallable_Check(func))
        {
            return nullptr;
        }

        PyObject* result = PyObject_CallObject(func, static_cast<PyObject*>(args));
        Py_DECREF(func);

        return result;
    }

    std::string PythonHost::GetLastError()
    {
        if (!m_Initialized)
        {
            return "Python not initialized";
        }

        PyObject* exception = PyErr_Occurred();
        if (exception == nullptr)
        {
            return m_LastError;
        }

        PyObject* type = nullptr;
        PyObject* value = nullptr;
        PyObject* traceback = nullptr;

        PyErr_Fetch(&type, &value, &traceback);
        PyErr_NormalizeException(&type, &value, &traceback);

        std::ostringstream oss;
        if (value != nullptr)
        {
            PyObject* str = PyObject_Str(value);
            if (str != nullptr)
            {
                const char* cstr = PyUnicode_AsUTF8(str);
                if (cstr != nullptr)
                {
                    oss << cstr;
                }
                Py_DECREF(str);
            }
        }

        Py_XDECREF(type);
        Py_XDECREF(value);
        Py_XDECREF(traceback);

        return oss.str();
    }

    void PythonHost::ClearError()
    {
        if (m_Initialized)
        {
            PyErr_Clear();
        }
        m_LastError.clear();
    }

} // namespace NcmaEngine