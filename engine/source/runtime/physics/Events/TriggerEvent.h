#pragma once

#include "CoreMinimal.h"
#include "core/event/Event.h"
#include "scene/entity/Entity.h"

namespace NcmaEngine
{
	// 触发器事件 (进入/离开)
	class TriggerEvent : public Event
	{
	public:
		TriggerEvent()
			: m_EntityA()
			, m_EntityB()
			, m_IsEnter(true)
		{
		}

		TriggerEvent(Entity entityA, Entity entityB, bool isEnter)
			: m_EntityA(entityA)
			, m_EntityB(entityB)
			, m_IsEnter(isEnter)
		{
		}

		Entity GetEntityA() const { return m_EntityA; }
		Entity GetEntityB() const { return m_EntityB; }
		bool IsEnter() const { return m_IsEnter; }
		bool IsLeave() const { return !m_IsEnter; }

		DECLARE_EVENT_TYPE(TriggerEvent)

	private:
		Entity m_EntityA;
		Entity m_EntityB;
		bool m_IsEnter;
	};
}
