#include "interop/NcmaAnimationApi.h"
#include "core/log/NativeDiagnostics.h"
#include "animation/ActionDemoLibrary.h"
#include <algorithm>
#include <cmath>
#include <cstring>
#include <iomanip>
#include <limits>
#include <locale>
#include <mutex>
#include <sstream>
#include <unordered_map>

namespace {
using namespace NcmaEngine;
using namespace NcmaEngine::Animation;
struct Resource { std::shared_ptr<const AnimationLibrary> Library; std::string Metadata; };
std::mutex Gate;
std::unordered_map<std::uint64_t, Resource> Resources;
std::uint64_t Next = 1;
thread_local char Error[4096]{};
void SetError(const char* message) noexcept {
    std::size_t n = std::min(std::strlen(message),sizeof(Error)-1);
    if (n < std::strlen(message)) while (n && (static_cast<unsigned char>(message[n]) & 0xc0) == 0x80) --n;
    std::memcpy(Error,message,n); Error[n]=0;
}
void Require(bool value,const char* message) { if (!value) throw std::invalid_argument(message); }
template<class F> std::uint8_t Guard(F&& action) noexcept {
    try { const auto result=action(); Error[0]=0; return result; }
    catch (const std::exception& e) { SetError(e.what()); } catch (...) { SetError("Animation kernel failure"); }
    return 0;
}
const Resource& Get(std::uint64_t handle) {
    const auto it=Resources.find(handle); Require(it!=Resources.end(),"Unknown/released animation resource"); return it->second;
}
std::uint8_t Copy(const std::string& text,char* output,std::uint32_t capacity,std::uint32_t* required) {
    Require(required && text.size()<1048576,"Invalid animation text buffer");
    *required=static_cast<std::uint32_t>(text.size()+1);
    if (capacity<*required) return 2;
    Require(output,"Null animation destination"); std::memcpy(output,text.c_str(),*required); return 1;
}
void Write(float* output,const Transform& t) {
    for (int i=0;i<3;i++) output[i]=t.Position[i];
    for (int i=0;i<4;i++) output[3+i]=t.Rotation.coeffs()[i];
    for (int i=0;i<3;i++) output[7+i]=t.Scale[i];
}
Transform Read(const float* input) {
    Transform t; t.Position={input[0],input[1],input[2]};
    t.Rotation=Quaternion(input[6],input[3],input[4],input[5]); t.Scale={input[7],input[8],input[9]};
    Require(t.Position.allFinite() && t.Scale.allFinite() && (t.Scale.array()>0.000001F).all() &&
        t.Rotation.coeffs().allFinite() && std::abs(t.Rotation.norm()-1)<.001F,"Invalid source pose");
    return t;
}
void Init(std::ostream& s) { s.imbue(std::locale::classic()); s<<std::setprecision(9)<<std::boolalpha; }
void JsonTransform(std::ostream& s,const Transform& t) {
    s<<"{\"position\":["<<t.Position.x()<<','<<t.Position.y()<<','<<t.Position.z()
     <<"],\"rotation_xyzw\":["<<t.Rotation.x()<<','<<t.Rotation.y()<<','<<t.Rotation.z()<<','<<t.Rotation.w()
     <<"],\"scale\":["<<t.Scale.x()<<','<<t.Scale.y()<<','<<t.Scale.z()<<"]}";
}
std::string Metadata(const AnimationLibrary& lib) {
    std::ostringstream s; Init(s); s<<"{\"schema_version\":2,\"skeleton_uuid\":\""<<lib.GetSkeleton().Id.ToString()<<"\",\"bones\":[";
    const auto& bones=lib.GetSkeleton().Bones;
    for (std::size_t i=0;i<bones.size();i++) {
        if (i) s<<','; s<<"{\"name\":\""<<bones[i].Name<<"\",\"parent\":"<<bones[i].Parent<<",\"local\":";
        JsonTransform(s,bones[i].BindLocal); s<<'}';
    }
    s<<"],\"clips\":[";
    const auto& clips=lib.GetClips();
    for (std::size_t i=0;i<clips.size();i++) {
        if (i) s<<','; const auto& c=clips[i];
        s<<"{\"uuid\":\""<<c.Id.ToString()<<"\",\"name\":\""<<c.Name<<"\",\"duration\":"<<c.Duration
         <<",\"loop\":"<<c.Loop<<",\"root_motion\":"<<c.ExtractRootMotion<<'}';
    }
    s<<"]}"; return s.str();
}
}
extern "C" {
std::uint32_t NCMA_NATIVE_CALL ncma_animation_abi_version() { return 2; }
std::uint64_t NCMA_NATIVE_CALL ncma_animation_create(std::uint32_t version) {
    try {
        Require(version==2,"Animation ABI 2 required; policy ABI 1 rejected");
        std::scoped_lock lock(Gate); Require(Resources.size()<64,"Animation resource limit");
        Require(Next!=std::numeric_limits<std::uint64_t>::max(),"Animation generations exhausted");
        auto library=CreateActionDemoLibrary(); auto metadata=Metadata(*library);
        const auto handle=Next; Resources.emplace(handle,Resource{std::move(library),std::move(metadata)}); ++Next; NcmaEngine::NativeDiagnostic("animation.library","Immutable numerical demo library created."); Error[0]=0; return handle;
    } catch (const std::exception& e) { SetError(e.what()); } catch (...) { SetError("Animation create failed"); }
    return 0;
}
void NCMA_NATIVE_CALL ncma_animation_destroy(std::uint64_t handle) {
    Guard([&]() -> std::uint8_t { std::scoped_lock lock(Gate); Require(Resources.erase(handle)==1,"Unknown/released animation resource"); return 1; });
}
std::uint8_t NCMA_NATIVE_CALL ncma_animation_read_library(std::uint64_t handle,char* output,std::uint32_t capacity,std::uint32_t* required) {
    return Guard([&] { std::scoped_lock lock(Gate); return Copy(Get(handle).Metadata,output,capacity,required); });
}
std::uint8_t NCMA_NATIVE_CALL ncma_animation_read_error(char* output,std::uint32_t capacity,std::uint32_t* required) {
    // Reading must not clear or allocate in the exception path.
    if (!required) return 0; *required=static_cast<std::uint32_t>(std::strlen(Error)+1);
    if (capacity<*required) return 2; if (!output) return 0; std::memcpy(output,Error,*required); return 1;
}
std::uint8_t NCMA_NATIVE_CALL ncma_animation_sample(std::uint64_t handle,std::uint32_t clip,double time,
    const float* source,std::uint32_t sourceCount,float weight,float* output,std::uint32_t capacity,std::uint32_t* required) {
    return Guard([&]() -> std::uint8_t {
        std::scoped_lock lock(Gate); const auto& lib=*Get(handle).Library;
        const auto count=lib.GetSkeleton().Bones.size();
        Require(clip<lib.GetClips().size() && std::isfinite(time) && time>=0 && time<=1e9 &&
            std::isfinite(weight) && weight>=0 && weight<=1 && required,"Invalid animation sample");
        Require(sourceCount ? source && sourceCount==count*10 : source==nullptr && weight==1,"Invalid animation blend source");
        std::vector<Transform> from(count), pose(count), target(count);
        if (sourceCount) for (std::size_t i=0;i<count;i++) from[i]=Read(source+i*10);
        *required=static_cast<std::uint32_t>(count*26);
        if (capacity<*required) return 2;
        Require(output,"Null animation sample destination");
        lib.Sample(clip,time,target);
        if (sourceCount) BlendPoses(from,target,weight,pose); else pose=std::move(target);
        std::vector<Matrix4> model(count),skin(count); lib.BuildMatrices(pose,model,skin);
        std::vector<float> result(*required);
        for (std::size_t i=0;i<count;i++) {
            Require(model[i].allFinite(),"Animation matrix overflow");
            Write(result.data()+i*10,pose[i]);
            std::copy_n(model[i].data(),16,result.data()+count*10+i*16);
        }
        std::copy(result.begin(),result.end(),output); return 1;
    });
}
std::uint8_t NCMA_NATIVE_CALL ncma_animation_motion(std::uint64_t handle,std::uint32_t clip,double from,double to,
    float* output,std::uint32_t capacity,std::uint32_t* required) {
    return Guard([&]() -> std::uint8_t {
        std::scoped_lock lock(Gate); const auto& lib=*Get(handle).Library;
        Require(required,"Null motion count"); const auto motion=lib.ExtractMotion(clip,from,to); *required=10;
        if (capacity<10) return 2; Require(output,"Null motion destination"); Write(output,motion); return 1;
    });
}
std::uint8_t NCMA_NATIVE_CALL ncma_animation_notifies(std::uint64_t handle,std::uint32_t clip,double from,double to,
    char* output,std::uint32_t capacity,std::uint32_t* required) {
    return Guard([&]() -> std::uint8_t {
        std::scoped_lock lock(Gate); const auto& lib=*Get(handle).Library; std::vector<FiredNotify> events;
        lib.CollectNotifies(clip,from,to,events); std::ostringstream s; Init(s); s<<'[';
        for (std::size_t i=0;i<events.size();i++) {
            if (i) s<<','; const auto& e=events[i];
            s<<"{\"clip_uuid\":\""<<e.ClipId.ToString()<<"\",\"name\":\""<<e.Name<<"\",\"offset\":"<<e.Offset<<'}';
        }
        s<<']'; return Copy(s.str(),output,capacity,required);
    });
}
std::uint8_t NCMA_NATIVE_CALL ncma_animation_command(std::uint64_t,std::uint32_t,double,const char*) {
    SetError("Animation policy ABI removed; use consumer-owned state and numerical ABI 2"); return 0;
}
const char* NCMA_NATIVE_CALL ncma_animation_inspect(std::uint64_t) {
    SetError("Animation policy inspection removed; use copied library metadata"); return nullptr;
}
const char* NCMA_NATIVE_CALL ncma_animation_last_error() { return Error; }
}
