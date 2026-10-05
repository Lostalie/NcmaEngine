#include "contracts/NcmaPose.h"
#include "PluginSupport.h"
#include <Eigen/Geometry>
#include <array>
#include <cmath>
#include <memory>
#include <mutex>
#include <unordered_map>
#include <vector>

namespace {
struct Rejection final : std::exception { uint32_t code; explicit Rejection(uint32_t c):code(c){} };
void Require(bool value,uint32_t code=NCMA_INVALID_ARGUMENT) { if(!value)throw Rejection(code); }
bool Overlap(const void* a,uint64_t as,const void* b,uint64_t bs) {
 auto av=reinterpret_cast<uintptr_t>(a),bv=reinterpret_cast<uintptr_t>(b);
 Require(as<=UINTPTR_MAX-av&&bs<=UINTPTR_MAX-bv);return av<bv+bs&&bv<av+as;
}
template<class F> uint32_t Run(NcmaErrorV1* error,F&& action) noexcept {
 if(!error)return NCMA_INVALID_ARGUMENT; *error={};
 try{return action();}catch(const Rejection& e){return NcmaPlugin::Error(error,e.code,"Pose resource/thread/budget/TRS/batch rejected.");}
 catch(...){return NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,"Pose numerical operation failed.");}
}
constexpr uint64_t Budget=512ull*1024*1024;
struct Rig { std::vector<NcmaPoseBoneV1> bones; uint32_t clips=0; };
struct Clip { uint64_t rig=0; double duration=0; std::vector<NcmaPoseTrackV1> tracks; std::vector<NcmaPoseKeyV1> keys; std::array<int32_t,1024> index; };
struct Context {
 std::thread::id owner=std::this_thread::get_id(); bool busy=false;
 std::unordered_map<uint64_t,Rig> rigs; std::unordered_map<uint64_t,Clip> clips;
 std::array<NcmaPoseTrsV1,NCMA_POSE_MAX_OUTPUT> local{};
 std::array<NcmaPoseMatrixV1,NCMA_POSE_MAX_OUTPUT> model{};
 NcmaPoseStatsV1 stats{48,32,0,0,0,0,0};
};
std::mutex Gate; std::unordered_map<uint64_t,std::shared_ptr<Context>> Contexts; uint64_t Next=1;
uint64_t NewId() { std::scoped_lock lock(Gate);Require(Next!=UINT64_MAX,NCMA_BUSY);return Next++; }
std::shared_ptr<Context> Find(uint64_t id) {
 std::shared_ptr<Context> c; {std::scoped_lock lock(Gate);auto i=Contexts.find(id);Require(i!=Contexts.end(),NCMA_INVALID_HANDLE);c=i->second;}
 Require(c->owner==std::this_thread::get_id(),NCMA_WRONG_THREAD);Require(!c->busy,NCMA_BUSY);return c;
}
struct Busy { Context& c; explicit Busy(Context& v):c(v){c.busy=true;} ~Busy(){c.busy=false;} };
void ValidTrs(const NcmaPoseTrsV1& t) {
 for(float v:t.position)Require(std::isfinite(v));for(float v:t.rotation)Require(std::isfinite(v));for(float v:t.scale)Require(std::isfinite(v));
 float s=t.scale[0];Require(s>=.00001f && s<=1000.f && std::abs(t.scale[1]-s)<=s*.00001f && std::abs(t.scale[2]-s)<=s*.00001f);
 Eigen::Quaternionf q(t.rotation[3],t.rotation[0],t.rotation[1],t.rotation[2]);Require(std::abs(q.squaredNorm()-1.f)<=.002f);
}
NcmaPoseTrsV1 Blend(const NcmaPoseTrsV1& a,const NcmaPoseTrsV1& b,float alpha) {
 NcmaPoseTrsV1 t{};for(int i=0;i<3;i++){t.position[i]=a.position[i]+(b.position[i]-a.position[i])*alpha;t.scale[i]=a.scale[i]+(b.scale[i]-a.scale[i])*alpha;}
 Eigen::Quaternionf x(a.rotation[3],a.rotation[0],a.rotation[1],a.rotation[2]),y(b.rotation[3],b.rotation[0],b.rotation[1],b.rotation[2]);
 auto q=x.normalized().slerp(alpha,y.normalized()).normalized();for(int i=0;i<4;i++)t.rotation[i]=q.coeffs()[i];return t;
}
NcmaPoseTrsV1 At(const Rig& rig,const Clip* clip,uint32_t bone,double time) {
 if(!clip || clip->index[bone]<0)return rig.bones[bone].local;
 const auto& track=clip->tracks[static_cast<size_t>(clip->index[bone])];const auto* keys=clip->keys.data()+track.first_key;
 if(time<=keys[0].time)return keys[0].local;if(time>=keys[track.key_count-1].time)return keys[track.key_count-1].local;
 uint32_t lo=0,hi=track.key_count-1;while(hi-lo>1){auto mid=lo+(hi-lo)/2;if(keys[mid].time<=time)lo=mid;else hi=mid;}
 return Blend(keys[lo].local,keys[hi].local,static_cast<float>((time-keys[lo].time)/(keys[hi].time-keys[lo].time)));
}
uint32_t NCMA_CALL Create(uint64_t* out,NcmaErrorV1* error) {return Run(error,[&]()->uint32_t{
 Require(out);auto c=std::make_shared<Context>();auto id=NewId();std::scoped_lock lock(Gate);Require(Contexts.size()<4,NCMA_BUSY);Contexts.emplace(id,c);*out=id;return NCMA_OK;});}
