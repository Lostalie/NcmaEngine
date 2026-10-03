#pragma once

#include "Core.h"
#include <vector>
#include <unordered_map>
#include <cassert>

namespace NcmaEngine
{
	// Forward declarations
	class Scene;

	// Entity ID - just a uint32_t wrapper
	using EntityID = uint32_t;
	const EntityID INVALID_ENTITY_ID = 0;

	// Entity class - lightweight wrapper around an ID
	class Entity
	{
	public:
		Entity() : m_ID(INVALID_ENTITY_ID), m_Scene(nullptr) {}
		Entity(EntityID id, Scene* scene) : m_ID(id), m_Scene(scene) {}

		EntityID GetID() const { return m_ID; }
		Scene* GetScene() const { return m_Scene; }

		bool IsValid() const { return m_ID != INVALID_ENTITY_ID && m_Scene != nullptr; }

		// Comparison
		bool operator==(const Entity& other) const { return m_ID == other.m_ID && m_Scene == other.m_Scene; }
		bool operator!=(const Entity& other) const { return !(*this == other); }
		operator bool() const { return IsValid(); }

	private:
		EntityID m_ID;
		Scene* m_Scene = nullptr;
	};

	// Base component class
	class Component
	{
	public:
		virtual ~Component() = default;

		Entity GetEntity() const { return m_Entity; }
		Scene* GetScene() const { return m_Entity.GetScene(); }

	protected:
		Entity m_Entity;
	};

	// Component type ID
	using ComponentTypeID = uint32_t;
	const ComponentTypeID INVALID_COMPONENT_TYPE_ID = 0;

	// Component pool for efficient component storage
	class IComponentPool
	{
	public:
		virtual ~IComponentPool() = default;
		virtual void RemoveComponent(EntityID entity) = 0;
		virtual void OnEntityDestroyed(EntityID entity) = 0;
	};

	template<typename T>
	class ComponentPool : public IComponentPool
	{
	public:
		ComponentPool() = default;

		T* AddComponent(EntityID entity)
		{
			assert(m_EntityToIndex.find(entity) == m_EntityToIndex.end() && "Component already exists on entity!");
			size_t index = m_Components.size();
			m_Components.emplace_back();
			m_EntityToIndex[entity] = index;
			m_IndexToEntity[index] = entity;
			return &m_Components.back();
		}

		T* GetComponent(EntityID entity)
		{
			auto it = m_EntityToIndex.find(entity);
			if (it != m_EntityToIndex.end())
			{
				return &m_Components[it->second];
			}
			return nullptr;
		}

		bool HasComponent(EntityID entity) const
		{
			return m_EntityToIndex.find(entity) != m_EntityToIndex.end();
		}

		virtual void RemoveComponent(EntityID entity) override
		{
			auto it = m_EntityToIndex.find(entity);
			if (it != m_EntityToIndex.end())
			{
				size_t removedIndex = it->second;
				size_t lastIndex = m_Components.size() - 1;

				// Swap with last element
				m_Components[removedIndex] = m_Components[lastIndex];
				m_Components.pop_back();

				// Update maps
				EntityID lastEntity = m_IndexToEntity[lastIndex];
				m_EntityToIndex[lastEntity] = removedIndex;
				m_IndexToEntity[removedIndex] = lastEntity;

				m_EntityToIndex.erase(entity);
				m_IndexToEntity.erase(lastIndex);
			}
		}

		virtual void OnEntityDestroyed(EntityID entity) override
		{
			RemoveComponent(entity);
		}

		size_t GetSize() const { return m_Components.size(); }

	private:
		std::vector<T> m_Components;
		std::unordered_map<EntityID, size_t> m_EntityToIndex;
		std::unordered_map<size_t, EntityID> m_IndexToEntity;
	};
}
