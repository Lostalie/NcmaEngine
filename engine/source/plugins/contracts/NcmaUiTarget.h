#ifndef NCMA_UI_TARGET_H
#define NCMA_UI_TARGET_H
#include "NcmaUiRender.h"
#ifdef __cplusplus
extern "C" {
#endif
/* Renderer query7, independent API1. Color-only RGBA8 UI targets; no scene kernels/depth.
   Owner thread; max4 targets/64 presentation leases. Combined UI resident budget128MiB,
   dimension1..4096. Produce BEFORE the main frame: no Present/frame activation here.
   content_revision is nonzero and strictly increasing per target; unchanged contents need
   no submit. Acquire creates an opaque lease for ONE upcoming presentation frame, retaining
   exact target/content/produced frame. GUI1.6 CachedImage pins that lease until render/discard.
   Target writes/deletion reject live leases; lease release rejects GUI pins. No hidden retry.
   Destroy waits at most2s and retains ownership on failure. Capture is a test-only GPU readback.
   Query1..6 and GUI1.3 exact-frame images remain unchanged. */
typedef struct NcmaUiTargetDescriptionV1 { uint32_t struct_size,width,height,reserved; } NcmaUiTargetDescriptionV1;
typedef struct NcmaUiTargetFrameV1 { uint32_t struct_size,reserved; uint64_t frame,content_revision;
    NcmaUiKeyV1 target,list; float clear[4]; } NcmaUiTargetFrameV1;
typedef struct NcmaUiTargetStatsV1 { uint32_t struct_size,reserved; uint64_t generation,targets,leases,resident_bytes,productions,presentations; } NcmaUiTargetStatsV1;
typedef uint32_t (NCMA_CALL *NcmaUiCreateTargetFnV1)(uint64_t,uint64_t,const NcmaUiTargetDescriptionV1*,NcmaUiKeyV1*,NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaUiTargetSubmitFnV1)(uint64_t,uint64_t,const NcmaUiTargetFrameV1*,NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaUiTargetCaptureFnV1)(uint64_t,uint64_t,NcmaUiKeyV1,uint8_t*,uint32_t,NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaUiTargetAcquireFnV1)(uint64_t,uint64_t,NcmaUiKeyV1,uint64_t,uint64_t,NcmaUiKeyV1*,NcmaErrorV1*);
typedef struct NcmaUiTargetApiV1 { uint32_t struct_size,version; uint64_t capabilities;
    NcmaUiCreateTargetFnV1 create_target; NcmaUiDestroyFnV1 destroy_target;
    NcmaUiTargetSubmitFnV1 submit; NcmaUiTargetCaptureFnV1 capture;
    NcmaUiTargetAcquireFnV1 acquire; NcmaUiDestroyFnV1 release;
    uint32_t (NCMA_CALL *stats)(uint64_t,uint64_t,NcmaUiTargetStatsV1*,NcmaErrorV1*);
} NcmaUiTargetApiV1;
#ifdef __cplusplus
}
static_assert(sizeof(NcmaUiTargetDescriptionV1)==16);
static_assert(sizeof(NcmaUiTargetFrameV1)==72);
static_assert(sizeof(NcmaUiTargetStatsV1)==56);
static_assert(sizeof(NcmaUiTargetApiV1)==72);
#endif
#endif
