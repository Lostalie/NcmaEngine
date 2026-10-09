#ifndef NCMA_RUNTIME_SHADERS_H
#define NCMA_RUNTIME_SHADERS_H
#include "NcmaShaderStages.h"
#ifdef __cplusplus
extern "C" {
#endif
/* Independent query12/API1. Caller-owned bounded bytecode, synchronous copied reflection
   only; owner/idle/healthy. No retained handles, allocation transfer, GPU install or grant.
   profile 0 UI/1 Scene; flags bit0 shadow/bit1 skin. All unused groups must be zero.
   Successful validation is NOT code safety. Create/replace still validates actual bytes. */
typedef struct NcmaRuntimeShadersV1 {
 uint32_t struct_size,version,profile,flags;
 NcmaShaderPairV1 ui;
 NcmaSceneShadersV1 scene;
 NcmaComputeShaderV1 skin;
} NcmaRuntimeShadersV1;
typedef struct NcmaRuntimeShadersApiV1 {
 uint32_t struct_size,version; uint64_t capabilities;
 uint32_t (NCMA_CALL *validate)(uint64_t,uint64_t,const NcmaRuntimeShadersV1*,NcmaErrorV1*);
} NcmaRuntimeShadersApiV1;
#ifdef __cplusplus
}
static_assert(sizeof(NcmaRuntimeShadersV1)==176);
static_assert(sizeof(NcmaRuntimeShadersApiV1)==24);
#endif
#endif
