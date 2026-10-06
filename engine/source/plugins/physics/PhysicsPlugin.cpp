#include "../contracts/NcmaPhysics.h"
#include "../PluginSupport.h"
#include "physics/runtime/PhysicsWorld2D.h"
#include "physics/runtime/PhysicsWorld3D.h"
#include <array>
#include <chrono>
#include <cmath>
#include <memory>
#include <unordered_map>
#include <limits>
namespace {
using namespace NcmaEngine;
using Clock = std::chrono::steady_clock;
const auto loadingThread = std::this_thread::get_id();
uint64_t module = 0, nextModule = 1, nextResource = 1;
bool busy = false;
struct Body { uint64_t kernel = 0, correlation = 0; bool dynamic = false; };
struct World {
    uint32_t dimension = 0, maximum = 0, subSteps = 0;
    std::unique_ptr<Physics::PhysicsWorld2D> two;
    std::unique_ptr<Physics::PhysicsWorld3D> three;
    std::unordered_map<uint64_t,Body> bodies;
    std::unordered_map<uint64_t,uint64_t> characters; // Public opaque handle -> numerical kernel handle.
    NcmaPhysicsStatsV1 stats{};
    std::array<double,256> samples{};
    uint64_t sampleCount = 0;
    double lastStepMs = 0; // Raw instrumentation only; v1.1 high-level policy lives in C#.
};
std::unordered_map<uint64_t,std::unique_ptr<World>> worlds;
uint64_t Resource() {
    if (nextResource >= (uint64_t{1} << 48)) throw std::overflow_error("Physics handle space exhausted.");
    return 0x5048000000000000ull | nextResource++;
}
uint32_t Validate(uint64_t context,NcmaErrorV1* error) {
    if (std::this_thread::get_id()!=loadingThread) return NcmaPlugin::Error(error,NCMA_WRONG_THREAD);
    if (!context || context!=module) return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);
    if (busy) return NcmaPlugin::Error(error,NCMA_BUSY);
    return NCMA_OK;
}
uint32_t Find(uint64_t context,uint64_t handle,World*& output,NcmaErrorV1* error,bool allowFault=false) {
    auto result=Validate(context,error); if(result) return result;
    auto it=worlds.find(handle); if(it==worlds.end()) return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);
    output=it->second.get();
    if(output->stats.state==2 && !allowFault) return NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,"WorldFaulted: destroy and recreate explicitly.");
    return NCMA_OK;
}
uint32_t Reject(World& w,NcmaErrorV1* error,uint32_t code,std::string_view message={}) {
    ++w.stats.errors; return NcmaPlugin::Error(error,code,message);
}
bool Finite(float value,float bound=100000.0F) { return std::isfinite(value) && std::abs(value)<=bound; }
template<size_t N> bool Values(const float (&values)[N],bool extent=false) {
    for(float value:values) if(!Finite(value,extent ? 1000.0F : 100000.0F) || (extent && value<.001F)) return false;
    return true;
}
bool Buffer(uint32_t count,const void* input) { return count<=NCMA_MAX_PHYSICS_BATCH && (!count || input); }
uint32_t Targets(World& w,const uint64_t* targets,uint32_t count,NcmaErrorV1* error) {
    if(!Buffer(count,targets)) return Reject(w,error,NCMA_INVALID_ARGUMENT);
    std::array<uint64_t,NCMA_MAX_PHYSICS_BATCH> sorted{};
    for(uint32_t i=0;i<count;++i) {
        if(!w.bodies.contains(targets[i])) return Reject(w,error,NCMA_INVALID_HANDLE);
        sorted[i]=targets[i];
    }
    std::sort(sorted.begin(),sorted.begin()+count);
    if(std::adjacent_find(sorted.begin(),sorted.begin()+count)!=sorted.begin()+count) return Reject(w,error,NCMA_INVALID_ARGUMENT,"Duplicate body target.");
    return NCMA_OK;
}
struct Execution {
    World& world; int exceptions=std::uncaught_exceptions();
    explicit Execution(World& w):world(w) {busy=true;}
    ~Execution() {busy=false; if(std::uncaught_exceptions()>exceptions) {world.stats.state=2; ++world.stats.errors;}}
};
uint32_t NCMA_CALL Initialize(const uint8_t* input,uint32_t length,uint64_t* output,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t{
        if(std::this_thread::get_id()!=loadingThread) return NcmaPlugin::Error(error,NCMA_WRONG_THREAD);
        if(!output || input || length) return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        *output=0; if(module || busy) return NcmaPlugin::Error(error,NCMA_BUSY);
        module=0x504D000000000000ull | nextModule++; *output=module; return NCMA_OK;
    });
}
uint32_t NCMA_CALL Shutdown(uint64_t context,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t{
        auto result=Validate(context,error); if(result)return result;
        if(!worlds.empty()) return NcmaPlugin::Error(error,NCMA_BUSY);
        module=0; return NCMA_OK;
    });
}
uint32_t NCMA_CALL Status(uint64_t context,NcmaModuleStatusV1* output,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t{
        auto result=Validate(context,error); if(result)return result;
        if(!output)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        *output={sizeof(*output),1,static_cast<uint64_t>(worlds.size()),0,0};
        for(const auto& item:worlds) {output->live_resources+=item.second->bodies.size()+item.second->characters.size(); output->sequence+=item.second->stats.sequence;}
        return NCMA_OK;
    });
}
uint32_t NCMA_CALL Diagnostic(uint64_t context,uint8_t* output,uint32_t capacity,uint32_t* required,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t{
        auto result=Validate(context,error); if(result)return result;
        if(!required || (capacity&&!output))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        *required=0;return NCMA_OK;
    });
}
uint32_t NCMA_CALL CreateWorld(uint64_t context,const NcmaPhysicsWorldDescriptionV1* desc,uint64_t* output,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t{
        auto result=Validate(context,error); if(result)return result;
        if(!output || !desc || desc->struct_size!=sizeof(*desc) || desc->reserved || (desc->dimension!=2 && desc->dimension!=3) ||
            !desc->max_bodies || desc->max_bodies>4096 || !desc->sub_steps || desc->sub_steps>16 || !Values(desc->gravity) ||
            (desc->dimension==2 && desc->gravity[2]!=0) || (desc->dimension==3 && desc->sub_steps!=1))
            return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        *output=0; if(worlds.size()==NCMA_MAX_PHYSICS_WORLDS) return NcmaPlugin::Error(error,NCMA_BUSY,"World budget exceeded.");
        auto w=std::make_unique<World>(); w->dimension=desc->dimension; w->maximum=desc->max_bodies; w->subSteps=desc->sub_steps;
        w->stats.struct_size=sizeof(w->stats); w->stats.state=1; w->stats.dimension=w->dimension;
        if(w->dimension==2)w->two=std::make_unique<Physics::PhysicsWorld2D>(Vector2(desc->gravity[0],desc->gravity[1]));
        else w->three=std::make_unique<Physics::PhysicsWorld3D>(Vector3(desc->gravity[0],desc->gravity[1],desc->gravity[2]));
        auto handle=Resource(); worlds.emplace(handle,std::move(w)); *output=handle; return NCMA_OK;
    });
}
template<class Box,uint32_t Dimension>
uint32_t Create(uint64_t context,uint64_t handle,const Box* input,uint32_t count,uint64_t* output,uint32_t capacity,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t{
        World* ptr=nullptr; auto result=Find(context,handle,ptr,error);if(result)return result;auto& w=*ptr;
        if(w.dimension!=Dimension || !Buffer(count,input) || capacity>4096 || (capacity&&!output))return Reject(w,error,NCMA_INVALID_ARGUMENT);
        if(capacity<count) {Reject(w,error,NCMA_BUFFER_TOO_SMALL); error->required_bytes=count*sizeof(uint64_t);return NCMA_BUFFER_TOO_SMALL;}
        if(count>w.maximum-w.bodies.size())return Reject(w,error,NCMA_BUSY,"Body budget exceeded.");
        if(!w.characters.empty())return Reject(w,error,NCMA_BUSY,"Character domain topology is frozen; close characters first.");
        for(uint32_t i=0;i<count;++i) if(!Values(input[i].position) || !Values(input[i].half_extents,true) ||
            !Finite(input[i].density,10000) || input[i].density<.001F || input[i].dynamic_body>1) return Reject(w,error,NCMA_INVALID_ARGUMENT);
        w.bodies.reserve(w.bodies.size()+count);
        std::array<uint64_t,4096> created{}; uint32_t completed=0;
        try {
            for(uint32_t i=0;i<count;++i) {
                const auto& box=input[i]; uint64_t kernel=0; auto id=Resource();
                // Insert bookkeeping first: allocation failure cannot leak a kernel body.
                auto it=w.bodies.emplace(id,Body{}).first;
                try {
                    if constexpr(Dimension==2) kernel=w.two->CreateBox(Vector2(box.position[0],box.position[1]),Vector2(box.half_extents[0],box.half_extents[1]),box.dynamic_body!=0,box.density);
                    else kernel=w.three->CreateBox(Vector3(box.position[0],box.position[1],box.position[2]),Vector3(box.half_extents[0],box.half_extents[1],box.half_extents[2]),box.dynamic_body!=0,box.density);
                } catch(...) {w.bodies.erase(it);throw;}
                it->second={kernel,box.correlation,box.dynamic_body!=0}; created[completed++]=id;
            }
        } catch(...) {
            for(uint32_t i=0;i<completed;++i) {
                auto kernel=w.bodies.at(created[i]).kernel;
                if constexpr(Dimension==2)w.two->DestroyBody(kernel);else w.three->DestroyBody(kernel);
                w.bodies.erase(created[i]);
            }
            ++w.stats.errors; throw;
        }
        if(count)std::memcpy(output,created.data(),count*sizeof(uint64_t));
        ++w.stats.batches;w.stats.copied_bytes+=count*(sizeof(Box)+sizeof(uint64_t));return NCMA_OK;
    });
}
uint32_t NCMA_CALL Create2(uint64_t c,uint64_t w,const NcmaPhysicsBox2DV1* b,uint32_t n,uint64_t* o,uint32_t cap,NcmaErrorV1* e) noexcept {return Create<NcmaPhysicsBox2DV1,2>(c,w,b,n,o,cap,e);}
uint32_t NCMA_CALL Create3(uint64_t c,uint64_t w,const NcmaPhysicsBox3DV1* b,uint32_t n,uint64_t* o,uint32_t cap,NcmaErrorV1* e) noexcept {return Create<NcmaPhysicsBox3DV1,3>(c,w,b,n,o,cap,e);}
uint32_t NCMA_CALL DestroyBodies(uint64_t context,uint64_t handle,const uint64_t* input,uint32_t count,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t{
        World* ptr=nullptr;auto result=Find(context,handle,ptr,error);if(result)return result;auto& w=*ptr;
        result=Targets(w,input,count,error);if(result)return result;
        if(!w.characters.empty())return Reject(w,error,NCMA_BUSY,"Close characters before changing collider topology.");
        Execution execution(w);
        for(uint32_t i=0;i<count;++i) {auto body=w.bodies.at(input[i]).kernel;
            if(w.dimension==2)w.two->DestroyBody(body);else w.three->DestroyBody(body);w.bodies.erase(input[i]);}
        ++w.stats.batches;w.stats.copied_bytes+=count*sizeof(uint64_t);return NCMA_OK;
    });
}
template<class Velocity,uint32_t Dimension>
uint32_t Set(uint64_t context,uint64_t handle,const Velocity* input,uint32_t count,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t{
        World* ptr=nullptr;auto result=Find(context,handle,ptr,error);if(result)return result;auto& w=*ptr;
        if(w.dimension!=Dimension || !Buffer(count,input))return Reject(w,error,NCMA_INVALID_ARGUMENT);
        std::array<uint64_t,4096> ids{};
        for(uint32_t i=0;i<count;++i) {
            if(!Values(input[i].velocity))return Reject(w,error,NCMA_INVALID_ARGUMENT);
            if constexpr(Dimension==3)if(input[i].reserved)return Reject(w,error,NCMA_INVALID_ARGUMENT);
            ids[i]=input[i].body;
        }
        result=Targets(w,ids.data(),count,error);if(result)return result;
        for(uint32_t i=0;i<count;++i)if(!w.bodies.at(ids[i]).dynamic)return Reject(w,error,NCMA_INVALID_ARGUMENT,"Static body velocity cannot be changed.");
        Execution execution(w);
        for(uint32_t i=0;i<count;++i) {
            auto body=w.bodies.at(ids[i]).kernel;const auto& v=input[i].velocity;
            if constexpr(Dimension==2)w.two->SetVelocity(body,Vector2(v[0],v[1]));
            else w.three->SetVelocity(body,Vector3(v[0],v[1],v[2]));
        }
        ++w.stats.batches;w.stats.copied_bytes+=count*sizeof(Velocity);return NCMA_OK;
    });
}
uint32_t NCMA_CALL Set2(uint64_t c,uint64_t w,const NcmaPhysicsVelocity2DV1* b,uint32_t n,NcmaErrorV1* e) noexcept {return Set<NcmaPhysicsVelocity2DV1,2>(c,w,b,n,e);}
uint32_t NCMA_CALL Set3(uint64_t c,uint64_t w,const NcmaPhysicsVelocity3DV1* b,uint32_t n,NcmaErrorV1* e) noexcept {return Set<NcmaPhysicsVelocity3DV1,3>(c,w,b,n,e);}
uint32_t NCMA_CALL Step(uint64_t context,uint64_t handle,float dt,uint64_t* output,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t{
        World* ptr=nullptr;auto result=Find(context,handle,ptr,error);if(result)return result;auto& w=*ptr;
        if(!output || !Finite(dt,.25F) || dt<=0 || w.stats.sequence==std::numeric_limits<uint64_t>::max())return Reject(w,error,NCMA_INVALID_ARGUMENT);
        if(!w.characters.empty())return Reject(w,error,NCMA_BUSY,"Use the complete character quantum, not ordinary Step.");
        Execution execution(w);const auto started=Clock::now();
        if(w.dimension==2)w.two->Step(dt,static_cast<int>(w.subSteps));else w.three->Step(dt);
        const double ms=std::chrono::duration<double,std::milli>(Clock::now()-started).count();
        w.samples[w.sampleCount++%w.samples.size()]=ms;
        w.lastStepMs=ms;
        w.stats.step_max_ms=std::max(w.stats.step_max_ms,ms);*output=++w.stats.sequence;
        ++w.stats.batches;w.stats.copied_bytes+=sizeof(dt)+sizeof(*output);return NCMA_OK;
    });
}
template<class State,uint32_t Dimension>
uint32_t Read(uint64_t context,uint64_t handle,uint64_t sequence,const uint64_t* input,uint32_t count,State* output,uint32_t capacity,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t{
        World* ptr=nullptr;auto result=Find(context,handle,ptr,error);if(result)return result;auto& w=*ptr;
        if(w.dimension!=Dimension || capacity>4096 || (capacity&&!output))return Reject(w,error,NCMA_INVALID_ARGUMENT);
        result=Targets(w,input,count,error);if(result)return result;
        if(sequence!=w.stats.sequence)return Reject(w,error,NCMA_INVALID_ARGUMENT,"Stale physics sequence.");
        if(capacity<count) {Reject(w,error,NCMA_BUFFER_TOO_SMALL);error->required_bytes=count*sizeof(State);return NCMA_BUFFER_TOO_SMALL;}
        std::array<State,4096> states{};
        for(uint32_t i=0;i<count;++i) {
            const auto& body=w.bodies.at(input[i]);auto& state=states[i];state.body=input[i];state.correlation=body.correlation;state.sequence=sequence;
            if constexpr(Dimension==2) {
                auto p=w.two->GetPosition(body.kernel),v=w.two->GetVelocity(body.kernel);
                state.position[0]=p.x();state.position[1]=p.y();state.velocity[0]=v.x();state.velocity[1]=v.y();state.angle=w.two->GetRotation(body.kernel);
            }else {
                auto p=w.three->GetPosition(body.kernel),v=w.three->GetVelocity(body.kernel);auto q=w.three->GetRotation(body.kernel);
                state.position[0]=p.x();state.position[1]=p.y();state.position[2]=p.z();
                state.velocity[0]=v.x();state.velocity[1]=v.y();state.velocity[2]=v.z();
                state.rotation[0]=q.x();state.rotation[1]=q.y();state.rotation[2]=q.z();state.rotation[3]=q.w();
            }
        }
        if(count)std::memcpy(output,states.data(),count*sizeof(State));
        ++w.stats.batches;w.stats.copied_bytes+=count*(sizeof(uint64_t)+sizeof(State));return NCMA_OK;
    });
}
uint32_t NCMA_CALL Read2(uint64_t c,uint64_t w,uint64_t seq,const uint64_t* b,uint32_t n,NcmaPhysicsBodyState2DV1* o,uint32_t cap,NcmaErrorV1* e) noexcept {return Read<NcmaPhysicsBodyState2DV1,2>(c,w,seq,b,n,o,cap,e);}
uint32_t NCMA_CALL Read3(uint64_t c,uint64_t w,uint64_t seq,const uint64_t* b,uint32_t n,NcmaPhysicsBodyState3DV1* o,uint32_t cap,NcmaErrorV1* e) noexcept {return Read<NcmaPhysicsBodyState3DV1,3>(c,w,seq,b,n,o,cap,e);}
uint32_t NCMA_CALL Stats(uint64_t context,uint64_t handle,NcmaPhysicsStatsV1* output,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t{
        World* ptr=nullptr;auto result=Find(context,handle,ptr,error,true);if(result)return result;
        if(!output)return Reject(*ptr,error,NCMA_INVALID_ARGUMENT);
        *output=ptr->stats;output->live_bodies=ptr->bodies.size();output->live_worlds=static_cast<uint32_t>(worlds.size());
        // Compatibility for ABI 1.0 clients, evaluated only on their explicit stats query.
        if(ptr->sampleCount) {
            auto sorted=ptr->samples;auto n=static_cast<size_t>(std::min(ptr->sampleCount,static_cast<uint64_t>(sorted.size())));
            std::sort(sorted.begin(),sorted.begin()+n);
            output->step_median_ms=sorted[(n-1)/2];output->step_p95_ms=sorted[(95*n+99)/100-1];
        }
        return NCMA_OK;
    });
}
uint32_t NCMA_CALL DestroyWorld(uint64_t context,uint64_t handle,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t{
        World* ptr=nullptr;auto result=Find(context,handle,ptr,error,true);if(result)return result;
        // Updates are synchronous and Jolt barriers complete before returning Step.
        if(!ptr->characters.empty())return Reject(*ptr,error,NCMA_BUSY,"Destroy characters before the world.");
        worlds.erase(handle);return NCMA_OK;
    });
}
uint32_t NCMA_CALL ReadCounters(uint64_t context,uint64_t handle,NcmaPhysicsCountersV1* output,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t{
        World* ptr=nullptr;auto result=Find(context,handle,ptr,error,true);if(result)return result;
        if(!output)return Reject(*ptr,error,NCMA_INVALID_ARGUMENT);
        const auto& s=ptr->stats;
        *output={sizeof(*output),s.state,ptr->dimension,static_cast<uint32_t>(worlds.size()),
            static_cast<uint64_t>(ptr->bodies.size()),s.sequence,s.batches,s.copied_bytes,s.errors,0,ptr->lastStepMs};
        return NCMA_OK;
    });
}
const NcmaPhysicsApiV1 api{{sizeof(NcmaPhysicsApiV1),1,0,NCMA_PHYSICS,NCMA_PHYSICS_CAPABILITIES,Initialize,Shutdown,Status,Diagnostic},
    CreateWorld,Create2,Create3,DestroyBodies,Set2,Set3,Step,Read2,Read3,Stats,DestroyWorld};
