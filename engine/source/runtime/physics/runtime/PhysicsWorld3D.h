#pragma once
#include "foundation/MathTypes.h"
#include "PhysicsKernelExport.h"
#include <cstdint>
#include <memory>
namespace NcmaEngine::Physics {
using BodyHandle3D = std::uint64_t;
class PhysicsWorld3D final {
public:
    NCMA_PHYSICS_KERNEL explicit PhysicsWorld3D(Vector3 gravity = Vector3(0.0F, -9.81F, 0.0F));
    NCMA_PHYSICS_KERNEL ~PhysicsWorld3D();
    PhysicsWorld3D(const PhysicsWorld3D&) = delete;
    PhysicsWorld3D& operator=(const PhysicsWorld3D&) = delete;
    NCMA_PHYSICS_KERNEL BodyHandle3D CreateBox(const Vector3& position, const Vector3& halfExtents, bool dynamicBody, float density = 1.0F);
    NCMA_PHYSICS_KERNEL void DestroyBody(BodyHandle3D body);
    NCMA_PHYSICS_KERNEL void Step(float deltaSeconds);
    NCMA_PHYSICS_KERNEL Vector3 GetPosition(BodyHandle3D body) const;
    NCMA_PHYSICS_KERNEL Vector3 GetVelocity(BodyHandle3D body) const;
    NCMA_PHYSICS_KERNEL Quaternion GetRotation(BodyHandle3D body) const;
    NCMA_PHYSICS_KERNEL void SetVelocity(BodyHandle3D body, const Vector3& velocity);
    NCMA_PHYSICS_KERNEL std::size_t GetBodyCount() const noexcept;
private:
    struct State;
    std::unique_ptr<State> m_State;
};
}
