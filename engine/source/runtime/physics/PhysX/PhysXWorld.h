#pragma once

#include "CoreMinimal.h"
#include "Interface/IPhysicsWorld.h"
#include <PxPhysicsAPI.h>
#include <unordered_map>

namespace NcmaEngine
{
	// PhysX 刚体数据
	struct PhysXBodyData
	{
		physx::PxRigidActor* Actor = nullptr;
		uint64_t UserData = 0;
	};

	// PhysX 碰撞体数据
	struct PhysXColliderData
	{
		physx::PxShape* Shape = nullptr;
		uint64_t UserData = 0;
	};

	// PhysX 物理世界实现
	class PhysXWorld : public IPhysicsWorld
	{
	public:
		PhysXWorld();
		virtual ~PhysXWorld();

		// IPhysicsWorld 接口
		virtual void Step(float deltaTime) override;
		virtual Eigen::Vector3f GetGravity() const override;
		virtual void SetGravity(const Eigen::Vector3f& gravity) override;
		virtual PhysicsType GetPhysicsType() const override { return PhysicsType::PhysX; }

		// 刚体管理
		uint64_t CreateStaticBody(const Eigen::Vector3f& position, const Eigen::Vector3f& halfExtents);
		uint64_t CreateDynamicBody(const Eigen::Vector3f& position, const Eigen::Vector3f& halfExtents, float density = 1.0f);
		void RemoveBody(uint64_t bodyId);
		void SetBodyTransform(uint64_t bodyId, const Eigen::Vector3f& position, const Eigen::Quaternionf& rotation);
		void SetBodyVelocity(uint64_t bodyId, const Eigen::Vector3f& velocity);
		void SetBodyAngularVelocity(uint64_t bodyId, const Eigen::Vector3f& angularVelocity);
		Eigen::Vector3f GetBodyPosition(uint64_t bodyId) const;
		Eigen::Quaternionf GetBodyRotation(uint64_t bodyId) const;
		Eigen::Vector3f GetBodyVelocity(uint64_t bodyId) const;
		Eigen::Vector3f GetBodyAngularVelocity(uint64_t bodyId) const;

		// 碰撞体管理
		uint64_t CreateBoxCollider(uint64_t bodyId, const Eigen::Vector3f& halfExtents, const Eigen::Vector3f& offset, float density = 1.0f, float friction = 0.3f, float restitution = 0.3f);
		uint64_t CreateSphereCollider(uint64_t bodyId, float radius, const Eigen::Vector3f& offset, float density = 1.0f, float friction = 0.3f, float restitution = 0.3f);

		// 查找函数
		physx::PxRigidActor* GetBody(uint64_t bodyId);
		const physx::PxRigidActor* GetBody(uint64_t bodyId) const;

	private:
		void InitializePhysX();
		void CleanupPhysX();

		physx::PxFoundation* m_Foundation = nullptr;
		physx::PxPhysics* m_Physics = nullptr;
		physx::PxScene* m_Scene = nullptr;
		physx::PxDefaultCpuDispatcher* m_Dispatcher = nullptr;
		physx::PxMaterial* m_DefaultMaterial = nullptr;

		std::unordered_map<uint64_t, PhysXBodyData> m_Bodies;
		std::unordered_map<uint64_t, PhysXColliderData> m_Colliders;
		uint64_t m_NextBodyId = 1;
		uint64_t m_NextColliderId = 1;
		Eigen::Vector3f m_Gravity = Eigen::Vector3f(0.0f, -9.81f, 0.0f);
		bool m_bInitialized = false;
	};
}
