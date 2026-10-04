#include "PhysicsWorld2D.h"
#include <box2d/box2d.h>
#include <unordered_map>
#include <stdexcept>
namespace NcmaEngine::Physics {
struct PhysicsWorld2D::State {
    b2WorldId world = b2_nullWorldId;
    std::unordered_map<BodyHandle2D, b2BodyId> bodies;
    BodyHandle2D next = 1;
    explicit State(Vector2 gravity) {
        auto def = b2DefaultWorldDef(); def.gravity = {gravity.x(), gravity.y()};
        world = b2CreateWorld(&def);
        if (!b2World_IsValid(world)) throw std::runtime_error("Box2D world creation failed.");
    }
    ~State() { if (b2World_IsValid(world)) b2DestroyWorld(world); }
    b2BodyId Require(BodyHandle2D id) const {
        auto it = bodies.find(id);
        if (it == bodies.end() || !b2Body_IsValid(it->second)) throw std::out_of_range("Unknown Box2D body.");
        return it->second;
    }
};
PhysicsWorld2D::PhysicsWorld2D(Vector2 gravity) : m_State(std::make_unique<State>(gravity)) {}
PhysicsWorld2D::~PhysicsWorld2D() = default;
BodyHandle2D PhysicsWorld2D::CreateBox(const Vector2& position, const Vector2& extent, bool dynamic, float density) {
    auto& s = *m_State;
    s.bodies.reserve(s.bodies.size() + 1);
    auto def = b2DefaultBodyDef(); def.type = dynamic ? b2_dynamicBody : b2_staticBody;
    def.position = {position.x(), position.y()};
    const auto body = b2CreateBody(s.world, &def);
    if (!b2Body_IsValid(body)) throw std::runtime_error("Box2D body creation failed.");
    try {
        auto shapeDef = b2DefaultShapeDef(); shapeDef.density = density; shapeDef.material.friction = .4F;
        const auto box = b2MakeBox(extent.x(), extent.y());
        if (!b2Shape_IsValid(b2CreatePolygonShape(body, &shapeDef, &box))) throw std::runtime_error("Box2D shape creation failed.");
        const auto handle = s.next++;
        s.bodies.emplace(handle, body); return handle;
    } catch (...) { b2DestroyBody(body); throw; }
}
void PhysicsWorld2D::DestroyBody(BodyHandle2D body) {
    auto& s = *m_State; const auto it = s.bodies.find(body);
    if (it == s.bodies.end()) return;
    b2DestroyBody(it->second); s.bodies.erase(it);
}
void PhysicsWorld2D::Step(float dt, int subSteps) { if (dt > 0) b2World_Step(m_State->world, dt, subSteps); }
Vector2 PhysicsWorld2D::GetPosition(BodyHandle2D body) const { auto v = b2Body_GetPosition(m_State->Require(body)); return {v.x,v.y}; }
Vector2 PhysicsWorld2D::GetVelocity(BodyHandle2D body) const { auto v = b2Body_GetLinearVelocity(m_State->Require(body)); return {v.x,v.y}; }
float PhysicsWorld2D::GetRotation(BodyHandle2D body) const { return b2Rot_GetAngle(b2Body_GetRotation(m_State->Require(body))); }
void PhysicsWorld2D::SetVelocity(BodyHandle2D body, const Vector2& v) { b2Body_SetLinearVelocity(m_State->Require(body), {v.x(),v.y()}); }
std::size_t PhysicsWorld2D::GetBodyCount() const noexcept { return m_State->bodies.size(); }
}
