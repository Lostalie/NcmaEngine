#pragma once

#include "entity\Entity.h"
#include <string>
#include <vector>
#include <memory>
#include <unordered_map>

namespace NcmaEngine
{
	class Scene
	{
	public:
		Scene(const std::string& name = "UnnamedScene");
		~Scene();

		// Scene lifecycle
		void OnUpdate(float deltaTime);
		void OnEvent(Event& event);

		// Entity management
		Entity CreateEntity(const std::string& name = "");
		void DestroyEntity(Entity entity);
		void DestroyEntity(EntityID entityID);

		// Entity queries
		Entity FindEntityByName(const std::string& name);
		const std::string& GetEntityName(EntityID entityID) const;
		void SetEntityName(EntityID entityID, const std::string& name);

		// Component management
		template<typename T, typename... Args>
		T* AddComponent(EntityID entity, Args&&... args);

		template<typename T>
		T* GetComponent(EntityID entity);

		template<typename T>
		bool HasComponent(EntityID entity) const;

		template<typename T>
		void RemoveComponent(EntityID entity);

		// Get all entities with a component
		template<typename T>
		std::vector<Entity> GetEntitiesWithComponent();

		// Registry access
		EntityID GetEntityCount() const { return m_EntityCount; }

	private:
		std::string m_Name;
		EntityID m_EntityCount = 0;
		uint32_t m_ComponentTypeCount = 0;

		// Entity name mapping
		std::unordered_map<EntityID, std::string> m_EntityNames;

		// Component pools
		std::unordered_map<ComponentTypeID, std::unique_ptr<IComponentPool>> m_ComponentPools;

		template<typename T>
		ComponentTypeID GetComponentTypeID()
		{
			static ComponentTypeID typeID = ++m_ComponentTypeCount;
			return typeID;
		}

		friend class Entity;
	};

	// Template implementations

	template<typename T, typename... Args>
	T* Scene::AddComponent(EntityID entity, Args&&... args)
	{
		assert(entity != INVALID_ENTITY_ID && "Invalid entity!");

		ComponentTypeID typeID = GetComponentTypeID<T>();

		// Create pool if doesn't exist
		if (m_ComponentPools.find(typeID) == m_ComponentPools.end())
		{
			m_ComponentPools[typeID] = std::make_unique<ComponentPool<T>>();
		}

		auto* pool = static_cast<ComponentPool<T>*>(m_ComponentPools[typeID].get());
		T* component = pool->AddComponent(entity);
		component->m_Entity = Entity(entity, this);
		return component;
	}

	template<typename T>
	T* Scene::GetComponent(EntityID entity)
	{
		ComponentTypeID typeID = GetComponentTypeID<T>();

		auto it = m_ComponentPools.find(typeID);
		if (it != m_ComponentPools.end())
		{
			auto* pool = static_cast<ComponentPool<T>*>(it->second.get());
			return pool->GetComponent(entity);
		}
		return nullptr;
	}

	template<typename T>
	bool Scene::HasComponent(EntityID entity) const
	{
		ComponentTypeID typeID = GetComponentTypeID<T>();

		auto it = m_ComponentPools.find(typeID);
		if (it != m_ComponentPools.end())
		{
			auto* pool = static_cast<ComponentPool<T>*>(it->second.get());
			return pool->HasComponent(entity);
		}
		return false;
	}

	template<typename T>
	void Scene::RemoveComponent(EntityID entity)
	{
		ComponentTypeID typeID = GetComponentTypeID<T>();

		auto it = m_ComponentPools.find(typeID);
		if (it != m_ComponentPools.end())
		{
			auto* pool = static_cast<ComponentPool<T>*>(it->second.get());
			pool->RemoveComponent(entity);
		}
	}

	template<typename T>
	std::vector<Entity> Scene::GetEntitiesWithComponent()
	{
		std::vector<Entity> entities;

		ComponentTypeID typeID = GetComponentTypeID<T>();
		auto it = m_ComponentPools.find(typeID);
		if (it != m_ComponentPools.end())
		{
			auto* pool = static_cast<ComponentPool<T>*>(it->second.get());
			// This is a simplified version - in production you'd track entities separately
		}

		return entities;
	}
}
