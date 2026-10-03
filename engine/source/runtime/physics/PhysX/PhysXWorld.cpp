#include "PhysXWorld.h"
#include <iostream>

// 禁用 PhysX 的 pragma comment lib 链接，我们需要手动链接
#ifdef _DEBUG
#define PX_PHYSICS_LIB "PhysXDEBUG_x64.lib"
#define PX_FOUNDATION_LIB "PxFoundationDEBUG_x64.lib"
#define PX_PVD_LIB "PhysXPVDDEBUG_x64.lib"
#else
#define PX_PHYSICS_LIB "PhysX_x64.lib"
#define PX_FOUNDATION_LIB "PxFoundation_x64.lib"
#define PX_PVD_LIB "PhysXPVD_x64.lib"
#endif

#pragma comment(lib, PX_PHYSICS_LIB)
#pragma comment(lib, PX_FOUNDATION_LIB)
#pragma comment(lib, PX_PVD_LIB)

namespace NcmaEngine
{
	PhysXWorld::PhysXWorld()
	{
		InitializePhysX();
	}

	PhysXWorld::~PhysXWorld()
	{
		CleanupPhysX();
	}

	void PhysXWorld::InitializePhysX()
	{
		if (m_bInitialized)
			return;

		// 创建 Foundation
		m_Foundation = PxCreateFoundation(PX_PHYSICS_VERSION, PxDefaultAllocatorCallback(), PxDefaultErrorCallback());
		if (!m_Foundation)
		{
			std::cerr << "Failed to create PhysX Foundation!" << std::endl;
			return;
		}

		// 创建 Physics 对象
		m_Physics = PxCreatePhysics(PX_PHYSICS_VERSION, *m_Foundation, physx::PxTolerancesScale(), true);
		if (!m_Physics)
		{
			std::cerr << "Failed to create PhysX Physics!" << std::endl;
			CleanupPhysX();
			return;
		}

		// 创建默认 CPU 调度器
		m_Dispatcher = physx::PxDefaultCpuDispatcherCreate(2);
		if (!m_Dispatcher)
		{
			std::cerr << "Failed to create PhysX Dispatcher!" << std::endl;
			CleanupPhysX();
			return;
		}

		// 创建场景描述
		physx::PxSceneDesc sceneDesc(m_Physics->getTolerancesScale());
		sceneDesc.gravity = physx::PxVec3(m_Gravity.x(), m_Gravity.y(), m_Gravity.z());
		sceneDesc.cpuDispatcher = m_Dispatcher;
		sceneDesc.filterShader = physx::PxDefaultSimulationFilterShader;

		// 创建场景
		m_Scene = m_Physics->createScene(sceneDesc);
		if (!m_Scene)
		{
			std::cerr << "Failed to create PhysX Scene!" << std::endl;
			CleanupPhysX();
			return;
		}

		// 创建默认材质
		m_DefaultMaterial = m_Physics->createMaterial(0.3f, 0.3f, 0.3f);
		if (!m_DefaultMaterial)
		{
			std::cerr << "Failed to create PhysX Material!" << std::endl;
			CleanupPhysX();
			return;
		}

		m_bInitialized = true;
	}

	void PhysXWorld::CleanupPhysX()
	{
		m_Colliders.clear();
		m_Bodies.clear();

		if (m_Scene)
		{
			m_Scene->release();
			m_Scene = nullptr;
		}

		if (m_Dispatcher)
		{
			m_Dispatcher->release();
			m_Dispatcher = nullptr;
		}

		if (m_DefaultMaterial)
		{
			m_DefaultMaterial->release();
			m_DefaultMaterial = nullptr;
		}

		if (m_Physics)
		{
			m_Physics->release();
			m_Physics = nullptr;
		}

		if (m_Foundation)
		{
			m_Foundation->release();
			m_Foundation = nullptr;
		}

		m_bInitialized = false;
	}

	void PhysXWorld::Step(float deltaTime)
	{
		if (m_Scene && !m_bPaused)
		{
			m_Scene->simulate(deltaTime);
			m_Scene->fetchResults(true);
		}
	}

