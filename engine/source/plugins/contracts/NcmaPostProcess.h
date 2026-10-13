#pragma once
#include "NcmaShaderPipeline.h"
/* Renderer query16 / API1. Owner thread, healthy/off-frame preparation only.
 * All input arrays/bytecode are copied during the call, never retained. No world/policy.
 * Input resource0 is the scene HDR; resources1..N are owned linear RGBA16F targets.
 * Each target has one writer, no read/write alias, ordered dependencies, final full size.
 * Zero/NULL binding removes the extension; no post targets/programs exist when Off.
 * Max8 programs/32 targets/32 passes/8MiB bytecode/128MiB aggregate targets.
 * Replacement validates/builds/drains before exchange. Destroy scene releases binding.
 */
typedef struct NcmaPostProgramV1 { NcmaShaderPairV1 shaders; uint32_t inputs, reserved; } NcmaPostProgramV1;
typedef struct NcmaPostTargetV1 { uint32_t width,height,format,color_space; } NcmaPostTargetV1;
typedef struct NcmaPostPassV1 { uint32_t program,input0,input1,output; float parameters[4]; } NcmaPostPassV1;
typedef struct NcmaPostDescriptionV1 {
    uint32_t struct_size,version,width,height,program_count,target_count,pass_count,output;
    const NcmaPostProgramV1* programs; const NcmaPostTargetV1* targets; const NcmaPostPassV1* passes;
} NcmaPostDescriptionV1;
typedef struct NcmaPostStatsV1 {
    uint32_t struct_size,version; uint64_t generation,bindings,targets,programs,resident_bytes,publications,draws,constant_upload_bytes;
} NcmaPostStatsV1;
typedef uint32_t (NCMA_CALL *NcmaValidatePostV1)(uint64_t,uint64_t,const NcmaPostProgramV1*,uint32_t,NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaBindPostV1)(uint64_t,uint64_t,NcmaGpuResourceV3,const NcmaPostDescriptionV1*,NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaReadPostStatsV1)(uint64_t,uint64_t,NcmaPostStatsV1*,NcmaErrorV1*);
typedef struct NcmaPostApiV1 {uint32_t struct_size,version;uint64_t capabilities;NcmaValidatePostV1 validate;NcmaBindPostV1 bind;NcmaReadPostStatsV1 stats;} NcmaPostApiV1;
#ifdef __cplusplus
static_assert(sizeof(NcmaPostProgramV1)==40&&sizeof(NcmaPostTargetV1)==16&&sizeof(NcmaPostPassV1)==32);
static_assert(sizeof(NcmaPostDescriptionV1)==56&&sizeof(NcmaPostStatsV1)==72&&sizeof(NcmaPostApiV1)==40);
#endif
