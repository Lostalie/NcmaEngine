#include "../contracts/NcmaGui.h"
#include "../PluginSupport.h"
#include "../Utf8.h"
#include "../platform/PlatformPrivate.h"
#include <imgui.h>
#include <imgui_internal.h>
#include <imgui_impl_glfw.h>
#include <imgui_impl_dx11.h>
#include "../renderer/RendererPrivate.h"
#include <array>
#include <cmath>
#include <memory>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>
#include <fstream>
#include <charconv>
#include <wrl/client.h>
#include <d3d11.h>
#include "ToolbarPresentation.h"
#include "WindowChrome.h"
namespace {
const auto loadingThread = std::this_thread::get_id();
uint64_t module = 0, nextModule = 1, nextGui = 1;
bool busy = false;
struct Id { uint64_t high, low; bool operator==(const Id&) const = default; };
struct Hash { size_t operator()(const Id& key) const noexcept { return static_cast<size_t>(key.high ^ key.low); } };
struct Gui {
    uint64_t handle = 0, platform = 0, window = 0, frames = 0, generation = 0, revision = 0, lastFrame = 0, viewGeneration = 0;
    ImGuiContext* context = nullptr;
    std::string fontPath, iniPath;
    float fontSize = 18, rasterScale = 1;
    ImVector<ImWchar> fontRanges;
    int theme = 0;
    uint32_t chromeMask = 0;
    bool frameActive = false, cancel = false, gpuDrawReady = false;
    uint64_t rendererModule = 0, rendererHandle = 0;
    std::array<NcmaGpuResourceV3,64> images{};
    std::array<bool,64> cachedImages{};
    uint32_t imageCount=0;
    std::vector<uint8_t> toolbarPixels;
    Microsoft::WRL::ComPtr<ID3D11ShaderResourceView> toolbarIcon;
    std::unordered_map<Id, std::array<char, 1024>, Hash> textDrafts;
    std::unordered_map<Id, double, Hash> numberDrafts;
    std::unordered_set<Id, Hash> textPending;
};
std::unique_ptr<Gui> gui;
uint32_t Validate(uint64_t context, NcmaErrorV1* error)
{
    if (std::this_thread::get_id() != loadingThread) return NcmaPlugin::Error(error, NCMA_WRONG_THREAD);
    if (!module || module != context) return NcmaPlugin::Error(error, NCMA_INVALID_HANDLE);
    if (busy) return NcmaPlugin::Error(error, NCMA_BUSY);
    return NCMA_OK;
}
uint32_t ValidateGui(uint64_t context, uint64_t handle, NcmaErrorV1* error)
{
    auto valid = Validate(context, error); if (valid) return valid;
    if (!gui || gui->handle != handle) return NcmaPlugin::Error(error, NCMA_INVALID_HANDLE);
    ImGui::SetCurrentContext(gui->context); return NCMA_OK;
}
struct BusyScope { BusyScope() { busy = true; } ~BusyScope() { busy = false; } };
uint32_t ReleaseImages(Gui& g,NcmaErrorV1* error) {
    while(g.imageCount){const auto i=g.imageCount-1;auto result=g.cachedImages[i]?ncma_renderer_release_gui_cached_image_v1(g.rendererModule,g.rendererHandle,g.images[i],error):ncma_renderer_release_gui_image_v1(g.rendererModule,g.rendererHandle,g.images[i],error);if(result)return result;g.imageCount--;}
    return NCMA_OK;
}
NcmaGpuResourceV3 ImageToken(const NcmaGuiItemV1& item) {
    return {item.reserved[0]|(static_cast<uint64_t>(item.reserved[1])<<32),item.reserved[2]|(static_cast<uint64_t>(item.reserved[3])<<32)};
}
uint32_t NCMA_CALL Initialize(const uint8_t* input, uint32_t length, uint64_t* output, NcmaErrorV1* error) noexcept
{
    return NcmaPlugin::Guard(error, [&]() -> uint32_t {
        if (std::this_thread::get_id() != loadingThread) return NcmaPlugin::Error(error, NCMA_WRONG_THREAD);
        if (!output || input || length) return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT);
        if (module || busy) return NcmaPlugin::Error(error, NCMA_BUSY);
        module = 0x4755000000000000ull | nextModule++; *output = module; return NCMA_OK;
    });
}
uint32_t NCMA_CALL Shutdown(uint64_t context, NcmaErrorV1* error) noexcept
{
    return NcmaPlugin::Guard(error, [&]() -> uint32_t {
        auto valid = Validate(context, error); if (valid) return valid;
        if (gui) return NcmaPlugin::Error(error, NCMA_BUSY);
        module = 0; return NCMA_OK;
    });
}
uint32_t NCMA_CALL Status(uint64_t context, NcmaModuleStatusV1* output, NcmaErrorV1* error) noexcept
{
    return NcmaPlugin::Guard(error, [&]() -> uint32_t {
        if (!output) return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT);
        auto valid = Validate(context, error); if (valid) return valid;
        *output = {sizeof(*output), 1, gui ? 1u : 0u, 0, gui ? gui->frames : 0}; return NCMA_OK;
    });
}
uint32_t NCMA_CALL Diagnostic(uint64_t context, uint8_t* output, uint32_t capacity, uint32_t* required, NcmaErrorV1* error) noexcept
{
    return NcmaPlugin::Guard(error, [&]() -> uint32_t {
        auto valid = Validate(context, error); if (valid) return valid;
        return NcmaPlugin::Diagnostic(context, output, capacity, required, error);
    });
}
uint32_t NCMA_CALL Create(uint64_t context, const NcmaGuiDescriptionV1* desc, uint64_t* output, NcmaErrorV1* error) noexcept
{
    return NcmaPlugin::Guard(error, [&]() -> uint32_t {
        auto valid = Validate(context, error); if (valid) return valid;
        auto candidate = std::make_unique<Gui>();
        if (!desc || !output || desc->struct_size != sizeof(*desc) || desc->reserved || desc->reserved2 ||
            !std::isfinite(desc->font_size) || desc->font_size < 8 || desc->font_size > 64 ||
            !NcmaPlugin::CopyUtf8(desc->font_path, desc->font_path_length, 4096, candidate->fontPath) ||
            !NcmaPlugin::CopyUtf8(desc->ini_path, desc->ini_path_length, 4096, candidate->iniPath))
            return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT);
        if (gui) return NcmaPlugin::Error(error, NCMA_BUSY);
        GLFWwindow* pointer = nullptr;
        valid = ncma_platform_borrow_window_v1(desc->platform_module, desc->window, &pointer, error); if (valid) return valid;
        BusyScope scope; bool backend = false;
        try {
            candidate->platform = desc->platform_module; candidate->window = desc->window; candidate->fontSize = desc->font_size;
            candidate->context = ImGui::CreateContext(); ImGui::SetCurrentContext(candidate->context);
            ImGuiIO& io = ImGui::GetIO();
            io.IniFilename = candidate->iniPath.empty() ? nullptr : candidate->iniPath.c_str();
            io.LogFilename = nullptr;
            if (!candidate->fontPath.empty()) {
                std::ifstream font(candidate->fontPath, std::ios::binary);
                std::array<char, 4> signature{};
                if (!font.read(signature.data(), 4) ||
                    !(signature == std::array<char, 4>{0, 1, 0, 0} || signature == std::array<char, 4>{'t', 't', 'c', 'f'} ||
                      signature == std::array<char, 4>{'O', 'T', 'T', 'O'}))
                    throw std::runtime_error("GUI font is missing or not a trusted TTF/TTC/OTF.");
                ImFontGlyphRangesBuilder ranges;ranges.AddRanges(io.Fonts->GetGlyphRangesChineseSimplifiedCommon());
                ranges.AddText(reinterpret_cast<const char*>(u8"编辑资源浏览器属性检查场景对象未命名项目动作测试启用本机暂停继续固定步运行删除确认类型上一页下一页控制台菜单偏好变换脚本窗口帮助▾…"));ranges.BuildRanges(&candidate->fontRanges);
                if (!io.Fonts->AddFontFromFileTTF(candidate->fontPath.c_str(), candidate->fontSize, nullptr, candidate->fontRanges.Data))
                    throw std::runtime_error("Requested GUI font could not be loaded.");
            } else { ImFontConfig config; config.SizePixels = candidate->fontSize; io.Fonts->AddFontDefault(&config); } // Latin-only fallback.
            unsigned char* pixels = nullptr; int width = 0, height = 0;
            io.Fonts->GetTexDataAsRGBA32(&pixels, &width, &height);
            if (!pixels || !width || !height) throw std::runtime_error("Font atlas build failed.");
            ImGui::StyleColorsDark();
            if (!ImGui_ImplGlfw_InitForOther(pointer, true)) throw std::runtime_error("GLFW GUI backend initialization failed.");
            backend = true;
            candidate->handle = 0x4947000000000000ull | nextGui++;
            *output = candidate->handle; gui = std::move(candidate); return NCMA_OK;
        } catch (...) {
            if (backend) ImGui_ImplGlfw_Shutdown();
            if (candidate->context) ImGui::DestroyContext(candidate->context);
            NcmaErrorV1 ignored{}; ncma_platform_release_window_v1(desc->platform_module, desc->window, &ignored); throw;
        }
    });
}
uint32_t NCMA_CALL Begin(uint64_t context, uint64_t handle, const NcmaWindowStateV1* state, double delta,
    NcmaGuiCaptureV1* output, NcmaErrorV1* error) noexcept
{
    return NcmaPlugin::Guard(error, [&]() -> uint32_t {
        auto valid = ValidateGui(context, handle, error); if (valid) return valid;
        if (!state || !output || state->struct_size != sizeof(*state) || state->focused > 1 || state->overflow > 1 || state->input_reset > 1 ||
            state->width > 16384 || state->height > 16384 || !std::isfinite(delta) || delta < 0 ||
            !std::isfinite(state->pointer_x) || !std::isfinite(state->pointer_y) ||
            !std::isfinite(state->scale_x) || !std::isfinite(state->scale_y) || state->scale_x <= 0 || state->scale_y <= 0)
            return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT);
        if (gui->frameActive) return NcmaPlugin::Error(error, NCMA_BUSY);
        if (state->scale_x > 8 || state->scale_y > 8) return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT);
        BusyScope scope;
        valid=ReleaseImages(*gui,error);if(valid)return valid;
        gui->gpuDrawReady = false;
        float scale = std::max(state->scale_x, state->scale_y);
        if (std::abs(scale - gui->rasterScale) > 0.01f) {
            if (gui->rendererHandle) ImGui_ImplDX11_InvalidateDeviceObjects();
            auto& io = ImGui::GetIO(); io.Fonts->Clear();
            if (!gui->fontPath.empty()) {
                if (!io.Fonts->AddFontFromFileTTF(gui->fontPath.c_str(), gui->fontSize * scale, nullptr, gui->fontRanges.Data))
                    throw std::runtime_error("DPI font atlas rebuild failed.");
            } else { ImFontConfig config; config.SizePixels = gui->fontSize * scale; io.Fonts->AddFontDefault(&config); }
            unsigned char* pixels = nullptr; int width = 0, height = 0; io.Fonts->GetTexDataAsRGBA32(&pixels, &width, &height);
            if (!pixels) throw std::runtime_error("DPI font atlas rebuild failed.");
            io.FontGlobalScale = 1 / scale; gui->rasterScale = scale;
        }
        gui->cancel = !state->focused || state->overflow || state->input_reset;
        if (gui->cancel) { ImGui::ClearActiveID(); gui->textDrafts.clear(); gui->numberDrafts.clear(); gui->textPending.clear(); }
        if (gui->rendererHandle) ImGui_ImplDX11_NewFrame();
        ImGui_ImplGlfw_NewFrame();
        auto& io = ImGui::GetIO();
        io.DisplaySize = ImVec2(static_cast<float>(state->width), static_cast<float>(state->height));
        io.DisplayFramebufferScale = ImVec2(state->width ? static_cast<float>(state->framebuffer_width) / static_cast<float>(state->width) : 1,
            state->height ? static_cast<float>(state->framebuffer_height) / static_cast<float>(state->height) : 1);
        io.DeltaTime = static_cast<float>(std::clamp(delta, 0.000001, 1.0));
        ImGui::NewFrame(); gui->frameActive = true;
        if (ImGui::IsKeyPressed(ImGuiKey_Escape, false)) {
            gui->cancel = true; ImGui::ClearActiveID(); gui->textDrafts.clear(); gui->numberDrafts.clear(); gui->textPending.clear();
        }
        // Bootstrap frame captures all gameplay input; subsequent frames use NewFrame's current input.
        *output = {sizeof(*output), (io.WantCaptureKeyboard || !gui->frames) ? 1u : 0u,
            (io.WantCaptureMouse || !gui->frames) ? 1u : 0u, gui->cancel ? 1u : 0u};
        return NCMA_OK;
    });
}
bool Text(const uint8_t* bytes, uint32_t total, uint32_t offset, uint32_t length, std::string& result, uint32_t max)
{
    return offset <= total && length <= total - offset && NcmaPlugin::CopyUtf8(length ? bytes + offset : nullptr, length, max, result);
}
uint32_t NCMA_CALL Draw(uint64_t context, uint64_t handle, const NcmaGuiFrameV1* frame, const NcmaGuiItemV1* items,
    const uint8_t* text, NcmaGuiEventV1* events, uint32_t capacity, uint32_t* count, uint8_t* outputText, uint32_t textCapacity,
    NcmaGuiStatsV1* stats, NcmaErrorV1* error) noexcept
{
    return NcmaPlugin::Guard(error, [&]() -> uint32_t {
        auto valid = ValidateGui(context, handle, error); if (valid) return valid;
        if (!gui->frameActive) return NcmaPlugin::Error(error, NCMA_BUSY);
        if(gui->imageCount)return NcmaPlugin::Error(error,NCMA_BUSY,"Previous failed image draw requires discard/shutdown.");
        if (!events || capacity < NCMA_GUI_MAX_EVENTS || !count || !stats || !outputText || textCapacity < NCMA_GUI_OUTPUT_TEXT_BYTES)
            return NcmaPlugin::Error(error, NCMA_BUFFER_TOO_SMALL);
        if (!frame || frame->struct_size != sizeof(*frame) || frame->reserved || frame->item_count > NCMA_GUI_MAX_ITEMS ||
            frame->text_bytes > NCMA_GUI_MAX_TEXT_BYTES - frame->item_count * sizeof(NcmaGuiItemV1) ||
            (frame->item_count && !items) || (frame->text_bytes && !text) || !frame->frame || !frame->view_generation ||
            !frame->document_generation || frame->frame <= gui->lastFrame || frame->document_generation < gui->generation ||
            frame->view_generation < gui->viewGeneration || (frame->document_generation == gui->generation && frame->revision < gui->revision))
            return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT);
        std::vector<std::string> labels, values; labels.reserve(frame->item_count); values.reserve(frame->item_count);
        std::unordered_set<Id, Hash> ids;
        int panels = 0, canvases = 0, toolbar = 0; uint64_t expandedText = 0;
        std::array<float,4> toolbarBounds{};
        std::vector<std::vector<float>> lines(frame->item_count);
        std::vector<ID3D11ShaderResourceView*> imageViews(frame->item_count,nullptr);
        uint32_t imageCount=0;
        uint32_t segments = 0;
        for (uint32_t i = 0; i < frame->item_count; i++) {
            const auto& item = items[i]; std::string label, value;
            expandedText += static_cast<uint64_t>(item.label_length) + item.text_length;
            if (expandedText + frame->item_count * sizeof(NcmaGuiItemV1) > NCMA_GUI_MAX_TEXT_BYTES) return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT);
            if (item.kind < NCMA_GUI_PANEL_BEGIN || item.kind > NCMA_GUI_SELECTION_BUTTON || item.enabled > 1 ||
                (item.kind!=NCMA_GUI_IMAGE&&item.kind!=NCMA_GUI_CACHED_IMAGE&&(item.reserved[0] || item.reserved[1] || item.reserved[2] || item.reserved[3])) ||
                !std::isfinite(item.value) || !std::isfinite(item.minimum) || !std::isfinite(item.maximum) || item.minimum > item.maximum ||
                !Text(text, frame->text_bytes, item.label_offset, item.label_length, label, 4096) ||
                !Text(text, frame->text_bytes, item.text_offset, item.text_length, value, 1023))
                return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT);
            for (float number : item.rect) if (!std::isfinite(number)) return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT);
            if(item.kind==NCMA_GUI_SPLITTER&&((item.value!=0&&item.value!=1)||item.rect[2]<=0||item.rect[3]<=0||item.rect[2]>16384||item.rect[3]>16384))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
            if (item.kind == NCMA_GUI_PANEL_BEGIN || item.kind==NCMA_GUI_TOOLBAR_BEGIN) {
                if (panels || item.rect[2] <= 0 || item.rect[3] <= 0 || item.rect[2] > 16384 || item.rect[3] > 16384) return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT);
                panels++;
                if(item.kind==NCMA_GUI_TOOLBAR_BEGIN){if(item.rect[3]<40||item.rect[3]>96)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);toolbar=1;std::copy_n(item.rect,4,toolbarBounds.begin());}
            } else if(item.kind==NCMA_GUI_TOOLBAR_END) {
                if(!toolbar||!panels||canvases)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);panels--;toolbar=0;
            } else if (item.kind == NCMA_GUI_PANEL_END) {
                if (!panels || canvases || toolbar) return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT); panels--;
            } else if (!panels) return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT);
            if((item.kind>=NCMA_GUI_TOOLBAR_BUTTON&&item.kind<=NCMA_GUI_TOOLBAR_DIVIDER)||item.kind==NCMA_GUI_MENU_BUTTON||item.kind==NCMA_GUI_MENU_BRAND) {
                if(!toolbar||item.rect[2]<=0||item.rect[3]<=0||item.rect[0]<toolbarBounds[0]||item.rect[1]<toolbarBounds[1]||
                   item.rect[0]+item.rect[2]>toolbarBounds[0]+toolbarBounds[2]||item.rect[1]+item.rect[3]>toolbarBounds[1]+toolbarBounds[3])return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
                if((item.kind==NCMA_GUI_TOOLBAR_BUTTON||item.kind==NCMA_GUI_MENU_BUTTON)&&(item.value<0||item.value>(item.kind==NCMA_GUI_MENU_BUTTON?14:12)||std::floor(item.value)!=item.value||item.minimum<0||item.minimum>2||std::floor(item.minimum)!=item.minimum||item.maximum!=2))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
            } else if(toolbar&&item.kind!=NCMA_GUI_TOOLBAR_BEGIN)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
            if (item.kind == NCMA_GUI_THEME && (item.value < 0 || item.value > 3 || std::floor(item.value)!=item.value))
                return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
            if (item.kind == NCMA_GUI_OVERLAY_BEGIN) {
                if(canvases||item.rect[2]<=0||item.rect[2]>16384||item.rect[3]<=0||item.rect[3]>16384)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
                canvases=2;
            } else if (item.kind == NCMA_GUI_CANVAS_BEGIN) {
                if (canvases || item.rect[3] < 1 || item.rect[3] > 4096) return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT);
                canvases = 1;
            } else if (item.kind == NCMA_GUI_CANVAS_END) {
                if (!canvases) return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT);
                canvases = 0;
            } else if (item.kind == NCMA_GUI_CANVAS_LINES) {
                if (!canvases || item.value < 0 || item.value > 4294967295.0 || std::floor(item.value) != item.value ||
                    item.minimum < 0.5 || item.minimum > 8) return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT);
                const char* cursor = value.data(); const char* end = cursor + value.size();
                while (cursor != end) {
                    if (*cursor == ' ') { ++cursor; continue; }
                    float coordinate = 0;
                    auto parsed = std::from_chars(cursor, end, coordinate);
                    if (parsed.ec != std::errc{} || parsed.ptr == cursor || !std::isfinite(coordinate) || std::abs(coordinate) > 64 ||
                        (parsed.ptr != end && *parsed.ptr != ' ') || lines[i].size() >= 64) return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT);
                    lines[i].push_back(coordinate); cursor = parsed.ptr;
                }
                if (lines[i].empty() || lines[i].size() % 4 || (segments += static_cast<uint32_t>(lines[i].size() / 4)) > 32768)
                    return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT);
            } else if (canvases && !(canvases==2&&item.kind==NCMA_GUI_CACHED_IMAGE)) return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT);
            if(item.kind==NCMA_GUI_IMAGE||item.kind==NCMA_GUI_CACHED_IMAGE) {
                if(++imageCount>64||item.rect[2]<=0||item.rect[3]<=0||item.rect[2]>16384||item.rect[3]>16384||!label.empty()||!value.empty()||!gui->rendererHandle)
                    return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
                valid=item.kind==NCMA_GUI_CACHED_IMAGE?ncma_renderer_gui_cached_image_v1(gui->rendererModule,gui->rendererHandle,ImageToken(item),frame->frame,0,&imageViews[i],error):ncma_renderer_gui_image_v1(gui->rendererModule,gui->rendererHandle,ImageToken(item),0,0,&imageViews[i],error);if(valid)return valid;
            }
            if(item.kind==NCMA_GUI_ASSET_BUTTON) {
                if(value.size()!=36)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
                for(size_t j=0;j<36;j++)if(j==8||j==13||j==18||j==23){if(value[j]!='-')return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);}
                    else if(!((value[j]>='0'&&value[j]<='9')||(value[j]>='a'&&value[j]<='f')))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
            }
            if (item.kind != NCMA_GUI_PANEL_END && item.kind!=NCMA_GUI_TOOLBAR_END && (!ids.insert({item.widget_high, item.widget_low}).second || (!item.widget_high && !item.widget_low)))
                return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT);
            labels.push_back(std::move(label)); values.push_back(std::move(value));
        }
        if (panels || canvases || toolbar) return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT);
        BusyScope scope;
        // Complete preflight above precedes all resource retention and ImGui mutation.
        for(uint32_t i=0;i<frame->item_count;i++)if(items[i].kind==NCMA_GUI_IMAGE||items[i].kind==NCMA_GUI_CACHED_IMAGE) {
            const bool cached=items[i].kind==NCMA_GUI_CACHED_IMAGE;
            valid=cached?ncma_renderer_gui_cached_image_v1(gui->rendererModule,gui->rendererHandle,ImageToken(items[i]),frame->frame,1,&imageViews[i],error):ncma_renderer_gui_image_v1(gui->rendererModule,gui->rendererHandle,ImageToken(items[i]),0,1,&imageViews[i],error);
            if(valid){NcmaErrorV1 ignored{};ReleaseImages(*gui,&ignored);return valid;}
            gui->cachedImages[gui->imageCount]=cached;gui->images[gui->imageCount++]=ImageToken(items[i]);
        }
        if (gui->generation != frame->document_generation || gui->viewGeneration != frame->view_generation) {
            ImGui::ClearActiveID(); gui->textDrafts.clear(); gui->numberDrafts.clear(); gui->textPending.clear(); gui->cancel = true;
        }
        gui->generation = frame->document_generation; gui->revision = frame->revision; gui->viewGeneration = frame->view_generation;
        uint32_t written = 0, textWritten = 0; bool overflow = false, panelVisible = false;
        auto emit = [&](const NcmaGuiItemV1& item, uint32_t phase, double value, const char* input = nullptr) {
            if (overflow || gui->cancel) return;
            uint32_t length = input ? static_cast<uint32_t>(std::strlen(input)) : 0;
            if (written == NCMA_GUI_MAX_EVENTS || length > NCMA_GUI_OUTPUT_TEXT_BYTES - textWritten) { overflow = true; return; }
            events[written++] = {item.kind, phase, item.widget_high, item.widget_low, value, frame->frame, frame->view_generation,
                frame->document_generation, frame->revision, textWritten, length};
            if (length) std::memcpy(outputText + textWritten, input, length);
            textWritten += length;
        };
        ImVec2 canvasOrigin{}, canvasSize{};bool overlayCanvas=false;
        bool splitterPanel=false;
        for (uint32_t i = 0; i < frame->item_count; i++) {
            const auto& item = items[i];
            if(item.kind==NCMA_GUI_TOOLBAR_BEGIN) {
                ImGui::PushStyleColor(ImGuiCol_WindowBg,IM_COL32(11,20,32,255));
                ImGui::PushStyleVar(ImGuiStyleVar_WindowPadding,{0,0});ImGui::PushStyleVar(ImGuiStyleVar_WindowBorderSize,0);
                ImGui::PushStyleVar(ImGuiStyleVar_WindowRounding,0);
                ImGui::SetNextWindowPos({item.rect[0],item.rect[1]},ImGuiCond_Always);ImGui::SetNextWindowSize({item.rect[2],item.rect[3]},ImGuiCond_Always);
                std::string name="###toolbar:"+std::to_string(item.widget_high)+":"+std::to_string(item.widget_low);
                panelVisible=ImGui::Begin(name.c_str(),nullptr,ImGuiWindowFlags_NoMove|ImGuiWindowFlags_NoResize|ImGuiWindowFlags_NoTitleBar|ImGuiWindowFlags_NoScrollbar|ImGuiWindowFlags_NoScrollWithMouse|ImGuiWindowFlags_NoSavedSettings);
                ImGui::GetWindowDrawList()->AddLine({item.rect[0],item.rect[1]+item.rect[3]-1},{item.rect[0]+item.rect[2],item.rect[1]+item.rect[3]-1},IM_COL32(40,61,81,255));continue;
            }
            if(item.kind==NCMA_GUI_TOOLBAR_END){ImGui::End();ImGui::PopStyleVar(3);ImGui::PopStyleColor();continue;}
            if (item.kind == NCMA_GUI_PANEL_BEGIN) {
                splitterPanel=item.value==3;if(splitterPanel)ImGui::PushStyleVar(ImGuiStyleVar_WindowPadding,{0,0});
                ImGui::SetNextWindowPos(ImVec2(item.rect[0], item.rect[1]), ImGuiCond_Always);
                ImGui::SetNextWindowSize(ImVec2(item.rect[2], item.rect[3]), ImGuiCond_Always);
                std::string name = labels[i] + "###" + std::to_string(item.widget_high) + ":" + std::to_string(item.widget_low);
                const auto flags=ImGuiWindowFlags_NoMove | ImGuiWindowFlags_NoResize | ImGuiWindowFlags_NoCollapse | (item.value==4?ImGuiWindowFlags_NoScrollbar|ImGuiWindowFlags_NoScrollWithMouse:ImGuiWindowFlags_HorizontalScrollbar) |
                    (gui->theme==3&&item.value!=2?ImGuiWindowFlags_NoBringToFrontOnFocus:0);
                if(gui->theme==3&&item.value==2)ImGui::SetNextWindowFocus();
                panelVisible = ImGui::Begin(name.c_str(), nullptr, flags | (splitterPanel?ImGuiWindowFlags_NoTitleBar|ImGuiWindowFlags_NoBackground|ImGuiWindowFlags_NoScrollbar|ImGuiWindowFlags_NoScrollWithMouse:gui->theme==3&&item.value==1?ImGuiWindowFlags_NoTitleBar:0));
                if(gui->theme==3&&item.value!=1&&!values[i].empty()) {
                    const auto position=ImGui::GetWindowPos();const float width=ImGui::GetWindowSize().x,height=ImGui::GetFrameHeight();
                    const float titleRight=position.x+8+ImGui::CalcTextSize(labels[i].c_str()).x+12;
                    const float right=position.x+width-8;
                    if(titleRight<right) {
                        constexpr float fontSize=12;
                        const float textWidth=ImGui::GetFont()->CalcTextSizeA(fontSize,FLT_MAX,0,values[i].c_str()).x;
                        auto* draw=ImGui::GetWindowDrawList();
                        draw->PushClipRect({titleRight,position.y},{right,position.y+height},false);
                        draw->AddText(ImGui::GetFont(),fontSize,{right-textWidth,position.y+(height-fontSize)*.5f},IM_COL32(147,167,189,255),values[i].c_str());
                        draw->PopClipRect();
                    }
                }
                continue;
            }
            if (item.kind == NCMA_GUI_PANEL_END) { ImGui::End();if(splitterPanel){ImGui::PopStyleVar();splitterPanel=false;}continue; }
            if (!panelVisible) continue;
            if (item.kind == NCMA_GUI_THEME) {
                if (gui->theme != static_cast<int>(item.value)) {
                    gui->theme = static_cast<int>(item.value);
                    if (gui->theme == 0) ImGui::StyleColorsDark();
                    else if (gui->theme == 1) ImGui::StyleColorsLight();
                    else if(gui->theme==2) ImGui::StyleColorsClassic();
                    else NcmaToolbar::WorkspaceTheme();
                    // Theme3 is explicitly requested by C#; legacy GUI themes keep system chrome.
                    const auto hwnd = static_cast<HWND>(ImGui::GetMainViewport()->PlatformHandleRaw);
                    if(gui->theme==3||gui->chromeMask)gui->chromeMask=NcmaChrome::Apply(hwnd,gui->theme==3);
                }
                continue;
            }
            if (item.kind == NCMA_GUI_SAME_LINE) { ImGui::SameLine(); continue; }
            if(item.kind==NCMA_GUI_OVERLAY_BEGIN){
                canvasOrigin={item.rect[0],item.rect[1]};canvasSize={item.rect[2],item.rect[3]};
                overlayCanvas=true;ImGui::PushClipRect(canvasOrigin,{canvasOrigin.x+canvasSize.x,canvasOrigin.y+canvasSize.y},true);continue;
            }
            if (item.kind == NCMA_GUI_CANVAS_BEGIN) {
                canvasOrigin = ImGui::GetCursorScreenPos();
                canvasSize = {std::max(1.0f, ImGui::GetContentRegionAvail().x), item.rect[3]};
                ImGui::PushID(reinterpret_cast<const char*>(&item.widget_high), reinterpret_cast<const char*>(&item.widget_high) + 16);
                ImGui::InvisibleButton("##canvas", canvasSize); ImGui::PopID();
                auto* draw = ImGui::GetWindowDrawList();
                draw->PushClipRect(canvasOrigin, {canvasOrigin.x + canvasSize.x, canvasOrigin.y + canvasSize.y}, true);
                draw->AddRectFilled(canvasOrigin, {canvasOrigin.x + canvasSize.x, canvasOrigin.y + canvasSize.y}, IM_COL32(16,21,30,255));
                continue;
            }
            if (item.kind == NCMA_GUI_CANVAS_END) { if(overlayCanvas){ImGui::PopClipRect();overlayCanvas=false;}else ImGui::GetWindowDrawList()->PopClipRect(); continue; }
            if (item.kind == NCMA_GUI_CANVAS_LINES) {
                const auto& coordinates = lines[i];
                for (size_t j = 0; j < coordinates.size(); j += 4) {
                    ImVec2 a{canvasOrigin.x + coordinates[j]*canvasSize.x, canvasOrigin.y + coordinates[j+1]*canvasSize.y};
                    ImVec2 b{canvasOrigin.x + coordinates[j+2]*canvasSize.x, canvasOrigin.y + coordinates[j+3]*canvasSize.y};
                    if (a.x == b.x && a.y == b.y) ImGui::GetWindowDrawList()->AddCircleFilled(a, static_cast<float>(item.minimum), static_cast<ImU32>(item.value));
                    else ImGui::GetWindowDrawList()->AddLine(a,b,static_cast<ImU32>(item.value),static_cast<float>(item.minimum));
                }
                continue;
            }
            ImGui::PushID(reinterpret_cast<const char*>(&item.widget_high), reinterpret_cast<const char*>(&item.widget_high) + 16);
            ImGui::BeginDisabled(!item.enabled&&item.kind!=NCMA_GUI_IMAGE&&item.kind!=NCMA_GUI_CACHED_IMAGE);
            std::string label = labels[i] + "###value";
            const Id widget{item.widget_high, item.widget_low};
            double value = item.value;
            if (auto draft = gui->numberDrafts.find(widget); draft != gui->numberDrafts.end()) value = draft->second;
            const char* input = nullptr; bool changed = false;
            if(gui->theme==3&&(item.kind==NCMA_GUI_NUMBER||item.kind==NCMA_GUI_TEXT)){
                ImGui::PushTextWrapPos(0);ImGui::TextUnformatted(labels[i].c_str());ImGui::PopTextWrapPos();
                label="###value";ImGui::SetNextItemWidth(-1);
            }
            switch (item.kind) {
                case NCMA_GUI_SPLITTER: {
                    ImGui::SetCursorScreenPos({item.rect[0],item.rect[1]});ImGui::InvisibleButton("##splitter",{item.rect[2],item.rect[3]});
                    if(ImGui::IsItemHovered()||ImGui::IsItemActive())ImGui::SetMouseCursor(item.value==0?ImGuiMouseCursor_ResizeEW:ImGuiMouseCursor_ResizeNS);
                    if(item.enabled){const auto& pointer=ImGui::GetIO().MousePos;double position=std::clamp(static_cast<double>(item.value==0?pointer.x:pointer.y),item.minimum,item.maximum);
                        if(ImGui::IsItemActivated())emit(item,1,position);else if(ImGui::IsItemDeactivated())emit(item,3,position);else if(ImGui::IsItemActive())emit(item,2,position);}
                    break;
                }
                case NCMA_GUI_TOOLBAR_BUTTON:
                    if(NcmaToolbar::Button("##tool",labels[i],values[i],item))emit(item,3,value);break;
                case NCMA_GUI_MENU_BUTTON:
                    if(NcmaToolbar::MenuButton("##menu",labels[i],values[i],item))emit(item,3,value);break;
                case NCMA_GUI_MENU_BRAND: {
                    auto* d=ImGui::GetWindowDrawList();const float x=item.rect[0],y=item.rect[1],height=item.rect[3];
                    d->PushClipRect({x,y},{x+item.rect[2],y+height},true);
                    d->AddText(ImGui::GetFont(),17,{x,y+(height-17)*.5f},IM_COL32(223,233,243,255),labels[i].c_str());
                    d->PopClipRect();break;
                }
                case NCMA_GUI_TOOLBAR_BRAND: {
                    auto* d=ImGui::GetWindowDrawList();const float x=item.rect[0],y=item.rect[1];float start=x;
                    d->PushClipRect({x,y},{x+item.rect[2],y+item.rect[3]},true);
                    if(gui->toolbarIcon){d->AddImage(static_cast<ImTextureID>(reinterpret_cast<uintptr_t>(gui->toolbarIcon.Get())),{x,y+4},{x+36,y+40});start+=48;}
                    d->AddText(ImGui::GetFont(),19,{start,y+5},IM_COL32(223,234,245,255),labels[i].c_str());
                    d->AddText(ImGui::GetFont(),11,{start,y+30},IM_COL32(116,146,173,255),values[i].c_str());d->PopClipRect();break;
                }
                case NCMA_GUI_TOOLBAR_DIVIDER:
                    ImGui::GetWindowDrawList()->AddLine({item.rect[0],item.rect[1]},{item.rect[0],item.rect[1]+item.rect[3]},IM_COL32(42,67,91,255));break;
                case NCMA_GUI_CACHED_IMAGE: {
                    ImGui::SetCursorScreenPos({item.rect[0],item.rect[1]});
                    ImGui::GetWindowDrawList()->AddImage(static_cast<ImTextureID>(reinterpret_cast<uintptr_t>(imageViews[i])),{item.rect[0],item.rect[1]},{item.rect[0]+item.rect[2],item.rect[1]+item.rect[3]});
                    ImGui::InvisibleButton("##ui_canvas",{item.rect[2],item.rect[3]},ImGuiButtonFlags_MouseButtonLeft|ImGuiButtonFlags_MouseButtonMiddle);
                    if(item.enabled) {
                        const auto& io=ImGui::GetIO();std::array<char,128> coordinates{};
                        auto u=std::to_chars(coordinates.data(),coordinates.data()+40,std::clamp((io.MousePos.x-item.rect[0])/item.rect[2],-64.0f,64.0f));
                        if(u.ec==std::errc{}){*u.ptr++=' ';auto v=std::to_chars(u.ptr,coordinates.data()+90,std::clamp((io.MousePos.y-item.rect[1])/item.rect[3],-64.0f,64.0f));
                            if(v.ec==std::errc{}){*v.ptr++=' ';auto wheel=std::to_chars(v.ptr,coordinates.data()+127,std::clamp(io.MouseWheel,-32.0f,32.0f));
                                if(wheel.ec==std::errc{}){
                                    double flags=(io.KeyCtrl?1:0)+(io.KeyShift?2:0)+(io.KeyAlt?4:0)+(ImGui::IsKeyDown(ImGuiKey_Space)?8:0)+(io.MouseDown[2]?16:0);
                                    if(ImGui::IsItemActivated())emit(item,1,flags,coordinates.data());
                                    else if(ImGui::IsItemDeactivated())emit(item,3,flags,coordinates.data());
                                    else if(ImGui::IsItemActive()&&(io.MouseDelta.x!=0||io.MouseDelta.y!=0))emit(item,2,flags,coordinates.data());
                                    if(ImGui::IsItemHovered()&&io.MouseWheel!=0)emit(item,3,flags+64,coordinates.data());
                                }
                            }
                        }
                    }
                    break;
                }
                case NCMA_GUI_IMAGE: {
                    ImGui::SetCursorScreenPos({item.rect[0],item.rect[1]});
                    ImGui::Image(static_cast<ImTextureID>(reinterpret_cast<uintptr_t>(imageViews[i])),{item.rect[2],item.rect[3]});
                    if(item.enabled&&ImGui::IsItemClicked(ImGuiMouseButton_Left)) {
                        const auto pointer=ImGui::GetIO().MousePos;
                        std::array<char,96> coordinates{};
                        auto u=std::to_chars(coordinates.data(),coordinates.data()+40,std::clamp((pointer.x-item.rect[0])/item.rect[2],0.0f,1.0f));
                        if(u.ec==std::errc{}){*u.ptr++=' ';auto v=std::to_chars(u.ptr,coordinates.data()+95,std::clamp((pointer.y-item.rect[1])/item.rect[3],0.0f,1.0f));if(v.ec==std::errc{})emit(item,3,0,coordinates.data());}
                    }
                    if(item.enabled&&ImGui::BeginDragDropTarget()) {
                        if(const auto* payload=ImGui::AcceptDragDropPayload("NCMA_MODEL_UUID_V1"))if(payload->DataSize==37)emit(item,3,1,static_cast<const char*>(payload->Data));
                        ImGui::EndDragDropTarget();
                    }
                    break;
                }
                case NCMA_GUI_LABEL:
                    if(gui->theme==3){ImGui::PushTextWrapPos(0);ImGui::TextUnformatted(labels[i].c_str());ImGui::PopTextWrapPos();}
                    else ImGui::TextUnformatted(labels[i].c_str());break;
                case NCMA_GUI_BUTTON:
                    if (ImGui::Button(label.c_str())) emit(item, 3, value);
                    break;
                case NCMA_GUI_SELECTION_BUTTON:
                    if(ImGui::Button(label.c_str()))emit(item,3,(ImGui::GetIO().KeyCtrl?1:0)|(ImGui::GetIO().KeyShift?2:0));
                    break;
                case NCMA_GUI_ASSET_BUTTON:
                    if(ImGui::Button(label.c_str()))emit(item,3,value);
                    if(item.enabled&&ImGui::BeginDragDropSource()) {
                        ImGui::SetDragDropPayload("NCMA_MODEL_UUID_V1",values[i].c_str(),37);ImGui::TextUnformatted(labels[i].c_str());ImGui::EndDragDropSource();
                    }
                    break;
                case NCMA_GUI_NUMBER:
                    changed = ImGui::SliderScalar(label.c_str(), ImGuiDataType_Double, &value, &item.minimum, &item.maximum);
                    break;
                case NCMA_GUI_CHECKBOX: {
                    bool flag = value != 0; changed = ImGui::Checkbox(label.c_str(), &flag); value = flag ? 1 : 0; break;
                }
                case NCMA_GUI_TEXT: {
                    auto [position, inserted] = gui->textDrafts.try_emplace(widget);
                    auto& draft = position->second;
                    if (inserted || (!gui->textPending.contains(widget) && ImGui::GetActiveID() != ImGui::GetID(label.c_str()))) {
                        draft.fill(0); std::memcpy(draft.data(), values[i].data(), values[i].size());
                    }
                    changed = ImGui::InputText(label.c_str(), draft.data(), draft.size()); input = draft.data(); break;
                }
                default: break;
            }
            if (item.kind >= NCMA_GUI_NUMBER && item.kind <= NCMA_GUI_TEXT) {
                if (ImGui::IsItemActivated()) { if (item.kind == NCMA_GUI_TEXT) gui->textPending.insert(widget); emit(item, 1, value, input); }
                if (changed) { gui->numberDrafts[widget] = value; if (item.kind == NCMA_GUI_TEXT) gui->textPending.insert(widget); emit(item, 2, value, input); }
                // End an activated but unchanged interaction too, so the managed draft cannot
                // remain busy after focus moves away. Its commit is a deterministic no_change.
                if (ImGui::IsItemDeactivated()) { emit(item, 3, value, input); gui->numberDrafts.erase(widget); gui->textPending.erase(widget); }
                if (!ImGui::IsItemActive() && !changed) gui->numberDrafts.erase(widget);
            }
            ImGui::EndDisabled(); ImGui::PopID();
        }
        std::erase_if(gui->textPending, [&](const auto& key) { return !ids.contains(key); });
        std::erase_if(gui->numberDrafts, [&](const auto& pair) { return !ids.contains(pair.first); });
        std::erase_if(gui->textDrafts, [&](const auto& pair) { return !ids.contains(pair.first); });
        ImGui::Render(); gui->frameActive = false; gui->gpuDrawReady = true; gui->frames++; gui->lastFrame = frame->frame;
        auto* data = ImGui::GetDrawData();
        *stats = {sizeof(*stats), static_cast<uint32_t>(data->TotalVtxCount), static_cast<uint32_t>(data->TotalIdxCount),
            static_cast<uint32_t>(data->CmdListsCount), overflow ? 1u : 0u, overflow ? 0u : textWritten, frame->frame};
        if (overflow) { written = 0; gui->textDrafts.clear(); gui->numberDrafts.clear(); gui->textPending.clear(); ImGui::ClearActiveID(); }
        *count = written; return NCMA_OK;
    });
}
uint32_t NCMA_CALL AttachRenderer(uint64_t context,uint64_t handle,uint64_t rendererModule,uint64_t rendererHandle,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {
        auto valid=ValidateGui(context,handle,error);if(valid)return valid;
        if(gui->frameActive||gui->rendererHandle)return NcmaPlugin::Error(error,NCMA_BUSY);
        ID3D11Device* device=nullptr; ID3D11DeviceContext* dc=nullptr;
        valid=ncma_renderer_borrow_dx11_v1(rendererModule,rendererHandle,&device,&dc,error);if(valid)return valid;
        BusyScope scope;
        if(!ImGui_ImplDX11_Init(device,dc)) {
            NcmaErrorV1 ignored{}; (void)ncma_renderer_release_dx11_v1(rendererModule,rendererHandle,&ignored);
            return NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,"GUI DX11 initialization failed.");
        }
        gui->rendererModule=rendererModule;gui->rendererHandle=rendererHandle;return NCMA_OK;
    });
}
uint32_t NCMA_CALL ConfigureToolbarIcon(uint64_t context,uint64_t handle,const uint8_t* bytes,uint32_t length,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t {
        auto valid=ValidateGui(context,handle,error);if(valid)return valid;
        if(gui->frameActive||gui->gpuDrawReady||gui->frames)return NcmaPlugin::Error(error,NCMA_BUSY,"Toolbar icon setup is startup-only.");
        if(!bytes||length<22||length>1024u*1024)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        auto u16=[&](uint32_t n){return static_cast<uint32_t>(bytes[n]|(static_cast<uint32_t>(bytes[n+1])<<8));};
        auto u32=[&](uint32_t n){return static_cast<uint32_t>(bytes[n])|(static_cast<uint32_t>(bytes[n+1])<<8)|(static_cast<uint32_t>(bytes[n+2])<<16)|(static_cast<uint32_t>(bytes[n+3])<<24);};
        const uint32_t entries=u16(4);if(u16(0)||u16(2)!=1||!entries||entries>64||6+entries*16>length)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        uint32_t offset=0,count=0;int best=10000;
        for(uint32_t i=0;i<entries;i++){uint32_t pos=6+i*16,w=bytes[pos]?bytes[pos]:256,h=bytes[pos+1]?bytes[pos+1]:256,size=u32(pos+8),start=u32(pos+12);
            if(start<6+entries*16||start>length||size<4||size>length-start)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
            int score=std::abs(static_cast<int>(w)-64)+std::abs(static_cast<int>(h)-64);if(score<best){best=score;offset=start;count=size;}}
        constexpr int side=64;
        std::vector<uint8_t> pixels(side*side*4); // Allocate before taking any GDI/icon ownership.
        HICON icon=CreateIconFromResourceEx(const_cast<PBYTE>(bytes+offset),count,TRUE,0x00030000,side,side,LR_DEFAULTCOLOR);
        if(!icon)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Cannot decode toolbar ICO.");
        HDC dc=CreateCompatibleDC(nullptr);BITMAPINFO info{};info.bmiHeader.biSize=sizeof(BITMAPINFOHEADER);info.bmiHeader.biWidth=side;info.bmiHeader.biHeight=-side;info.bmiHeader.biPlanes=1;info.bmiHeader.biBitCount=32;info.bmiHeader.biCompression=BI_RGB;
        void* data=nullptr;HBITMAP bitmap=CreateDIBSection(dc,&info,DIB_RGB_COLORS,&data,nullptr,0);
        if(!dc||!bitmap||!data){if(bitmap)DeleteObject(bitmap);if(dc)DeleteDC(dc);DestroyIcon(icon);return NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,"Toolbar icon raster allocation.");}
        HGDIOBJ previous=SelectObject(dc,bitmap);std::memset(data,0,side*side*4);
        bool drawn=DrawIconEx(dc,0,0,icon,side,side,0,nullptr,DI_NORMAL)!=FALSE;
        std::memcpy(pixels.data(),data,pixels.size());
        SelectObject(dc,previous);DeleteObject(bitmap);DeleteDC(dc);DestroyIcon(icon);
        if(!drawn)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Toolbar icon raster failed.");
        for(size_t i=0;i<pixels.size();i+=4){std::swap(pixels[i],pixels[i+2]);uint32_t alpha=pixels[i+3];if(alpha)for(size_t c=0;c<3;c++)pixels[i+c]=static_cast<uint8_t>(std::min(255u,(static_cast<uint32_t>(pixels[i+c])*255+alpha/2)/alpha));}
        Microsoft::WRL::ComPtr<ID3D11ShaderResourceView> view;
        if(gui->rendererHandle){ID3D11Device* device=nullptr;ID3D11DeviceContext* dcGpu=nullptr;valid=ncma_renderer_borrow_dx11_v1(gui->rendererModule,gui->rendererHandle,&device,&dcGpu,error);if(valid)return valid;
            D3D11_TEXTURE2D_DESC d{};d.Width=side;d.Height=side;d.MipLevels=1;d.ArraySize=1;d.Format=DXGI_FORMAT_R8G8B8A8_UNORM;d.SampleDesc.Count=1;d.Usage=D3D11_USAGE_IMMUTABLE;d.BindFlags=D3D11_BIND_SHADER_RESOURCE;
            D3D11_SUBRESOURCE_DATA source{pixels.data(),side*4,0};Microsoft::WRL::ComPtr<ID3D11Texture2D> texture;
            HRESULT result=device->CreateTexture2D(&d,&source,&texture);if(SUCCEEDED(result))result=device->CreateShaderResourceView(texture.Get(),nullptr,&view);
            NcmaErrorV1 release{};auto released=ncma_renderer_release_dx11_v1(gui->rendererModule,gui->rendererHandle,&release);
            if(released){*error=release;return released;}if(FAILED(result))return NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,"Toolbar icon upload failed.");}
        gui->toolbarPixels=std::move(pixels);gui->toolbarIcon=std::move(view);return NCMA_OK;
    });
}
uint32_t NCMA_CALL RenderGpu(uint64_t context,uint64_t handle,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {
        auto valid=ValidateGui(context,handle,error);if(valid)return valid;
        if(!gui->rendererHandle)return NcmaPlugin::Error(error,NCMA_UNSUPPORTED_FEATURE);
        if(!gui->gpuDrawReady||gui->frameActive)return NcmaPlugin::Error(error,NCMA_BUSY);
        for(uint32_t i=0;i<gui->imageCount;i++) {ID3D11ShaderResourceView* ignored=nullptr;
            valid=gui->cachedImages[i]?ncma_renderer_gui_cached_image_v1(gui->rendererModule,gui->rendererHandle,gui->images[i],gui->lastFrame,0,&ignored,error):ncma_renderer_gui_image_v1(gui->rendererModule,gui->rendererHandle,gui->images[i],gui->lastFrame,0,&ignored,error);if(valid)return valid;}
        valid=ncma_renderer_validate_gui_frame_v1(gui->rendererModule,gui->rendererHandle,error);if(valid)return valid;
        BusyScope scope; ImGui_ImplDX11_RenderDrawData(ImGui::GetDrawData());gui->gpuDrawReady=false;return ReleaseImages(*gui,error);
    });
}
uint32_t NCMA_CALL Destroy(uint64_t context, uint64_t handle, NcmaErrorV1* error) noexcept
{
    return NcmaPlugin::Guard(error, [&]() -> uint32_t {
        auto valid = ValidateGui(context, handle, error); if (valid) return valid;
        BusyScope scope;
        if (gui->frameActive) ImGui::EndFrame();
        if (gui->rendererHandle) {
            valid=ReleaseImages(*gui,error);if(valid)return valid;
            gui->toolbarIcon.Reset();gui->toolbarPixels.clear();
            ImGui_ImplDX11_Shutdown(); NcmaErrorV1 releaseError{};
            auto released = ncma_renderer_release_dx11_v1(gui->rendererModule,gui->rendererHandle,&releaseError);
            if (released) { *error=releaseError; return released; }
            gui->rendererHandle=0;
        }
        if(gui->chromeMask)NcmaChrome::Apply(static_cast<HWND>(ImGui::GetMainViewport()->PlatformHandleRaw),false);
        ImGui_ImplGlfw_Shutdown(); ImGui::DestroyContext(gui->context);
        NcmaErrorV1 ignored{}; uint32_t released = ncma_platform_release_window_v1(gui->platform, gui->window, &ignored);
        gui.reset(); return released ? NcmaPlugin::Error(error, released, "Window borrow release failed.") : NCMA_OK;
    });
}
}
extern "C" NCMA_EXPORT uint32_t NCMA_CALL ncma_plugin_get_api(uint32_t major, uint32_t minor, void* output, uint32_t capacity, NcmaErrorV1* error) noexcept
{
    const NcmaGuiApiV1 api{{sizeof(NcmaGuiApiV1), 1, 3, NCMA_GUI, 0, Initialize, Shutdown, Status, Diagnostic},
        Create, Begin, Draw, Destroy, AttachRenderer, RenderGpu};
    if(minor<=3)return NcmaPlugin::CopyApi(major, minor, output, capacity, error, api, 3);
    NcmaGuiApiV1_4 toolbar{api,ConfigureToolbarIcon};toolbar.base.module.struct_size=sizeof(toolbar);toolbar.base.module.minor=4;
    if(minor>=5)toolbar.base.module.minor=minor;
    return NcmaPlugin::CopyApi(major,minor,output,capacity,error,toolbar,6);
}