	Eigen::Vector3f PhysXWorld::GetGravity() const
	{
		return m_Gravity;
	}

	void PhysXWorld::SetGravity(const Eigen::Vector3f& gravity)
	{
		m_Gravity = gravity;
		if (m_Scene)
		{
			m_Scene->setGravity(physx::PxVec3(gravity.x(), gravity.y(), gravity.z()));
		}
	}

	uint64_t PhysXWorld::CreateStaticBody(const Eigen::Vector3f& position, const Eigen::Vector3f& halfExtents)
	{
		if (!m_Physics || !m_Scene)
			return 0;

		physx::PxRigidStatic* actor = m_Physics->createRigidStatic(physx::PxTransform(position.x(), position.y(), position.z()));
		if (!actor)
			return 0;

		// 创建盒形几何体
		physx::PxBoxGeometry geometry(halfExtents.x(), halfExtents.y(), halfExtents.z());
		physx::PxShape* shape = actor->createShape(geometry, *m_DefaultMaterial);
		if (!shape)
		{
			actor->release();
			return 0;
		}

		m_Scene->addActor(*actor);

		uint64_t bodyId = m_NextBodyId++;
		PhysXBodyData data;
		data.Actor = actor;
		data.UserData = bodyId;
		m_Bodies[bodyId] = data;

		return bodyId;
	}

	uint64_t PhysXWorld::CreateDynamicBody(const Eigen::Vector3f& position, const Eigen::Vector3f& halfExtents, float density)
	{
		if (!m_Physics || !m_Scene)
			return 0;

		physx::PxRigidDynamic* actor = m_Physics->createRigidDynamic(physx::PxTransform(position.x(), position.y(), position.z()));
		if (!actor)
			return 0;

		// 创建盒形几何体
		physx::PxBoxGeometry geometry(halfExtents.x(), halfExtents.y(), halfExtents.z());
		physx::PxShape* shape = actor->createShape(geometry, *m_DefaultMaterial);
		if (!shape)
		{
			actor->release();
			return 0;
		}

		// 设置质量
		physx::PxRigidBodyExt::setMassAndUpdateInertia(*actor, density);

		m_Scene->addActor(*actor);

		uint64_t bodyId = m_NextBodyId++;
		PhysXBodyData data;
		data.Actor = actor;
		data.UserData = bodyId;
		m_Bodies[bodyId] = data;

		return bodyId;
	}

	void PhysXWorld::RemoveBody(uint64_t bodyId)
	{
		auto it = m_Bodies.find(bodyId);
		if (it != m_Bodies.end() && it->second.Actor)
		{
			it->second.Actor->release();
			m_Bodies.erase(it);
		}
	}

	void PhysXWorld::SetBodyTransform(uint64_t bodyId, const Eigen::Vector3f& position, const Eigen::Quaternionf& rotation)
	{
		physx::PxRigidActor* actor = GetBody(bodyId);
		if (actor)
		{
			physx::PxQuat quat(rotation.x(), rotation.y(), rotation.z(), rotation.w());
			actor->setGlobalPose(physx::PxTransform(position.x(), position.y(), position.z(), quat));
		}
	}

	void PhysXWorld::SetBodyVelocity(uint64_t bodyId, const Eigen::Vector3f& velocity)
	{
		physx::PxRigidBody* body = dynamic_cast<physx::PxRigidBody*>(GetBody(bodyId));
		if (body)
		{
			body->setLinearVelocity(physx::PxVec3(velocity.x(), velocity.y(), velocity.z()));
		}
	}

	void PhysXWorld::SetBodyAngularVelocity(uint64_t bodyId, const Eigen::Vector3f& angularVelocity)
	{
		physx::PxRigidBody* body = dynamic_cast<physx::PxRigidBody*>(GetBody(bodyId));
		if (body)
		{
			body->setAngularVelocity(physx::PxVec3(angularVelocity.x(), angularVelocity.y(), angularVelocity.z()));
		}
	}