const NcmaPhysicsApiV1_1 api11=[] { NcmaPhysicsApiV1_1 table{api,ReadCounters};table.base.module.struct_size=sizeof(table);table.base.module.minor=1;return table; }();

bool Category(uint32_t value) {return value && !(value&(value-1));}
bool UnitRotation(const float (&q)[4],bool yaw=false) {
    if(!Values(q))return false;
    const auto length=q[0]*q[0]+q[1]*q[1]+q[2]*q[2]+q[3]*q[3];
    return std::abs(length-1)<1e-5F && (!yaw || (std::abs(q[0])<1e-6F && std::abs(q[2])<1e-6F));
}
uint64_t PublicResource(const World& w,uint64_t kernel,uint32_t kind) {
    if(!kernel)return 0;
    if(kind==1){for(const auto& item:w.bodies)if(item.second.kernel==kernel)return item.first;}
    else if(kind==2){for(const auto& item:w.characters)if(item.second==kernel)return item.first;}
    throw std::runtime_error("Unmapped numerical resource.");
}
uint64_t KernelResource(const World& w,uint64_t id) {
    if(!id)return 0;
    if(auto it=w.bodies.find(id);it!=w.bodies.end())return it->second.kernel;
    if(auto it=w.characters.find(id);it!=w.characters.end())return it->second;
    throw std::out_of_range("Unknown query ignore resource.");
}
NcmaCharacterStateV1 CharacterState(World& w,uint64_t id,uint64_t sequence) {
    auto state=w.three->ReadCharacter(w.characters.at(id));state.character=id;state.sequence=sequence;
    state.ground_body=PublicResource(w,state.ground_body,state.ground_kind);
    if(!Values(state.position)||!Values(state.velocity)||!UnitRotation(state.rotation)||!Values(state.ground_normal)||!Values(state.ground_velocity)||state.ground_state>3)
        throw std::runtime_error("Invalid Jolt character output.");
    return state;
}
uint32_t CharacterTargets(World& w,const uint64_t* ids,uint32_t count,NcmaErrorV1* error) {
    if(count>NCMA_MAX_CHARACTERS || (count&&!ids))return Reject(w,error,NCMA_INVALID_ARGUMENT);
    std::array<uint64_t,NCMA_MAX_CHARACTERS> sorted{};
    for(uint32_t i=0;i<count;++i){if(!w.characters.contains(ids[i]))return Reject(w,error,NCMA_INVALID_HANDLE);sorted[i]=ids[i];}
    std::sort(sorted.begin(),sorted.begin()+count);
    if(std::adjacent_find(sorted.begin(),sorted.begin()+count)!=sorted.begin()+count)return Reject(w,error,NCMA_INVALID_ARGUMENT);
    return NCMA_OK;
}
uint32_t NCMA_CALL CollisionBoxes(uint64_t context,uint64_t handle,const NcmaCollisionBoxV1* input,uint32_t count,uint64_t* output,uint32_t capacity,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t{
        World* ptr=nullptr;auto result=Find(context,handle,ptr,error);if(result)return result;auto& w=*ptr;
        if(w.dimension!=3 || !Buffer(count,input) || capacity>4096 || (capacity&&!output))return Reject(w,error,NCMA_INVALID_ARGUMENT);
        if(capacity<count)return Reject(w,error,NCMA_BUFFER_TOO_SMALL);
        if(!w.characters.empty() || count>w.maximum-w.bodies.size())return Reject(w,error,NCMA_BUSY);
        for(uint32_t i=0;i<count;++i){const auto& d=input[i];if(d.struct_size!=sizeof(d)||!Category(d.category)||d.dynamic_body>1||d.reserved||d.reserved_float!=0||
            !Values(d.position)||!Values(d.half_extents,true)||!Finite(d.density,10000)||d.density<.001F||!UnitRotation(d.rotation))return Reject(w,error,NCMA_INVALID_ARGUMENT);}
        w.bodies.reserve(w.bodies.size()+count);std::array<uint64_t,4096> created{};uint32_t n=0;
        try {for(uint32_t i=0;i<count;++i){const auto id=Resource();auto it=w.bodies.emplace(id,Body{}).first;
            try {it->second={w.three->CreateCollisionBox(input[i]),input[i].correlation,input[i].dynamic_body!=0};created[n++]=id;}
            catch(...){w.bodies.erase(it);throw;}}}
        catch(...){for(uint32_t i=0;i<n;++i){w.three->DestroyBody(w.bodies.at(created[i]).kernel);w.bodies.erase(created[i]);}++w.stats.errors;throw;}
        if(count)std::memcpy(output,created.data(),count*sizeof(uint64_t));++w.stats.batches;w.stats.copied_bytes+=count*(sizeof(*input)+8);return NCMA_OK;
    });
}
uint32_t NCMA_CALL CreateCapsules(uint64_t context,uint64_t handle,const NcmaCapsuleDescriptionV1* input,uint32_t count,uint64_t* output,uint32_t capacity,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t{
        World* ptr=nullptr;auto result=Find(context,handle,ptr,error);if(result)return result;auto& w=*ptr;
        if(w.dimension!=3 || count>NCMA_MAX_CHARACTERS || (count&&!input)||capacity>NCMA_MAX_CHARACTERS||(capacity&&!output))return Reject(w,error,NCMA_INVALID_ARGUMENT);
        if(capacity<count)return Reject(w,error,NCMA_BUFFER_TOO_SMALL);
        if(w.stats.sequence || count>NCMA_MAX_CHARACTERS-w.characters.size())return Reject(w,error,NCMA_BUSY);
        for(uint32_t i=0;i<count;++i){const auto& d=input[i];if(d.struct_size!=sizeof(d)||!Category(d.category)||d.reserved||d.reserved_float!=0||!Values(d.position)||
            !Finite(d.radius,10)||d.radius<.01F||!Finite(d.half_height,10)||d.half_height<.001F||!Finite(d.max_slope_radians,1.55F)||d.max_slope_radians<=0||
            !Finite(d.step_height,1)||d.step_height<0||!Finite(d.floor_distance,1)||d.floor_distance<0||!Finite(d.mass,1000)||d.mass<=0||
            !Finite(d.max_strength,100000)||d.max_strength<0||!Finite(d.padding,1)||d.padding<0||d.padding>d.radius*.25F)return Reject(w,error,NCMA_INVALID_ARGUMENT);}
        w.characters.reserve(w.characters.size()+count);std::array<uint64_t,NCMA_MAX_CHARACTERS> created{};uint32_t n=0;
        try {for(uint32_t i=0;i<count;++i){const auto id=Resource();auto it=w.characters.emplace(id,0).first;
            try {it->second=w.three->CreateCapsule(input[i]);created[n++]=id;}catch(...){w.characters.erase(it);throw;}}}
        catch(...){for(uint32_t i=0;i<n;++i){w.three->DestroyCharacter(w.characters.at(created[i]));w.characters.erase(created[i]);}++w.stats.errors;throw;}
        if(count)std::memcpy(output,created.data(),count*sizeof(uint64_t));++w.stats.batches;w.stats.copied_bytes+=count*(sizeof(*input)+8);return NCMA_OK;
    });
}
uint32_t NCMA_CALL DestroyCharacters(uint64_t context,uint64_t handle,const uint64_t* input,uint32_t count,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t{
        World* ptr=nullptr;auto result=Find(context,handle,ptr,error,true);if(result)return result;auto& w=*ptr;
        result=CharacterTargets(w,input,count,error);if(result)return result;
        // Freeze live topology; no dangling contact IDs from partial character deletion.
        if(count && count!=w.characters.size())return Reject(w,error,NCMA_UNSUPPORTED_FEATURE,"Destroy the complete character set at a safe shutdown boundary.");
        Execution execution(w);
        for(uint32_t i=0;i<count;++i){w.three->DestroyCharacter(w.characters.at(input[i]));w.characters.erase(input[i]);}
        ++w.stats.batches;return NCMA_OK;
    });
}
uint32_t NCMA_CALL ReadCharacters(uint64_t context,uint64_t handle,uint64_t sequence,const uint64_t* ids,uint32_t count,NcmaCharacterStateV1* output,uint32_t capacity,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t{
        World* ptr=nullptr;auto result=Find(context,handle,ptr,error);if(result)return result;auto& w=*ptr;
        if(w.dimension!=3 || sequence!=w.stats.sequence || capacity>NCMA_MAX_CHARACTERS || (capacity&&!output))return Reject(w,error,NCMA_INVALID_ARGUMENT);
        result=CharacterTargets(w,ids,count,error);if(result)return result;if(capacity<count)return Reject(w,error,NCMA_BUFFER_TOO_SMALL);
        std::array<NcmaCharacterStateV1,NCMA_MAX_CHARACTERS> states{};
        for(uint32_t i=0;i<count;++i)states[i]=CharacterState(w,ids[i],sequence);
        if(count)std::memcpy(output,states.data(),count*sizeof(*output));w.stats.copied_bytes+=count*sizeof(*output);return NCMA_OK;
    });
}
uint32_t NCMA_CALL StepCharacters(uint64_t context,uint64_t handle,uint64_t expected,float dt,const NcmaCharacterVelocityV1* inputs,uint32_t count,
    NcmaCharacterStateV1* output,uint32_t capacity,NcmaCharacterContactV1* contacts,uint32_t contactCapacity,NcmaCharacterStepReceiptV1* receipt,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t{
        World* ptr=nullptr;auto result=Find(context,handle,ptr,error);if(result)return result;auto& w=*ptr;
        if(w.dimension!=3 || !count || count>NCMA_MAX_CHARACTERS || !inputs || !output || !contacts || !receipt || expected!=w.stats.sequence ||
            expected==std::numeric_limits<uint64_t>::max() || !Finite(dt,.25F)||dt<=0||capacity>NCMA_MAX_CHARACTERS||contactCapacity>NCMA_MAX_CHARACTERS*NCMA_MAX_CHARACTER_CONTACTS)
            return Reject(w,error,NCMA_INVALID_ARGUMENT);
        if(capacity<count || contactCapacity<count*NCMA_MAX_CHARACTER_CONTACTS)return Reject(w,error,NCMA_BUFFER_TOO_SMALL);
        if(count!=w.characters.size())return Reject(w,error,NCMA_INVALID_ARGUMENT,"Exactly the complete character set is required.");
        std::array<NcmaCharacterVelocityV1,NCMA_MAX_CHARACTERS> kernel{};
        for(uint32_t i=0;i<count;++i){const auto& v=inputs[i];if(!w.characters.contains(v.character))return Reject(w,error,NCMA_INVALID_HANDLE);
            if(v.reserved || !Values(v.velocity) || !UnitRotation(v.rotation,true) || (i && inputs[i-1].character>=v.character))return Reject(w,error,NCMA_INVALID_ARGUMENT);
            kernel[i]=v;kernel[i].character=w.characters.at(v.character);}
        std::array<NcmaCharacterStateV1,NCMA_MAX_CHARACTERS> states{};
        std::array<NcmaCharacterContactV1,NCMA_MAX_CHARACTERS*NCMA_MAX_CHARACTER_CONTACTS> copied{};
        Execution execution(w);const auto started=Clock::now();w.three->StepCharacters(dt,kernel.data(),count);
        uint32_t n=0;
        for(uint32_t i=0;i<count;++i){states[i]=CharacterState(w,inputs[i].character,expected+1);
            const auto size=w.three->ReadContacts(kernel[i].character,copied.data()+n,NCMA_MAX_CHARACTER_CONTACTS);
            for(uint32_t j=n;j<n+size;++j){auto& c=copied[j];c.character=inputs[i].character;c.other=PublicResource(w,c.other,c.other_kind);c.sequence=expected+1;
                if(!Values(c.position)||!Values(c.normal)||!Finite(c.separation))throw std::runtime_error("Invalid character contact output.");}
            n+=size;}
        w.lastStepMs=std::chrono::duration<double,std::milli>(Clock::now()-started).count();
        w.samples[w.sampleCount++%w.samples.size()]=w.lastStepMs;w.stats.step_max_ms=std::max(w.stats.step_max_ms,w.lastStepMs);
        ++w.stats.sequence;++w.stats.batches;w.stats.copied_bytes+=count*(sizeof(*inputs)+sizeof(*output))+n*sizeof(*contacts)+sizeof(*receipt);
        std::memcpy(output,states.data(),count*sizeof(*output));if(n)std::memcpy(contacts,copied.data(),n*sizeof(*contacts));*receipt={w.stats.sequence,count,n};return NCMA_OK;
    });
}
uint32_t NCMA_CALL Ray(uint64_t context,uint64_t handle,uint64_t sequence,const NcmaPhysicsRayV1* input,NcmaPhysicsQueryHitV1* output,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t{
        World* ptr=nullptr;auto result=Find(context,handle,ptr,error);if(result)return result;auto& w=*ptr;
        if(w.dimension!=3||sequence!=w.stats.sequence||!input||!output||input->struct_size!=sizeof(*input)||!Values(input->origin)||!Values(input->displacement)||
            (input->displacement[0]==0 && input->displacement[1]==0 && input->displacement[2]==0))return Reject(w,error,NCMA_INVALID_ARGUMENT);
        if(input->ignore && !w.bodies.contains(input->ignore)&&!w.characters.contains(input->ignore))return Reject(w,error,NCMA_INVALID_HANDLE);
        auto d=*input;d.ignore=KernelResource(w,d.ignore);auto hit=w.three->Ray(d);hit.sequence=sequence;
        hit.resource=PublicResource(w,hit.resource,hit.kind);
        if(!Finite(hit.fraction,1)||hit.fraction<0||!Values(hit.position)||!Values(hit.normal))throw std::runtime_error("Invalid ray output.");
        *output=hit;return NCMA_OK;
    });
}
uint32_t NCMA_CALL Sweep(uint64_t context,uint64_t handle,uint64_t sequence,const NcmaPhysicsCapsuleSweepV1* input,NcmaPhysicsQueryHitV1* output,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t{
        World* ptr=nullptr;auto result=Find(context,handle,ptr,error);if(result)return result;auto& w=*ptr;
        if(w.dimension!=3||sequence!=w.stats.sequence||!input||!output||input->struct_size!=sizeof(*input)||!Values(input->foot)||!Values(input->displacement)||
            !Finite(input->radius,10)||input->radius<.01F||!Finite(input->half_height,10)||input->half_height<.001F||!UnitRotation(input->rotation)||
            (input->displacement[0]==0 && input->displacement[1]==0 && input->displacement[2]==0))return Reject(w,error,NCMA_INVALID_ARGUMENT);
        if(input->ignore && !w.bodies.contains(input->ignore)&&!w.characters.contains(input->ignore))return Reject(w,error,NCMA_INVALID_HANDLE);
        auto d=*input;d.ignore=KernelResource(w,d.ignore);auto hit=w.three->Sweep(d);hit.sequence=sequence;
        hit.resource=PublicResource(w,hit.resource,hit.kind);
        if(!Finite(hit.fraction,1)||hit.fraction<0||!Values(hit.position)||!Values(hit.normal))throw std::runtime_error("Invalid sweep output.");
        *output=hit;return NCMA_OK;
    });
}
const NcmaCharacterApiV1 characterApi{sizeof(NcmaCharacterApiV1),1,0,NCMA_MAX_CHARACTERS,31,
    CollisionBoxes,CreateCapsules,DestroyCharacters,StepCharacters,ReadCharacters,Ray,Sweep};
uint32_t NCMA_CALL QueryCharacters(uint64_t context,uint32_t major,uint32_t minor,void* output,uint32_t capacity,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t{auto result=Validate(context,error);if(result)return result;return NcmaPlugin::CopyApi(major,minor,output,capacity,error,characterApi);});
}
const NcmaPhysicsApiV1_2 api12=[] {NcmaPhysicsApiV1_2 table{api11,QueryCharacters};table.base.base.module.struct_size=sizeof(table);
    table.base.base.module.minor=2;table.base.base.module.capabilities=NCMA_PHYSICS_CAPABILITIES|64ull|128ull;return table;}();
}
extern "C" NCMA_EXPORT uint32_t NCMA_CALL ncma_plugin_get_api(uint32_t major,uint32_t minor,void* output,uint32_t capacity,NcmaErrorV1* error) noexcept {
    if(minor==0)return NcmaPlugin::CopyApi(major,minor,output,capacity,error,api);
    if(minor==1)return NcmaPlugin::CopyApi(major,minor,output,capacity,error,api11,1);
    return NcmaPlugin::CopyApi(major,minor,output,capacity,error,api12,2);
}
