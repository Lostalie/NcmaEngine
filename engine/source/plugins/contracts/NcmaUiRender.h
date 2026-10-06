#ifndef NCMA_UI_RENDER_H
#define NCMA_UI_RENDER_H
#include "NcmaRenderer.h"
#ifdef __cplusplus
extern "C" {
#endif
/* Independent renderer query 6, UI API 1. Resident, ordered display lists; no World/layout/text
   policy or ImGui dependency. Renderer/device generations scope every resource. UI-only create
   disables default depth and initializes no PBR/shadow/skin resources. No per-frame vertex upload.
   Owner thread only. Bounded copied create inputs, immutable handles, texture pins by display list.
   Destroy is wait-idle off-frame (2s timeout retains ownership). Present closes each increasing
   frame. A failed submit fail-stops. Colors are sRGB-display values with straight alpha blending;
   do not feed linear PBR textures. Max 4096 image dimension,64MiB image,128MiB resident,64 lists,
   128 images,65536 vertices/list and4096 ordered batches/list. Rotation is baked by C#.
   Axis-aligned clip and rounded rectangle; rotated clip explicitly rejected by managed layout. */
typedef struct NcmaUiKeyV1 { uint64_t value, generation; } NcmaUiKeyV1;
typedef struct NcmaUiVertexV1 { float position[2], uv[2], color[4], local[2], extent[2], radius; } NcmaUiVertexV1;
typedef struct NcmaUiBatchV1 { uint32_t first_vertex, vertex_count; NcmaUiKeyV1 image; float clip[4]; } NcmaUiBatchV1;
typedef struct NcmaUiImageV1 { uint32_t struct_size, width, height, byte_count; const uint8_t* pixels; } NcmaUiImageV1;
typedef struct NcmaUiListV1 { uint32_t struct_size, vertex_count, batch_count, reserved; const NcmaUiVertexV1* vertices; const NcmaUiBatchV1* batches; } NcmaUiListV1;
typedef struct NcmaUiFrameV1 { uint32_t struct_size, overlay; uint64_t frame; NcmaUiKeyV1 list; float clear[4]; } NcmaUiFrameV1;
typedef struct NcmaUiStatsV1 { uint32_t struct_size, pure_ui; uint64_t generation, images, lists, resident_bytes, uploaded_bytes, draws, submits; } NcmaUiStatsV1;
typedef uint32_t (NCMA_CALL *NcmaUiCreateImageFnV1)(uint64_t,uint64_t,const NcmaUiImageV1*,NcmaUiKeyV1*,NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaUiCreateListFnV1)(uint64_t,uint64_t,const NcmaUiListV1*,NcmaUiKeyV1*,NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaUiDestroyFnV1)(uint64_t,uint64_t,NcmaUiKeyV1,NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaUiSubmitFnV1)(uint64_t,uint64_t,const NcmaUiFrameV1*,NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaUiStatsFnV1)(uint64_t,uint64_t,NcmaUiStatsV1*,NcmaErrorV1*);
typedef struct NcmaUiApiV1 { uint32_t struct_size, version; uint64_t capabilities;
    NcmaCreateRendererV1 create_ui_renderer;
    NcmaUiCreateImageFnV1 create_image;
    NcmaUiCreateListFnV1 create_list;
    NcmaUiDestroyFnV1 destroy;
    NcmaUiSubmitFnV1 submit;
    NcmaUiStatsFnV1 stats;
} NcmaUiApiV1;
#ifdef __cplusplus
}
static_assert(sizeof(NcmaUiVertexV1)==52);
static_assert(sizeof(NcmaUiBatchV1)==40);
static_assert(sizeof(NcmaUiImageV1)==24);
static_assert(sizeof(NcmaUiListV1)==32);
static_assert(sizeof(NcmaUiFrameV1)==48);
static_assert(sizeof(NcmaUiStatsV1)==64);
static_assert(sizeof(NcmaUiApiV1)==64);
#endif
#endif
