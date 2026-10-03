#pragma once

#include "Core.h"
#include "entity\Entity.h"
#include "scene\Scene.h"
#include "scene\components\TransformComponent.h"
#include <string>
#include <memory>
#include <vector>

// Forward declare Python types
struct _object;
typedef _object PyObject;

namespace NcmaEngine
{
    /**
     * @brief Python-accessible wrapper for an Entity.
     *
     * This class provides a Python-friendly interface to engine entities.
     * It allows Python scripts to:
     * - Access and modify transform
     * - Get/Add other components
     * - Manage entity lifecycle
     */
    class GameObject
    {
    public:
        GameObject();
        GameObject(Entity entity);
        GameObject(EntityID id, Scene* scene);
        ~GameObject();

        // Wrap an existing entity
        void Wrap(Entity entity);
        void Wrap(EntityID id, Scene* scene);

        // Entity identity
        EntityID GetID() const { return m_ID; }
        Scene* GetScene() const { return m_Scene; }
        bool IsValid() const;

        // Name
        const std::string& GetName() const;
        void SetName(const std::string& name);

        // Transform (cached for performance)
        TransformComponent* GetTransform() const;

        // Component access
        bool HasComponent(ComponentTypeID typeID) const;
        Component* GetComponent(ComponentTypeID typeID) const;
        void* GetComponentByTypeName(const std::string& typeName);

        // Add/Remove components
        template<typename T, typename... Args>
        T* AddComponent(Args&&... args);

        template<typename T>
        bool RemoveComponent();

        // Python-friendly component access
        PyObject* GetComponentPy(const std::string& componentType);
        PyObject* AddComponentPy(const std::string& componentType);

        // Create a new entity as a child of this GameObject
        GameObject CreateChild(const std::string& name = "");
        void Destroy();

    private:
        EntityID m_ID = INVALID_ENTITY_ID;
        Scene* m_Scene = nullptr;
        mutable TransformComponent* m_CachedTransform = nullptr;

        template<typename T>
        ComponentTypeID GetComponentTypeID() const;

        friend class ScriptBindings;
    };

    // Template implementation
    template<typename T>
    ComponentTypeID GameObject::GetComponentTypeID() const
    {
        static ComponentTypeID typeID = 0;
        return typeID;
    }

    template<typename T, typename... Args>
    T* GameObject::AddComponent(Args&&... args)
    {
        if (!IsValid())
            return nullptr;

        return m_Scene->AddComponent<T>(m_ID, std::forward<Args>(args)...);
    }

    template<typename T>
    bool GameObject::RemoveComponent()
    {
        if (!IsValid())
            return false;

        m_Scene->RemoveComponent<T>(m_ID);
        return true;
    }

} // namespace NcmaEngine