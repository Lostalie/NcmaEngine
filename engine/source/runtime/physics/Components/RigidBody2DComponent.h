#pragma once

#include "CoreMinimal.h"
#include "scene/entity/Entity.h"
#include "Interface/IPhysicsWorld.h"
#include <Eigen/Dense>

namespace NcmaEngine
{
	// 2D 刚体组件
	class RigidBody2DComponent : public Component
	{
	public:
		RigidBody2DComponent()
			: m_BodyId(0)
			, m_Type(RigidBodyType::Dynamic)
			, m_Velocity(Eigen::Vector2f::Zero())
			, m_AngularVelocity(0.0f)
			, m_LinearDamping(0.0f)
			, m_AngularDamping(0.0f)
			, m_FixedRotation(false)
		{
		}

		// 刚体ID (由物理世界分配)
		uint64_t m_BodyId;

		// 刚体类型
		RigidBodyType m_Type;

		// 速度
		Eigen::Vector2f m_Velocity;
		float m_AngularVelocity;

		// 阻尼
		float m_LinearDamping;
		float m_AngularDamping;

		// 固定旋转
		bool m_FixedRotation;

		// 检查是否有效
		bool IsValid() const { return m_BodyId != 0; }
	};
}
