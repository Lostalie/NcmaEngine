#pragma once

#include "foundation/MathTypes.h"

#include <Jolt/Jolt.h>
#include <Jolt/Core/JobSystemThreadPool.h>
#include <Jolt/Core/TempAllocator.h>
#include <Jolt/Physics/Body/BodyID.h>
#include <Jolt/Physics/Collision/BroadPhase/BroadPhaseLayerInterfaceTable.h>
#include <Jolt/Physics/Collision/BroadPhase/ObjectVsBroadPhaseLayerFilterTable.h>
#include <Jolt/Physics/Collision/ObjectLayerPairFilterTable.h>
#include <Jolt/Physics/PhysicsSystem.h>

#include <cstdint>
#include <memory>
#include <unordered_map>

namespace NcmaEngine::Physics
{
    using BodyHandle3D = std::uint64_t;

    class PhysicsWorld3D final
    {
    public:
        explicit PhysicsWorld3D(Vector3 gravity = Vector3(0.0F, -9.81F, 0.0F));
        ~PhysicsWorld3D();

        PhysicsWorld3D(const PhysicsWorld3D&) = delete;
        PhysicsWorld3D& operator=(const PhysicsWorld3D&) = delete;

        [[nodiscard]] BodyHandle3D CreateBox(
            const Vector3& position, const Vector3& halfExtents,
            bool dynamicBody, float density = 1.0F);
        void DestroyBody(BodyHandle3D body);
        void Step(float deltaSeconds);

        [[nodiscard]] Vector3 GetPosition(BodyHandle3D body) const;
        [[nodiscard]] Vector3 GetVelocity(BodyHandle3D body) const;
        void SetVelocity(BodyHandle3D body, const Vector3& velocity);
        [[nodiscard]] std::size_t GetBodyCount() const noexcept { return m_Bodies.size(); }

    private:
        [[nodiscard]] JPH::BodyID RequireBody(BodyHandle3D body) const;

        static constexpr JPH::ObjectLayer NonMovingLayer = 0;
        static constexpr JPH::ObjectLayer MovingLayer = 1;
        static constexpr JPH::BroadPhaseLayer NonMovingBroadPhase{0};
        static constexpr JPH::BroadPhaseLayer MovingBroadPhase{1};

        std::unique_ptr<JPH::ObjectLayerPairFilterTable> m_ObjectPairs;
        std::unique_ptr<JPH::BroadPhaseLayerInterfaceTable> m_BroadPhaseLayers;
        std::unique_ptr<JPH::ObjectVsBroadPhaseLayerFilterTable> m_ObjectVsBroadPhase;
        std::unique_ptr<JPH::TempAllocatorImpl> m_TempAllocator;
        std::unique_ptr<JPH::JobSystemThreadPool> m_JobSystem;
        std::unique_ptr<JPH::PhysicsSystem> m_System;
        BodyHandle3D m_NextBody = 1;
        std::unordered_map<BodyHandle3D, JPH::BodyID> m_Bodies;
    };
}
