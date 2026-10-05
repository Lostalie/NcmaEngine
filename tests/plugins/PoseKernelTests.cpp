#include "contracts/NcmaPose.h"
#include <cassert>
#include <cmath>
#include <iostream>
#include <thread>
#include <vector>
int main() {
 NcmaErrorV1 error{};NcmaPoseApiV1 api{};
 assert(ncma_pose_get_api(2,0,&api,72,&error)==NCMA_ABI_MISMATCH);
 assert(ncma_pose_get_api(1,1,&api,72,&error)==NCMA_ABI_MISMATCH);
 assert(ncma_pose_get_api(1,0,&api,71,&error)==NCMA_BUFFER_TOO_SMALL&&error.required_bytes==72);
 assert(ncma_pose_get_api(1,0,&api,72,&error)==0&&api.max_bones==1024);
 uint64_t context=0,rig=0,clip=0;assert(api.create(&context,&error)==0&&context);
 NcmaPoseTrsV1 bind{{1,2,3},{0,0,0,1},{1,1,1}};NcmaPoseBoneV1 bones[2]{{-1,bind},{0,{{0,1,0},{0,0,0,1},{1,1,1}}}};
 auto invalid=bones[0];invalid.parent=0;assert(api.create_rig(context,&invalid,1,&rig,&error)==NCMA_INVALID_ARGUMENT&&!rig);
 invalid=bones[0];invalid.local.scale[1]=2;assert(api.create_rig(context,&invalid,1,&rig,&error)==NCMA_INVALID_ARGUMENT);
 assert(api.create_rig(context,bones,2,&rig,&error)==0&&rig);
 NcmaPoseTrackV1 track{0,0,2,0};NcmaPoseKeyV1 keys[2]{{0,bind},{1,bind}};keys[1].local.position[0]=3;
 assert(api.create_clip(context,rig,1,&track,1,keys,2,&clip,&error)==0&&clip);
 assert(api.release(context,rig,&error)==NCMA_BUSY&&api.close(context,&error)==NCMA_BUSY);
 NcmaPoseTrsV1 local[4]{};NcmaPoseMatrixV1 model[4]{};
 NcmaPoseRequestV1 requests[2]{{rig,clip,0,1,.5f,0},{rig,0,0,0,1,2}};
 assert(api.sample(context,requests,2,local,model,4,&error)==0);
 assert(std::abs(local[0].position[0]-2)<1e-6f&&std::abs(model[1].column_major[12]-2)<1e-6f&&std::abs(model[1].column_major[13]-3)<1e-6f);
 assert(std::abs(model[3].column_major[12]-1)<1e-6f);NcmaPoseStatsV1 before{},after{};assert(api.stats(context,&before,&error)==0);
 requests[1].clip=UINT64_MAX;model[0].column_major[0]=77;assert(api.sample(context,requests,2,local,model,4,&error)==NCMA_INVALID_HANDLE);
 assert(model[0].column_major[0]==77&&api.stats(context,&after,&error)==0&&after.sample_calls==before.sample_calls);
 requests[1].clip=0;requests[1].output_offset=1;assert(api.sample(context,requests,2,local,model,4,&error)==NCMA_INVALID_ARGUMENT);requests[1].output_offset=2;
 assert(api.sample(context,requests,2,local,model,3,&error)==NCMA_INVALID_ARGUMENT);
 requests[0].current_time=2;assert(api.sample(context,requests,2,local,model,4,&error)==NCMA_INVALID_ARGUMENT);requests[0].current_time=1;
 uint32_t threadCode=0;std::thread wrong([&](){NcmaErrorV1 e{};NcmaPoseStatsV1 s{};threadCode=api.stats(context,&s,&e);});wrong.join();assert(threadCode==NCMA_WRONG_THREAD);
 for(int i=0;i<4096;i++)assert(api.sample(context,requests,2,local,model,4,&error)==0);
 uint64_t other=0;assert(api.create(&other,&error)==0);assert(api.sample(other,requests,2,local,model,4,&error)==NCMA_INVALID_HANDLE);assert(api.close(other,&error)==0);
 assert(api.release(context,clip,&error)==0);assert(api.sample(context,requests,2,local,model,4,&error)==NCMA_INVALID_HANDLE);
 assert(api.release(context,rig,&error)==0&&api.stats(context,&after,&error)==0&&after.retained_bytes==0&&after.rigs==0&&after.clips==0);
 assert(api.close(context,&error)==0&&api.stats(context,&after,&error)==NCMA_INVALID_HANDLE);
 std::cout<<"Pose-only ABI: layouts, TRS, bindings-independent composition, interpolation, atomic batches, owner, stale/foreign and cleanup passed.\n";
}
