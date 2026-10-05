#ifndef NCMA_SCENE_RENDER_H
#define NCMA_SCENE_RENDER_H
#include "NcmaPlugin.h"
#ifdef __cplusplus
extern "C" {
#endif
/* Independent service v1: static, unlit mesh draws only. No UUID/scene/material ownership.
   Layout 1: LE float3 position, float3 unit normal, float2 UV, float4 unit tangent/sign;
   stride 48, uint32 triangle indices. Input pointers are borrowed only during create.
   Bounds: mesh 64MiB, renderer 256MiB geometry, 128 meshes, 4096 draws/frame.
   Handles are owner-renderer/device-generation scoped, never persistent IDs.
   MVP: RH asset space (+Y, -Z forward), column vectors, column-major, depth [0,1].
   Viewport: top-left framebuffer pixels. Unlit colors/clear are LINEAR; shader encodes sRGB.
   Preview is two-sided, depth tested/written; not PBR, alpha mask or GPU skinning.
   Batch validation precedes GPU work; execution failure is fail-stop. No automatic retries.
   Create is transactional; destroy waits (2s) and retains handle on timeout. */
typedef struct NcmaGpuMeshV1 { uint64_t value, generation; } NcmaGpuMeshV1;
typedef struct NcmaMeshDescriptionV1 {
    uint32_t struct_size, layout, vertex_count, index_count;
    uint32_t vertex_bytes, index_bytes, stride, reserved;
    const uint8_t* vertices;
    const uint32_t* indices;
} NcmaMeshDescriptionV1;
typedef struct NcmaMeshDrawV1 {
    NcmaGpuMeshV1 mesh;
    uint32_t first_index, index_count, reserved[2];
    float model_view_projection[16], color[4];
} NcmaMeshDrawV1;
typedef struct NcmaMeshFrameV1 {
    uint32_t struct_size, draw_count;
    uint64_t frame, generation;
    float viewport[4], clear[4];
    uint32_t reserved[2];
} NcmaMeshFrameV1;
typedef struct NcmaSceneRenderStatsV1 {
    uint32_t struct_size, reserved;
    uint64_t generation, live_meshes, resident_bytes, mesh_creates, uploaded_bytes, draws;
} NcmaSceneRenderStatsV1;
typedef uint32_t (NCMA_CALL *NcmaCreateMeshV1)(uint64_t, uint64_t, const NcmaMeshDescriptionV1*, NcmaGpuMeshV1*, NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaDestroyMeshV1)(uint64_t, uint64_t, NcmaGpuMeshV1, NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaSubmitMeshesV1)(uint64_t, uint64_t, const NcmaMeshFrameV1*, const NcmaMeshDrawV1*, NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaSceneRenderStatsFnV1)(uint64_t, uint64_t, NcmaSceneRenderStatsV1*, NcmaErrorV1*);
typedef struct NcmaSceneRenderApiV1 {
    uint32_t struct_size, version;
    uint64_t capabilities; /* bit 0: static-unlit-v1 only */
    NcmaCreateMeshV1 create_mesh;
    NcmaDestroyMeshV1 destroy_mesh;
    NcmaSubmitMeshesV1 submit_meshes;
    NcmaSceneRenderStatsFnV1 stats;
} NcmaSceneRenderApiV1;
/* Independent v2: preserves all v1 operations/layouts. Adds explicit CPU-baked bind-pose
   geometry, NOT GPU animated skinning. Layout2 stride80: first48 baked preview basis,
   uint16x4 ORIGINAL binding-palette joints at48, float4 weights at56, zero bytes72..79.
   Palette = bone-global-bind * geometry-to-bone (COLUMN vectors), column-major float16 per binding.
   mesh.struct_size=64; palette has 1..4096 finite affine matrices, palette_bytes=count*64.
   Native validates the palette and copies vertex/index data during create; never applies bind twice.
   Source skin/rig/UUID ownership stays managed. Dynamic palettes are NOT accepted by submit. */
typedef struct NcmaBindPoseMeshDescriptionV2 {
    NcmaMeshDescriptionV1 mesh;
    uint32_t palette_count, palette_bytes;
    const float* palette;
} NcmaBindPoseMeshDescriptionV2;
typedef uint32_t (NCMA_CALL *NcmaCreateBindPoseMeshV2)(uint64_t, uint64_t, const NcmaBindPoseMeshDescriptionV2*, NcmaGpuMeshV1*, NcmaErrorV1*);
typedef struct NcmaSceneRenderApiV2 {
    NcmaSceneRenderApiV1 base; /* size56/version2/capabilities3: static + CPU-baked bind pose */
    NcmaCreateBindPoseMeshV2 create_bind_pose_mesh;
} NcmaSceneRenderApiV2;
typedef uint32_t (NCMA_CALL *NcmaQuerySceneRenderV1)(uint64_t, uint32_t, void*, uint32_t, NcmaErrorV1*);
#ifdef __cplusplus
}
static_assert(sizeof(NcmaGpuMeshV1)==16);
static_assert(sizeof(NcmaMeshDescriptionV1)==48);
static_assert(sizeof(NcmaMeshDrawV1)==112);
static_assert(sizeof(NcmaMeshFrameV1)==64);
static_assert(sizeof(NcmaSceneRenderStatsV1)==56);
static_assert(sizeof(NcmaSceneRenderApiV1)==48);
static_assert(sizeof(NcmaBindPoseMeshDescriptionV2)==64);
static_assert(sizeof(NcmaSceneRenderApiV2)==56);
#endif
#endif
