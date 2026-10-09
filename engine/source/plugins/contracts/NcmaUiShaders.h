#ifndef NCMA_UI_SHADERS_H
#define NCMA_UI_SHADERS_H
#include "NcmaShaderPipeline.h"
#ifdef __cplusplus
extern "C" {
#endif
/* Independent query11/API1. Fixed UI vertex52, Frame16 VS b0, PS Image t0/Linear s0,
   exact stage linkage/SV_TARGET0, ordered straight-alpha display-space draws.
   Owner/off-frame only. Source <=256KiB; pair bytecode 4..1MiB/stage copied during call.
   Create requires no UI kernel; replace requires existing kernel and no presentation leases.
   Candidate validation/allocation/drain precede atomic publication; failure retains old kernel.
   Images/lists/targets survive replacement. Cached target pixels remain their OLD content revision:
   host explicitly submits a new increasing revision to refresh. No automatic per-frame redraw.
   No new resource handle, allocator, World, inference or compile-on-tick authority. */
typedef uint32_t (NCMA_CALL *NcmaUiShadersSourceFnV1)(uint64_t,uint64_t,uint8_t*,uint32_t,uint32_t*,NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaUiShadersInstallFnV1)(uint64_t,uint64_t,const NcmaShaderPairV1*,NcmaErrorV1*);
typedef struct NcmaUiShadersApiV1 { uint32_t struct_size,version;uint64_t capabilities;
    NcmaUiShadersSourceFnV1 copy_source;
    NcmaUiShadersInstallFnV1 create,replace;
} NcmaUiShadersApiV1;
#ifdef __cplusplus
}
static_assert(sizeof(NcmaUiShadersApiV1)==40);
#endif
#endif
