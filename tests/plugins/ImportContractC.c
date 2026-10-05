#include "contracts/NcmaImport.h"
_Static_assert(sizeof(NcmaImportApiV1) == 96, "import x64 table");
_Static_assert(sizeof(NcmaImportApiV1_1) == 104, "import 1.1 x64 table");
_Static_assert(sizeof(NcmaImportVertexV1) == 56, "vertex layout");
_Static_assert(sizeof(NcmaImportBindingV1) == 68, "binding layout");
_Static_assert(sizeof(NcmaImportKeyV1) == 48, "key layout");
_Static_assert(offsetof(NcmaImportKeyV1, value) == 8, "key TRS offset");
int main(void) { return NCMA_IMPORT_ABI_MAJOR != 1; }
