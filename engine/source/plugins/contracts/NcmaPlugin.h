#ifndef NCMA_PLUGIN_H
#define NCMA_PLUGIN_H
#include <stdint.h>
#include <stddef.h>
#if defined(_WIN32)
#define NCMA_CALL __cdecl
#define NCMA_EXPORT __declspec(dllexport)
#else
#define NCMA_CALL
#define NCMA_EXPORT __attribute__((visibility("default")))
#endif
#ifdef __cplusplus
extern "C" {
#endif
#define NCMA_MODULE_ABI_MAJOR 1u
#define NCMA_MODULE_ABI_MINOR 0u
#define NCMA_MAX_API_BYTES 4096u
typedef enum NcmaResult {
    NCMA_OK = 0, NCMA_ABI_MISMATCH = 1, NCMA_INVALID_ARGUMENT = 2,
    NCMA_WRONG_THREAD = 3, NCMA_INVALID_HANDLE = 4, NCMA_BUFFER_TOO_SMALL = 5,
    NCMA_UNSUPPORTED_FEATURE = 6, NCMA_BUSY = 7, NCMA_DEVICE_LOST = 8,
    NCMA_SHUTDOWN_TIMEOUT = 9, NCMA_INTERNAL_ERROR = 10
} NcmaResult;
typedef enum NcmaModuleKind {
    NCMA_PLATFORM = 1, NCMA_GUI = 2, NCMA_RENDERER = 3, NCMA_PHYSICS = 4, NCMA_TEXT_MODULE = 5, NCMA_FIXTURE = 127
} NcmaModuleKind;
typedef struct NcmaErrorV1 {
    uint32_t code, reserved, required_bytes, message_length;
    uint8_t message[512]; /* UTF-8 bytes, no trailing NUL required; no borrowed memory. */
} NcmaErrorV1;
typedef struct NcmaModuleStatusV1 {
    uint32_t struct_size, state;
    uint64_t live_resources, live_jobs, sequence;
} NcmaModuleStatusV1;
/* Every operation is synchronous, copies retained input, runs on the initializing owner thread.
   Output/error pointers and capacities are validated BEFORE mutation. No exceptions cross ABI.
   Shutdown returns busy while resources/jobs remain. A failed initialize returns no context.
   Module/context/resources must be destroyed before FreeLibrary. No hot unload or finalizer calls. */
typedef uint32_t (NCMA_CALL *NcmaInitializeV1)(const uint8_t*, uint32_t, uint64_t*, NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaShutdownV1)(uint64_t, NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaGetStatusV1)(uint64_t, NcmaModuleStatusV1*, NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaReadDiagnosticV1)(uint64_t, uint8_t*, uint32_t, uint32_t*, NcmaErrorV1*);
typedef struct NcmaModuleApiV1 {
    uint32_t struct_size, major, minor, kind;
    uint64_t capabilities;
    NcmaInitializeV1 initialize;
    NcmaShutdownV1 shutdown;
    NcmaGetStatusV1 get_status;
    NcmaReadDiagnosticV1 read_diagnostic;
} NcmaModuleApiV1;
typedef uint32_t (NCMA_CALL *NcmaGetApiV1)(uint32_t, uint32_t, void*, uint32_t, NcmaErrorV1*);
/* One export per module: ncma_plugin_get_api. Unknown majors/minors are rejected, not guessed.
   Pointer-containing tables are process-local x64; POD messages are not wire serialization. */
#ifdef __cplusplus
}
static_assert(sizeof(NcmaErrorV1) == 528 && alignof(NcmaErrorV1) == 4);
static_assert(sizeof(NcmaModuleStatusV1) == 32 && offsetof(NcmaModuleStatusV1, sequence) == 24);
static_assert(sizeof(void*) != 8 || (sizeof(NcmaModuleApiV1) == 56 && offsetof(NcmaModuleApiV1, initialize) == 24));
#endif
#endif
