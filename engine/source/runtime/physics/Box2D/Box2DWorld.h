#pragma once

#include "CoreMinimal.h"
#include "Interface/IPhysicsWorld.h"
#include <box2d/box2d.h>
#include <unordered_map>
#include <unordered_set>

namespace NcmaEngine
{
	// Box2D 刚体数据
	struct Box2DBodyData
	{
		b2BodyId BodyId = b2NullBodyId;
		uint64_t UserData = 0;  // 存储实体ID或其他用户数据
	};

	// Box2D 碰撞体数据
	struct Box2DColliderData
	{
		b2ShapeId ShapeId = b2NullShapeId;
		uint64_t UserData = 0;
	};

	// Box2D 物理世界实现
	class Box2DWorld : public IPhysicsWorld
	{
	public:
		Box2DWorld();
		virtual ~Box2DWorld();

		// IPhysicsWorld 接口
		virtual void Step(float deltaTime) override;
		virtual Eigen::Vector3f GetGravity() const override;
		virtual void SetGravity(const Eigen::Vector3f& gravity) override;
		virtual PhysicsType GetPhysicsType() const override { return PhysicsType::Box2D; }

		// 刚体管理
		uint64_t CreateBody(RigidBodyType type, const Eigen::Vector3f& position, const Eigen::Vector2f& halfExtents, float density = 1.0f);
		void RemoveBody(uint64_t bodyId);
		void SetBodyTransform(uint64_t bodyId, const Eigen::Vector3f& position, float rotation);
		void SetBodyVelocity(uint64_t bodyId, const Eigen::Vector2f& velocity);
		void SetBodyAngularVelocity(uint64_t bodyId, float angularVelocity);
		Eigen::Vector3f GetBodyPosition(uint64_t bodyId) const;
		float GetBodyRotation(uint64_t bodyId) const;
		Eigen::Vector2f GetBodyVelocity(uint64_t bodyId) const;
		float GetBodyAngularVelocity(uint64_t bodyId) const;

		// 碰撞体管理
		uint64_t CreateBoxCollider(uint64_t bodyId, const Eigen::Vector2f& halfExtents, const Eigen::Vector2f& offset, float density = 1.0f, float friction = 0.3f, float restitution = 0.3f);
		uint64_t CreateCircleCollider(uint64_t bodyId, float radius, const Eigen::Vector2f& offset, float density = 1.0f, float friction = 0.3f, float restitution = 0.3f);

		// 查找函数
		b2BodyId GetBodyId(uint64_t bodyId);
		const b2BodyId GetBodyId(uint64_t bodyId) const;

	private:
		b2WorldId m_WorldId = b2NullWorldId;
		std::unordered_map<uint64_t, Box2DBodyData> m_Bodies;
		std::unordered_map<uint64_t, Box2DColliderData> m_Colliders;
		uint64_t m_NextBodyId = 1;
		uint64_t m_NextColliderId = 1;
		Eigen::Vector3f m_Gravity = Eigen::Vector3f(0.0f, -9.81f, 0.0f);
	};
}