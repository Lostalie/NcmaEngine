#ifndef NCMA_PLATFORM_H
#define NCMA_PLATFORM_H
#include "NcmaPlugin.h"
#ifdef __cplusplus
extern "C" {
#endif
#define NCMA_MAX_INPUT_EVENTS 4096u
typedef enum NcmaInputEventKind { NCMA_KEY = 1, NCMA_MOUSE_BUTTON = 2, NCMA_POINTER = 3, NCMA_SCROLL = 4, NCMA_TEXT = 5, NCMA_FOCUS = 6, NCMA_RESIZE = 7 } NcmaInputEventKind;
typedef struct NcmaInputEventV1 {
    uint32_t kind, key, action, codepoint;
    double x, y;
    uint64_t sequence;
} NcmaInputEventV1;
typedef struct NcmaWindowStateV1 {
    uint32_t struct_size, width, height, framebuffer_width, framebuffer_height;
    uint32_t focused, minimized, close_requested, overflow, input_reset;
    float scale_x, scale_y;
    double pointer_x, pointer_y;
    uint64_t sequence;
    uint64_t held[8]; /* GLFW key 0..348; mouse button 0..7 maps to bits 384..391. */
} NcmaWindowStateV1;
typedef struct NcmaWindowDescriptionV1 {
    uint32_t struct_size, width, height, visible;
    const uint8_t* title;
    uint32_t title_length, reserved;
} NcmaWindowDescriptionV1;
typedef uint32_t (NCMA_CALL *NcmaCreateWindowV1)(uint64_t, const NcmaWindowDescriptionV1*, uint64_t*, NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaDestroyWindowV1)(uint64_t, uint64_t, NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaPollEventsV1)(uint64_t, uint64_t, NcmaInputEventV1*, uint32_t, uint32_t*, NcmaWindowStateV1*, NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaGetWindowStateV1)(uint64_t, uint64_t, NcmaWindowStateV1*, NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaSetWindowTextV1)(uint64_t, uint64_t, const uint8_t*, uint32_t, NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaRequestCloseV1)(uint64_t, uint64_t, NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaWaitEventsV1)(uint64_t, double, NcmaErrorV1*);
typedef struct NcmaPlatformApiV1 {
    NcmaModuleApiV1 module;
    NcmaCreateWindowV1 create_window;
    NcmaDestroyWindowV1 destroy_window;
    NcmaPollEventsV1 poll_events;
    NcmaGetWindowStateV1 get_window_state;
    NcmaSetWindowTextV1 set_title;
    NcmaSetWindowTextV1 set_icon;
    NcmaRequestCloseV1 request_close;
    NcmaWaitEventsV1 wait_events;
} NcmaPlatformApiV1;
/* Explicit 1.1 request only; 1.0 remains exactly 120 bytes. At most one timer per
   module, independent of windows/renderers. No timer exists until create succeeds.
   Owner-thread, synchronous, non-alertable, no callbacks/event pump/global timer
   resolution change. Wait requests are 1..8000 microseconds, not a wake deadline
   guarantee. C# owns clock, admission policy, final spin and cancellation checks.
   Unsupported high-resolution timers fail explicitly (no coarse fallback).
   Shutdown is busy while timer exists. Failed destroy retains ownership. */
typedef uint32_t (NCMA_CALL *NcmaCreatePrecisionWaitV1)(uint64_t, uint64_t*, NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaPrecisionWaitV1)(uint64_t, uint64_t, uint32_t, NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaDestroyPrecisionWaitV1)(uint64_t, uint64_t, NcmaErrorV1*);
typedef struct NcmaPlatformApiV1_1 {
    NcmaPlatformApiV1 base;
    NcmaCreatePrecisionWaitV1 create_precision_wait;
    NcmaPrecisionWaitV1 precision_wait;
    NcmaDestroyPrecisionWaitV1 destroy_precision_wait;
} NcmaPlatformApiV1_1;
/* Explicit 1.2 request; earlier tables retain their exact sizes. Pixel creation is
   a non-resizable, undecorated window at the primary monitor origin. Width/height
   are requested framebuffer pixels, not DPI-scaled content points. Not exclusive
   fullscreen; does not change display mode, topmost state or global DPI policy.
   Exact initial dimensions must be verified before publishing a handle; mismatch
   destroys the candidate and returns UNSUPPORTED_FEATURE. Later OS changes remain
   observable through normal WindowState/Poll, not an eternal pixel-size guarantee. */
typedef enum NcmaPixelWindowMode { NCMA_BORDERLESS_PIXELS = 1 } NcmaPixelWindowMode;
typedef struct NcmaPixelWindowDescriptionV1 {
    NcmaWindowDescriptionV1 window;
    uint32_t mode, reserved;
} NcmaPixelWindowDescriptionV1;
typedef uint32_t (NCMA_CALL *NcmaCreatePixelWindowV1)(uint64_t, const NcmaPixelWindowDescriptionV1*, uint64_t*, NcmaErrorV1*);
typedef struct NcmaPlatformApiV1_2 {
    NcmaPlatformApiV1_1 base;
    NcmaCreatePixelWindowV1 create_pixel_window;
} NcmaPlatformApiV1_2;
/* Poll requires capacity >=4096; capacity errors do not pump/drain. Overflow returns no transient
   events, current held/focus, and overflow=1. Input text is Unicode scalars, never gameplay key bits.
   Callback events are recorded even outside Poll. WindowState uses logical points vs framebuffer
   pixels; pointer coordinates are logical points. All pointers only valid during synchronous call. */
#ifdef __cplusplus
}
static_assert(sizeof(NcmaInputEventV1) == 40);
static_assert(sizeof(NcmaWindowStateV1) == 136);
static_assert(sizeof(NcmaWindowDescriptionV1) == 32);
static_assert(sizeof(NcmaPlatformApiV1) == 120);
static_assert(sizeof(NcmaPlatformApiV1_1) == 144 && offsetof(NcmaPlatformApiV1_1, create_precision_wait) == 120);
static_assert(sizeof(NcmaPixelWindowDescriptionV1) == 40 && offsetof(NcmaPixelWindowDescriptionV1, mode) == 32);
static_assert(sizeof(NcmaPlatformApiV1_2) == 152 && offsetof(NcmaPlatformApiV1_2, create_pixel_window) == 144);
#endif
#endif