	Eigen::Vector3f PhysXWorld::GetBodyPosition(uint64_t bodyId) const
	{
		const physx::PxRigidActor* actor = GetBody(bodyId);
		if (actor)
		{
			const physx::PxVec3& pos = actor->getGlobalPose().p;
			return Eigen::Vector3f(pos.x, pos.y, pos.z);
		}
		return Eigen::Vector3f::Zero();
	}

	Eigen::Quaternionf PhysXWorld::GetBodyRotation(uint64_t bodyId) const
	{
		const physx::PxRigidActor* actor = GetBody(bodyId);
		if (actor)
		{
			const physx::PxQuat& quat = actor->getGlobalPose().q;
			return Eigen::Quaternionf(quat.w, quat.x, quat.y, quat.z);
		}
		return Eigen::Quaternionf::Identity();
	}

	Eigen::Vector3f PhysXWorld::GetBodyVelocity(uint64_t bodyId) const
	{
		const physx::PxRigidBody* body = dynamic_cast<const physx::PxRigidBody*>(GetBody(bodyId));
		if (body)
		{
			const physx::PxVec3& vel = body->getLinearVelocity();
			return Eigen::Vector3f(vel.x, vel.y, vel.z);
		}
		return Eigen::Vector3f::Zero();
	}

	Eigen::Vector3f PhysXWorld::GetBodyAngularVelocity(uint64_t bodyId) const
	{
		const physx::PxRigidBody* body = dynamic_cast<const physx::PxRigidBody*>(GetBody(bodyId));
		if (body)
		{
			const physx::PxVec3& vel = body->getAngularVelocity();
			return Eigen::Vector3f(vel.x, vel.y, vel.z);
		}
		return Eigen::Vector3f::Zero();
	}

	uint64_t PhysXWorld::CreateBoxCollider(uint64_t bodyId, const Eigen::Vector3f& halfExtents, const Eigen::Vector3f& offset, float density, float friction, float restitution)
	{
		physx::PxRigidActor* actor = GetBody(bodyId);
		if (!actor)
			return 0;

		physx::PxMaterial* material = m_Physics->createMaterial(friction, friction, restitution);
		if (!material)
			material = m_DefaultMaterial;

		physx::PxBoxGeometry geometry(halfExtents.x(), halfExtents.y(), halfExtents.z());
		physx::PxShape* shape = actor->createShape(geometry, *material);
		if (!shape)
			return 0;

		physx::PxVec3 localOffset(offset.x(), offset.y(), offset.z());
		shape->setLocalPose(physx::PxTransform(localOffset));

		uint64_t colliderId = m_NextColliderId++;
		PhysXColliderData data;
		data.Shape = shape;
		data.UserData = colliderId;
		m_Colliders[colliderId] = data;

		return colliderId;
	}

	uint64_t PhysXWorld::CreateSphereCollider(uint64_t bodyId, float radius, const Eigen::Vector3f& offset, float density, float friction, float restitution)
	{
		physx::PxRigidActor* actor = GetBody(bodyId);
		if (!actor)
			return 0;

		physx::PxMaterial* material = m_Physics->createMaterial(friction, friction, restitution);
		if (!material)
			material = m_DefaultMaterial;

		physx::PxSphereGeometry geometry(radius);
		physx::PxShape* shape = actor->createShape(geometry, *material);
		if (!shape)
			return 0;

		physx::PxVec3 localOffset(offset.x(), offset.y(), offset.z());
		shape->setLocalPose(physx::PxTransform(localOffset));

		uint64_t colliderId = m_NextColliderId++;
		PhysXColliderData data;
		data.Shape = shape;
		data.UserData = colliderId;
		m_Colliders[colliderId] = data;

		return colliderId;
	}

	physx::PxRigidActor* PhysXWorld::GetBody(uint64_t bodyId)
	{
		auto it = m_Bodies.find(bodyId);
		if (it != m_Bodies.end())
		{
			return it->second.Actor;
		}
		return nullptr;
	}

	const physx::PxRigidActor* PhysXWorld::GetBody(uint64_t bodyId) const
	{
		auto it = m_Bodies.find(bodyId);
		if (it != m_Bodies.end())
		{
			return it->second.Actor;
		}
		return nullptr;
	}
}
