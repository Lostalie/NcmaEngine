#ifndef NCMA_POSE_LAYER_H
#define NCMA_POSE_LAYER_H
#include "NcmaPose.h"
#ifdef __cplusplus
extern "C" {
#endif
/* Optional layer1.0 uses existing owner-thread context/rig, no owned resources.
   mode0 override, mode1 local additive with explicit reference pose.
   32 requests,65536 inputTRS,32768 mask floats/output bones. Distinct buffers;
   all referenced inputs validated and results staged before ANY output/counter publication.
   Positive uniform scale; q=base*slerp(identity,inverse(reference)*layer,mask*weight).
   Release original clips/rig/context before unload. No graph/World/clock/root authority. */
typedef struct NcmaPoseLayerRequestV1 {
 uint64_t rig;
 uint32_t source_a,source_b,reference,mask_offset,output_offset,mode;
 float weight; uint32_t reserved;
} NcmaPoseLayerRequestV1;
typedef struct NcmaPoseLayerStatsV1 {
 uint32_t struct_size,max_requests;uint64_t layer_calls,layered_bones;
} NcmaPoseLayerStatsV1;
typedef struct NcmaPoseLayerApiV1 {
 uint32_t struct_size,major,minor,max_requests;
 uint32_t (NCMA_CALL *layer)(uint64_t,const NcmaPoseLayerRequestV1*,uint32_t,
  const NcmaPoseTrsV1*,uint32_t,const float*,uint32_t,NcmaPoseTrsV1*,NcmaPoseMatrixV1*,uint32_t,NcmaErrorV1*);
 uint32_t (NCMA_CALL *stats)(uint64_t,NcmaPoseLayerStatsV1*,NcmaErrorV1*);
} NcmaPoseLayerApiV1;
NCMA_POSE_API uint32_t NCMA_CALL ncma_pose_get_layer_api(uint32_t,uint32_t,void*,uint32_t,NcmaErrorV1*);
#ifdef __cplusplus
}
static_assert(sizeof(NcmaPoseLayerRequestV1)==40 && sizeof(NcmaPoseLayerStatsV1)==24);
static_assert(sizeof(void*)!=8 || sizeof(NcmaPoseLayerApiV1)==32);
#endif
#endif
