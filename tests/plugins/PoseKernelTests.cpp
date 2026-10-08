#include "contracts/NcmaPose.h"
#include "contracts/NcmaPoseBlend.h"
#include "contracts/NcmaPoseLayer.h"
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
 NcmaPoseBlendApiV1 mix{};assert(ncma_pose_get_blend_api(2,0,&mix,32,&error)==NCMA_ABI_MISMATCH);
 assert(ncma_pose_get_blend_api(1,1,&mix,32,&error)==NCMA_ABI_MISMATCH);
 assert(ncma_pose_get_blend_api(1,0,&mix,31,&error)==NCMA_BUFFER_TOO_SMALL&&error.required_bytes==32);
 assert(ncma_pose_get_blend_api(1,0,&mix,32,&error)==0&&mix.max_requests==32);
 NcmaPoseBlendRequestV1 blending[2]{{rig,0,2,0,0,.5f,0},{rig,2,0,2,0,1,0}};
 NcmaPoseTrsV1 mixed[4]{};NcmaPoseMatrixV1 mixedModel[4]{};
 assert(mix.blend(context,blending,2,local,4,mixed,mixedModel,4,&error)==0);
 assert(std::abs(mixed[0].position[0]-1.5f)<1e-6f&&std::abs(mixedModel[1].column_major[13]-3)<1e-6f&&mixed[2].position[0]==2);
 NcmaPoseBlendStatsV1 mixBefore{},mixAfter{};assert(mix.stats(context,&mixBefore,&error)==0&&mixBefore.struct_size==24&&mixBefore.blended_bones==4);
 mixedModel[0].column_major[0]=77;blending[1].reserved1=1;assert(mix.blend(context,blending,2,local,4,mixed,mixedModel,4,&error)==NCMA_INVALID_ARGUMENT);blending[1].reserved1=0;
 local[3].scale[1]=2;assert(mix.blend(context,blending,2,local,4,mixed,mixedModel,4,&error)==NCMA_INVALID_ARGUMENT);local[3].scale[1]=1;
 assert(mixedModel[0].column_major[0]==77&&mix.stats(context,&mixAfter,&error)==0&&mixAfter.blend_calls==mixBefore.blend_calls);
 assert(mix.blend(context,blending,2,local,4,local,mixedModel,4,&error)==NCMA_INVALID_ARGUMENT);
 blending[1].source_b=UINT32_MAX;assert(mix.blend(context,blending,2,local,4,mixed,mixedModel,4,&error)==NCMA_INVALID_ARGUMENT);blending[1].source_b=0;
 blending[1].weight=-1;assert(mix.blend(context,blending,2,local,4,mixed,mixedModel,4,&error)==NCMA_INVALID_ARGUMENT);blending[1].weight=1;
 std::thread blendWrong([&](){NcmaErrorV1 e{};NcmaPoseBlendStatsV1 s{};threadCode=mix.stats(context,&s,&e);});blendWrong.join();assert(threadCode==NCMA_WRONG_THREAD);
 assert(api.create(&other,&error)==0);assert(mix.blend(other,blending,2,local,4,mixed,mixedModel,4,&error)==NCMA_INVALID_HANDLE);assert(api.close(other,&error)==0);
 NcmaPoseLayerApiV1 layer{};assert(ncma_pose_get_layer_api(2,0,&layer,32,&error)==NCMA_ABI_MISMATCH);
 assert(ncma_pose_get_layer_api(1,1,&layer,32,&error)==NCMA_ABI_MISMATCH);
 assert(ncma_pose_get_layer_api(1,0,&layer,31,&error)==NCMA_BUFFER_TOO_SMALL&&error.required_bytes==32);
 assert(ncma_pose_get_layer_api(1,0,&layer,32,&error)==0&&layer.max_requests==32);
 float masks[2]{0,1};NcmaPoseLayerRequestV1 layers[2]{{rig,0,2,0,0,0,0,.5f,0},{rig,0,2,0,0,2,1,.5f,0}};
 assert(layer.layer(context,layers,2,local,4,masks,2,mixed,mixedModel,4,&error)==0&&mixed[0].position[0]==local[0].position[0]);
 NcmaPoseLayerStatsV1 lsBefore{},lsAfter{};assert(layer.stats(context,&lsBefore,&error)==0&&lsBefore.layer_calls==1&&lsBefore.layered_bones==4);
 mixedModel[0].column_major[0]=77;masks[1]=NAN;assert(layer.layer(context,layers,2,local,4,masks,2,mixed,mixedModel,4,&error)==NCMA_INVALID_ARGUMENT);masks[1]=1;
 layers[1].reserved=1;assert(layer.layer(context,layers,2,local,4,masks,2,mixed,mixedModel,4,&error)==NCMA_INVALID_ARGUMENT);layers[1].reserved=0;
 layers[1].reference=UINT32_MAX;assert(layer.layer(context,layers,2,local,4,masks,2,mixed,mixedModel,4,&error)==NCMA_INVALID_ARGUMENT);layers[1].reference=0;
 assert(layer.layer(context,layers,2,local,4,masks,2,local,mixedModel,4,&error)==NCMA_INVALID_ARGUMENT);
 assert(mixedModel[0].column_major[0]==77&&layer.stats(context,&lsAfter,&error)==0&&lsAfter.layer_calls==lsBefore.layer_calls);
 std::thread layerWrong([&](){NcmaErrorV1 e{};NcmaPoseLayerStatsV1 s{};threadCode=layer.stats(context,&s,&e);});layerWrong.join();assert(threadCode==NCMA_WRONG_THREAD);
 assert(api.release(context,clip,&error)==0);assert(api.sample(context,requests,2,local,model,4,&error)==NCMA_INVALID_HANDLE);
 assert(api.release(context,rig,&error)==0&&api.stats(context,&after,&error)==0&&after.retained_bytes==0&&after.rigs==0&&after.clips==0);
 assert(mix.blend(context,blending,2,local,4,mixed,mixedModel,4,&error)==NCMA_INVALID_HANDLE);
 assert(layer.layer(context,layers,2,local,4,masks,2,mixed,mixedModel,4,&error)==NCMA_INVALID_HANDLE);
 assert(api.close(context,&error)==0&&api.stats(context,&after,&error)==NCMA_INVALID_HANDLE);
 std::cout<<"Pose-only ABI: layouts, TRS, bindings-independent composition, interpolation, atomic batches, owner, stale/foreign and cleanup passed.\n";
}
