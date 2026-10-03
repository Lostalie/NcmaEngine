#pragma once

#include "CoreMinimal.h"
#include "scene/entity/Entity.h"
#include "Interface/IPhysicsWorld.h"
#include <Eigen/Dense>

namespace NcmaEngine
{
	// 碰撞体组件 - 2D/3D 共用
	class ColliderComponent : public Component
	{
	public:
		ColliderComponent()
			: m_ColliderId(0)
			, m_Shape(ColliderShape::Box)
			, m_Offset(Eigen::Vector3f::Zero())
			, m_HalfExtents(Eigen::Vector3f(0.5f, 0.5f, 0.5f))
			, m_Radius(0.5f)
			, m_Height(1.0f)
			, m_Mass(1.0f)
			, m_Friction(0.3f)
			, m_Restitution(0.3f)
		{
		}

		// 碰撞体ID (由物理世界分配)
		uint64_t m_ColliderId;

		// 碰撞体形状
		ColliderShape m_Shape;

		// 偏移
		Eigen::Vector3f m_Offset;

		// 盒形碰撞体尺寸
		Eigen::Vector3f m_HalfExtents;

		// 球形碰撞体半径
		float m_Radius;

		// 胶囊碰撞体高度
		float m_Height;

		// 物理属性
		float m_Mass;
		float m_Friction;
		float m_Restitution;

		// 检查是否有效
		bool IsValid() const { return m_ColliderId != 0; }
	};
}
