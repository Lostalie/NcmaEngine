#pragma once

#include "CoreMinimal.h"
#include "core/event/Event.h"
#include "scene/entity/Entity.h"
#include <Eigen/Dense>

namespace NcmaEngine
{
	// 碰撞事件
	class CollisionEvent : public Event
	{
	public:
		CollisionEvent()
			: m_EntityA()
			, m_EntityB()
			, m_ContactPoint(Eigen::Vector3f::Zero())
			, m_ContactNormal(Eigen::Vector3f::Zero())
			, m_Penetration(0.0f)
		{
		}

		CollisionEvent(Entity entityA, Entity entityB, const Eigen::Vector3f& contactPoint,
			const Eigen::Vector3f& contactNormal, float penetration)
			: m_EntityA(entityA)
			, m_EntityB(entityB)
			, m_ContactPoint(contactPoint)
			, m_ContactNormal(contactNormal)
			, m_Penetration(penetration)
		{
		}

		Entity GetEntityA() const { return m_EntityA; }
		Entity GetEntityB() const { return m_EntityB; }
		const Eigen::Vector3f& GetContactPoint() const { return m_ContactPoint; }
		const Eigen::Vector3f& GetContactNormal() const { return m_ContactNormal; }
		float GetPenetration() const { return m_Penetration; }

		DECLARE_EVENT_TYPE(CollisionEvent)

	private:
		Entity m_EntityA;
		Entity m_EntityB;
		Eigen::Vector3f m_ContactPoint;
		Eigen::Vector3f m_ContactNormal;
		float m_Penetration;
	};
}
