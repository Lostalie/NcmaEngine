#include "JoltPhysicsWorld.h"
#include <iostream>
#include <cmath>

JPH_SUPPRESS_WARNING_PUSH
JPH_SUPPRESS_WARNINGS
JPH_USE_STD_UNIQUE_PTR
#include <Jolt/Jolt.h>
#include <Jolt/Core/Factory.h>
#include <Jolt/Physics/Collision/Shape/BoxShape.h>
#include <Jolt/Physics/Collision/Shape/SphereShape.h>
#include <Jolt/Physics/Collision/BroadPhaseLayer.h>
#include <Jolt/Physics/Collision/ObjectLayer.h>
JPH_SUPPRESS_WARNING_POP

JPH_NS_BEGIN

// Jolt 5.x uses a different broadphase layer system
// Define broadphase layers
namespace BroadPhaseLayers
{
	static constexpr JPH::BroadPhaseLayer STATIC(0);
	static constexpr JPH::BroadPhaseLayer DYNAMIC(1);
};

// Create mapping from object layer to broadphase layer
class ObjectLayerPairFilter : public JPH::ObjectLayerPairFilter
{
public:
	virtual bool ShouldCollide(JPH::ObjectLayer inLayer1, JPH::ObjectLayer inLayer2) const override
	{
		switch (inLayer1)
		{
		case 0: return inLayer2 == 1; // Static collides with dynamic
		case 1: return true;          // Dynamic collides with everything
		default: return false;
		}
	}
};

// Create broadphase layer interface
class BroadPhaseLayerInterface : public JPH::BroadPhaseLayerInterface
{
public:
	virtual void GetBroadPhaseLayer(JPH::ObjectLayer inLayer, JPH::BroadPhaseLayer& outLayer) const override
	{
		switch (inLayer)
		{
		case 0: outLayer = BroadPhaseLayers::STATIC; break;
		case 1: outLayer = BroadPhaseLayers::DYNAMIC; break;
		default: outLayer = JPH::BroadPhaseLayer(0xffffffff); break;
		}
	}
};

JPH_NS_END

namespace NcmaEngine
{
	JoltPhysicsWorld::JoltPhysicsWorld()
	{
		InitializeJolt();
	}

	JoltPhysicsWorld::~JoltPhysicsWorld()
	{
		CleanupJolt();
	}

	void JoltPhysicsWorld::InitializeJolt()
	{
		if (m_bInitialized)
			return;

		// 创建 Jolt 物理系统
		m_PhysicsSystem = new JPH::PhysicsSystem();

		// 初始化物理系统 - Jolt 5.x 使用更简单的默认broadphase层
		m_PhysicsSystem->Init(
			1024,    // 最大刚体数量
			0,       // 刚体互斥锁数量
			1024,    // 最大触点数量
			1024,    // 最大约束数量
			JPH::BroadPhaseLayerInterfaceDefault(),
			JPH::ObjectVsBroadPhaseLayerFilterDefault(),
			JPH::ObjectLayerPairFilterDefault()
		);

		// 设置重力
		m_PhysicsSystem->SetGravity(JPH::Vec3(m_Gravity.x(), m_Gravity.y(), m_Gravity.z()));

		// 获取 BodyInterface
		m_BodyInterface = &m_PhysicsSystem->GetBodyInterface();

		// 创建临时分配器和作业系统
		m_TempAllocator = new JPH::TempAllocatorImpl(10 * 1024 * 1024); // 10MB
		m_JobSystem = new JPH::JobSystemThreadPool(JPH::cMaxPhysicsJobs, JPH::cMaxPhysicsBarriers, JPH::cNo多头数);

		m_bInitialized = true;
	}

	void JoltPhysicsWorld::CleanupJolt()
	{
		m_Colliders.clear();
		m_Bodies.clear();
		m_BodyIDToUser.clear();

		if (m_JobSystem)
		{
			delete m_JobSystem;
			m_JobSystem = nullptr;
		}

		if (m_TempAllocator)
		{
			delete m_TempAllocator;
			m_TempAllocator = nullptr;
		}

		if (m_PhysicsSystem)
		{
			delete m_PhysicsSystem;
			m_PhysicsSystem = nullptr;
		}

		m_BodyInterface = nullptr;
		m_bInitialized = false;
	}

