#ifndef NCMA_SHADER_PIPELINE_H
#define NCMA_SHADER_PIPELINE_H
#include "NcmaScenePipeline.h"
#ifdef __cplusplus
extern "C" {
#endif
/* Renderer query9/API1, trusted off-frame Scene Tone ONLY. Query1-8 frozen.
   Pair caller-owned, <=1MiB each, complete reflection/link validation before GPU allocation.
   Source returns a copied trusted default, not an Agent tool or arbitrary file read.
   New scene owns programs; replacement drains GPU before atomic exchange; failed drain retains old.
   Owner exact module/renderer, idle healthy; no independent shader handles or serialized pointers. */
typedef struct NcmaShaderPairV1 { uint32_t struct_size,version,vertex_bytes,pixel_bytes; const uint8_t* vertex; const uint8_t* pixel; } NcmaShaderPairV1;
typedef struct NcmaShaderPipelineApiV1 {
    uint32_t struct_size,version; uint64_t capabilities;
    uint32_t (NCMA_CALL *copy_tone_source)(uint64_t,uint64_t,uint8_t*,uint32_t,uint32_t*,NcmaErrorV1*);
    uint32_t (NCMA_CALL *create_scene)(uint64_t,uint64_t,const NcmaScenePipelineDescriptionV4*,const NcmaShaderPairV1*,NcmaGpuResourceV3*,NcmaErrorV1*);
    uint32_t (NCMA_CALL *replace_tone)(uint64_t,uint64_t,NcmaGpuResourceV3,const NcmaShaderPairV1*,NcmaErrorV1*);
} NcmaShaderPipelineApiV1;
#ifdef __cplusplus
}
static_assert(sizeof(NcmaShaderPairV1)==32);
static_assert(sizeof(NcmaShaderPipelineApiV1)==40);
#endif
#endif
