#include "Scene.h"
#include "core/log/Log.h"

namespace NcmaEngine
{
	Scene::Scene(const std::string& name)
		: m_Name(name)
	{
		CORE_LOG_INFO("Scene '{}' created", name);
	}

	Scene::~Scene()
	{
		CORE_LOG_INFO("Scene '{}' destroyed", m_Name);
	}

	void Scene::OnUpdate(float deltaTime)
	{
		// Update all entities and components
	}

	void Scene::OnEvent(Event& event)
	{
		// Handle events
	}

	Entity Scene::CreateEntity(const std::string& name)
	{
		EntityID id = ++m_EntityCount;
		m_EntityNames[id] = name.empty() ? "Entity_" + std::to_string(id) : name;

		Entity entity(id, this);
		CORE_LOG_INFO("Created entity '{}' with ID {}", m_EntityNames[id], id);

		return entity;
	}

	void Scene::DestroyEntity(Entity entity)
	{
		DestroyEntity(entity.GetID());
	}

	void Scene::DestroyEntity(EntityID entityID)
	{
		if (entityID == INVALID_ENTITY_ID)
			return;

		// Notify all component pools
		for (auto& [typeID, pool] : m_ComponentPools)
		{
			pool->OnEntityDestroyed(entityID);
		}

		// Remove entity name
		m_EntityNames.erase(entityID);

		CORE_LOG_INFO("Destroyed entity with ID {}", entityID);
	}

	Entity Scene::FindEntityByName(const std::string& name)
	{
		for (auto& [id, entityName] : m_EntityNames)
		{
			if (entityName == name)
			{
				return Entity(id, this);
			}
		}
		return Entity();
	}

	const std::string& Scene::GetEntityName(EntityID entityID) const
	{
		static std::string s_Empty;
		auto it = m_EntityNames.find(entityID);
		if (it != m_EntityNames.end())
			return it->second;
		return s_Empty;
	}

	void Scene::SetEntityName(EntityID entityID, const std::string& name)
	{
		m_EntityNames[entityID] = name;
	}
}
