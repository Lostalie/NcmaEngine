#ifndef NCMA_GUI_H
#define NCMA_GUI_H
#include "NcmaPlatform.h"
#ifdef __cplusplus
extern "C" {
#endif
#define NCMA_GUI_MAX_ITEMS 8192u
#define NCMA_GUI_MAX_TEXT_BYTES 2097152u
#define NCMA_GUI_MAX_EVENTS 256u
#define NCMA_GUI_OUTPUT_TEXT_BYTES 65536u
/* GUI module ABI 1.2 adds bounded presentation-only canvases; these emit no events. */
typedef enum NcmaGuiItemKind { NCMA_GUI_PANEL_BEGIN = 1, NCMA_GUI_PANEL_END = 2, NCMA_GUI_LABEL = 3, NCMA_GUI_BUTTON = 4, NCMA_GUI_NUMBER = 5, NCMA_GUI_CHECKBOX = 6, NCMA_GUI_TEXT = 7, NCMA_GUI_SAME_LINE = 8, NCMA_GUI_CANVAS_BEGIN = 9, NCMA_GUI_CANVAS_LINES = 10, NCMA_GUI_CANVAS_END = 11, NCMA_GUI_THEME = 12 } NcmaGuiItemKind;
typedef struct NcmaGuiDescriptionV1 {
    uint32_t struct_size, reserved;
    uint64_t platform_module, window;
    const uint8_t* font_path;
    uint32_t font_path_length;
    float font_size;
    const uint8_t* ini_path;
    uint32_t ini_path_length, reserved2;
} NcmaGuiDescriptionV1;
typedef struct NcmaGuiFrameV1 {
    uint32_t struct_size, item_count, text_bytes, reserved;
    uint64_t frame, view_generation, document_generation, revision;
} NcmaGuiFrameV1;
typedef struct NcmaGuiItemV1 {
    uint32_t kind, enabled;
    uint64_t widget_high, widget_low;
    uint32_t label_offset, label_length, text_offset, text_length;
    double value, minimum, maximum;
    float rect[4];
    uint32_t reserved[4];
} NcmaGuiItemV1;
typedef struct NcmaGuiEventV1 {
    uint32_t kind, phase; /* activate=1, change=2, commit=3; cancel is whole-frame capture flag. */
    uint64_t widget_high, widget_low;
    double value;
    uint64_t frame, view_generation, document_generation, revision;
    uint32_t text_offset, text_length;
} NcmaGuiEventV1;
typedef struct NcmaGuiCaptureV1 { uint32_t struct_size, keyboard, mouse, cancel_interaction; } NcmaGuiCaptureV1;
typedef struct NcmaGuiStatsV1 {
    uint32_t struct_size, vertices, indices, draw_lists, event_overflow, text_bytes;
    uint64_t frame;
} NcmaGuiStatsV1;
typedef uint32_t (NCMA_CALL *NcmaCreateGuiV1)(uint64_t, const NcmaGuiDescriptionV1*, uint64_t*, NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaBeginGuiV1)(uint64_t, uint64_t, const NcmaWindowStateV1*, double, NcmaGuiCaptureV1*, NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaDrawGuiV1)(uint64_t, uint64_t, const NcmaGuiFrameV1*, const NcmaGuiItemV1*, const uint8_t*,
    NcmaGuiEventV1*, uint32_t, uint32_t*, uint8_t*, uint32_t, NcmaGuiStatsV1*, NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaDestroyGuiV1)(uint64_t, uint64_t, NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaAttachGuiRendererV1)(uint64_t, uint64_t, uint64_t, uint64_t, NcmaErrorV1*);
typedef uint32_t (NCMA_CALL *NcmaRenderGuiV1)(uint64_t, uint64_t, NcmaErrorV1*);
typedef struct NcmaGuiApiV1 {
    NcmaModuleApiV1 module;
    NcmaCreateGuiV1 create_gui;
    NcmaBeginGuiV1 begin_frame;
    NcmaDrawGuiV1 draw;
    NcmaDestroyGuiV1 destroy_gui;
    NcmaAttachGuiRendererV1 attach_renderer;
    NcmaRenderGuiV1 render_gpu;
} NcmaGuiApiV1;
/* Copied semantic view. IDs are panel/object/field-derived, never list indices. No command execution
   or callbacks into CLR. Events apply at the NEXT owner safe boundary with generation/revision checks.
   Draw creates CPU draw data only until a versioned native renderer service is attached.
   Output cap >=256 events and >=65536 text bytes BEFORE draw. Overflow drops all events and cancels
   interaction. Native stores ONLY active-widget/text cursor drafts, never authoritative scene state. */
#ifdef __cplusplus
}
static_assert(sizeof(NcmaGuiDescriptionV1) == 56);
static_assert(sizeof(NcmaGuiFrameV1) == 48);
static_assert(sizeof(NcmaGuiItemV1) == 96);
static_assert(sizeof(NcmaGuiEventV1) == 72);
static_assert(sizeof(NcmaGuiCaptureV1) == 16);
static_assert(sizeof(NcmaGuiStatsV1) == 32);
static_assert(sizeof(NcmaGuiApiV1) == 104);
#endif
#endif
