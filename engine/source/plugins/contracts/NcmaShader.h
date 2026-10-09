#ifndef NCMA_SHADER_H
#define NCMA_SHADER_H
#include "NcmaPlugin.h"
#ifdef __cplusplus
extern "C" {
#endif
/* Renderer query8/API1. Trusted owner-thread off-frame SM5 compilation, not pipeline install.
   Source256KiB, macros16, rows256, bytecode1MiB. No filesystem include handler.
   Compiler blobs are scoped to the call; outputs copied only after COMPLETE validation.
   On failure only NcmaErrorV1 changes (sanitized, bounded diagnostic). No retained native handles.
   Fixed strict/warnings-as-errors/O3 flags. Compile requires idle, healthy exact renderer.
   name/parent are NUL-terminated ASCII, max63; unknown reflection forms reject.
   row kinds:1=vertex input,2=constant buffer,3=member,4=resource.
   stage:0=VS,1=PS,2=CS; scalar:0=float32,1=int32,2=uint32;
   order:0=none,1=row,2=column; resource:0=2D,1=cube,2=structured,3=byteaddress,
   4=sampler,5=RWstructured,6=RWbyteaddress; access:0=read,1=readwrite.
   Input offset is unknown UINT_MAX; stream offsets belong to later mesh validation.
   Member bytes is REAL reflected span, not padded reservation. Structured stride is
   independently read from actual bytecode declarations, never NumSamples/author metadata. */
typedef struct NcmaShaderMacroV1 { char name[64],value[64]; } NcmaShaderMacroV1;
typedef struct NcmaShaderCompileV1 {
    uint32_t struct_size,version,stage,source_bytes,entry_bytes,macro_count,reserved,reserved2;
    const uint8_t* source; const char* entry; const NcmaShaderMacroV1* macros;
} NcmaShaderCompileV1;
typedef struct NcmaShaderRowV1 {
    uint32_t kind,stage,scalar,columns,rows,index,slot,count,offset,bytes,order,array_count,array_stride,resource_kind,access,stride;
    char name[64],parent[64];
} NcmaShaderRowV1;
typedef struct NcmaShaderOutputV1 { uint32_t struct_size,version,stage,row_count,bytecode_bytes,compiler_version,flags,reserved; } NcmaShaderOutputV1;
typedef uint32_t (NCMA_CALL *NcmaShaderCompileFnV1)(uint64_t,uint64_t,const NcmaShaderCompileV1*,NcmaShaderOutputV1*,NcmaShaderRowV1*,uint32_t,uint8_t*,uint32_t,NcmaErrorV1*);
typedef struct NcmaShaderApiV1 { uint32_t struct_size,version; uint64_t capabilities; NcmaShaderCompileFnV1 compile;
    uint32_t (NCMA_CALL *validate_preparation)(uint64_t,uint64_t,NcmaErrorV1*); } NcmaShaderApiV1;
#ifdef __cplusplus
}
static_assert(sizeof(NcmaShaderMacroV1)==128);
static_assert(sizeof(NcmaShaderCompileV1)==56);
static_assert(sizeof(NcmaShaderRowV1)==192);
static_assert(sizeof(NcmaShaderOutputV1)==32);
static_assert(sizeof(NcmaShaderApiV1)==32);
#endif
#endif
