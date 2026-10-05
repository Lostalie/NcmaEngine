#ifndef NCMA_IMPORT_H
#define NCMA_IMPORT_H
#include "NcmaPlugin.h"
#if defined(_WIN32)
#if defined(NCMA_IMPORT_EXPORTS)
#define NCMA_IMPORT_API __declspec(dllexport)
#else
#define NCMA_IMPORT_API __declspec(dllimport)
#endif
#else
#define NCMA_IMPORT_API __attribute__((visibility("default")))
#endif
#ifdef __cplusplus
extern "C" {
#endif
#define NCMA_IMPORT_ABI_MAJOR 1u
#define NCMA_IMPORT_ABI_MINOR 1u
#define NCMA_IMPORT_CANCELLED 256u
#define NCMA_IMPORT_MAX_PAGE 4096u
typedef enum NcmaImportStreamV1 {
    NCMA_IMPORT_VERTICES = 1, NCMA_IMPORT_INDICES = 2, NCMA_IMPORT_TRIANGLE_MATERIALS = 3,
    NCMA_IMPORT_BINDINGS = 4, NCMA_IMPORT_BONES = 5, NCMA_IMPORT_TRACKS = 6, NCMA_IMPORT_KEYS = 7
} NcmaImportStreamV1;
typedef enum NcmaImportTextV1 {
    NCMA_IMPORT_MESH_NAME = 1, NCMA_IMPORT_BONE_NAME = 2, NCMA_IMPORT_CLIP_NAME = 3,
    NCMA_IMPORT_MATERIAL_NAME = 4, NCMA_IMPORT_WARNING = 5
} NcmaImportTextV1;
typedef struct NcmaImportTransformV1 { float position[3], rotation_xyzw[4], scale[3]; } NcmaImportTransformV1;
typedef struct NcmaImportVertexV1 { float position[3], normal[3], uv[2]; uint16_t joints[4]; float weights[4]; } NcmaImportVertexV1;
typedef struct NcmaImportBindingV1 { uint32_t bone; float geometry_to_bone[16]; } NcmaImportBindingV1;
typedef struct NcmaImportBoneV1 { int32_t parent; NcmaImportTransformV1 bind_local; } NcmaImportBoneV1;
typedef struct NcmaImportTrackV1 { uint32_t bone, keys; } NcmaImportTrackV1;
typedef struct NcmaImportKeyV1 { double time; NcmaImportTransformV1 value; } NcmaImportKeyV1;
typedef struct NcmaImportInfoV1 {
    uint32_t struct_size, fbx_version, binary, bones, meshes, clips, warnings, reserved;
    double source_unit_metres, sample_rate;
    uint64_t vertices, indices, keys;
} NcmaImportInfoV1;
typedef struct NcmaImportMeshInfoV1 { uint32_t struct_size, flags, vertices, indices, triangles, bindings, materials, reserved; } NcmaImportMeshInfoV1;
typedef struct NcmaImportClipInfoV1 { uint32_t struct_size, tracks, keys, reserved; double duration; } NcmaImportClipInfoV1;
typedef struct NcmaImportStatusV1 { uint32_t struct_size, phase, busy, cancellation_requested; uint64_t bytes_read, bytes_total; } NcmaImportStatusV1;
/* All outputs are caller-owned copies. Context IDs are process-local, not persistent UUIDs.
   Create/load/read/close are owner-thread operations. Cancel/status alone are thread-safe.
   One immutable character per context, maximum four contexts. No World, database or disk writes.
   Load is synchronous INSIDE the future worker process, never an editor frame operation.
   Positions/normals are mesh-local RH/+Y/metres; UV is top-left; matrices column-major.
   v1.0 does NOT advertise static mode, tangents, embedded textures or stable source identity.
   Page: object=mesh/clip; sub=track only for keys; both zero for bones. Count <=4096.
   Phase: 0 idle,1 parsing,2 converting,3 sampling,4 ready,5 cancelled,6 failed.
   Copy all outputs before close/unload; never unload while any context/call remains. */
typedef struct NcmaImportApiV1 {
    uint32_t struct_size, major, minor, reserved;
    uint32_t (NCMA_CALL *create)(uint64_t*, NcmaErrorV1*);
    uint32_t (NCMA_CALL *close)(uint64_t, NcmaErrorV1*);
    uint32_t (NCMA_CALL *load)(uint64_t, const uint8_t*, uint32_t, double, NcmaImportInfoV1*, NcmaErrorV1*);
    uint32_t (NCMA_CALL *info)(uint64_t, NcmaImportInfoV1*, NcmaErrorV1*);
    uint32_t (NCMA_CALL *mesh_info)(uint64_t, uint32_t, NcmaImportMeshInfoV1*, NcmaErrorV1*);
    uint32_t (NCMA_CALL *clip_info)(uint64_t, uint32_t, NcmaImportClipInfoV1*, NcmaErrorV1*);
    uint32_t (NCMA_CALL *read_page)(uint64_t, uint32_t, uint32_t, uint32_t, uint32_t, uint32_t, void*, uint32_t, uint32_t*, NcmaErrorV1*);
    uint32_t (NCMA_CALL *read_text)(uint64_t, uint32_t, uint32_t, uint32_t, uint8_t*, uint32_t, uint32_t*, NcmaErrorV1*);
    uint32_t (NCMA_CALL *cancel)(uint64_t, NcmaErrorV1*);
    uint32_t (NCMA_CALL *status)(uint64_t, NcmaImportStatusV1*, NcmaErrorV1*);
} NcmaImportApiV1;
/* 1.1 additive table. mode 0 strict character, 1 static-only (rejects skin).
   1.0 negotiation still returns exactly 96 bytes and retains old semantics.
   Static helper/bind data is tool-side baking input, not a gameplay skeleton. */
typedef struct NcmaImportApiV1_1 {
    NcmaImportApiV1 base;
    uint32_t (NCMA_CALL *load_mode)(uint64_t, const uint8_t*, uint32_t, double, uint32_t, NcmaImportInfoV1*, NcmaErrorV1*);
} NcmaImportApiV1_1;
NCMA_IMPORT_API uint32_t NCMA_CALL ncma_import_get_api(uint32_t, uint32_t, void*, uint32_t, NcmaErrorV1*);
#ifdef __cplusplus
}
static_assert(sizeof(NcmaImportTransformV1) == 40 && sizeof(NcmaImportVertexV1) == 56);
static_assert(sizeof(NcmaImportBindingV1) == 68 && sizeof(NcmaImportBoneV1) == 44);
static_assert(sizeof(NcmaImportTrackV1) == 8 && sizeof(NcmaImportKeyV1) == 48);
static_assert(sizeof(NcmaImportInfoV1) == 72 && sizeof(NcmaImportMeshInfoV1) == 32);
static_assert(sizeof(NcmaImportClipInfoV1) == 24 && sizeof(NcmaImportStatusV1) == 32);
static_assert(sizeof(void*) != 8 || sizeof(NcmaImportApiV1) == 96);
static_assert(sizeof(void*) != 8 || sizeof(NcmaImportApiV1_1) == 104);
#endif
#endif
