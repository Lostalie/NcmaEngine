#ifndef NCMA_POSE_BLEND_H
#define NCMA_POSE_BLEND_H
#include "NcmaPose.h"
#ifdef __cplusplus
extern "C" {
#endif
/* Independent optional numerical extension. Original pose1.0 table/requests/stats are frozen.
   Uses the SAME owner-thread context/rig handles. No graph, clock, World or resource ownership.
   Input local TRS are copied numerics, at most65536; outputs contiguous, at most32768.
   All requests/referenced TRS validated and scratch composed before ANY output/counter publication.
   Distinct request/input/output buffers; sourceA/sourceB may share immutable input rows.
   No additional resources; release clips/rigs/context through original pose API before unload. */
typedef struct NcmaPoseBlendRequestV1 {
    uint64_t rig;
    uint32_t source_a, source_b, output_offset, reserved0;
    float weight; uint32_t reserved1;
} NcmaPoseBlendRequestV1;
typedef struct NcmaPoseBlendStatsV1 {
    uint32_t struct_size, max_requests; uint64_t blend_calls, blended_bones;
} NcmaPoseBlendStatsV1;
typedef struct NcmaPoseBlendApiV1 {
    uint32_t struct_size, major, minor, max_requests;
    uint32_t (NCMA_CALL *blend)(uint64_t, const NcmaPoseBlendRequestV1*, uint32_t,
        const NcmaPoseTrsV1*, uint32_t, NcmaPoseTrsV1*, NcmaPoseMatrixV1*, uint32_t, NcmaErrorV1*);
    uint32_t (NCMA_CALL *stats)(uint64_t, NcmaPoseBlendStatsV1*, NcmaErrorV1*);
} NcmaPoseBlendApiV1;
NCMA_POSE_API uint32_t NCMA_CALL ncma_pose_get_blend_api(uint32_t, uint32_t, void*, uint32_t, NcmaErrorV1*);
#ifdef __cplusplus
}
static_assert(sizeof(NcmaPoseBlendRequestV1)==32 && sizeof(NcmaPoseBlendStatsV1)==24);
static_assert(sizeof(void*)!=8 || sizeof(NcmaPoseBlendApiV1)==32);
#endif
#endif