uint32_t NCMA_CALL Close(uint64_t id,NcmaErrorV1* error) {return Run(error,[&]()->uint32_t{
 auto c=Find(id);Require(c->rigs.empty()&&c->clips.empty(),NCMA_BUSY);std::scoped_lock lock(Gate);Contexts.erase(id);return NCMA_OK;});}
uint32_t NCMA_CALL CreateRig(uint64_t id,const NcmaPoseBoneV1* input,uint32_t count,uint64_t* output,NcmaErrorV1* error) {return Run(error,[&]()->uint32_t{
 auto c=Find(id);Require(output&&input&&count&&count<=1024&&c->rigs.size()<64);Busy busy(*c);Rig r;r.bones.assign(input,input+count);
 for(uint32_t i=0;i<count;i++){const auto& b=r.bones[i];Require(b.parent>=-1&&b.parent<static_cast<int32_t>(i));ValidTrs(b.local);}
 auto bytes=static_cast<uint64_t>(count)*44;Require(bytes<=Budget-c->stats.retained_bytes);auto key=NewId();c->rigs.emplace(key,std::move(r));c->stats.rigs++;c->stats.retained_bytes+=bytes;*output=key;return NCMA_OK;});}
uint32_t NCMA_CALL CreateClip(uint64_t id,uint64_t rigId,double duration,const NcmaPoseTrackV1* tracks,uint32_t trackCount,const NcmaPoseKeyV1* keys,uint32_t keyCount,uint64_t* output,NcmaErrorV1* error) {return Run(error,[&]()->uint32_t{
 auto c=Find(id);Require(c->rigs.contains(rigId),NCMA_INVALID_HANDLE);auto& r=c->rigs.at(rigId);
 Require(output&&std::isfinite(duration)&&duration>0&&duration<=600&&trackCount<=r.bones.size()&&keyCount<=2000000&&(!trackCount||tracks)&&(!keyCount||keys)&&c->clips.size()<128);
 uint64_t bytes=static_cast<uint64_t>(trackCount)*16+static_cast<uint64_t>(keyCount)*48+4096;Require(bytes<=64ull*1024*1024&&bytes<=Budget-c->stats.retained_bytes);Busy busy(*c);
 Clip p;p.rig=rigId;p.duration=duration;p.index.fill(-1);if(trackCount)p.tracks.assign(tracks,tracks+trackCount);if(keyCount)p.keys.assign(keys,keys+keyCount);
 uint32_t next=0;for(uint32_t i=0;i<trackCount;i++){const auto& t=p.tracks[i];Require(!t.reserved&&t.bone<r.bones.size()&&p.index[t.bone]<0&&t.key_count>=2&&t.first_key==next&&t.first_key<=keyCount&&t.key_count<=keyCount-t.first_key);
 p.index[t.bone]=static_cast<int32_t>(i);double previous=-1;for(uint32_t k=0;k<t.key_count;k++){const auto& v=p.keys[t.first_key+k];Require(std::isfinite(v.time)&&v.time>=0&&v.time<=duration&&v.time>previous);previous=v.time;ValidTrs(v.local);}Require(p.keys[t.first_key].time==0&&p.keys[t.first_key+t.key_count-1].time==duration);next+=t.key_count;}
 Require(next==keyCount);auto key=NewId();c->clips.emplace(key,std::move(p));r.clips++;c->stats.clips++;c->stats.retained_bytes+=bytes;*output=key;return NCMA_OK;});}
