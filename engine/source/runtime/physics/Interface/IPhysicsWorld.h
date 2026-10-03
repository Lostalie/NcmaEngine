#pragma once

#include "CoreMinimal.h"
#include <Eigen/Dense>

namespace NcmaEngine
{
	// 物理引擎类型
	enum class PhysicsType
	{
		None = 0,
		Box2D = 1,
		Jolt = 2
	};

	// 碰撞体形状类型
	enum class ColliderShape
	{
		Box,
		Sphere,
		Capsule,
		Plane
	};

	// 刚体类型
	enum class RigidBodyType
	{
		Static = 0,
		Dynamic = 1,
		Kinematic = 2
	};

	// 物理世界接口 - 抽象 2D/3D 物理世界
	class IPhysicsWorld
	{
	public:
		virtual ~IPhysicsWorld() = default;

		// 物理世界步进
		virtual void Step(float deltaTime) = 0;

		// 重力设置
		virtual Eigen::Vector3f GetGravity() const = 0;
		virtual void SetGravity(const Eigen::Vector3f& gravity) = 0;

		// 物理类型查询
		virtual PhysicsType GetPhysicsType() const = 0;

		// 同步标志
		virtual void SetPaused(bool paused) { m_bPaused = paused; }
		virtual bool IsPaused() const { return m_bPaused; }

	protected:
		bool m_bPaused = false;
	};

	// 物理世界创建函数类型
	using PhysicsWorldCreateFunc = std::unique_ptr<IPhysicsWorld>(*)();

	// 获取物理世界类型名称
	inline const char* GetPhysicsTypeName(PhysicsType type)
	{
		switch (type)
		{
		case PhysicsType::Box2D: return "Box2D";
		case PhysicsType::Jolt: return "Jolt";
		default: return "None";
		}
	}
}
