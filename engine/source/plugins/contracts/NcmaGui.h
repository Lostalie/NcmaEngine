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
/* GUI module ABI 1.3 adds Image. Its reserved words hold a renderer-owned target token
   (value low/high, generation low/high), NOT ImTextureID/SRV. Other reserved words remain zero.
   Rect is a logical screen rectangle; native GUI pins at most 64 images until render/discard. */
typedef enum NcmaGuiItemKind { NCMA_GUI_PANEL_BEGIN = 1, NCMA_GUI_PANEL_END = 2, NCMA_GUI_LABEL = 3, NCMA_GUI_BUTTON = 4, NCMA_GUI_NUMBER = 5, NCMA_GUI_CHECKBOX = 6, NCMA_GUI_TEXT = 7, NCMA_GUI_SAME_LINE = 8, NCMA_GUI_CANVAS_BEGIN = 9, NCMA_GUI_CANVAS_LINES = 10, NCMA_GUI_CANVAS_END = 11, NCMA_GUI_THEME = 12, NCMA_GUI_IMAGE = 13, NCMA_GUI_ASSET_BUTTON = 14,
    NCMA_GUI_TOOLBAR_BEGIN = 15, NCMA_GUI_TOOLBAR_BUTTON = 16, NCMA_GUI_TOOLBAR_BRAND = 17, NCMA_GUI_TOOLBAR_DIVIDER = 18, NCMA_GUI_TOOLBAR_END = 19,
    NCMA_GUI_MENU_BUTTON = 20, NCMA_GUI_MENU_BRAND = 21, NCMA_GUI_CACHED_IMAGE = 22,
    NCMA_GUI_OVERLAY_BEGIN = 23, NCMA_GUI_SPLITTER = 24, NCMA_GUI_SELECTION_BUTTON = 25 } NcmaGuiItemKind;
/* ABI1.6 retains112-byte table; CachedImage reserved holds query7 presentation-lease token.
   This is not an old Image/target token. Native retains lease until render/discard; exact
   current frame required. Same copied rectangle/click syntax; no change to old Image. */
/* ABI1.6 SelectionButton emits phase3 with current modifier bits ctrl=1/shift=2;
   presentation only. The host determines selection policy. Old Button value is unchanged. */
/* ABI1.5 uses the unchanged112-byte1.4 table. Adds single-row MenuButton/Brand inside
   ToolbarBegin/End and Theme3 (matte blue-gray). MenuButton uses the existing icon/role
   contract (MenuButton icon0..14, including generic user and dropdown icons); icon0 draws text only. Brand
   label is only the current project name; no product icon/subtitle is drawn. Theme3 PanelBegin
   value1 hides its title strip (status presentation); value2 is a foreground overlay.
   Theme3 titled PanelBegin text is optional copied right-aligned header status, clipped
   after the title with an8-logical-pixel right inset; no body row or input event.
   Theme3 tiled panels do not rise above overlays on focus. Also requests matching native
   decorated-window chrome where supported; high contrast keeps system colors. No menu policy. */
/* ABI1.4: scoped toolbar-only presentation. rect=absolute logical bounds. ToolbarButton value
   is icon0..12, minimum role0 neutral/1 accent/2 selected, maximum=2. text=tooltip, no command.
   Brand label=product, text=subtitle. Header height40..96; no nested panels/legacy widgets.
   Existing1.0..1.3 items/structs/reserved meanings stay frozen. */
/* AssetButton text is one canonical UUID (36 bytes). A drop on enabled Image emits commit
   value=1/text=UUID; click emits value=0/text="u v". No file path or native command payload. */
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
typedef uint32_t (NCMA_CALL *NcmaConfigureToolbarIconV1)(uint64_t,uint64_t,const uint8_t*,uint32_t,NcmaErrorV1*);
/* Explicit copied ICO bytes, <=1MiB, parsed/rasterized at trusted startup only; no file path.
   Requires no active/retained frame; current1.4 accepts setup before first Draw only.
   Decoded pixels/SRV belong to GUI and release before renderer borrow/module shutdown. */
typedef struct NcmaGuiApiV1_4 { NcmaGuiApiV1 base; NcmaConfigureToolbarIconV1 configure_toolbar_icon; } NcmaGuiApiV1_4;
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
static_assert(sizeof(NcmaGuiApiV1_4) == 112);
#endif
#endif
