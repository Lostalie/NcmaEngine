#ifndef NCMA_POSE_H
#define NCMA_POSE_H
#include "NcmaPlugin.h"
#if defined(_WIN32) && defined(NCMA_POSE_EXPORTS)
#define NCMA_POSE_API __declspec(dllexport)
#elif defined(_WIN32)
#define NCMA_POSE_API __declspec(dllimport)
#else
#define NCMA_POSE_API __attribute__((visibility("default")))
#endif
#ifdef __cplusplus
extern "C" {
#endif
#define NCMA_POSE_MAX_BONES 1024u
#define NCMA_POSE_MAX_REQUESTS 32u
#define NCMA_POSE_MAX_OUTPUT 32768u
typedef struct NcmaPoseTrsV1 { float position[3], rotation[4], scale[3]; } NcmaPoseTrsV1;
typedef struct NcmaPoseBoneV1 { int32_t parent; NcmaPoseTrsV1 local; } NcmaPoseBoneV1;
typedef struct NcmaPoseKeyV1 { double time; NcmaPoseTrsV1 local; } NcmaPoseKeyV1;
typedef struct NcmaPoseTrackV1 { uint32_t bone, first_key, key_count, reserved; } NcmaPoseTrackV1;
typedef struct NcmaPoseRequestV1 {
    uint64_t rig, clip; double previous_time, current_time; float alpha; uint32_t output_offset;
} NcmaPoseRequestV1;
typedef struct NcmaPoseMatrixV1 { float column_major[16]; } NcmaPoseMatrixV1;
typedef struct NcmaPoseStatsV1 {
    uint32_t struct_size, max_requests; uint64_t rigs, clips, retained_bytes, sample_calls, sampled_bones;
} NcmaPoseStatsV1;
/* Immutable numerical resources only. Explicit bounded times, never a World/tick/loop owner.
   Caller-owned copied outputs; local TRS slerp followed by parent composition (RH Y-up/metres).
   Uniform positive bone scale only; geometry bindings remain mesh-local and are not inferred.
   All operations owner-thread; resources close before context, context before unloading DLL.
   Sample validates the COMPLETE batch before publishing outputs/counters; no CPU vertices.
   No aliasing output/input buffers; managed adapters own/pin distinct scratch arrays. */
typedef struct NcmaPoseApiV1 {
    uint32_t struct_size, major, minor, max_bones;
    uint32_t (NCMA_CALL *create)(uint64_t*, NcmaErrorV1*);
    uint32_t (NCMA_CALL *close)(uint64_t, NcmaErrorV1*);
    uint32_t (NCMA_CALL *create_rig)(uint64_t, const NcmaPoseBoneV1*, uint32_t, uint64_t*, NcmaErrorV1*);
    uint32_t (NCMA_CALL *create_clip)(uint64_t, uint64_t, double, const NcmaPoseTrackV1*, uint32_t, const NcmaPoseKeyV1*, uint32_t, uint64_t*, NcmaErrorV1*);
    uint32_t (NCMA_CALL *release)(uint64_t, uint64_t, NcmaErrorV1*);
    uint32_t (NCMA_CALL *sample)(uint64_t, const NcmaPoseRequestV1*, uint32_t, NcmaPoseTrsV1*, NcmaPoseMatrixV1*, uint32_t, NcmaErrorV1*);
    uint32_t (NCMA_CALL *stats)(uint64_t, NcmaPoseStatsV1*, NcmaErrorV1*);
} NcmaPoseApiV1;
NCMA_POSE_API uint32_t NCMA_CALL ncma_pose_get_api(uint32_t, uint32_t, void*, uint32_t, NcmaErrorV1*);
#ifdef __cplusplus
}
static_assert(sizeof(NcmaPoseTrsV1)==40 && sizeof(NcmaPoseBoneV1)==44 && sizeof(NcmaPoseKeyV1)==48);
static_assert(sizeof(NcmaPoseTrackV1)==16 && sizeof(NcmaPoseRequestV1)==40 && sizeof(NcmaPoseMatrixV1)==64 && sizeof(NcmaPoseStatsV1)==48);
static_assert(sizeof(void*)!=8 || sizeof(NcmaPoseApiV1)==72);
#endif
#endif
