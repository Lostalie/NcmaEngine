#ifndef NCMA_RESOURCE_RENDER_H
#define NCMA_RESOURCE_RENDER_H
#include "NcmaSceneRender.h"
#ifdef __cplusplus
extern "C" {
#endif
/* Independent scene-render service v3, frozen v1/v2 preserved. Owner thread, borrowed
   create/submit buffers, generation-scoped handles. Texture full RGBA8 mip chain,
   format1=linear/2=sRGB; <=64MiB each, <=256MiB texture+target, <=256 textures,
   <=256 materials, <=16 targets. Destroy rejects material-pinned textures/active frames.
   Opaque/AlphaMask two-sided GGX/Schlick PBR, no shadows/IBL/animation.
   target{0,0}=swapchain. Capture is test-only, never a production frame requirement. */
typedef NcmaGpuMeshV1 NcmaGpuResourceV3;
typedef struct NcmaTextureMipV3 {uint32_t width,height,offset,row_pitch;} NcmaTextureMipV3;
typedef struct NcmaTextureDescriptionV3 {
    uint32_t struct_size,width,height,format,mip_count,data_bytes,reserved[2];
    const NcmaTextureMipV3* mips;const uint8_t* pixels;
} NcmaTextureDescriptionV3;
typedef struct NcmaMaterialDescriptionV3 {
    uint32_t struct_size,flags; /* bit0 alpha-mask, bit1 normal-map, bit2 normalYDown */
    NcmaGpuResourceV3 textures[6]; /* baseColor,normal,metallic,roughness,AO,emissive */
    float base_color[4],emissive[4],surface[4]; /* metallic,roughness,normalScale,alphaCutoff */
    uint32_t channels[2]; /* packed data channel metal/rough/AO in bytes0..2, reserved zero */
} NcmaMaterialDescriptionV3;
typedef struct NcmaTargetDescriptionV3 {uint32_t struct_size,width,height,reserved;} NcmaTargetDescriptionV3;
typedef struct NcmaResourceDrawV3 {
    NcmaGpuMeshV1 mesh;NcmaGpuResourceV3 material;
    uint32_t first_index,index_count,reserved[2];
    float model[16],model_view_projection[16],normal_matrix[16];
} NcmaResourceDrawV3;
typedef struct NcmaResourceFrameV3 {
    uint32_t struct_size,draw_count;uint64_t frame,generation;NcmaGpuResourceV3 target;
    float viewport[4],clear[4],camera[4],light_direction[4],light_color[4];
    float exposure,ambient;uint32_t mode,reserved; /* mode0 PBR,1 unlit,2 normal diagnostic */
} NcmaResourceFrameV3;
typedef struct NcmaResourceStatsV3 {
    uint32_t struct_size,reserved;uint64_t generation,textures,materials,targets,resident_bytes,uploaded_bytes,creates,draws;
} NcmaResourceStatsV3;
typedef uint32_t (NCMA_CALL *NcmaCreateTextureV3)(uint64_t,uint64_t,const NcmaTextureDescriptionV3*,NcmaGpuResourceV3*,NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaCreateMaterialV3)(uint64_t,uint64_t,const NcmaMaterialDescriptionV3*,NcmaGpuResourceV3*,NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaCreateTargetV3)(uint64_t,uint64_t,const NcmaTargetDescriptionV3*,NcmaGpuResourceV3*,NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaDestroyResourceV3)(uint64_t,uint64_t,NcmaGpuResourceV3,NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaSubmitResourcesV3)(uint64_t,uint64_t,const NcmaResourceFrameV3*,const NcmaResourceDrawV3*,NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaResourceStatsFnV3)(uint64_t,uint64_t,NcmaResourceStatsV3*,NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaCaptureTargetV3)(uint64_t,uint64_t,NcmaGpuResourceV3,uint8_t*,uint32_t,NcmaErrorV1*);
typedef struct NcmaResourceRenderApiV3 {
    uint32_t struct_size,version;uint64_t capabilities; /* 0x3c textures/material/targets/typedPBR */
    NcmaCreateTextureV3 create_texture;NcmaCreateMaterialV3 create_material;NcmaCreateTargetV3 create_target;
    NcmaDestroyResourceV3 destroy_resource;NcmaSubmitResourcesV3 submit;NcmaResourceStatsFnV3 stats;NcmaCaptureTargetV3 capture_target;
} NcmaResourceRenderApiV3;
#ifdef __cplusplus
}
static_assert(sizeof(NcmaTextureDescriptionV3)==48);
static_assert(sizeof(NcmaMaterialDescriptionV3)==160);
static_assert(sizeof(NcmaResourceDrawV3)==240);
static_assert(sizeof(NcmaResourceFrameV3)==136);
static_assert(sizeof(NcmaResourceStatsV3)==72);
static_assert(sizeof(NcmaResourceRenderApiV3)==72);
#endif
#endif
