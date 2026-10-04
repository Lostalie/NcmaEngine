#include "NcmaPlugin.h"
#include "NcmaPhysics.h"
_Static_assert(sizeof(NcmaPhysicsApiV1) == 144, "physics function table");
_Static_assert(sizeof(NcmaPhysicsApiV1_1) == 152, "physics 1.1 table");
_Static_assert(sizeof(NcmaPhysicsCountersV1) == 72, "physics counters");
_Static_assert(sizeof(NcmaPhysicsStatsV1) == 88, "physics stats");
_Static_assert(sizeof(NcmaPhysicsBodyState3DV1) == 64, "physics state3");
_Static_assert(sizeof(NcmaErrorV1) == 528, "error layout");
_Static_assert(sizeof(NcmaModuleStatusV1) == 32, "status layout");
_Static_assert(sizeof(NcmaModuleApiV1) == 56, "x64 API layout");
_Static_assert(offsetof(NcmaModuleApiV1, initialize) == 24, "API offset");
int main(void) { return NCMA_MODULE_ABI_MAJOR != 1; }
