#ifndef NCMA_RENDERER_H
#define NCMA_RENDERER_H
#include "NcmaPlugin.h"
#include "NcmaSceneRender.h"
#ifdef __cplusplus
extern "C" {
#endif
#define NCMA_RENDER_MAX_PASSES 64u
/* v1 bounded operation set, not an arbitrary RHI or a production scene pipeline. */
typedef enum NcmaRenderOperation {
    NCMA_RENDER_SHADOW = 1, NCMA_RENDER_GEOMETRY = 2, NCMA_RENDER_TONEMAP = 3, NCMA_RENDER_CLEAR = 4
} NcmaRenderOperation;
typedef struct NcmaRendererDescriptionV1 {
    uint32_t struct_size, backend, validation, vsync; /* backend 1=DX11; 2=Vulkan rejected */
    uint64_t platform_module, window;
    uint32_t width, height, reserved[2];
} NcmaRendererDescriptionV1;
typedef struct NcmaRenderFrameV1 {
    uint32_t struct_size, pass_count;
    uint64_t frame;
    float model[16]; /* Eigen/HLSL column-major, column vectors, LH metres, +Y up */
    float viewport[4]; /* framebuffer pixels top-left x,y,width,height */
    float exposure, metallic, roughness, reserved;
} NcmaRenderFrameV1;
typedef struct NcmaRenderPassV1 {
    uint32_t operation, shader_contract; /* 1=reference.pbr.v1, 2=reference.tonemap.v1, 0=clear */
    uint64_t resources; /* renderer-owned group handle, not a serialized UUID */
    float color[4]; /* clear only, finite linear RGBA [0,1] */
} NcmaRenderPassV1;
typedef struct NcmaRendererStatsV1 {
    uint32_t struct_size, state, width, height;
    uint64_t submitted_frames, presents, live_groups, validation_errors, validation_warnings;
    double submit_ms, present_ms, gpu_ms;
    uint32_t gpu_sample_valid, reserved;
} NcmaRendererStatsV1;
typedef uint32_t (NCMA_CALL *NcmaCreateRendererV1)(uint64_t, const NcmaRendererDescriptionV1*, uint64_t*, NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaCreateRenderResourcesV1)(uint64_t, uint64_t, uint64_t*, NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaDestroyRenderResourcesV1)(uint64_t, uint64_t, uint64_t, NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaSubmitRenderV1)(uint64_t, uint64_t, const NcmaRenderFrameV1*, const NcmaRenderPassV1*, NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaPresentRenderV1)(uint64_t, uint64_t, NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaResizeRenderV1)(uint64_t, uint64_t, uint32_t, uint32_t, NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaReadRenderStatsV1)(uint64_t, uint64_t, NcmaRendererStatsV1*, NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaWaitRenderV1)(uint64_t, uint64_t, NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaDestroyRendererV1)(uint64_t, uint64_t, NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaCaptureRenderV1)(uint64_t, uint64_t, uint8_t*, uint32_t, uint32_t*, NcmaErrorV1*);
typedef struct NcmaRendererApiV1 {
    NcmaModuleApiV1 module;
    NcmaCreateRendererV1 create_renderer;
    NcmaCreateRenderResourcesV1 create_resources;
    NcmaDestroyRenderResourcesV1 destroy_resources;
    NcmaSubmitRenderV1 submit;
    NcmaPresentRenderV1 present;
    NcmaResizeRenderV1 resize;
    NcmaReadRenderStatsV1 stats;
    NcmaWaitRenderV1 wait_idle;
    NcmaDestroyRendererV1 destroy_renderer;
    NcmaCaptureRenderV1 capture;
} NcmaRendererApiV1;
/* Renderer ABI 1.1: fixed-resolution reference uniforms, no scene/material asset ownership. */
typedef struct NcmaReferenceSettingsV1 {
    uint32_t struct_size, shadow_enabled, shadow_filter, contact_enabled, contact_steps, reserved;
    float base_red, base_green, base_blue, light_intensity, ambient;
    float constant_bias, slope_bias, max_distance, cascade_lambda, light_radius;
    float contact_distance, contact_thickness, contact_strength;
} NcmaReferenceSettingsV1;
typedef uint32_t (NCMA_CALL *NcmaConfigureReferenceV1)(uint64_t, uint64_t, uint64_t, const NcmaReferenceSettingsV1*, NcmaErrorV1*);
typedef struct NcmaRendererApiV1_1 { NcmaRendererApiV1 base; NcmaConfigureReferenceV1 configure_reference; } NcmaRendererApiV1_1;
/* Additive ABI 1.2: query an independent bounded static scene-render service. */
typedef struct NcmaRendererApiV1_2 { NcmaRendererApiV1_1 base; NcmaQuerySceneRenderV1 query_scene_render; } NcmaRendererApiV1_2;
/* Resource create is transactional. Full batch is validated BEFORE GPU mutation; synchronous
   input copying/encoding does NOT imply GPU completion. Failures after execution starts fail-stop.
   Destroy/resize wait for GPU completion (bounded 2s event query, timeout retains resources).
   Single submit/present per increasing frame; GUI composites between submit/present.
   Capture is copied row-major RGBA8 UNORM, sRGB-display-encoded by reference tonemap; before Present.
   Max framebuffer 4096x4096, group count 8, passes 64. No pointers to scene/World/COM cross ABI. */
#ifdef __cplusplus
}
static_assert(sizeof(NcmaRendererDescriptionV1)==48);
static_assert(sizeof(NcmaRenderFrameV1)==112);
static_assert(sizeof(NcmaRenderPassV1)==32);
static_assert(sizeof(NcmaRendererStatsV1)==88);
static_assert(sizeof(NcmaRendererApiV1)==136);
static_assert(sizeof(NcmaReferenceSettingsV1)==76);
static_assert(sizeof(NcmaRendererApiV1_1)==144);
static_assert(sizeof(NcmaRendererApiV1_2)==152);
#endif
#endif
