#pragma once
#include "foundation/MathTypes.h"
#include "PhysicsKernelExport.h"
#include <cstdint>
#include <memory>
namespace NcmaEngine::Physics {
using BodyHandle2D = std::uint64_t;
class PhysicsWorld2D final {
public:
    NCMA_PHYSICS_KERNEL explicit PhysicsWorld2D(Vector2 gravity = Vector2(0.0F, -9.81F));
    NCMA_PHYSICS_KERNEL ~PhysicsWorld2D();
    PhysicsWorld2D(const PhysicsWorld2D&) = delete;
    PhysicsWorld2D& operator=(const PhysicsWorld2D&) = delete;
    NCMA_PHYSICS_KERNEL BodyHandle2D CreateBox(const Vector2& position, const Vector2& halfExtents, bool dynamicBody, float density = 1.0F);
    NCMA_PHYSICS_KERNEL void DestroyBody(BodyHandle2D body);
    NCMA_PHYSICS_KERNEL void Step(float deltaSeconds, int subSteps = 4);
    NCMA_PHYSICS_KERNEL Vector2 GetPosition(BodyHandle2D body) const;
    NCMA_PHYSICS_KERNEL Vector2 GetVelocity(BodyHandle2D body) const;
    NCMA_PHYSICS_KERNEL float GetRotation(BodyHandle2D body) const;
    NCMA_PHYSICS_KERNEL void SetVelocity(BodyHandle2D body, const Vector2& velocity);
    NCMA_PHYSICS_KERNEL std::size_t GetBodyCount() const noexcept;
private:
    struct State;
    std::unique_ptr<State> m_State;
};
}

