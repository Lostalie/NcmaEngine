#pragma once

#include "CoreMinimal.h"
#include "Interface/IPhysicsWorld.h"
#include <Jolt/Jolt.h>
#include <Jolt/Physics/PhysicsSystem.h>
#include <Jolt/Physics/Body/Body.h>
#include <Jolt/Physics/Body/BodyCreationSettings.h>
#include <Jolt/Physics/Body/BodyID.h>
#include <Jolt/Physics/Collision/Shape/BoxShape.h>
#include <Jolt/Physics/Collision/Shape/SphereShape.h>
#include <Jolt/Physics/Collision/Shape/Shape.h>
#include <Jolt/Core/TempAllocator.h>
#include <Jolt/Core/JobSystemThreadPool.h>
#include <unordered_map>

JPH_NAMESPACE_BEGIN

class PhysicsSystem;
class Body;
class BodyInterface;

JPH_NAMESPACE_END

namespace NcmaEngine
{
	// Jolt 刚体数据
	struct JoltBodyData
	{
		JPH::BodyID BodyID;
		uint64_t UserData = 0;
	};

	// Jolt 碰撞体数据
	struct JoltColliderData
	{
		JPH::ShapeRefC Shape;
		uint64_t UserData = 0;
	};

	// Jolt 物理世界实现
	class JoltPhysicsWorld : public IPhysicsWorld
	{
	public:
		JoltPhysicsWorld();
		virtual ~JoltPhysicsWorld();

		// IPhysicsWorld 接口
		virtual void Step(float deltaTime) override;
		virtual Eigen::Vector3f GetGravity() const override;
		virtual void SetGravity(const Eigen::Vector3f& gravity) override;
		virtual PhysicsType GetPhysicsType() const override { return PhysicsType::Jolt; }

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
		JPH::BodyID GetBodyID(uint64_t bodyId) const;
		JPH::Body* GetBody(uint64_t bodyId);
		const JPH::Body* GetBody(uint64_t bodyId) const;

	private:
		void InitializeJolt();
		void CleanupJolt();

		JPH::PhysicsSystem* m_PhysicsSystem = nullptr;
		JPH::BodyInterface* m_BodyInterface = nullptr;
		JPH::TempAllocator* m_TempAllocator = nullptr;
		JPH::JobSystemThreadPool* m_JobSystem = nullptr;

		std::unordered_map<uint64_t, JoltBodyData> m_Bodies;
		std::unordered_map<uint64_t, JoltColliderData> m_Colliders;
		std::unordered_map<JPH::BodyID, uint64_t> m_BodyIDToUser;
		uint64_t m_NextBodyId = 1;
		uint64_t m_NextColliderId = 1;
		Eigen::Vector3f m_Gravity = Eigen::Vector3f(0.0f, -9.81f, 0.0f);
		bool m_bInitialized = false;
	};
}