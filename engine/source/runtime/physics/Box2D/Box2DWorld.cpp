#include "Box2DWorld.h"
#include <iostream>

namespace NcmaEngine
{
	Box2DWorld::Box2DWorld()
	{
		// 创建 Box2D 世界定义
		b2WorldDef worldDef = b2DefaultWorldDef();
		worldDef.gravity = b2Vec2{ m_Gravity.x(), m_Gravity.y() };
		worldDef.restitutionThreshold = 0.3f;
		worldDef.contactHertz = 10.0f;
		worldDef.contactDampingRatio = 0.5f;

		m_WorldId = b2CreateWorld(&worldDef);
	}

	Box2DWorld::~Box2DWorld()
	{
		// 清理所有碰撞体
		for (auto& pair : m_Colliders)
		{
			// Box2D 自动管理 shapes
		}
		m_Colliders.clear();

		// 清理所有刚体
		for (auto& pair : m_Bodies)
		{
			if (b2Body_IsValid(pair.second.BodyId))
			{
				b2DestroyBody(pair.second.BodyId);
			}
		}
		m_Bodies.clear();

		// 销毁世界
		if (b2World_IsValid(m_WorldId))
		{
			b2DestroyWorld(m_WorldId);
		}
		m_WorldId = b2NullWorldId;
	}

	void Box2DWorld::Step(float deltaTime)
	{
		if (b2World_IsValid(m_WorldId) && !m_bPaused)
		{
			// Box2D 3.x 推荐使用固定时间步长，subStepCount 通常为 4-8
			const float timeStep = 1.0f / 60.0f;
			const int subStepCount = 4;

			b2World_Step(m_WorldId, timeStep, subStepCount);
		}
	}

	Eigen::Vector3f Box2DWorld::GetGravity() const
	{
		return m_Gravity;
	}

	void Box2DWorld::SetGravity(const Eigen::Vector3f& gravity)
	{
		m_Gravity = gravity;
		if (b2World_IsValid(m_WorldId))
		{
			b2World_SetGravity(m_WorldId, b2Vec2{ gravity.x(), gravity.y() });
		}
	}

	uint64_t Box2DWorld::CreateBody(RigidBodyType type, const Eigen::Vector3f& position, const Eigen::Vector2f& halfExtents, float density)
	{
		if (!b2World_IsValid(m_WorldId))
			return 0;

		b2BodyDef bodyDef = b2DefaultBodyDef();

		switch (type)
		{
		case RigidBodyType::Static:
			bodyDef.type = b2_staticBody;
			break;
		case RigidBodyType::Dynamic:
			bodyDef.type = b2_dynamicBody;
			break;
		case RigidBodyType::Kinematic:
			bodyDef.type = b2_kinematicBody;
			break;
		}

		bodyDef.position = b2Vec2{ position.x(), position.y() };
		bodyDef.rotation = b2MakeRot(0.0f);  // 默认无旋转
		bodyDef.linearDamping = 0.0f;
		bodyDef.angularDamping = 0.0f;

		b2BodyId bodyId = b2CreateBody(m_WorldId, &bodyDef);
		if (!b2Body_IsValid(bodyId))
			return 0;

		// 创建盒形碰撞体
		b2ShapeDef shapeDef = b2DefaultShapeDef();
		shapeDef.density = density;
		shapeDef.friction = 0.3f;
		shapeDef.restitution = 0.3f;

		b2Polygon polygon = b2MakeBox(halfExtents.x(), halfExtents.y());

		b2CreatePolygonShape(bodyId, &polygon, &shapeDef);

		uint64_t bodyIdLocal = m_NextBodyId++;
		m_Bodies[bodyIdLocal] = { bodyId, bodyIdLocal };

		return bodyIdLocal;
	}

	void Box2DWorld::RemoveBody(uint64_t bodyId)
	{
		auto it = m_Bodies.find(bodyId);
		if (it != m_Bodies.end() && b2Body_IsValid(it->second.BodyId))
		{
			b2DestroyBody(it->second.BodyId);
			m_Bodies.erase(it);
		}
	}

	void Box2DWorld::SetBodyTransform(uint64_t bodyId, const Eigen::Vector3f& position, float rotation)
	{
		b2BodyId id = GetBodyId(bodyId);
		if (b2Body_IsValid(id))
		{
			b2Rot rot = b2MakeRot(rotation);
			b2Body_SetTransform(id, b2Vec2{ position.x(), position.y() }, rot);
		}
	}

