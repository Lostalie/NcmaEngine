#ifndef NCMA_PHYSICS_H
#define NCMA_PHYSICS_H
#include "NcmaPlugin.h"
#ifdef __cplusplus
extern "C" {
#endif
#define NCMA_MAX_PHYSICS_BATCH 4096u
#define NCMA_MAX_PHYSICS_WORLDS 16u
#define NCMA_PHYSICS_CAPABILITIES 63ull /* boxes, velocity, step, copied states, 2D, 3D only */
typedef struct NcmaPhysicsWorldDescriptionV1 {
    uint32_t struct_size, dimension, max_bodies, sub_steps;
    float gravity[3]; uint32_t reserved;
} NcmaPhysicsWorldDescriptionV1;
typedef struct NcmaPhysicsBox2DV1 {
    float position[2], half_extents[2], density;
    uint32_t dynamic_body; uint64_t correlation;
} NcmaPhysicsBox2DV1;
typedef struct NcmaPhysicsBox3DV1 {
    float position[3], half_extents[3], density;
    uint32_t dynamic_body; uint64_t correlation;
} NcmaPhysicsBox3DV1;
typedef struct NcmaPhysicsVelocity2DV1 { uint64_t body; float velocity[2]; } NcmaPhysicsVelocity2DV1;
typedef struct NcmaPhysicsVelocity3DV1 { uint64_t body; float velocity[3]; uint32_t reserved; } NcmaPhysicsVelocity3DV1;
typedef struct NcmaPhysicsBodyState2DV1 {
    uint64_t body, correlation, sequence;
    float position[2], velocity[2], angle; uint32_t reserved;
} NcmaPhysicsBodyState2DV1;
typedef struct NcmaPhysicsBodyState3DV1 {
    uint64_t body, correlation, sequence;
    float position[3], velocity[3], rotation[4];
} NcmaPhysicsBodyState3DV1;
typedef struct NcmaPhysicsStatsV1 {
    uint32_t struct_size, state, dimension, live_worlds; /* state 1 ready, 2 faulted */
    uint64_t live_bodies, sequence, batches, copied_bytes, errors;
    double step_median_ms, step_p95_ms, step_max_ms;
    uint64_t live_jobs; /* outstanding jobs, not idle worker threads */
} NcmaPhysicsStatsV1;
/* v1.1 raw sample: aggregation/scheduling policy belongs to the managed service. */
typedef struct NcmaPhysicsCountersV1 {
    uint32_t struct_size, state, dimension, live_worlds;
    uint64_t live_bodies, sequence, batches, copied_bytes, errors, live_jobs;
    double last_step_ms;
} NcmaPhysicsCountersV1;
/* Counts/capacities are elements, bounded at 4096. Inputs are borrowed for the call only.
   metres, seconds, kg/m^2 (2D), kg/m^3 (3D), radians/quaternion xyzw.
   Owner-thread only. max 16 worlds, 4096 bodies/world. dt (0,1/4], substeps 1..16.
   Handles never reused while loaded; owner world/dimension checked. No persistence.
   Create preflights capacity and rolls back newly created bodies on failure.
   Destroy/Set validate every target and reject duplicates before writing.
   Step validates sequence output before mutation. Read takes exact committed sequence;
   small output/stale ticket never steps. Solver failure is fail-stop, NOT rollback.
   Faulted worlds permit stats/destroy_world only. World destruction drains synchronous
   solver work; shutdown busy while worlds remain. No callbacks, scene pointers or hot unload. */
typedef uint32_t (NCMA_CALL *NcmaPhysicsCreateWorldV1)(uint64_t,const NcmaPhysicsWorldDescriptionV1*,uint64_t*,NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaPhysicsCreateBoxes2DV1)(uint64_t,uint64_t,const NcmaPhysicsBox2DV1*,uint32_t,uint64_t*,uint32_t,NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaPhysicsCreateBoxes3DV1)(uint64_t,uint64_t,const NcmaPhysicsBox3DV1*,uint32_t,uint64_t*,uint32_t,NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaPhysicsDestroyBodiesV1)(uint64_t,uint64_t,const uint64_t*,uint32_t,NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaPhysicsSetVelocities2DV1)(uint64_t,uint64_t,const NcmaPhysicsVelocity2DV1*,uint32_t,NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaPhysicsSetVelocities3DV1)(uint64_t,uint64_t,const NcmaPhysicsVelocity3DV1*,uint32_t,NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaPhysicsStepV1)(uint64_t,uint64_t,float,uint64_t*,NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaPhysicsReadStates2DV1)(uint64_t,uint64_t,uint64_t,const uint64_t*,uint32_t,NcmaPhysicsBodyState2DV1*,uint32_t,NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaPhysicsReadStates3DV1)(uint64_t,uint64_t,uint64_t,const uint64_t*,uint32_t,NcmaPhysicsBodyState3DV1*,uint32_t,NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaPhysicsGetStatsV1)(uint64_t,uint64_t,NcmaPhysicsStatsV1*,NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaPhysicsDestroyWorldV1)(uint64_t,uint64_t,NcmaErrorV1*);
typedef struct NcmaPhysicsApiV1 {
    NcmaModuleApiV1 module;
    NcmaPhysicsCreateWorldV1 create_world;
    NcmaPhysicsCreateBoxes2DV1 create_boxes_2d;
    NcmaPhysicsCreateBoxes3DV1 create_boxes_3d;
    NcmaPhysicsDestroyBodiesV1 destroy_bodies;
    NcmaPhysicsSetVelocities2DV1 set_velocities_2d;
    NcmaPhysicsSetVelocities3DV1 set_velocities_3d;
    NcmaPhysicsStepV1 step;
    NcmaPhysicsReadStates2DV1 read_states_2d;
    NcmaPhysicsReadStates3DV1 read_states_3d;
    NcmaPhysicsGetStatsV1 stats;
    NcmaPhysicsDestroyWorldV1 destroy_world;
} NcmaPhysicsApiV1;
typedef uint32_t (NCMA_CALL *NcmaPhysicsReadCountersV1)(uint64_t,uint64_t,NcmaPhysicsCountersV1*,NcmaErrorV1*);
typedef struct NcmaPhysicsApiV1_1 {
    NcmaPhysicsApiV1 base;
    NcmaPhysicsReadCountersV1 read_counters;
} NcmaPhysicsApiV1_1;
#ifdef __cplusplus
}
static_assert(sizeof(NcmaPhysicsCountersV1)==72);
static_assert(sizeof(void*)!=8 || (sizeof(NcmaPhysicsApiV1_1)==152 && offsetof(NcmaPhysicsApiV1_1,read_counters)==144));
static_assert(sizeof(NcmaPhysicsWorldDescriptionV1)==32);
static_assert(sizeof(NcmaPhysicsBox2DV1)==32 && sizeof(NcmaPhysicsBox3DV1)==40);
static_assert(sizeof(NcmaPhysicsVelocity2DV1)==16 && sizeof(NcmaPhysicsVelocity3DV1)==24);
static_assert(sizeof(NcmaPhysicsBodyState2DV1)==48 && sizeof(NcmaPhysicsBodyState3DV1)==64);
static_assert(sizeof(NcmaPhysicsStatsV1)==88);
static_assert(sizeof(void*)!=8 || (sizeof(NcmaPhysicsApiV1)==144 && offsetof(NcmaPhysicsApiV1,create_world)==56));
#endif
#endif