uint32_t NCMA_CALL Release(uint64_t id,uint64_t key,NcmaErrorV1* error) {return Run(error,[&]()->uint32_t{
 auto c=Find(id);if(auto it=c->clips.find(key);it!=c->clips.end()){const auto& p=it->second;c->stats.retained_bytes-=p.tracks.size()*16ull+p.keys.size()*48ull+4096;c->rigs.at(p.rig).clips--;c->clips.erase(it);c->stats.clips--;return NCMA_OK;}
 auto it=c->rigs.find(key);Require(it!=c->rigs.end(),NCMA_INVALID_HANDLE);Require(!it->second.clips,NCMA_BUSY);c->stats.retained_bytes-=it->second.bones.size()*44ull;c->rigs.erase(it);c->stats.rigs--;return NCMA_OK;});}
uint32_t NCMA_CALL Sample(uint64_t id,const NcmaPoseRequestV1* input,uint32_t count,NcmaPoseTrsV1* local,NcmaPoseMatrixV1* model,uint32_t capacity,NcmaErrorV1* error) {return Run(error,[&]()->uint32_t{
 auto c=Find(id);Require(count&&count<=32&&input&&local&&model&&capacity&&capacity<=32768);
 Require(!Overlap(local,static_cast<uint64_t>(capacity)*40,model,static_cast<uint64_t>(capacity)*64)&&
 !Overlap(input,static_cast<uint64_t>(count)*40,local,static_cast<uint64_t>(capacity)*40)&&!Overlap(input,static_cast<uint64_t>(count)*40,model,static_cast<uint64_t>(capacity)*64));Busy busy(*c);
 std::array<NcmaPoseRequestV1,32> requests{};std::copy_n(input,count,requests.data());uint32_t end=0;
 for(uint32_t i=0;i<count;i++){const auto& p=requests[i];Require(c->rigs.contains(p.rig),NCMA_INVALID_HANDLE);const auto& r=c->rigs.at(p.rig);
 Require(std::isfinite(p.previous_time)&&std::isfinite(p.current_time)&&std::isfinite(p.alpha)&&p.alpha>=0&&p.alpha<=1&&p.output_offset==end&&r.bones.size()<=capacity-end);
 if(p.clip){Require(c->clips.contains(p.clip)&&c->clips.at(p.clip).rig==p.rig,NCMA_INVALID_HANDLE);double duration=c->clips.at(p.clip).duration;Require(p.previous_time>=0&&p.previous_time<=duration&&p.current_time>=0&&p.current_time<=duration);}
 else Require(p.previous_time==0&&p.current_time==0);end+=static_cast<uint32_t>(r.bones.size());}
 for(uint32_t i=0;i<count;i++){const auto& p=requests[i];const auto& r=c->rigs.at(p.rig);const auto* clip=p.clip?&c->clips.at(p.clip):nullptr;
 for(uint32_t b=0;b<r.bones.size();b++){auto index=p.output_offset+b;auto t=Blend(At(r,clip,b,p.previous_time),At(r,clip,b,p.current_time),p.alpha);c->local[index]=t;
 Eigen::Quaternionf q(t.rotation[3],t.rotation[0],t.rotation[1],t.rotation[2]);Eigen::Matrix4f m=Eigen::Matrix4f::Identity();m.block<3,3>(0,0)=q.toRotationMatrix()*t.scale[0];m.block<3,1>(0,3)=Eigen::Map<const Eigen::Vector3f>(t.position);
 if(r.bones[b].parent>=0)m=Eigen::Map<const Eigen::Matrix4f>(c->model[p.output_offset+static_cast<uint32_t>(r.bones[b].parent)].column_major)*m;
 Require(m.allFinite()&&m.block<3,3>(0,0).col(0).norm()>=.000001f);Eigen::Map<Eigen::Matrix4f>(c->model[index].column_major)=m;}}
 std::copy_n(c->local.data(),end,local);std::copy_n(c->model.data(),end,model);c->stats.sample_calls++;c->stats.sampled_bones+=end;return NCMA_OK;});}
uint32_t NCMA_CALL Stats(uint64_t id,NcmaPoseStatsV1* output,NcmaErrorV1* error) {return Run(error,[&]()->uint32_t{auto c=Find(id);Require(output);*output=c->stats;return NCMA_OK;});}
const NcmaPoseApiV1 Api{72,1,0,1024,Create,Close,CreateRig,CreateClip,Release,Sample,Stats};
}
uint32_t NCMA_CALL ncma_pose_get_api(uint32_t major,uint32_t minor,void* output,uint32_t bytes,NcmaErrorV1* error) {return NcmaPlugin::CopyApi(major,minor,output,bytes,error,Api);}