	void JoltPhysicsWorld::Step(float deltaTime)
	{
		if (m_PhysicsSystem && !m_bPaused)
		{
			// Jolt 5.x 使用固定时间步长，需要TempAllocator和JobSystem
			const float timeStep = std::min(deltaTime, 1.0f / 30.0f);
			const int collisionSteps = 1;

			m_PhysicsSystem->Update(timeStep, collisionSteps, m_TempAllocator, m_JobSystem);
		}
	}

	Eigen::Vector3f JoltPhysicsWorld::GetGravity() const
	{
		return m_Gravity;
	}

	void JoltPhysicsWorld::SetGravity(const Eigen::Vector3f& gravity)
	{
		m_Gravity = gravity;
		if (m_PhysicsSystem)
		{
			m_PhysicsSystem->SetGravity(JPH::Vec3(gravity.x(), gravity.y(), gravity.z()));
		}
	}

	uint64_t JoltPhysicsWorld::CreateStaticBody(const Eigen::Vector3f& position, const Eigen::Vector3f& halfExtents)
	{
		if (!m_BodyInterface)
			return 0;

		JPH::BoxShapeSettings shapeSettings(JPH::Vec3(halfExtents.x(), halfExtents.y(), halfExtents.z()));
		JPH::ShapeRefC shape = shapeSettings.Create().Get();
		if (!shape)
			return 0;

		JPH::BodyCreationSettings settings(shape, JPH::RVec3(position.x(), position.y(), position.z()), JPH::Quat::sIdentity(), JPH::EMotionType::Static);
		settings.mUserData = m_NextBodyId;

		JPH::Body* body = m_BodyInterface->CreateBody(settings);
		if (!body)
			return 0;

		JPH::BodyID bodyID = body->GetID();
		m_BodyInterface->AddBody(bodyID, JPH::EActivation::DontActivate);

		uint64_t bodyId = m_NextBodyId++;
		m_Bodies[bodyId] = { bodyID, bodyId };
		m_BodyIDToUser[bodyID] = bodyId;

		return bodyId;
	}

	uint64_t JoltPhysicsWorld::CreateDynamicBody(const Eigen::Vector3f& position, const Eigen::Vector3f& halfExtents, float density)
	{
		if (!m_BodyInterface)
			return 0;

		JPH::BoxShapeSettings shapeSettings(JPH::Vec3(halfExtents.x(), halfExtents.y(), halfExtents.z()));
		JPH::ShapeRefC shape = shapeSettings.Create().Get();
		if (!shape)
			return 0;

		JPH::BodyCreationSettings settings(shape, JPH::RVec3(position.x(), position.y(), position.z()), JPH::Quat::sIdentity(), JPH::EMotionType::Dynamic);
		settings.mUserData = m_NextBodyId;
		settings.mDensity = density;

		JPH::Body* body = m_BodyInterface->CreateBody(settings);
		if (!body)
			return 0;

		JPH::BodyID bodyID = body->GetID();
		m_BodyInterface->AddBody(bodyID, JPH::EActivation::Activate);

		uint64_t bodyId = m_NextBodyId++;
		m_Bodies[bodyId] = { bodyID, bodyId };
		m_BodyIDToUser[bodyID] = bodyId;

		return bodyId;
	}

	void JoltPhysicsWorld::RemoveBody(uint64_t bodyId)
	{
		auto it = m_Bodies.find(bodyId);
		if (it != m_Bodies.end())
		{
			m_BodyInterface->RemoveBody(it->second.BodyID);
			m_BodyIDToUser.erase(it->second.BodyID);
			m_Bodies.erase(it);
		}
	}

	void JoltPhysicsWorld::SetBodyTransform(uint64_t bodyId, const Eigen::Vector3f& position, const Eigen::Quaternionf& rotation)
	{
		JPH::Body* body = GetBody(bodyId);
		if (body)
		{
			JPH::Quat quat(rotation.x(), rotation.y(), rotation.z(), rotation.w());
			body->SetPositionAndRotation(JPH::RVec3(position.x(), position.y(), position.z()), quat);
		}
	}

	void JoltPhysicsWorld::SetBodyVelocity(uint64_t bodyId, const Eigen::Vector3f& velocity)
	{
		JPH::Body* body = GetBody(bodyId);
		if (body)
		{
			body->SetLinearVelocity(JPH::Vec3(velocity.x(), velocity.y(), velocity.z()));
		}
	}

