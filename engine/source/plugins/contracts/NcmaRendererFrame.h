#ifndef NCMA_RENDERER_FRAME_H
#define NCMA_RENDERER_FRAME_H
#include "NcmaPlugin.h"
/* Optional query 17 / v1. Owner-thread only. No allocation or ownership transfer.
   Abandon requires the exact active frame and permanently stops this renderer.
   It closes timing/state WITHOUT Present, new draws, GPU waits or forced release.
   Existing leases still require their ordinary bounded retirement before unload. */
typedef struct NcmaRendererFrameStateV1 {
    uint32_t struct_size, version, flags, reserved; /* flags: active=1, fail-stop=2 */
    uint64_t generation, frame, abandoned_frames;
} NcmaRendererFrameStateV1;
typedef uint32_t (NCMA_CALL *NcmaReadRendererFrameV1)(uint64_t,uint64_t,NcmaRendererFrameStateV1*,NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaAbandonRendererFrameV1)(uint64_t,uint64_t,uint64_t,NcmaErrorV1*);
typedef struct NcmaRendererFrameApiV1 {
    uint32_t struct_size,version;uint64_t capabilities;
    NcmaReadRendererFrameV1 read;NcmaAbandonRendererFrameV1 abandon;
} NcmaRendererFrameApiV1;
#ifdef __cplusplus
static_assert(sizeof(NcmaRendererFrameStateV1)==40&&sizeof(NcmaRendererFrameApiV1)==32);
#endif
#endif
