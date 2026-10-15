#ifndef NCMA_RENDERER_FAULT_H
#define NCMA_RENDERER_FAULT_H
#include "NcmaPlugin.h"
/* Optional read-only query18/v1. Copied owner-thread snapshot, no strings/pointers.
   First GPU failure per renderer is immutable, including recoverable wait failures.
   This is diagnostic history, NOT proof that a later exception has the same cause.
   handle=0 reads the most recent attempted device-startup failure (cleared on next
   admitted startup); nonzero requires the exact live renderer. Copy before unload.
   flags: has_failure=1, native_code_available=2, device_reason_available=4.
   backend1 native codes are signed HRESULT bits; absent values MUST remain zero.
   No synthesized HRESULT for timeouts, host exceptions or explicit abandonment.
   frame identifies CPU-side execution admission or last submitted frame at detection,
   NOT the causal GPU instruction or a resolved GPU timing sample's origin. */
typedef enum NcmaGpuFaultOperationV1 {
    NCMA_GPU_FAULT_NONE=0, NCMA_GPU_FAULT_STARTUP=1, NCMA_GPU_FAULT_NATIVE_API=2,
    NCMA_GPU_FAULT_WAIT_CREATE=3, NCMA_GPU_FAULT_WAIT_POLL=4, NCMA_GPU_FAULT_WAIT_TIMEOUT=5,
    NCMA_GPU_FAULT_DEVICE_REMOVED=6, NCMA_GPU_FAULT_PRESENT=7, NCMA_GPU_FAULT_READBACK=8,
    NCMA_GPU_FAULT_RESIZE=9, NCMA_GPU_FAULT_EXECUTION=10, NCMA_GPU_FAULT_ABANDON=11,
    NCMA_GPU_FAULT_TIMING=12
} NcmaGpuFaultOperationV1;
typedef struct NcmaRendererFaultV1 {
    uint32_t struct_size,version,flags,result;
    uint64_t generation,frame;
    uint32_t operation; int32_t native_code,device_reason; uint32_t backend;
} NcmaRendererFaultV1;
typedef uint32_t (NCMA_CALL *NcmaReadRendererFaultV1)(uint64_t,uint64_t,NcmaRendererFaultV1*,NcmaErrorV1*);
typedef struct NcmaRendererFaultApiV1 {
    uint32_t struct_size,version;uint64_t capabilities;NcmaReadRendererFaultV1 read;
} NcmaRendererFaultApiV1;
#ifdef __cplusplus
static_assert(sizeof(NcmaRendererFaultV1)==48&&sizeof(NcmaRendererFaultApiV1)==24);
#endif
#endif
