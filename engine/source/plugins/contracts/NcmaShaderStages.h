#ifndef NCMA_SHADER_STAGES_H
#define NCMA_SHADER_STAGES_H
#include "NcmaShaderPipeline.h"
#include "NcmaSkin.h"
#ifdef __cplusplus
extern "C" {
#endif
/* Independent query10/API1, owner/idle/healthy trusted preparation ONLY.
   source kind0 scene, kind1 skin; copied <=256KiB, no filesystem.
   Complete caller-owned bytecode <=1MiB/stage. No persistent shader handle.
   Scene group atomic/no resource rebuild; absent shadow pair must be entirely zero.
   Skin is the same shared numerical kernel. Existing kernel requires exact code;
   explicit replacement drains GPU and preserves mesh/palette resources.
   Last skin release destroys its shader. Reflection is NOT a code safety sandbox. */
typedef struct NcmaSceneShadersV1 { uint32_t struct_size,version; NcmaShaderPairV1 geometry,shadow,tone; } NcmaSceneShadersV1;
typedef struct NcmaComputeShaderV1 { uint32_t struct_size,version,bytes,reserved; const uint8_t* bytecode; } NcmaComputeShaderV1;
typedef struct NcmaShaderStagesApiV1 {
 uint32_t struct_size,version; uint64_t capabilities;
 uint32_t (NCMA_CALL *copy_source)(uint64_t,uint64_t,uint32_t,uint8_t*,uint32_t,uint32_t*,NcmaErrorV1*);
 uint32_t (NCMA_CALL *create_scene)(uint64_t,uint64_t,const NcmaScenePipelineDescriptionV4*,const NcmaSceneShadersV1*,NcmaGpuResourceV3*,NcmaErrorV1*);
 uint32_t (NCMA_CALL *replace_scene)(uint64_t,uint64_t,NcmaGpuResourceV3,const NcmaSceneShadersV1*,NcmaErrorV1*);
 uint32_t (NCMA_CALL *create_skin)(uint64_t,uint64_t,const NcmaSkinMeshV5*,const NcmaComputeShaderV1*,NcmaGpuMeshV1*,NcmaErrorV1*);
 uint32_t (NCMA_CALL *replace_skin)(uint64_t,uint64_t,const NcmaComputeShaderV1*,NcmaErrorV1*);
} NcmaShaderStagesApiV1;
#ifdef __cplusplus
}
static_assert(sizeof(NcmaSceneShadersV1)==104);
static_assert(sizeof(NcmaComputeShaderV1)==24);
static_assert(sizeof(NcmaShaderStagesApiV1)==56);
#endif
#endif