	void Box2DWorld::SetBodyVelocity(uint64_t bodyId, const Eigen::Vector2f& velocity)
	{
		b2BodyId id = GetBodyId(bodyId);
		if (b2Body_IsValid(id))
		{
			b2Body_SetLinearVelocity(id, b2Vec2{ velocity.x(), velocity.y() });
		}
	}

	void Box2DWorld::SetBodyAngularVelocity(uint64_t bodyId, float angularVelocity)
	{
		b2BodyId id = GetBodyId(bodyId);
		if (b2Body_IsValid(id))
		{
			b2Body_SetAngularVelocity(id, angularVelocity);
		}
	}

	Eigen::Vector3f Box2DWorld::GetBodyPosition(uint64_t bodyId) const
	{
		b2BodyId id = GetBodyId(bodyId);
		if (b2Body_IsValid(id))
		{
			b2Vec2 pos = b2Body_GetPosition(id);
			return Eigen::Vector3f(pos.x, pos.y, 0.0f);
		}
		return Eigen::Vector3f::Zero();
	}

	float Box2DWorld::GetBodyRotation(uint64_t bodyId) const
	{
		b2BodyId id = GetBodyId(bodyId);
		if (b2Body_IsValid(id))
		{
			b2Rot rot = b2Body_GetRotation(id);
			return b2Rot_GetAngle(rot);
		}
		return 0.0f;
	}

	Eigen::Vector2f Box2DWorld::GetBodyVelocity(uint64_t bodyId) const
	{
		b2BodyId id = GetBodyId(bodyId);
		if (b2Body_IsValid(id))
		{
			b2Vec2 vel = b2Body_GetLinearVelocity(id);
			return Eigen::Vector2f(vel.x, vel.y);
		}
		return Eigen::Vector2f::Zero();
	}

	float Box2DWorld::GetBodyAngularVelocity(uint64_t bodyId) const
	{
		b2BodyId id = GetBodyId(bodyId);
		if (b2Body_IsValid(id))
		{
			return b2Body_GetAngularVelocity(id);
		}
		return 0.0f;
	}

	uint64_t Box2DWorld::CreateBoxCollider(uint64_t bodyId, const Eigen::Vector2f& halfExtents, const Eigen::Vector2f& offset, float density, float friction, float restitution)
	{
		b2BodyId id = GetBodyId(bodyId);
		if (!b2Body_IsValid(id))
			return 0;

		b2ShapeDef shapeDef = b2DefaultShapeDef();
		shapeDef.density = density;
		shapeDef.friction = friction;
		shapeDef.restitution = restitution;

		// 使用 b2MakeOffsetBox 创建带偏移的盒形
		b2Polygon polygon = b2MakeOffsetBox(halfExtents.x(), halfExtents.y(),
			b2Vec2{ offset.x(), offset.y() }, b2MakeRot(0.0f));

		b2ShapeId shapeId = b2CreatePolygonShape(id, &polygon, &shapeDef);
		if (!b2Shape_IsValid(shapeId))
			return 0;

		uint64_t colliderId = m_NextColliderId++;
		m_Colliders[colliderId] = { shapeId, colliderId };

		return colliderId;
	}

	uint64_t Box2DWorld::CreateCircleCollider(uint64_t bodyId, float radius, const Eigen::Vector2f& offset, float density, float friction, float restitution)
	{
		b2BodyId id = GetBodyId(bodyId);
		if (!b2Body_IsValid(id))
			return 0;

		b2ShapeDef shapeDef = b2DefaultShapeDef();
		shapeDef.density = density;
		shapeDef.friction = friction;
		shapeDef.restitution = restitution;

		b2Circle circle;
		circle.center = b2Vec2{ offset.x(), offset.y() };
		circle.radius = radius;

		b2ShapeId shapeId = b2CreateCircleShape(id, &circle, &shapeDef);
		if (!b2Shape_IsValid(shapeId))
			return 0;

		uint64_t colliderId = m_NextColliderId++;
		m_Colliders[colliderId] = { shapeId, colliderId };

		return colliderId;
	}

	b2BodyId Box2DWorld::GetBodyId(uint64_t bodyId)
	{
		auto it = m_Bodies.find(bodyId);
		if (it != m_Bodies.end())
		{
			return it->second.BodyId;
		}
		return b2NullBodyId;
	}

	const b2BodyId Box2DWorld::GetBodyId(uint64_t bodyId) const
	{
		auto it = m_Bodies.find(bodyId);
		if (it != m_Bodies.end())
		{
			return it->second.BodyId;
		}
		return b2NullBodyId;
	}
}