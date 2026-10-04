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
    int theme = 0;
    bool frameActive = false, cancel = false, gpuDrawReady = false;
    uint64_t rendererModule = 0, rendererHandle = 0;
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
                if (!io.Fonts->AddFontFromFileTTF(candidate->fontPath.c_str(), candidate->fontSize, nullptr, io.Fonts->GetGlyphRangesChineseSimplifiedCommon()))
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
        gui->gpuDrawReady = false;
        float scale = std::max(state->scale_x, state->scale_y);
        if (std::abs(scale - gui->rasterScale) > 0.01f) {
            if (gui->rendererHandle) ImGui_ImplDX11_InvalidateDeviceObjects();
            auto& io = ImGui::GetIO(); io.Fonts->Clear();
            if (!gui->fontPath.empty()) {
                if (!io.Fonts->AddFontFromFileTTF(gui->fontPath.c_str(), gui->fontSize * scale, nullptr, io.Fonts->GetGlyphRangesChineseSimplifiedCommon()))
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
        int panels = 0, canvases = 0; uint64_t expandedText = 0;
        std::vector<std::vector<float>> lines(frame->item_count);
        uint32_t segments = 0;
        for (uint32_t i = 0; i < frame->item_count; i++) {
            const auto& item = items[i]; std::string label, value;
            expandedText += static_cast<uint64_t>(item.label_length) + item.text_length;
            if (expandedText + frame->item_count * sizeof(NcmaGuiItemV1) > NCMA_GUI_MAX_TEXT_BYTES) return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT);
            if (item.kind < NCMA_GUI_PANEL_BEGIN || item.kind > NCMA_GUI_THEME || item.enabled > 1 ||
                item.reserved[0] || item.reserved[1] || item.reserved[2] || item.reserved[3] ||
                !std::isfinite(item.value) || !std::isfinite(item.minimum) || !std::isfinite(item.maximum) || item.minimum > item.maximum ||
                !Text(text, frame->text_bytes, item.label_offset, item.label_length, label, 4096) ||
                !Text(text, frame->text_bytes, item.text_offset, item.text_length, value, 1023))
                return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT);
            for (float number : item.rect) if (!std::isfinite(number)) return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT);
            if (item.kind == NCMA_GUI_PANEL_BEGIN) {
                if (panels || item.rect[2] <= 0 || item.rect[3] <= 0 || item.rect[2] > 16384 || item.rect[3] > 16384) return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT);
                panels++;
            } else if (item.kind == NCMA_GUI_PANEL_END) {
                if (!panels || canvases) return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT); panels--;
            } else if (!panels) return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT);
            if (item.kind == NCMA_GUI_THEME && (item.value < 0 || item.value > 2 || std::floor(item.value)!=item.value))
                return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
            if (item.kind == NCMA_GUI_CANVAS_BEGIN) {
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
            } else if (canvases && item.kind != NCMA_GUI_CANVAS_BEGIN) return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT);
            if (item.kind != NCMA_GUI_PANEL_END && (!ids.insert({item.widget_high, item.widget_low}).second || (!item.widget_high && !item.widget_low)))
                return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT);
            labels.push_back(std::move(label)); values.push_back(std::move(value));
        }
        if (panels || canvases) return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT);
        BusyScope scope;
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
        ImVec2 canvasOrigin{}, canvasSize{};
        for (uint32_t i = 0; i < frame->item_count; i++) {
            const auto& item = items[i];
            if (item.kind == NCMA_GUI_PANEL_BEGIN) {
                ImGui::SetNextWindowPos(ImVec2(item.rect[0], item.rect[1]), ImGuiCond_Always);
                ImGui::SetNextWindowSize(ImVec2(item.rect[2], item.rect[3]), ImGuiCond_Always);
                std::string name = labels[i] + "###" + std::to_string(item.widget_high) + ":" + std::to_string(item.widget_low);
                panelVisible = ImGui::Begin(name.c_str(), nullptr, ImGuiWindowFlags_NoMove | ImGuiWindowFlags_NoResize | ImGuiWindowFlags_NoCollapse | ImGuiWindowFlags_HorizontalScrollbar);
                continue;
            }
            if (item.kind == NCMA_GUI_PANEL_END) { ImGui::End(); continue; }
            if (!panelVisible) continue;
            if (item.kind == NCMA_GUI_THEME) {
                if (gui->theme != static_cast<int>(item.value)) {
                    gui->theme = static_cast<int>(item.value);
                    if (gui->theme == 0) ImGui::StyleColorsDark();
                    else if (gui->theme == 1) ImGui::StyleColorsLight();
                    else ImGui::StyleColorsClassic();
                }
                continue;
            }
            if (item.kind == NCMA_GUI_SAME_LINE) { ImGui::SameLine(); continue; }
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
            if (item.kind == NCMA_GUI_CANVAS_END) { ImGui::GetWindowDrawList()->PopClipRect(); continue; }
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
            ImGui::BeginDisabled(!item.enabled);
            std::string label = labels[i] + "###value";
            const Id widget{item.widget_high, item.widget_low};
            double value = item.value;
            if (auto draft = gui->numberDrafts.find(widget); draft != gui->numberDrafts.end()) value = draft->second;
            const char* input = nullptr; bool changed = false;
            switch (item.kind) {
                case NCMA_GUI_LABEL: ImGui::TextUnformatted(labels[i].c_str()); break;
                case NCMA_GUI_BUTTON:
                    if (ImGui::Button(label.c_str())) emit(item, 3, value);
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
uint32_t NCMA_CALL RenderGpu(uint64_t context,uint64_t handle,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {
        auto valid=ValidateGui(context,handle,error);if(valid)return valid;
        if(!gui->rendererHandle)return NcmaPlugin::Error(error,NCMA_UNSUPPORTED_FEATURE);
        if(!gui->gpuDrawReady||gui->frameActive)return NcmaPlugin::Error(error,NCMA_BUSY);
        valid=ncma_renderer_validate_gui_frame_v1(gui->rendererModule,gui->rendererHandle,error);if(valid)return valid;
        BusyScope scope; ImGui_ImplDX11_RenderDrawData(ImGui::GetDrawData());gui->gpuDrawReady=false;return NCMA_OK;
    });
}
uint32_t NCMA_CALL Destroy(uint64_t context, uint64_t handle, NcmaErrorV1* error) noexcept
{
    return NcmaPlugin::Guard(error, [&]() -> uint32_t {
        auto valid = ValidateGui(context, handle, error); if (valid) return valid;
        BusyScope scope;
        if (gui->frameActive) ImGui::EndFrame();
        if (gui->rendererHandle) {
            ImGui_ImplDX11_Shutdown(); NcmaErrorV1 releaseError{};
            auto released = ncma_renderer_release_dx11_v1(gui->rendererModule,gui->rendererHandle,&releaseError);
            if (released) { *error=releaseError; return released; }
            gui->rendererHandle=0;
        }
        ImGui_ImplGlfw_Shutdown(); ImGui::DestroyContext(gui->context);
        NcmaErrorV1 ignored{}; uint32_t released = ncma_platform_release_window_v1(gui->platform, gui->window, &ignored);
        gui.reset(); return released ? NcmaPlugin::Error(error, released, "Window borrow release failed.") : NCMA_OK;
    });
}
}
extern "C" NCMA_EXPORT uint32_t NCMA_CALL ncma_plugin_get_api(uint32_t major, uint32_t minor, void* output, uint32_t capacity, NcmaErrorV1* error) noexcept
{
    const NcmaGuiApiV1 api{{sizeof(NcmaGuiApiV1), 1, 2, NCMA_GUI, 0, Initialize, Shutdown, Status, Diagnostic},
        Create, Begin, Draw, Destroy, AttachRenderer, RenderGpu};
    return NcmaPlugin::CopyApi(major, minor, output, capacity, error, api, 2);
}
