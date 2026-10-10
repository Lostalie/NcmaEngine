#ifndef NCMA_ENVIRONMENT_SCENE_H
#define NCMA_ENVIRONMENT_SCENE_H
#include "NcmaRuntimeShaders.h"
#ifdef __cplusplus
extern "C" {
#endif
/* Renderer query15/API1: independent environment Scene group (104B/version2),
   geometry C416 + cube t7/t8, LUT t9, sampler s7; old Shadow/Tone C400.
   Whole source-free admission: RuntimeShaders176/version2/profile3, exact flags0..3,
   empty Ui, Scene.version2, optional original Skin24/version1. No shader safety sandbox.
   Bind pins renderer-scoped environment token; pinned resources cannot replace/destroy.
   Off: zero token/strength/rotation/reserved, no environment allocation. Bind/replacement
   owner/healthy/off-frame/nonreentrant, host additionally requires trusted off-simulation
   approval. All candidates/diagnostics/drain before publication. Scene close releases pin.
   Caller buffers copied/not retained; no World/UUID objects/graphics types or Agent authority. */
typedef struct NcmaEnvironmentBindingV1 {
 uint32_t struct_size,version;NcmaGpuResourceV3 environment;
 float strength,rotation;uint32_t reserved,reserved2;
} NcmaEnvironmentBindingV1;
typedef struct NcmaEnvironmentSceneApiV1 {
 uint32_t struct_size,version;uint64_t capabilities;
 uint32_t (NCMA_CALL *copy_source)(uint64_t,uint64_t,uint8_t*,uint32_t,uint32_t*,NcmaErrorV1*);
 uint32_t (NCMA_CALL *validate)(uint64_t,uint64_t,const NcmaRuntimeShadersV1*,NcmaErrorV1*);
 uint32_t (NCMA_CALL *create)(uint64_t,uint64_t,const NcmaScenePipelineDescriptionV4*,const NcmaSceneShadersV1*,NcmaGpuResourceV3*,NcmaErrorV1*);
 uint32_t (NCMA_CALL *replace)(uint64_t,uint64_t,NcmaGpuResourceV3,const NcmaSceneShadersV1*,NcmaErrorV1*);
 uint32_t (NCMA_CALL *bind)(uint64_t,uint64_t,NcmaGpuResourceV3,const NcmaEnvironmentBindingV1*,NcmaErrorV1*);
} NcmaEnvironmentSceneApiV1;
#ifdef __cplusplus
}
static_assert(sizeof(NcmaEnvironmentBindingV1)==40);
static_assert(sizeof(NcmaEnvironmentSceneApiV1)==56);
#endif
#endif
