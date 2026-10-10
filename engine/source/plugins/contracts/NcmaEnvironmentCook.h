#ifndef NCMA_ENVIRONMENT_COOK_H
#define NCMA_ENVIRONMENT_COOK_H
#include "NcmaPlugin.h"
#ifdef __cplusplus
extern "C" {
#endif
/* Renderer query13/API1: bounded offline CPU numerical cook, NO GPU install/resources.
   Input copied; output entirely caller-owned, unchanged on every error.
   Linear equirect RGBA32F, width=2*height, power2 height2..256, RGB0..65504, alpha1.
   cube2..64, irradiance2..16, LUT2..64 powers2; samples64..2048 power2; work<=64M.
   Output: six irradiance faces (E, RGBA), then spec mip-major/six faces RGBA,
   then LUT roughness-y/NoV-x RG; face order +X,-X,+Y,-Y,+Z,-Z.
   Same owner thread/module; trusted host must additionally exclude simulation boundaries.
   No source path, retained pointer, allocation release API or interpreter. Algorithm1. */
typedef struct NcmaEnvironmentCookV1 {
    uint32_t struct_size,version,width,height,cube_size,irradiance_size,lut_size,samples,input_floats,reserved;
    const float* input;
} NcmaEnvironmentCookV1;
typedef struct NcmaEnvironmentCookOutputV1 {
    uint32_t struct_size,version,algorithm,float_count,levels,source_bytes,reserved,reserved2;
} NcmaEnvironmentCookOutputV1;
typedef uint32_t (NCMA_CALL *NcmaEnvironmentCookFnV1)(uint64_t,const NcmaEnvironmentCookV1*,NcmaEnvironmentCookOutputV1*,float*,uint32_t,NcmaErrorV1*);
typedef struct NcmaEnvironmentCookApiV1 {
    uint32_t struct_size,version;uint64_t capabilities;
    NcmaEnvironmentCookFnV1 cook;
    uint32_t (NCMA_CALL *validate_preparation)(uint64_t,NcmaErrorV1*);
} NcmaEnvironmentCookApiV1;
#ifdef __cplusplus
}
static_assert(sizeof(NcmaEnvironmentCookV1)==48);
static_assert(sizeof(NcmaEnvironmentCookOutputV1)==32);
static_assert(sizeof(NcmaEnvironmentCookApiV1)==32);
#endif
#endif
