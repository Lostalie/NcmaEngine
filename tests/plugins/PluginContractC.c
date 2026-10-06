#include "NcmaPlugin.h"
#include "NcmaPhysics.h"
#include "NcmaRenderer.h"
#include "NcmaResourceRender.h"
#include "NcmaScenePipeline.h"
#include "NcmaSkin.h"
#include "NcmaUiRender.h"
#include "NcmaText.h"
_Static_assert(sizeof(NcmaTextRequestV1)==48 && sizeof(NcmaTextMetricsV1)==32 && sizeof(NcmaTextApiV1)==88, "Text numerical layout");
_Static_assert(sizeof(NcmaUiVertexV1)==52 && sizeof(NcmaUiBatchV1)==40, "UI vertices/batches");
_Static_assert(sizeof(NcmaUiApiV1)==64 && sizeof(NcmaUiStatsV1)==64 && sizeof(NcmaUiFrameV1)==48, "UI table/stats/frame");
_Static_assert(sizeof(NcmaSkinMeshV5)==56 && sizeof(NcmaSkinPaletteV5)==128 && sizeof(NcmaSkinRequestV5)==24, "skin numerical layout");
_Static_assert(sizeof(NcmaSkinBatchV5)==32 && sizeof(NcmaSkinStatsV5)==96 && sizeof(NcmaSkinApiV5)==56, "skin table/counters");
#include "NcmaPose.h"
_Static_assert(sizeof(NcmaPoseTrsV1)==40 && sizeof(NcmaPoseKeyV1)==48 && sizeof(NcmaPoseBoneV1)==44,"pose TRS/key/bone layout");
_Static_assert(sizeof(NcmaPoseRequestV1)==40 && sizeof(NcmaPoseMatrixV1)==64 && sizeof(NcmaPoseApiV1)==72,"pose batch/table layout");
_Static_assert(sizeof(NcmaScenePipelineDescriptionV4) == 16, "scene pipeline description");
_Static_assert(sizeof(NcmaScenePassV4) == 16, "scene stage POD");
_Static_assert(sizeof(NcmaSceneDrawV4) == 256, "scene draw POD");
_Static_assert(sizeof(NcmaSceneFrameV4) == 240, "scene frame POD");
_Static_assert(sizeof(NcmaScenePipelineStatsV4) == 72, "scene pipeline counters");
_Static_assert(sizeof(NcmaScenePipelineApiV4) == 48, "scene pipeline table");
_Static_assert(offsetof(NcmaSceneFrameV4, light_view_projection) == 160, "light projection offset");
#include "NcmaImage.h"
_Static_assert(sizeof(NcmaImageInfoV1) == 32, "image info");
_Static_assert(sizeof(NcmaTextureDescriptionV3) == 48, "texture description");
_Static_assert(sizeof(NcmaMaterialDescriptionV3) == 160, "material description");
_Static_assert(sizeof(NcmaResourceDrawV3) == 240, "typed draw");
_Static_assert(sizeof(NcmaResourceFrameV3) == 136, "typed frame");
_Static_assert(sizeof(NcmaResourceStatsV3) == 72, "resource counters");
_Static_assert(sizeof(NcmaResourceRenderApiV3) == 72, "resource table");
_Static_assert(sizeof(NcmaRendererApiV1) == 136, "renderer original table");
_Static_assert(sizeof(NcmaRendererApiV1_1) == 144, "renderer reference 1.1 table");
_Static_assert(sizeof(NcmaRendererApiV1_2) == 152, "renderer additive 1.2 table");
_Static_assert(offsetof(NcmaRendererApiV1_2, query_scene_render) == 144, "renderer scene query offset");
_Static_assert(sizeof(NcmaSceneRenderApiV1) == 48, "scene render service");
_Static_assert(sizeof(NcmaSceneRenderApiV2) == 56, "bind-pose service");
_Static_assert(offsetof(NcmaSceneRenderApiV2, create_bind_pose_mesh) == 48, "bind-pose operation offset");
_Static_assert(sizeof(NcmaBindPoseMeshDescriptionV2) == 64, "bind-pose descriptor");
_Static_assert(sizeof(NcmaMeshDescriptionV1) == 48, "static mesh description");
_Static_assert(sizeof(NcmaGpuMeshV1) == 16, "GPU handle and generation");
_Static_assert(sizeof(NcmaMeshDrawV1) == 112, "static draw");
_Static_assert(sizeof(NcmaMeshFrameV1) == 64, "static frame");
_Static_assert(sizeof(NcmaSceneRenderStatsV1) == 56, "static counters");
_Static_assert(sizeof(NcmaPhysicsApiV1) == 144, "physics function table");
_Static_assert(sizeof(NcmaPhysicsApiV1_1) == 152, "physics 1.1 table");
_Static_assert(sizeof(NcmaPhysicsApiV1_2) == 160, "physics 1.2 table");
_Static_assert(sizeof(NcmaCharacterApiV1) == 80, "character table");
_Static_assert(sizeof(NcmaCapsuleDescriptionV1) == 72 && sizeof(NcmaCollisionBoxV1) == 72, "character descriptions");
_Static_assert(sizeof(NcmaCharacterVelocityV1) == 40 && sizeof(NcmaCharacterStateV1) == 104, "character state");
_Static_assert(sizeof(NcmaCharacterContactV1) == 64 && sizeof(NcmaCharacterStepReceiptV1) == 16, "character contacts");
_Static_assert(sizeof(NcmaPhysicsRayV1) == 40 && sizeof(NcmaPhysicsCapsuleSweepV1) == 64 && sizeof(NcmaPhysicsQueryHitV1) == 64, "numeric queries");
_Static_assert(sizeof(NcmaPhysicsCountersV1) == 72, "physics counters");
_Static_assert(sizeof(NcmaPhysicsStatsV1) == 88, "physics stats");
_Static_assert(sizeof(NcmaPhysicsBodyState3DV1) == 64, "physics state3");
_Static_assert(sizeof(NcmaErrorV1) == 528, "error layout");
_Static_assert(sizeof(NcmaModuleStatusV1) == 32, "status layout");
_Static_assert(sizeof(NcmaModuleApiV1) == 56, "x64 API layout");
_Static_assert(offsetof(NcmaModuleApiV1, initialize) == 24, "API offset");
int main(void) { return NCMA_MODULE_ABI_MAJOR != 1; }
