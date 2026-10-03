#include "physics/runtime/PhysicsWorld2D.h"

#include <algorithm>
#include <stdexcept>

namespace NcmaEngine::Physics
{
    PhysicsWorld2D::PhysicsWorld2D(Vector2 gravity)
    {
        b2WorldDef worldDefinition = b2DefaultWorldDef();
        worldDefinition.gravity = {gravity.x(), gravity.y()};
        m_World = b2CreateWorld(&worldDefinition);
        if (!b2World_IsValid(m_World))
            throw std::runtime_error("Failed to create Box2D world");
    }

    PhysicsWorld2D::~PhysicsWorld2D()
    {
        if (b2World_IsValid(m_World))
            b2DestroyWorld(m_World);
    }

    BodyHandle2D PhysicsWorld2D::CreateBox(
        const Vector2& position, const Vector2& halfExtents, bool dynamicBody, float density)
    {
        b2BodyDef bodyDefinition = b2DefaultBodyDef();
        bodyDefinition.type = dynamicBody ? b2_dynamicBody : b2_staticBody;
        bodyDefinition.position = {position.x(), position.y()};
        const b2BodyId body = b2CreateBody(m_World, &bodyDefinition);
        if (!b2Body_IsValid(body))
            throw std::runtime_error("Failed to create Box2D body");

        b2ShapeDef shapeDefinition = b2DefaultShapeDef();
        shapeDefinition.density = std::max(density, 0.0F);
        shapeDefinition.material.friction = 0.4F;
        const b2Polygon box = b2MakeBox(std::max(halfExtents.x(), 0.001F), std::max(halfExtents.y(), 0.001F));
        const b2ShapeId shape = b2CreatePolygonShape(body, &shapeDefinition, &box);
        if (!b2Shape_IsValid(shape))
        {
            b2DestroyBody(body);
            throw std::runtime_error("Failed to create Box2D box shape");
        }
        const BodyHandle2D handle = m_NextBody++;
        m_Bodies.emplace(handle, body);
        return handle;
    }

    void PhysicsWorld2D::DestroyBody(BodyHandle2D body)
    {
        const auto it = m_Bodies.find(body);
        if (it == m_Bodies.end())
            return;
        if (b2Body_IsValid(it->second))
            b2DestroyBody(it->second);
        m_Bodies.erase(it);
    }

    void PhysicsWorld2D::Step(float deltaSeconds, int subSteps)
    {
        if (deltaSeconds > 0.0F)
            b2World_Step(m_World, deltaSeconds, std::max(subSteps, 1));
    }

    Vector2 PhysicsWorld2D::GetPosition(BodyHandle2D body) const
    {
        const b2Vec2 position = b2Body_GetPosition(RequireBody(body));
        return {position.x, position.y};
    }

    Vector2 PhysicsWorld2D::GetVelocity(BodyHandle2D body) const
    {
        const b2Vec2 velocity = b2Body_GetLinearVelocity(RequireBody(body));
        return {velocity.x, velocity.y};
    }

    void PhysicsWorld2D::SetVelocity(BodyHandle2D body, const Vector2& velocity)
    {
        b2Body_SetLinearVelocity(RequireBody(body), {velocity.x(), velocity.y()});
    }

    b2BodyId PhysicsWorld2D::RequireBody(BodyHandle2D body) const
    {
        const auto it = m_Bodies.find(body);
        if (it == m_Bodies.end() || !b2Body_IsValid(it->second))
            throw std::out_of_range("Unknown Box2D body handle");
        return it->second;
    }
}
