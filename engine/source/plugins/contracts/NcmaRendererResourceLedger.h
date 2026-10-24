#ifndef NCMA_RENDERER_RESOURCE_LEDGER_H
#define NCMA_RENDERER_RESOURCE_LEDGER_H
#include "NcmaPlugin.h"
/* Optional query22/v1. Copies existing owner-thread logical counters only:
   no resource initialization, timing resolution, GPU calls/waits or allocation.
   Slots: meshes (including skin output), skin, textures/materials/scene targets,
   scene pipelines, environments, UI images/lists, UI targets, post bindings.
   Counts respectively: meshes, skins, sum of three resource handle sets,
   pipelines, environments, images+lists, targets, bindings. Not byte totals.
   Domains overlap (notably mesh/skin); NEVER sum as physical VRAM or native heap.
   Kernel/shader/driver/swapchain/ImGui/allocator overhead is not fully covered.
   Zero means no counted logical resources, NOT proof of no resident allocations.
   A new renderer generation resets its counters. No resource ownership transfer. */
typedef struct NcmaRendererResourceLedgerV1 {
    uint32_t struct_size,version; uint64_t generation;
    uint32_t flags,reserved; /* pure UI=1, fail-stop=2, active frame=4 */
    uint64_t logical_bytes[8],live_resources[8];
} NcmaRendererResourceLedgerV1;
typedef uint32_t (NCMA_CALL *NcmaReadRendererResourceLedgerV1)(uint64_t,uint64_t,NcmaRendererResourceLedgerV1*,uint32_t,NcmaErrorV1*);
typedef struct NcmaRendererResourceLedgerApiV1 {
    uint32_t struct_size,version;uint64_t capabilities;NcmaReadRendererResourceLedgerV1 read;
} NcmaRendererResourceLedgerApiV1;
#ifdef __cplusplus
static_assert(sizeof(NcmaRendererResourceLedgerV1)==152&&sizeof(NcmaRendererResourceLedgerApiV1)==24);
#endif
#endif
