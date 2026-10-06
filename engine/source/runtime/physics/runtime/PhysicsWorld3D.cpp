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
#include <Jolt/Physics/Collision/Shape/CapsuleShape.h>
#include <Jolt/Physics/Collision/Shape/RotatedTranslatedShape.h>
#include <Jolt/Physics/Character/CharacterVirtual.h>
#include <Jolt/Physics/Collision/RayCast.h>
#include <Jolt/Physics/Collision/CastResult.h>
#include <Jolt/Physics/Collision/ShapeCast.h>
#include <Jolt/Physics/Collision/CollisionCollectorImpl.h>
#include <Jolt/Physics/Body/BodyLock.h>
#include <Jolt/RegisterTypes.h>
#include <unordered_map>
#include <mutex>
#include <stdexcept>
#include <algorithm>
#include <tuple>
#include <array>
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
JPH::Vec3 Vec(const float* p) { return {p[0],p[1],p[2]}; }
JPH::RVec3 RVec(const float* p) { return {p[0],p[1],p[2]}; }
template<class V> void Copy(float* p,const V& v) { p[0]=static_cast<float>(v.GetX());p[1]=static_cast<float>(v.GetY());p[2]=static_cast<float>(v.GetZ()); }
void CopyRotation(float* p,JPH::QuatArg q) { p[0]=q.GetX();p[1]=q.GetY();p[2]=q.GetZ();p[3]=q.GetW(); }
JPH::Quat Rotation(const float* p) { return {p[0],p[1],p[2],p[3]}; }
JPH::RefConst<JPH::Shape> Capsule(float radius,float halfHeight) {
    JPH::RotatedTranslatedShapeSettings shape(JPH::Vec3(0,radius+halfHeight,0),JPH::Quat::sIdentity(),new JPH::CapsuleShape(halfHeight,radius));
    auto result=shape.Create();if(result.HasError())throw std::runtime_error(result.GetError().c_str());return result.Get();
}
// Conservative bounded candidate budget across ALL queries of one ExtendedUpdate.
// Jolt's GetMaxHitsExceeded describes its latest collector, which can be overwritten
// by stair/floor queries; never rely on that final flag alone to hide earlier overflow.
struct CharacterCandidateBudget {
    std::array<uint64_t,NCMA_MAX_CHARACTER_CONTACTS> seen{};uint32_t count=0;bool exceeded=false;
    bool Add(uint64_t id) {
        if(std::find(seen.begin(),seen.begin()+count,id)!=seen.begin()+count)return true;
        if(count==seen.size()){exceeded=true;return false;}seen[count++]=id;return true;
    }
};
class MaskFilter final : public JPH::BodyFilter {
    uint32_t mask; JPH::BodyID ignore; bool skipCharacters;
    CharacterCandidateBudget* budget;
public:
    MaskFilter(uint32_t m,JPH::BodyID i={},bool skip=false,CharacterCandidateBudget* b=nullptr):mask(m),ignore(i),skipCharacters(skip),budget(b){}
    bool ShouldCollide(const JPH::BodyID& id) const override { return id!=ignore; }
    bool ShouldCollideLocked(const JPH::Body& body) const override {
        const auto data=body.GetUserData();return (static_cast<uint32_t>(data)&mask)!=0 && (!skipCharacters || !(data&(uint64_t{1}<<32))) &&
            (!budget || budget->Add(body.GetID().GetIndexAndSequenceNumber()));
    }
};
class CharacterMaskListener final : public JPH::CharacterContactListener {
public:
    CharacterCandidateBudget* budget=nullptr; // Owner-thread numerical scope only, no external callback.
    bool OnCharacterContactValidate(const JPH::CharacterVirtual* a,const JPH::CharacterVirtual* b,const JPH::SubShapeID&) override {
        const auto ad=a->GetUserData(),bd=b->GetUserData();
        return (static_cast<uint32_t>(ad)&static_cast<uint32_t>(bd>>32))!=0 && (static_cast<uint32_t>(bd)&static_cast<uint32_t>(ad>>32))!=0 &&
            (!budget || budget->Add((uint64_t{1}<<32)|b->GetID().GetValue()));
    }
};
}
struct PhysicsWorld3D::State {
    std::shared_ptr<JoltRuntime> owner = AcquireRuntime();
    JPH::ObjectLayerPairFilterTable pairs{2};
    JPH::BroadPhaseLayerInterfaceTable layers{2,2};
    std::unique_ptr<JPH::ObjectVsBroadPhaseLayerFilterTable> filter;
    JPH::TempAllocatorImpl allocator{16 * 1024 * 1024};
    JPH::PhysicsSystem system;
    std::unordered_map<BodyHandle3D, JPH::BodyID> bodies;
    struct Character { JPH::Ref<JPH::CharacterVirtual> value; NcmaCapsuleDescriptionV1 settings; };
    CharacterMaskListener listener;
    JPH::CharacterVsCharacterCollisionSimple characterCollision;
    std::unordered_map<uint64_t,Character> characters;
    BodyHandle3D next = 1;
    explicit State(Vector3 gravity) {
        pairs.EnableCollision(0,1); pairs.EnableCollision(1,1);
        layers.MapObjectToBroadPhaseLayer(0,JPH::BroadPhaseLayer(0));
        layers.MapObjectToBroadPhaseLayer(1,JPH::BroadPhaseLayer(1));
        filter = std::make_unique<JPH::ObjectVsBroadPhaseLayerFilterTable>(layers,2,pairs,2);
        system.Init(4096+NCMA_MAX_CHARACTERS,0,16384,16384,layers,*filter,pairs);
        system.SetGravity({gravity.x(),gravity.y(),gravity.z()});
    }
    ~State() {
        for (auto& item:characters) characterCollision.Remove(item.second.value);
        characters.clear();
        auto& api = system.GetBodyInterface();
        for (const auto& entry : bodies) { api.RemoveBody(entry.second); api.DestroyBody(entry.second); }
        // system/allocator destroyed before last owner joins workers and removes factory.
    }
    JPH::BodyID Require(BodyHandle3D id) const {
        const auto it = bodies.find(id);
        if (it == bodies.end()) throw std::out_of_range("Unknown Jolt body.");
        return it->second;
    }
    const Character& RequireCharacter(uint64_t id) const {
        auto it=characters.find(id);if(it==characters.end())throw std::out_of_range("Unknown Jolt character.");return it->second;
    }
    std::pair<uint64_t,uint32_t> Identify(JPH::BodyID id) const {
        if(id.IsInvalid())return {0,0};
        for(const auto& item:bodies)if(item.second==id)return {item.first,1};
        for(const auto& item:characters)if(item.second.value->GetInnerBodyID()==id)return {item.first,2};
        throw std::runtime_error("Unmapped Jolt body.");
    }
    uint64_t Identify(JPH::CharacterID id) const {
        for(const auto& item:characters)if(item.second.value->GetID()==id)return item.first;
        throw std::runtime_error("Unmapped Jolt character.");
    }
    JPH::BodyID Ignore(uint64_t id) const {
        if(!id)return {};
        if(auto it=bodies.find(id);it!=bodies.end())return it->second;
        return RequireCharacter(id).value->GetInnerBodyID();
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
    settings.mUserData = 0xffffffffu; // Old boxes participate in every character/query category.
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
BodyHandle3D PhysicsWorld3D::CreateCollisionBox(const NcmaCollisionBoxV1& d) {
    auto handle=CreateBox({d.position[0],d.position[1],d.position[2]}, {d.half_extents[0],d.half_extents[1],d.half_extents[2]},d.dynamic_body!=0,d.density);
    auto& s=*m_State;
    try {
        auto id=s.Require(handle);s.system.GetBodyInterface().SetUserData(id,d.category);
        s.system.GetBodyInterface().SetRotation(id,Rotation(d.rotation),d.dynamic_body?JPH::EActivation::Activate:JPH::EActivation::DontActivate);
        return handle;
    }catch(...){DestroyBody(handle);throw;}
}
uint64_t PhysicsWorld3D::CreateCapsule(const NcmaCapsuleDescriptionV1& d) {
    auto& s=*m_State;s.characters.reserve(s.characters.size()+1);
    JPH::CharacterVirtualSettings settings;
    settings.mShape=Capsule(d.radius,d.half_height);settings.mInnerBodyShape=settings.mShape;settings.mInnerBodyLayer=1;
    settings.mMass=d.mass;settings.mMaxStrength=d.max_strength;settings.mMaxSlopeAngle=d.max_slope_radians;
    settings.mCharacterPadding=d.padding;settings.mMaxNumHits=NCMA_MAX_CHARACTER_CONTACTS;
    settings.mSupportingVolume=JPH::Plane(JPH::Vec3::sAxisY(),-d.radius);
    JPH::Ref<JPH::CharacterVirtual> value=new JPH::CharacterVirtual(&settings,RVec(d.position),JPH::Quat::sIdentity(),
        (static_cast<uint64_t>(d.mask)<<32)|d.category,&s.system);
    if(value->GetInnerBodyID().IsInvalid())throw std::runtime_error("Jolt character proxy capacity exhausted.");
    s.system.GetBodyInterface().SetUserData(value->GetInnerBodyID(),(uint64_t{1}<<32)|d.category);
    {
        JPH::BodyLockWrite lock(s.system.GetBodyLockInterface(),value->GetInnerBodyID());
        if(!lock.Succeeded())throw std::runtime_error("Character proxy lock failed.");
        lock.GetBody().SetIsSensor(true); // Query proxy only: no unfiltered duplicate body response.
    }
    value->SetListener(&s.listener);value->SetCharacterVsCharacterCollision(&s.characterCollision);
    const auto id=s.next++;
    s.characters.emplace(id,State::Character{value,d});
    try {s.characterCollision.Add(value);return id;}
    catch(...){s.characters.erase(id);throw;}
}
void PhysicsWorld3D::DestroyCharacter(uint64_t character) {
    auto& s=*m_State;auto it=s.characters.find(character);if(it==s.characters.end())throw std::out_of_range("Unknown Jolt character.");
    s.characterCollision.Remove(it->second.value);s.characters.erase(it);
}
void PhysicsWorld3D::StepCharacters(float dt,const NcmaCharacterVelocityV1* inputs,uint32_t count) {
    auto& s=*m_State;
    Step(dt); // Exactly one ordinary body quantum, not one per character.
    for(uint32_t i=0;i<count;++i) {
        const auto& item=s.RequireCharacter(inputs[i].character);auto& character=*item.value;
        character.SetRotation(Rotation(inputs[i].rotation));character.SetLinearVelocity(Vec(inputs[i].velocity));
        JPH::CharacterVirtual::ExtendedUpdateSettings settings;
        settings.mWalkStairsStepUp={0,item.settings.step_height,0};settings.mStickToFloorStepDown={0,-item.settings.floor_distance,0};
        CharacterCandidateBudget budget;s.listener.budget=&budget;
        struct Reset {CharacterMaskListener& listener;~Reset(){listener.budget=nullptr;}} reset{s.listener};
        character.ExtendedUpdate(dt,s.system.GetGravity(),settings,s.system.GetDefaultBroadPhaseLayerFilter(1),
            s.system.GetDefaultLayerFilter(1),MaskFilter(item.settings.mask,{},true,&budget),{},s.allocator);
        if(budget.exceeded || character.GetMaxHitsExceeded() || character.GetActiveContacts().size()>NCMA_MAX_CHARACTER_CONTACTS)
            throw std::runtime_error("Character contact budget exceeded; numerical domain is faulted.");
    }
}
NcmaCharacterStateV1 PhysicsWorld3D::ReadCharacter(uint64_t id) const {
    const auto& s=*m_State;const auto& item=s.RequireCharacter(id);const auto& c=*item.value;NcmaCharacterStateV1 result{};
    result.character=id;result.correlation=item.settings.correlation;result.ground_state=static_cast<uint32_t>(c.GetGroundState());
    Copy(result.position,c.GetPosition());Copy(result.velocity,c.GetLinearVelocity());CopyRotation(result.rotation,c.GetRotation());
    Copy(result.ground_normal,c.GetGroundNormal());Copy(result.ground_velocity,c.GetGroundVelocity());
    auto ground=s.Identify(c.GetGroundBodyID());
    // Virtual-character support has no Jolt body ID; map the exact copied support contact,
    // never dereference Jolt's potentially stale mCharacterB pointer.
    if(!ground.first && c.GetGroundState()!=JPH::CharacterBase::EGroundState::InAir) {
        for(const auto& contact:c.GetActiveContacts())if(contact.mBodyB.IsInvalid() && !contact.mCharacterIDB.IsInvalid() &&
            contact.mPosition==c.GetGroundPosition() && contact.mSubShapeIDB==c.GetGroundSubShapeID()) {
            ground={s.Identify(contact.mCharacterIDB),2};break;
        }
    }
    result.ground_body=ground.first;result.ground_kind=ground.second;return result;
}
uint32_t PhysicsWorld3D::ReadContacts(uint64_t id,NcmaCharacterContactV1* output,uint32_t capacity) const {
    const auto& s=*m_State;const auto& contacts=s.RequireCharacter(id).value->GetActiveContacts();
    if(contacts.size()>capacity)throw std::runtime_error("Character contact capacity exceeded.");
    uint32_t written=0;
    for(const auto& contact:contacts) {
        auto other=contact.mBodyB.IsInvalid()?std::pair<uint64_t,uint32_t>{s.Identify(contact.mCharacterIDB),2}:s.Identify(contact.mBodyB);
        auto& result=output[written++];result={};result.character=id;result.other=other.first;result.other_kind=other.second;
        result.subshape=contact.mSubShapeIDB.GetValue();result.separation=contact.mDistance;
        result.flags=(contact.mHadCollision?1u:0u)|(contact.mWasDiscarded?2u:0u)|(contact.mIsSensorB?4u:0u);
        Copy(result.position,contact.mPosition);Copy(result.normal,contact.mContactNormal);
    }
    std::sort(output,output+written,[](const auto& a,const auto& b){return std::tie(a.other_kind,a.other,a.subshape)<std::tie(b.other_kind,b.other,b.subshape);});
    return written;
}
NcmaPhysicsQueryHitV1 PhysicsWorld3D::Ray(const NcmaPhysicsRayV1& d) const {
    const auto& s=*m_State;JPH::RRayCast ray(RVec(d.origin),Vec(d.displacement));JPH::RayCastResult hit;NcmaPhysicsQueryHitV1 result{};
    result.fraction=1;
    if(s.system.GetNarrowPhaseQuery().CastRay(ray,hit,{}, {},MaskFilter(d.mask,s.Ignore(d.ignore)))) {
        const auto other=s.Identify(hit.mBodyID);result.hit=1;result.kind=other.second;result.resource=other.first;
        result.fraction=hit.mFraction;result.subshape=hit.mSubShapeID2.GetValue();const auto point=ray.GetPointOnRay(hit.mFraction);Copy(result.position,point);
        JPH::BodyLockRead lock(s.system.GetBodyLockInterface(),hit.mBodyID);
        if(!lock.Succeeded())throw std::runtime_error("Ray body lock failed.");
        Copy(result.normal,lock.GetBody().GetWorldSpaceSurfaceNormal(hit.mSubShapeID2,point));
    }
    return result;
}
NcmaPhysicsQueryHitV1 PhysicsWorld3D::Sweep(const NcmaPhysicsCapsuleSweepV1& d) const {
    const auto& s=*m_State;auto shape=Capsule(d.radius,d.half_height);
    const auto transform=JPH::RMat44::sRotationTranslation(Rotation(d.rotation),RVec(d.foot));
    const auto cast=JPH::RShapeCast::sFromWorldTransform(shape,JPH::Vec3::sReplicate(1),transform,Vec(d.displacement));
    JPH::ShapeCastSettings settings;settings.mReturnDeepestPoint=true;
    JPH::ClosestHitCollisionCollector<JPH::CastShapeCollector> collector;
    s.system.GetNarrowPhaseQuery().CastShape(cast,settings,JPH::RVec3::sZero(),collector,{}, {},MaskFilter(d.mask,s.Ignore(d.ignore)));
    NcmaPhysicsQueryHitV1 result{};result.fraction=1;
    if(collector.HadHit()) {
        const auto& hit=collector.mHit;const auto other=s.Identify(hit.mBodyID2);
        result.hit=1;result.kind=other.second;result.resource=other.first;result.fraction=hit.mFraction;result.subshape=hit.mSubShapeID2.GetValue();
        Copy(result.position,hit.mContactPointOn2);Copy(result.normal,-hit.mPenetrationAxis.NormalizedOr(JPH::Vec3::sAxisY()));
    }
    return result;
}
}
