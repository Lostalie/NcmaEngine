#ifndef NCMA_SCENE_PIPELINE_H
#define NCMA_SCENE_PIPELINE_H
#include "NcmaResourceRender.h"
#ifdef __cplusplus
extern "C" {
#endif
/* Independent v4: no scene ownership. Borrowed bounded batch; owner thread; create/destroy
   only outside active frames. Immutable resource v3 mesh/material handles. Single directional
   shadow map, HDR geometry, explicit tone passes. Allocation belongs to pipeline group;
   drain GPU before destroy. All input validates before GPU execution; execution faults fail-stop. */
typedef struct NcmaScenePipelineDescriptionV4 {uint32_t struct_size,width,height,shadow_resolution;} NcmaScenePipelineDescriptionV4; /* resolution 0 disables shadow resources */
typedef struct NcmaSceneDrawV4 {NcmaResourceDrawV3 draw;float override_surface[4];} NcmaSceneDrawV4; /* metallic,roughness,enabled(0/1),exclude_direct_light(0/1) */
typedef struct NcmaScenePassV4 {uint32_t operation;float parameters[3];} NcmaScenePassV4; /* 6 shadow,7 geometry,8 tone */
typedef struct NcmaSceneFrameV4 {
    NcmaResourceFrameV3 base;
    NcmaGpuResourceV3 pipeline;
    uint32_t caster_count,pass_count;
    float light_view_projection[16];
    float shadow[4]; /* constant bias,slope bias,filter0 PCF/1 PCSS,light radius in texels */
} NcmaSceneFrameV4;
typedef struct NcmaScenePipelineStatsV4 {uint32_t struct_size,max_draws;uint64_t generation,pipelines,resident_bytes,creates,geometry_draws,shadow_draws,copied_bytes,constant_upload_bytes;} NcmaScenePipelineStatsV4;
typedef uint32_t (NCMA_CALL *NcmaCreateScenePipelineV4)(uint64_t,uint64_t,const NcmaScenePipelineDescriptionV4*,NcmaGpuResourceV3*,NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaSubmitScenePipelineV4)(uint64_t,uint64_t,const NcmaSceneFrameV4*,const NcmaSceneDrawV4*,const NcmaSceneDrawV4*,const NcmaScenePassV4*,NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaScenePipelineStatsFnV4)(uint64_t,uint64_t,NcmaScenePipelineStatsV4*,NcmaErrorV1*);
typedef struct NcmaScenePipelineApiV4 {
    uint32_t struct_size,version;uint64_t capabilities;
    NcmaCreateScenePipelineV4 create;NcmaDestroyResourceV3 destroy;
    NcmaSubmitScenePipelineV4 submit;NcmaScenePipelineStatsFnV4 stats;
} NcmaScenePipelineApiV4;
#ifdef __cplusplus
}
static_assert(sizeof(NcmaSceneDrawV4)==256);
static_assert(sizeof(NcmaSceneFrameV4)==240);
static_assert(sizeof(NcmaScenePipelineApiV4)==48);
static_assert(sizeof(NcmaScenePipelineStatsV4)==72);
#endif
#endif
