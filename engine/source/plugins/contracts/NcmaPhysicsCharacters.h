#ifndef NCMA_PHYSICS_CHARACTERS_H
#define NCMA_PHYSICS_CHARACTERS_H
#include "NcmaPlugin.h"
#ifdef __cplusplus
extern "C" {
#endif
#define NCMA_MAX_CHARACTERS 32u
#define NCMA_MAX_CHARACTER_CONTACTS 64u
/* Numeric-only Character API 1.0, queried through Physics module 1.2.
   Y-up, metres/seconds; capsule position is FOOT origin, height=2*(half_height+radius).
   Category is one bit; mask filters body/character categories. Zero mask means no hits.
   Quaternion xyzw; characters use yaw-only rotation. No gravity integration in character
   velocity: caller supplies final velocity. Gravity affects body forces only (Jolt semantics).
   One step advances bodies once then ALL characters in ascending handle order; no callbacks
   cross ABI. Characters can push/slide each other in this sequential numerical order; this
   is not simultaneous crowd solving or a cross-machine bitwise determinism guarantee. No
   gameplay policy is native. Full input set, sequence and output capacities preflight before mutation.
   Contact/hit overflow after execution faults the whole numerical domain, never truncates it.
   Also conservatively cap distinct accepted collision candidates across a character's full
   stair/floor/movement quantum at 64 (including predictive candidates, not just final hits).
   Ray/sweep closest hit includes body OR character; expected sequence required, no stepping.
   Contacts are current/predictive numeric observations, NOT gameplay events.
   Collider topology freezes while characters exist; character creation closes at first step.
   Destroy the COMPLETE character set before bodies/world; partial destruction is unsupported.
   Faulted worlds allow complete char destruction. All owner-thread; close failure retains ownership.
   Zero-length ray/sweep directions are rejected. Queries are closest-hit, not all-hit events.
   Caller owns bounded valid, disjoint input/output buffers; runtime handles are not persistent IDs.
   Frozen Physics 1.0/1.1 layouts and old non-character world behaviour are unchanged. */
typedef struct NcmaCapsuleDescriptionV1 {
    uint32_t struct_size, category, mask, reserved;
    float position[3], radius, half_height, max_slope_radians, step_height, floor_distance;
    float mass, max_strength, padding, reserved_float;
    uint64_t correlation;
} NcmaCapsuleDescriptionV1;
typedef struct NcmaCollisionBoxV1 {
    uint32_t struct_size, category, dynamic_body, reserved;
    float position[3], density, half_extents[3], reserved_float, rotation[4];
    uint64_t correlation;
} NcmaCollisionBoxV1;
typedef struct NcmaCharacterVelocityV1 {
    uint64_t character;
    float velocity[3]; uint32_t reserved;
    float rotation[4];
} NcmaCharacterVelocityV1;
typedef struct NcmaCharacterStateV1 {
    uint64_t character, correlation, sequence;
    float position[3], velocity[3], rotation[4], ground_normal[3], ground_velocity[3];
    uint32_t ground_state, ground_kind; /* 0 ground, 1 steep, 2 unsupported, 3 air; kind 0/1/2 */
    uint64_t ground_body;
} NcmaCharacterStateV1;
typedef struct NcmaCharacterContactV1 {
    uint64_t character, other, sequence;
    uint32_t other_kind, subshape; /* 1 body, 2 character */
    float position[3], normal[3], separation; uint32_t flags; /* collided=1/discarded=2/sensor=4 */
} NcmaCharacterContactV1;
typedef struct NcmaCharacterStepReceiptV1 {
    uint64_t sequence; uint32_t states, contacts;
} NcmaCharacterStepReceiptV1;
typedef struct NcmaPhysicsRayV1 {
    uint32_t struct_size, mask;
    float origin[3], displacement[3]; uint64_t ignore; /* zero or body/character handle */
} NcmaPhysicsRayV1;
typedef struct NcmaPhysicsCapsuleSweepV1 {
    uint32_t struct_size, mask;
    float foot[3], displacement[3], radius, half_height, rotation[4]; uint64_t ignore;
} NcmaPhysicsCapsuleSweepV1;
typedef struct NcmaPhysicsQueryHitV1 {
    uint32_t hit, kind;
    uint64_t resource;
    float fraction, position[3], normal[3]; uint32_t subshape;
    uint64_t sequence, reserved;
} NcmaPhysicsQueryHitV1;
typedef uint32_t (NCMA_CALL *NcmaCreateCollisionBoxesV1)(uint64_t,uint64_t,const NcmaCollisionBoxV1*,uint32_t,uint64_t*,uint32_t,NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaCreateCapsulesV1)(uint64_t,uint64_t,const NcmaCapsuleDescriptionV1*,uint32_t,uint64_t*,uint32_t,NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaDestroyCharactersV1)(uint64_t,uint64_t,const uint64_t*,uint32_t,NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaStepCharactersV1)(uint64_t,uint64_t,uint64_t,float,const NcmaCharacterVelocityV1*,uint32_t,NcmaCharacterStateV1*,uint32_t,NcmaCharacterContactV1*,uint32_t,NcmaCharacterStepReceiptV1*,NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaReadCharactersV1)(uint64_t,uint64_t,uint64_t,const uint64_t*,uint32_t,NcmaCharacterStateV1*,uint32_t,NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaCharacterRayV1)(uint64_t,uint64_t,uint64_t,const NcmaPhysicsRayV1*,NcmaPhysicsQueryHitV1*,NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaCharacterSweepV1)(uint64_t,uint64_t,uint64_t,const NcmaPhysicsCapsuleSweepV1*,NcmaPhysicsQueryHitV1*,NcmaErrorV1*);
typedef struct NcmaCharacterApiV1 {
    uint32_t struct_size, major, minor, max_characters;
    uint64_t capabilities; /* 31: capsule, contacts, ray, sweep, rotated box */
    NcmaCreateCollisionBoxesV1 create_boxes;
    NcmaCreateCapsulesV1 create_capsules;
    NcmaDestroyCharactersV1 destroy_characters;
    NcmaStepCharactersV1 step;
    NcmaReadCharactersV1 read_states;
    NcmaCharacterRayV1 ray;
    NcmaCharacterSweepV1 sweep;
} NcmaCharacterApiV1;
typedef uint32_t (NCMA_CALL *NcmaQueryCharacterApiV1)(uint64_t,uint32_t,uint32_t,void*,uint32_t,NcmaErrorV1*);
#ifdef __cplusplus
}
static_assert(sizeof(NcmaCapsuleDescriptionV1)==72 && sizeof(NcmaCollisionBoxV1)==72);
static_assert(sizeof(NcmaCharacterVelocityV1)==40 && sizeof(NcmaCharacterStateV1)==104);
static_assert(sizeof(NcmaCharacterContactV1)==64 && sizeof(NcmaCharacterStepReceiptV1)==16);
static_assert(sizeof(NcmaPhysicsRayV1)==40 && sizeof(NcmaPhysicsCapsuleSweepV1)==64 && sizeof(NcmaPhysicsQueryHitV1)==64);
static_assert(sizeof(void*)!=8 || sizeof(NcmaCharacterApiV1)==80);
#endif
#endif
