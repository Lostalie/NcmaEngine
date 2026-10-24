#ifndef NCMA_RENDERER_TIMING_H
#define NCMA_RENDERER_TIMING_H
#include "NcmaPlugin.h"
/* Optional read-only query19/v1. Owner-thread, copied POD, caller-owned output.
   One bounded existing query slot. No waits/Flush: a read polls at most three
   queries with DONOTFLUSH. Busy frames increment skipped without replacing origin.
   Sequence increments on admitted samples, per device; identify with generation.
   Domain1: main submission commands through the timestamp BEFORE Present; excludes
   earlier skin/offscreen production/uploads, CPU time and display/VSync wait.
   observed_frame is the last successfully submitted frame, NOT sample origin.
   Only Valid has meaningful milliseconds; every other state returns zero.
   Last result is retained until another sample starts, not relabeled current.
   Query failure disables new sampling for this device (rendering may continue).
   Never reuse a query whose result retrieval failed; a new device resets it.
   Read after device destruction rejects; copied results survive module unload. */
typedef enum NcmaGpuSampleStateV1 {
    NCMA_GPU_SAMPLE_UNAVAILABLE=0, NCMA_GPU_SAMPLE_RECORDING=1,
    NCMA_GPU_SAMPLE_PENDING=2, NCMA_GPU_SAMPLE_VALID=3,
    NCMA_GPU_SAMPLE_DISJOINT=4, NCMA_GPU_SAMPLE_INVALID=5,
    NCMA_GPU_SAMPLE_FAILED=6, NCMA_GPU_SAMPLE_ABANDONED=7
} NcmaGpuSampleStateV1;
typedef struct NcmaGpuSampleV1 {
    uint32_t struct_size,version,state,flags;
    uint64_t generation,sequence,origin_frame,observed_frame,skipped;
    double milliseconds;
    uint32_t domain,reserved;
} NcmaGpuSampleV1;
typedef uint32_t (NCMA_CALL *NcmaReadGpuSampleV1)(uint64_t,uint64_t,NcmaGpuSampleV1*,NcmaErrorV1*);
typedef struct NcmaRendererTimingApiV1 {
    uint32_t struct_size,version;uint64_t capabilities;NcmaReadGpuSampleV1 read;
} NcmaRendererTimingApiV1;
/* Optional query20/v1: domain2 skin dispatch, domain3 offscreen UI production.
   Separate one-slot timing owners, <=3 DONOTFLUSH polls per read, no allocation.
   Skin scope excludes palette upload; UI scope is one target production, not the
   subsequent cached presentation. Both exclude main submission/Present.
   resource/content_revision are 0 for a skin batch; UI copies target token and
   content revision. Metadata does not pin resources or authorize access.
   Sequence/skipped persist across kernel replacement within the same device.
   Query failure quarantines that timing owner until explicit owner retirement.
   Buffer completion fences remain independent of timing availability.
   Missing owners read Unavailable (or retained Abandoned after owner retirement),
   and querying never initializes skin/UI resources. No domain overlap summation. */
typedef struct NcmaGpuWorkSampleV1 {
    NcmaGpuSampleV1 timing;
    uint64_t resource,content_revision;
} NcmaGpuWorkSampleV1;
typedef uint32_t (NCMA_CALL *NcmaReadGpuWorkSampleV1)(uint64_t,uint64_t,uint32_t,NcmaGpuWorkSampleV1*,NcmaErrorV1*);
typedef struct NcmaRendererWorkTimingApiV1 {
    uint32_t struct_size,version;uint64_t capabilities;NcmaReadGpuWorkSampleV1 read;
} NcmaRendererWorkTimingApiV1;
/* Optional query21/v1, domains1/2/3. Current slot AND last resolved result in
   one owner-thread snapshot, <=3 DONOTFLUSH polls total, no allocation/wait.
   Queries19/20 keep their current-slot semantics.
   last_resolved is Unavailable until a query resolves as Valid/Disjoint/Invalid;
   includes invalid resolutions, never silently substitutes an earlier valid one.
   Failed/Abandoned remain visible in current; do not erase the historical result.
   One copied result per domain, NOT a history or a promise of complete coverage.
   A new Begin cannot erase it; another resolution replaces it. Consumers dedup
   by generation/domain/sequence, retain source metadata, and report gaps.
   Retirement of a skin/UI owner retains the copy but grants no resource access.
   New renderer generation resets both slots. Both observed_frame fields describe
   the read horizon, not completion time. Main resource/content_revision are zero. */
typedef struct NcmaGpuTimingSnapshotV1 {
    NcmaGpuWorkSampleV1 current,last_resolved;
} NcmaGpuTimingSnapshotV1;
typedef uint32_t (NCMA_CALL *NcmaReadGpuTimingSnapshotV1)(uint64_t,uint64_t,uint32_t,NcmaGpuTimingSnapshotV1*,NcmaErrorV1*);
typedef struct NcmaRendererTimingSnapshotApiV1 {
    uint32_t struct_size,version;uint64_t capabilities;NcmaReadGpuTimingSnapshotV1 read;
} NcmaRendererTimingSnapshotApiV1;
#ifdef __cplusplus
static_assert(sizeof(NcmaGpuSampleV1)==72&&sizeof(NcmaRendererTimingApiV1)==24);
static_assert(sizeof(NcmaGpuWorkSampleV1)==88&&sizeof(NcmaRendererWorkTimingApiV1)==24);
static_assert(sizeof(NcmaGpuTimingSnapshotV1)==176&&sizeof(NcmaRendererTimingSnapshotApiV1)==24);
#endif
#endif
