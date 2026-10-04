#include "PhysicsWorld3D.h"
#include <Jolt/Jolt.h>
#include <Jolt/Core/Factory.h>
#include <Jolt/Core/Memory.h>
#include <Jolt/Core/JobSystemThreadPool.h>
#include <Jolt/Core/TempAllocator.h>
#include <Jolt/Physics/PhysicsSystem.h>
#include <Jolt/Physics/Collision/BroadPhase/BroadPhaseLayerInterfaceTable.h>
#include <Jolt/Physics/Collision/BroadPhase/ObjectVsBroadPhaseLayerFilterTable.h>
#include <Jolt/Physics/Collision/ObjectLayerPairFilterTable.h>
#include <Jolt/Physics/Body/BodyCreationSettings.h>
#include <Jolt/Physics/Collision/Shape/BoxShape.h>
#include <Jolt/RegisterTypes.h>
#include <unordered_map>
#include <mutex>
#include <stdexcept>
namespace NcmaEngine::Physics {
namespace {
// All active legacy/new worlds share this DLL's one Jolt factory and worker pool.
// Step is owner-thread serialized; workers never call managed code.
struct JoltRuntime {
    std::unique_ptr<JPH::JobSystemThreadPool> jobs;
    JoltRuntime() {
        JPH::RegisterDefaultAllocator();
        auto factory = std::make_unique<JPH::Factory>();
        JPH::Factory::sInstance = factory.get();
        try {
            JPH::RegisterTypes();
            jobs = std::make_unique<JPH::JobSystemThreadPool>(JPH::cMaxPhysicsJobs, JPH::cMaxPhysicsBarriers, 2);
            factory.release();
        } catch (...) { JPH::UnregisterTypes(); JPH::Factory::sInstance = nullptr; throw; }
    }
    ~JoltRuntime() { jobs.reset(); JPH::UnregisterTypes(); delete JPH::Factory::sInstance; JPH::Factory::sInstance = nullptr; }
};
std::mutex runtimeGate;
std::weak_ptr<JoltRuntime> runtime;
std::shared_ptr<JoltRuntime> AcquireRuntime() {
    std::scoped_lock lock(runtimeGate);
    auto value = runtime.lock();
    if (!value) { value = std::make_shared<JoltRuntime>(); runtime = value; }
    return value;
}
}
struct PhysicsWorld3D::State {
    std::shared_ptr<JoltRuntime> owner = AcquireRuntime();
    JPH::ObjectLayerPairFilterTable pairs{2};
    JPH::BroadPhaseLayerInterfaceTable layers{2,2};
    std::unique_ptr<JPH::ObjectVsBroadPhaseLayerFilterTable> filter;
    JPH::TempAllocatorImpl allocator{16 * 1024 * 1024};
    JPH::PhysicsSystem system;
    std::unordered_map<BodyHandle3D, JPH::BodyID> bodies;
    BodyHandle3D next = 1;
    explicit State(Vector3 gravity) {
        pairs.EnableCollision(0,1); pairs.EnableCollision(1,1);
        layers.MapObjectToBroadPhaseLayer(0,JPH::BroadPhaseLayer(0));
        layers.MapObjectToBroadPhaseLayer(1,JPH::BroadPhaseLayer(1));
        filter = std::make_unique<JPH::ObjectVsBroadPhaseLayerFilterTable>(layers,2,pairs,2);
        system.Init(4096,0,16384,16384,layers,*filter,pairs);
        system.SetGravity({gravity.x(),gravity.y(),gravity.z()});
    }
    ~State() {
        auto& api = system.GetBodyInterface();
        for (const auto& entry : bodies) { api.RemoveBody(entry.second); api.DestroyBody(entry.second); }
        // system/allocator destroyed before last owner joins workers and removes factory.
    }
    JPH::BodyID Require(BodyHandle3D id) const {
        const auto it = bodies.find(id);
        if (it == bodies.end()) throw std::out_of_range("Unknown Jolt body.");
        return it->second;
    }
};
PhysicsWorld3D::PhysicsWorld3D(Vector3 gravity) : m_State(std::make_unique<State>(gravity)) {}
PhysicsWorld3D::~PhysicsWorld3D() = default;
BodyHandle3D PhysicsWorld3D::CreateBox(const Vector3& position, const Vector3& extent, bool dynamic, float density) {
    auto& s = *m_State; s.bodies.reserve(s.bodies.size() + 1);
    JPH::BoxShapeSettings shapeSettings({extent.x(),extent.y(),extent.z()});
    shapeSettings.mDensity = density; shapeSettings.mConvexRadius = 0; // metres, kg/m^3; no small-box default-radius assertion.
    const auto shape = shapeSettings.Create();
    if (shape.HasError()) throw std::runtime_error(shape.GetError().c_str());
    JPH::BodyCreationSettings settings(shape.Get(),JPH::RVec3(position.x(),position.y(),position.z()),
        JPH::Quat::sIdentity(),dynamic ? JPH::EMotionType::Dynamic : JPH::EMotionType::Static,
        static_cast<JPH::ObjectLayer>(dynamic ? 1 : 0));
    settings.mFriction = .4F;
    auto& api = s.system.GetBodyInterface();
    const auto body = api.CreateAndAddBody(settings,dynamic ? JPH::EActivation::Activate : JPH::EActivation::DontActivate);
    if (body.IsInvalid()) throw std::runtime_error("Jolt body capacity exhausted.");
    try { const auto handle = s.next++; s.bodies.emplace(handle,body); return handle; }
    catch (...) { api.RemoveBody(body); api.DestroyBody(body); throw; }
}
void PhysicsWorld3D::DestroyBody(BodyHandle3D body) {
    auto& s = *m_State; auto it = s.bodies.find(body); if (it == s.bodies.end()) return;
    auto& api = s.system.GetBodyInterface(); api.RemoveBody(it->second); api.DestroyBody(it->second); s.bodies.erase(it);
}
void PhysicsWorld3D::Step(float dt) {
    if (dt > 0 && m_State->system.Update(dt,1,&m_State->allocator,m_State->owner->jobs.get()) != JPH::EPhysicsUpdateError::None)
        throw std::runtime_error("Jolt solver capacity/update failure.");
}
Vector3 PhysicsWorld3D::GetPosition(BodyHandle3D body) const {
    auto v = m_State->system.GetBodyInterface().GetPosition(m_State->Require(body));
    return {static_cast<float>(v.GetX()),static_cast<float>(v.GetY()),static_cast<float>(v.GetZ())};
}
Vector3 PhysicsWorld3D::GetVelocity(BodyHandle3D body) const {
    auto v = m_State->system.GetBodyInterface().GetLinearVelocity(m_State->Require(body)); return {v.GetX(),v.GetY(),v.GetZ()};
}
Quaternion PhysicsWorld3D::GetRotation(BodyHandle3D body) const {
    auto v = m_State->system.GetBodyInterface().GetRotation(m_State->Require(body)); return {v.GetW(),v.GetX(),v.GetY(),v.GetZ()};
}
void PhysicsWorld3D::SetVelocity(BodyHandle3D body, const Vector3& v) { m_State->system.GetBodyInterface().SetLinearVelocity(m_State->Require(body),{v.x(),v.y(),v.z()}); }
std::size_t PhysicsWorld3D::GetBodyCount() const noexcept { return m_State->bodies.size(); }
}
