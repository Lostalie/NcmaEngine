#ifndef NCMA_SKIN_H
#define NCMA_SKIN_H
#include "NcmaSceneRender.h"
#ifdef __cplusplus
extern "C" {
#endif
/* Renderer query 5, independent GPU numerical service. Frozen queries 1..4 unchanged.
   Source layout 3: original p/n/uv/tangent (48), uint4 mesh-local joints (16), float4 weights (16).
   GPU output layout 1 (48) is shared by geometry/shadow; no CPU-skinned vertex upload.
   Owner renderer/thread, <=32 requests/32768 palette entries; <=1024 bindings per instance.
   Complete preflight before dispatch, positive affine palettes and inverse-transpose normals.
   Three bounded default-memory palette slots; unfinished GPU slots return BUSY without mutation.
   Update once for a new frame; query-4 scene draws require output from that exact frame.
   Capture is diagnostic-only, copied caller output. Destroy before renderer/unload; wait failure retains.
   Source/output buffers remain GPU resident. No World/clock/asset identity crosses this ABI. */
typedef struct NcmaSkinMeshV5 { NcmaMeshDescriptionV1 mesh; uint32_t binding_count, reserved; } NcmaSkinMeshV5;
typedef struct NcmaSkinPaletteV5 { float model[16], normal[16]; } NcmaSkinPaletteV5;
typedef struct NcmaSkinRequestV5 { NcmaGpuMeshV1 mesh; uint32_t palette_offset, palette_count; } NcmaSkinRequestV5;
typedef struct NcmaSkinBatchV5 { uint32_t struct_size, request_count; uint64_t frame, generation; uint32_t palette_count, reserved; } NcmaSkinBatchV5;
typedef struct NcmaSkinStatsV5 {
 uint32_t struct_size, max_requests; uint64_t generation, meshes, resident_bytes, creates, batches, palette_bytes, vertices, captures;
 double cpu_ms, gpu_ms; uint32_t gpu_sample_valid, reserved;
} NcmaSkinStatsV5;
typedef struct NcmaSkinApiV5 {
 uint32_t struct_size, version; uint64_t capabilities;
 uint32_t (NCMA_CALL *create)(uint64_t,uint64_t,const NcmaSkinMeshV5*,NcmaGpuMeshV1*,NcmaErrorV1*);
 uint32_t (NCMA_CALL *destroy)(uint64_t,uint64_t,NcmaGpuMeshV1,NcmaErrorV1*);
 uint32_t (NCMA_CALL *update)(uint64_t,uint64_t,const NcmaSkinBatchV5*,const NcmaSkinRequestV5*,const NcmaSkinPaletteV5*,NcmaErrorV1*);
 uint32_t (NCMA_CALL *capture)(uint64_t,uint64_t,NcmaGpuMeshV1,uint8_t*,uint32_t,NcmaErrorV1*);
 uint32_t (NCMA_CALL *stats)(uint64_t,uint64_t,NcmaSkinStatsV5*,NcmaErrorV1*);
} NcmaSkinApiV5;
#ifdef __cplusplus
}
static_assert(sizeof(NcmaSkinMeshV5)==56 && sizeof(NcmaSkinPaletteV5)==128 && sizeof(NcmaSkinRequestV5)==24);
static_assert(sizeof(NcmaSkinBatchV5)==32 && sizeof(NcmaSkinStatsV5)==96 && sizeof(NcmaSkinApiV5)==56);
#endif
#endif
