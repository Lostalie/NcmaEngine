#ifndef NCMA_ENVIRONMENT_GPU_H
#define NCMA_ENVIRONMENT_GPU_H
#include "NcmaResourceRender.h"
#ifdef __cplusplus
extern "C" {
#endif
/* Independent Renderer query14/API1, immutable GPU environment resources, NOT scene IBL.
   Owner-thread, healthy, off-frame, no pureUI; host additionally excludes simulation.
   Copied linear floats: six irradiance faces RGBA(E), mip-major six specular faces RGBA,
   roughness-y/NoV-x LUT RG. +X,-X,+Y,-Y,+Z,-Z. alpha1, no negative zero;
   E<=65504*pi, spec<=65504, LUT<=2. Sizes powers2: cube2..64, irradiance2..16,
   LUT2..64, full spec mips. <=8 resources/32MiB; borrowed input/output only during call.
   Publish old{0,0} creates, else atomically replaces exactly renderer-scoped old token.
   Candidate creation/diagnostics/drain BEFORE publication, failure leaves old/output/counters.
   Destroy drains (failure retains for retry); resources must close before renderer/module.
   Capture is bounded diagnostic GPU staging, exact floats, atomic caller output, NOT tick-safe.
   No UUID, managed object, graphics headers, strength/rotation, live World or shader authority. */
typedef struct NcmaEnvironmentGpuDescriptionV1 {
    uint32_t struct_size,version,cube_size,irradiance_size,lut_size,levels,float_count,reserved;
    const float* values;
} NcmaEnvironmentGpuDescriptionV1;
typedef struct NcmaEnvironmentGpuStatsV1 {
    uint32_t struct_size,version;
    uint64_t live,resident_bytes,publications,uploaded_bytes,captures,retirements,budget_bytes;
} NcmaEnvironmentGpuStatsV1;
typedef struct NcmaEnvironmentGpuApiV1 {
    uint32_t struct_size,version;uint64_t capabilities;
    uint32_t (NCMA_CALL *publish)(uint64_t,uint64_t,NcmaGpuResourceV3,const NcmaEnvironmentGpuDescriptionV1*,NcmaGpuResourceV3*,NcmaErrorV1*);
    uint32_t (NCMA_CALL *destroy)(uint64_t,uint64_t,NcmaGpuResourceV3,NcmaErrorV1*);
    uint32_t (NCMA_CALL *capture)(uint64_t,uint64_t,NcmaGpuResourceV3,float*,uint32_t,NcmaErrorV1*);
    uint32_t (NCMA_CALL *stats)(uint64_t,uint64_t,NcmaEnvironmentGpuStatsV1*,NcmaErrorV1*);
    uint32_t (NCMA_CALL *validate_preparation)(uint64_t,uint64_t,NcmaErrorV1*);
} NcmaEnvironmentGpuApiV1;
#ifdef __cplusplus
}
static_assert(sizeof(NcmaEnvironmentGpuDescriptionV1)==40);
static_assert(sizeof(NcmaEnvironmentGpuStatsV1)==64);
static_assert(sizeof(NcmaEnvironmentGpuApiV1)==56);
#endif
#endif
