#include "GameObject.h"
#include "PythonHost.h"
#include "Core.h"
#include "core\log\Log.h"

#define PY_SSIZE_T_CLEAN
#include <Python.h>

namespace NcmaEngine
{
    GameObject::GameObject()
        : m_ID(INVALID_ENTITY_ID), m_Scene(nullptr)
    {
    }

    GameObject::GameObject(Entity entity)
        : m_ID(entity.GetID()), m_Scene(entity.GetScene())
    {
    }

    GameObject::GameObject(EntityID id, Scene* scene)
        : m_ID(id), m_Scene(scene)
    {
    }

    GameObject::~GameObject()
    {
        m_CachedTransform = nullptr;
    }

    void GameObject::Wrap(Entity entity)
    {
        m_ID = entity.GetID();
        m_Scene = entity.GetScene();
        m_CachedTransform = nullptr;
    }

    void GameObject::Wrap(EntityID id, Scene* scene)
    {
        m_ID = id;
        m_Scene = scene;
        m_CachedTransform = nullptr;
    }

    bool GameObject::IsValid() const
    {
        return m_ID != INVALID_ENTITY_ID && m_Scene != nullptr;
    }

    const std::string& GameObject::GetName() const
    {
        static std::string empty;
        if (!IsValid())
            return empty;
        return m_Scene->GetEntityName(m_ID);
    }

    void GameObject::SetName(const std::string& name)
    {
        if (!IsValid())
            return;
        m_Scene->SetEntityName(m_ID, name);
    }

    TransformComponent* GameObject::GetTransform() const
    {
        if (!IsValid())
            return nullptr;

        if (m_CachedTransform == nullptr)
        {
            m_CachedTransform = m_Scene->GetComponent<TransformComponent>(m_ID);
        }
        return m_CachedTransform;
    }

    bool GameObject::HasComponent(ComponentTypeID typeID) const
    {
        if (!IsValid())
            return false;

        // We would need access to Scene's internal HasComponent
        // For now, use GetComponent
        return false; // Simplified - actual implementation needs Scene friend access
    }

    Component* GameObject::GetComponent(ComponentTypeID typeID) const
    {
        if (!IsValid())
            return nullptr;

        // This needs proper implementation with Scene's type registry
        return nullptr;
    }

    void* GameObject::GetComponentByTypeName(const std::string& typeName)
    {
        if (!IsValid())
            return nullptr;

        // Map type names to actual components
        if (typeName == "TransformComponent" || typeName == "Transform")
        {
            return GetTransform();
        }
        else if (typeName == "MeshComponent" || typeName == "Mesh")
        {
            // Need to get from scene
            // return m_Scene->GetComponent<MeshComponent>(m_ID);
        }

        return nullptr;
    }

    PyObject* GameObject::GetComponentPy(const std::string& componentType)
    {
        void* comp = GetComponentByTypeName(componentType);
        if (comp == nullptr)
            Py_RETURN_NONE;

        // For now, return a wrapper object
        // In a full implementation, this would create a proper Python object
        Py_RETURN_NONE;
    }

    PyObject* GameObject::AddComponentPy(const std::string& componentType)
    {
        // This would need Python type creation
        Py_RETURN_NONE;
    }

    GameObject GameObject::CreateChild(const std::string& name)
    {
        if (!IsValid())
            return GameObject();

        Entity child = m_Scene->CreateEntity(name);
        return GameObject(child);
    }

    void GameObject::Destroy()
    {
        if (!IsValid())
            return;

        m_Scene->DestroyEntity(m_ID);
        m_ID = INVALID_ENTITY_ID;
        m_Scene = nullptr;
    }

} // namespace NcmaEngine