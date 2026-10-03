#pragma once

#include "foundation/MathTypes.h"

#include <box2d/box2d.h>

#include <cstdint>
#include <unordered_map>

namespace NcmaEngine::Physics
{
    using BodyHandle2D = std::uint64_t;

    class PhysicsWorld2D final
    {
    public:
        explicit PhysicsWorld2D(Vector2 gravity = Vector2(0.0F, -9.81F));
        ~PhysicsWorld2D();

        PhysicsWorld2D(const PhysicsWorld2D&) = delete;
        PhysicsWorld2D& operator=(const PhysicsWorld2D&) = delete;

        [[nodiscard]] BodyHandle2D CreateBox(
            const Vector2& position, const Vector2& halfExtents,
            bool dynamicBody, float density = 1.0F);
        void DestroyBody(BodyHandle2D body);
        void Step(float deltaSeconds, int subSteps = 4);

        [[nodiscard]] Vector2 GetPosition(BodyHandle2D body) const;
        [[nodiscard]] Vector2 GetVelocity(BodyHandle2D body) const;
        void SetVelocity(BodyHandle2D body, const Vector2& velocity);
        [[nodiscard]] std::size_t GetBodyCount() const noexcept { return m_Bodies.size(); }

    private:
        [[nodiscard]] b2BodyId RequireBody(BodyHandle2D body) const;

        b2WorldId m_World = b2_nullWorldId;
        BodyHandle2D m_NextBody = 1;
        std::unordered_map<BodyHandle2D, b2BodyId> m_Bodies;
    };
}

