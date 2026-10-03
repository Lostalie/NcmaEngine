#include "physics/runtime/PhysicsWorld3D.h"

#include <Jolt/Core/Factory.h>
#include <Jolt/Core/Memory.h>
#include <Jolt/Physics/Body/BodyCreationSettings.h>
#include <Jolt/Physics/Collision/Shape/BoxShape.h>
#include <Jolt/RegisterTypes.h>

#include <algorithm>
#include <mutex>
#include <stdexcept>
#include <thread>

namespace NcmaEngine::Physics
{
    namespace
    {
        std::mutex JoltMutex;
        std::size_t JoltWorldCount = 0;

        void AcquireJolt()
        {
            std::scoped_lock lock(JoltMutex);
            if (JoltWorldCount++ != 0)
                return;
            JPH::RegisterDefaultAllocator();
            JPH::Factory::sInstance = new JPH::Factory();
            JPH::RegisterTypes();
        }

        void ReleaseJolt()
        {
            std::scoped_lock lock(JoltMutex);
            if (--JoltWorldCount != 0)
                return;
            JPH::UnregisterTypes();
            delete JPH::Factory::sInstance;
            JPH::Factory::sInstance = nullptr;
        }
    }

    PhysicsWorld3D::PhysicsWorld3D(Vector3 gravity)
    {
        AcquireJolt();
        m_ObjectPairs = std::make_unique<JPH::ObjectLayerPairFilterTable>(2);
        m_BroadPhaseLayers = std::make_unique<JPH::BroadPhaseLayerInterfaceTable>(2, 2);
        m_ObjectPairs->EnableCollision(NonMovingLayer, MovingLayer);
        m_ObjectPairs->EnableCollision(MovingLayer, MovingLayer);
        m_BroadPhaseLayers->MapObjectToBroadPhaseLayer(NonMovingLayer, NonMovingBroadPhase);
        m_BroadPhaseLayers->MapObjectToBroadPhaseLayer(MovingLayer, MovingBroadPhase);
        m_ObjectVsBroadPhase = std::make_unique<JPH::ObjectVsBroadPhaseLayerFilterTable>(
            *m_BroadPhaseLayers, 2, *m_ObjectPairs, 2);

        m_TempAllocator = std::make_unique<JPH::TempAllocatorImpl>(8 * 1024 * 1024);
        const unsigned hardwareThreads = std::max(std::thread::hardware_concurrency(), 2U);
        const int workerThreads = static_cast<int>(std::clamp(hardwareThreads - 1U, 1U, 8U));
        m_JobSystem = std::make_unique<JPH::JobSystemThreadPool>(
            JPH::cMaxPhysicsJobs, JPH::cMaxPhysicsBarriers, workerThreads);
        m_System = std::make_unique<JPH::PhysicsSystem>();
        m_System->Init(4096, 0, 4096, 4096,
            *m_BroadPhaseLayers, *m_ObjectVsBroadPhase, *m_ObjectPairs);
        m_System->SetGravity({gravity.x(), gravity.y(), gravity.z()});
    }

    PhysicsWorld3D::~PhysicsWorld3D()
    {
        if (m_System)
        {
            auto& bodies = m_System->GetBodyInterface();
            for (const auto& [handle, body] : m_Bodies)
            {
                (void)handle;
                bodies.RemoveBody(body);
                bodies.DestroyBody(body);
            }
        }
        m_Bodies.clear();
        m_System.reset();
        m_JobSystem.reset();
        m_TempAllocator.reset();
        m_ObjectVsBroadPhase.reset();
        m_BroadPhaseLayers.reset();
        m_ObjectPairs.reset();
        ReleaseJolt();
    }

    BodyHandle3D PhysicsWorld3D::CreateBox(
        const Vector3& position, const Vector3& halfExtents, bool dynamicBody, float density)
    {
        const JPH::Vec3 extent(
            std::max(halfExtents.x(), 0.001F),
            std::max(halfExtents.y(), 0.001F),
            std::max(halfExtents.z(), 0.001F));
        JPH::BodyCreationSettings settings(
            new JPH::BoxShape(extent),
            JPH::RVec3(position.x(), position.y(), position.z()),
            JPH::Quat::sIdentity(),
            dynamicBody ? JPH::EMotionType::Dynamic : JPH::EMotionType::Static,
            dynamicBody ? MovingLayer : NonMovingLayer);
        settings.mOverrideMassProperties = dynamicBody
            ? JPH::EOverrideMassProperties::CalculateInertia
            : JPH::EOverrideMassProperties::CalculateMassAndInertia;
        settings.mMassPropertiesOverride.mMass = std::max(density, 0.001F);
        const JPH::BodyID id = m_System->GetBodyInterface().CreateAndAddBody(
            settings, dynamicBody ? JPH::EActivation::Activate : JPH::EActivation::DontActivate);
        if (id.IsInvalid())
            throw std::runtime_error("Failed to create Jolt body");
        const BodyHandle3D handle = m_NextBody++;
        m_Bodies.emplace(handle, id);
        return handle;
    }

    void PhysicsWorld3D::DestroyBody(BodyHandle3D body)
    {
        const auto it = m_Bodies.find(body);
        if (it == m_Bodies.end())
            return;
        auto& bodies = m_System->GetBodyInterface();
        bodies.RemoveBody(it->second);
        bodies.DestroyBody(it->second);
        m_Bodies.erase(it);
    }

    void PhysicsWorld3D::Step(float deltaSeconds)
    {
        if (deltaSeconds > 0.0F)
            (void)m_System->Update(deltaSeconds, 1, m_TempAllocator.get(), m_JobSystem.get());
    }

    Vector3 PhysicsWorld3D::GetPosition(BodyHandle3D body) const
    {
        const JPH::RVec3 position = m_System->GetBodyInterface().GetPosition(RequireBody(body));
        return {static_cast<float>(position.GetX()), static_cast<float>(position.GetY()), static_cast<float>(position.GetZ())};
    }

    Vector3 PhysicsWorld3D::GetVelocity(BodyHandle3D body) const
    {
        const JPH::Vec3 velocity = m_System->GetBodyInterface().GetLinearVelocity(RequireBody(body));
        return {velocity.GetX(), velocity.GetY(), velocity.GetZ()};
    }

    void PhysicsWorld3D::SetVelocity(BodyHandle3D body, const Vector3& velocity)
    {
        m_System->GetBodyInterface().SetLinearVelocity(
            RequireBody(body), {velocity.x(), velocity.y(), velocity.z()});
    }

    JPH::BodyID PhysicsWorld3D::RequireBody(BodyHandle3D body) const
    {
        const auto it = m_Bodies.find(body);
        if (it == m_Bodies.end())
            throw std::out_of_range("Unknown Jolt body handle");
        return it->second;
    }
}
