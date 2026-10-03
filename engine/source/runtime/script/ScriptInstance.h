#pragma once

#include "Core.h"
#include "entity\Entity.h"
#include "GameObject.h"
#include <string>
#include <memory>

// Forward declare Python types
struct _object;
typedef _object PyObject;

namespace NcmaEngine
{
    /**
     * @brief Represents a single instance of a Python script.
     *
     * Each ScriptComponent can host multiple script instances (multiple classes
     * from one Python file).
     */
    class ScriptInstance
    {
    public:
        ScriptInstance();
        ~ScriptInstance();

        // Load a script class from file
        bool LoadFromFile(const std::string& filePath, const std::string& className = "");
        bool LoadFromModule(const std::string& moduleName, const std::string& className);

        // Bind to an entity/game object
        void BindToEntity(Entity entity);
        void BindToGameObject(GameObject* gameObject);

        // Lifecycle methods (called by ScriptComponent)
        void OnAwake();
        void OnStart();
        void OnUpdate(float deltaTime);
        void OnLateUpdate(float deltaTime);
        void OnDestroy();
        void OnEnable();
        void OnDisable();

        // Check validity
        bool IsValid() const { return m_Instance != nullptr; }

        // Get underlying Python object
        PyObject* GetPyObject() const { return m_Instance; }

        // Get script class name
        const std::string& GetClassName() const { return m_ClassName; }

        // Get script file path
        const std::string& GetScriptPath() const { return m_ScriptPath; }

        // Call a custom method on the instance
        bool CallMethod(const std::string& methodName);

    private:
        PyObject* m_Class = nullptr;       // The Python class
        PyObject* m_Instance = nullptr;   // The instantiated object
        std::string m_ClassName;
        std::string m_ScriptPath;
        bool m_AwakeCalled = false;
        bool m_StartCalled = false;
        GameObject* m_GameObject = nullptr;  // The bound game object
    };

} // namespace NcmaEngine