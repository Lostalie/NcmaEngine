#include "script/runtime/PythonGameplayRuntime.h"

// CPython's Windows headers select python_d.lib under _DEBUG. We embed the installed
// release interpreter with the same DLL CRT; engine Debug does not require python_d.
#if defined(_MSC_VER) && defined(_DEBUG)
#define NCMA_RESTORE_DEBUG
#undef _DEBUG
#endif
#include <Python.h>
#if defined(NCMA_RESTORE_DEBUG)
#define _DEBUG 1
#undef NCMA_RESTORE_DEBUG
#endif

#include <cmath>
#include <limits>
#include <stdexcept>
#include <thread>

namespace NcmaEngine::Scripting
{
    namespace
    {
        struct Release final { void operator()(PyObject* value) const { Py_XDECREF(value); } };
        using Object = std::unique_ptr<PyObject, Release>;
        std::thread::id InterpreterThread;
        std::string Utf8(const std::filesystem::path& path)
        {
            const auto value = path.u8string(); return {reinterpret_cast<const char*>(value.data()), value.size()};
        }
        std::string PythonError()
        {
            PyObject *type = nullptr, *value = nullptr, *trace = nullptr;
            PyErr_Fetch(&type, &value, &trace);
            PyErr_NormalizeException(&type, &value, &trace);
            Object ownedType(type), ownedValue(value), ownedTrace(trace);
            Object formatter(PyImport_ImportModule("traceback"));
            Object lines(formatter ? PyObject_CallMethod(formatter.get(), "format_exception", "OOO",
                type ? type : Py_None, value ? value : Py_None, trace ? trace : Py_None) : nullptr);
            Object separator(PyUnicode_FromString(""));
            Object text(lines ? PyUnicode_Join(separator.get(), lines.get()) :
                (value ? PyObject_Str(value) : PyUnicode_FromString("Unknown Python error")));
            const char* bytes = text ? PyUnicode_AsUTF8(text.get()) : nullptr;
            std::string result = bytes ? bytes : "Python exception formatting failed";
            PyErr_Clear(); return result;
        }
        Object Require(PyObject* object)
        {
            if (!object) throw std::runtime_error(PythonError());
            return Object(object);
        }
        std::string Text(PyObject* object)
        {
            const char* value = PyUnicode_AsUTF8(object);
            if (!value) throw std::runtime_error(PythonError());
            return value;
        }
        Object At(PyObject* sequence, Py_ssize_t index) { return Require(PySequence_GetItem(sequence, index)); }
        void CheckThread()
        {
            if (InterpreterThread != std::this_thread::get_id())
                throw std::runtime_error("Python gameplay must run on its initialization thread");
        }
        void InitializeInterpreter()
        {
            if (Py_IsInitialized()) { CheckThread(); return; }
            PyConfig config;
            PyConfig_InitIsolatedConfig(&config);
            config.site_import = 0;
            config.write_bytecode = 0;
            auto status = PyConfig_SetBytesString(&config, &config.home, NCMA_PYTHON_HOME);
            if (!PyStatus_Exception(status)) status = Py_InitializeFromConfig(&config);
            const std::string error = PyStatus_Exception(status) ?
                (status.err_msg ? status.err_msg : "Python initialization failed") : "";
            PyConfig_Clear(&config);
            if (!error.empty()) throw std::runtime_error(error);
            InterpreterThread = std::this_thread::get_id();
            // Keep the process interpreter alive across Stop/Reload. Do not finalize and
            // reinitialize extension modules; Stop releases scene leases and project modules.
        }
    }
    struct PythonGameplayRuntime::Implementation final
    {
        std::filesystem::path Root, NativeLibrary, Scripts;
        RuntimeDescriptor Descriptor{Language::Python, RuntimeRole::Gameplay, "CPython Gameplay", PY_VERSION, true, false};
        Object Host;
        std::vector<BehaviourDescriptor> Types;
        int Count = 0;
        std::string LastError;
    };
    PythonGameplayRuntime::PythonGameplayRuntime(std::filesystem::path root, std::filesystem::path native,
        std::filesystem::path scripts)
        : m_Implementation(std::make_unique<Implementation>())
    {
        m_Implementation->Root = std::move(root); m_Implementation->NativeLibrary = std::move(native);
        m_Implementation->Scripts = scripts.empty() ? m_Implementation->Root / "gameplay" / "python" : std::move(scripts);
    }
    PythonGameplayRuntime::~PythonGameplayRuntime() { Stop(); }
    const RuntimeDescriptor& PythonGameplayRuntime::GetDescriptor() const noexcept { return m_Implementation->Descriptor; }
    bool PythonGameplayRuntime::Start(std::string& error)
    {
        if (IsStarted()) { error.clear(); return true; }
        try
        {
            InitializeInterpreter();
            const auto& root = m_Implementation->Root;
            auto source = Require(PyUnicode_FromString(Utf8(root / "python" / "src").c_str()));
            PyObject* search = PySys_GetObject("path"); // borrowed
            const int present = PySequence_Contains(search, source.get());
            if (present < 0 || (present == 0 && PyList_Append(search, source.get()) < 0))
                throw std::runtime_error(PythonError());
            auto module = Require(PyImport_ImportModule("ncma_gameplay.host"));
            auto factory = Require(PyObject_GetAttrString(module.get(), "Host"));
            auto host = Require(PyObject_CallFunction(factory.get(), "ss",
                Utf8(m_Implementation->Scripts).c_str(), Utf8(m_Implementation->NativeLibrary).c_str()));
            auto description = Require(PyObject_CallMethod(host.get(), "describe", nullptr));
            std::vector<BehaviourDescriptor> types;
            const Py_ssize_t count = PySequence_Size(description.get());
            if (count < 0 || count > 1024) throw std::runtime_error("Invalid Python Behaviour count");
            for (Py_ssize_t index = 0; index < count; ++index)
            {
                auto record = At(description.get(), index);
                auto name = At(record.get(), 0), properties = At(record.get(), 1);
                BehaviourDescriptor type{Text(name.get()), {}, BehaviourLanguage::Python};
                const Py_ssize_t propertyCount = PySequence_Size(properties.get());
                if (propertyCount < 0 || propertyCount > 1024) throw std::runtime_error("Invalid Python Export count");
                for (Py_ssize_t p = 0; p < propertyCount; ++p)
                {
                    auto property = At(properties.get(), p);
                    auto key = At(property.get(), 0), kind = At(property.get(), 1), value = At(property.get(), 2);
                    auto display = At(property.get(), 3), category = At(property.get(), 4);
                    ExportValue defaultValue{Text(key.get()), static_cast<ExportKind>(PyLong_AsUnsignedLong(kind.get())),
                        PyFloat_AsDouble(value.get())};
                    if (PyErr_Occurred()) throw std::runtime_error(PythonError());
                    if (!ValidExportValue(defaultValue)) throw std::runtime_error("Invalid Python Export metadata");
                    type.Properties.push_back({defaultValue, Text(display.get()), Text(category.get())});
                }
                types.push_back(std::move(type));
            }
            m_Implementation->Host = std::move(host);
            m_Implementation->Types = std::move(types);
            m_Implementation->LastError.clear(); error.clear(); return true;
        }
        catch (const std::exception& exception)
        { m_Implementation->LastError = error = exception.what(); return false; }
    }
    void PythonGameplayRuntime::EndScene() noexcept
    {
        if (!IsStarted()) return;
        try
        {
            CheckThread();
            auto result = Require(PyObject_CallMethod(m_Implementation->Host.get(), "end_scene", nullptr));
        }
        catch (const std::exception& error) { m_Implementation->LastError = error.what(); }
        m_Implementation->Count = 0;
    }
    void PythonGameplayRuntime::Stop() noexcept
    {
        if (!IsStarted()) return;
        try
        {
            CheckThread();
            try { auto result = Require(PyObject_CallMethod(m_Implementation->Host.get(), "close", nullptr)); }
            catch (const std::exception& error) { m_Implementation->LastError = error.what(); }
            m_Implementation->Host.reset();
            m_Implementation->Types.clear();
            m_Implementation->Count = 0;
        }
        catch (const std::exception& error) { m_Implementation->LastError = error.what(); }
    }
    bool PythonGameplayRuntime::Reload(std::string& error) { Stop(); return Start(error); }
    bool PythonGameplayRuntime::BindScene(SceneWorld& world, std::string& error)
    {
        if (!Start(error)) return false;
        EndScene();
        try
        {
            CheckThread();
            auto records = Require(PyList_New(0));
            for (const auto& node : world.CaptureSnapshot().Nodes)
                for (const auto& binding : node.Behaviours)
                {
                    if (binding.Language != BehaviourLanguage::Python) continue;
                    auto properties = Require(PyList_New(0));
                    for (const auto& value : binding.Properties)
                    {
                        if (!ValidExportValue(value)) throw std::runtime_error("Invalid Python Export value");
                        auto property = Require(Py_BuildValue("sId", value.Name.c_str(),
                            static_cast<unsigned int>(value.Kind), value.Value));
                        if (PyList_Append(properties.get(), property.get()) < 0) throw std::runtime_error(PythonError());
                    }
                    auto record = Require(Py_BuildValue("KsiO", static_cast<unsigned long long>(world.FindNode(node.PersistentId)),
                        binding.TypeName.c_str(), binding.Enabled ? 1 : 0, properties.get()));
                    if (PyList_Append(records.get(), record.get()) < 0) throw std::runtime_error(PythonError());
                }
            auto result = Require(PyObject_CallMethod(m_Implementation->Host.get(), "bind", "KO",
                static_cast<unsigned long long>(reinterpret_cast<std::uintptr_t>(&world)), records.get()));
            const long count = PyLong_AsLong(result.get());
            if (PyErr_Occurred()) throw std::runtime_error(PythonError());
            if (count < 0 || count > std::numeric_limits<int>::max()) throw std::runtime_error("Invalid bound Behaviour count");
            m_Implementation->Count = static_cast<int>(count);
            m_Implementation->LastError.clear(); error.clear(); return true;
        }
        catch (const std::exception& exception)
        { EndScene(); m_Implementation->LastError = error = exception.what(); return false; }
    }
    void PythonGameplayRuntime::Tick(double seconds)
    {
        if (!IsStarted()) return;
        try
        {
            CheckThread();
            if (!std::isfinite(seconds) || seconds < 0) throw std::runtime_error("Invalid Python delta time");
            auto result = Require(PyObject_CallMethod(m_Implementation->Host.get(), "tick", "d", seconds));
            m_Implementation->LastError.clear();
        }
        catch (const std::exception& error) { m_Implementation->LastError = error.what(); }
    }
    bool PythonGameplayRuntime::IsStarted() const noexcept { return m_Implementation->Host != nullptr; }
    const std::vector<BehaviourDescriptor>& PythonGameplayRuntime::GetTypes() const noexcept { return m_Implementation->Types; }
    int PythonGameplayRuntime::GetBehaviourCount() const noexcept { return m_Implementation->Count; }
    const std::string& PythonGameplayRuntime::GetLastError() const noexcept { return m_Implementation->LastError; }
}