	void JoltPhysicsWorld::SetBodyAngularVelocity(uint64_t bodyId, const Eigen::Vector3f& angularVelocity)
	{
		JPH::Body* body = GetBody(bodyId);
		if (body)
		{
			body->SetAngularVelocity(JPH::Vec3(angularVelocity.x(), angularVelocity.y(), angularVelocity.z()));
		}
	}

	Eigen::Vector3f JoltPhysicsWorld::GetBodyPosition(uint64_t bodyId) const
	{
		const JPH::Body* body = GetBody(bodyId);
		if (body)
		{
			const JPH::RVec3& pos = body->GetPosition();
			return Eigen::Vector3f(pos.GetX(), pos.GetY(), pos.GetZ());
		}
		return Eigen::Vector3f::Zero();
	}

	Eigen::Quaternionf JoltPhysicsWorld::GetBodyRotation(uint64_t bodyId) const
	{
		const JPH::Body* body = GetBody(bodyId);
		if (body)
		{
			const JPH::Quat& quat = body->GetRotation();
			return Eigen::Quaternionf(quat.GetW(), quat.GetX(), quat.GetY(), quat.GetZ());
		}
		return Eigen::Quaternionf::Identity();
	}

	Eigen::Vector3f JoltPhysicsWorld::GetBodyVelocity(uint64_t bodyId) const
	{
		const JPH::Body* body = GetBody(bodyId);
		if (body)
		{
			const JPH::Vec3& vel = body->GetLinearVelocity();
			return Eigen::Vector3f(vel.GetX(), vel.GetY(), vel.GetZ());
		}
		return Eigen::Vector3f::Zero();
	}

	Eigen::Vector3f JoltPhysicsWorld::GetBodyAngularVelocity(uint64_t bodyId) const
	{
		const JPH::Body* body = GetBody(bodyId);
		if (body)
		{
			const JPH::Vec3& vel = body->GetAngularVelocity();
			return Eigen::Vector3f(vel.GetX(), vel.GetY(), vel.GetZ());
		}
		return Eigen::Vector3f::Zero();
	}

	uint64_t JoltPhysicsWorld::CreateBoxCollider(uint64_t bodyId, const Eigen::Vector3f& halfExtents, const Eigen::Vector3f& offset, float density, float friction, float restitution)
	{
		JPH::Body* body = GetBody(bodyId);
		if (!body)
			return 0;

		JPH::BoxShapeSettings shapeSettings(JPH::Vec3(halfExtents.x(), halfExtents.y(), halfExtents.z()));
		JPH::ShapeRefC shape = shapeSettings.Create().Get();
		if (!shape)
			return 0;

		// 注意：Jolt 中碰撞体是通过形状添加的，这里简化处理
		uint64_t colliderId = m_NextColliderId++;
		m_Colliders[colliderId] = { shape, colliderId };

		return colliderId;
	}

	uint64_t JoltPhysicsWorld::CreateSphereCollider(uint64_t bodyId, float radius, const Eigen::Vector3f& offset, float density, float friction, float restitution)
	{
		JPH::Body* body = GetBody(bodyId);
		if (!body)
			return 0;

		JPH::SphereShapeSettings shapeSettings(radius);
		JPH::ShapeRefC shape = shapeSettings.Create().Get();
		if (!shape)
			return 0;

		uint64_t colliderId = m_NextColliderId++;
		m_Colliders[colliderId] = { shape, colliderId };

		return colliderId;
	}

	JPH::BodyID JoltPhysicsWorld::GetBodyID(uint64_t bodyId) const
	{
		auto it = m_Bodies.find(bodyId);
		if (it != m_Bodies.end())
		{
			return it->second.BodyID;
		}
		return JPH::BodyID();
	}

	JPH::Body* JoltPhysicsWorld::GetBody(uint64_t bodyId)
	{
		auto it = m_Bodies.find(bodyId);
		if (it != m_Bodies.end())
		{
			return m_PhysicsSystem->GetBodyLockInterface().GetBody(it->second.BodyID);
		}
		return nullptr;
	}

	const JPH::Body* JoltPhysicsWorld::GetBody(uint64_t bodyId) const
	{
		auto it = m_Bodies.find(bodyId);
		if (it != m_Bodies.end())
		{
			return m_PhysicsSystem->GetBodyLockInterface().GetBody(it->second.BodyID);
		}
		return nullptr;
	}
}