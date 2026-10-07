// White-box native adapter test; no test callback/function is exported in production.
#include <imgui.h>
#include <imgui_internal.h>
#include <iostream>
ImVec2 recordedButtonMin, recordedButtonMax;
namespace ImGui {
bool RecordedButton(const char* label) {
    bool clicked = Button(label); recordedButtonMin = GetItemRectMin(); recordedButtonMax = GetItemRectMax(); return clicked;
}
bool RecordedInputText(const char* label, char* text, size_t length) {
    bool changed = InputText(label, text, length); recordedButtonMin = GetItemRectMin(); recordedButtonMax = GetItemRectMax(); return changed;
}
}
#define Button RecordedButton
#define InputText RecordedInputText
#include "../../engine/source/plugins/gui/GuiPlugin.cpp"
#undef Button
#undef InputText
#define GLFW_INCLUDE_NONE
#define GLFW_EXPOSE_NATIVE_WIN32
#include <GLFW/glfw3.h>
#include <GLFW/glfw3native.h>
#include <windows.h>
#include <cassert>
#include <stdexcept>
#undef assert
#define assert(expression) do { if (!(expression)) throw std::runtime_error(#expression); } while (false)
#include <iostream>
int main(int argc, char** argv)
{
    try {
    assert(argc == 3);
    HMODULE platformLibrary = LoadLibraryExA(argv[1], nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
    assert(platformLibrary);
    auto getPlatform = reinterpret_cast<NcmaGetApiV1>(GetProcAddress(platformLibrary, "ncma_plugin_get_api"));
    assert(getPlatform);
    NcmaErrorV1 error{}; NcmaPlatformApiV1 api{};
    NcmaGuiApiV1 guiApi{};
    assert(ncma_plugin_get_api(1, 0, &guiApi, sizeof(guiApi), &error) == NCMA_OK && guiApi.module.minor == 3);
    assert(ncma_plugin_get_api(1, 1, &guiApi, sizeof(guiApi), &error) == NCMA_OK);
    assert(ncma_plugin_get_api(1, 2, &guiApi, sizeof(guiApi), &error) == NCMA_OK);
    assert(ncma_plugin_get_api(1, 3, &guiApi, sizeof(guiApi), &error) == NCMA_OK);
    NcmaGuiApiV1_4 toolbarApi{};
    assert(ncma_plugin_get_api(1,4,&toolbarApi,sizeof(toolbarApi),&error)==NCMA_OK&&toolbarApi.base.module.minor==4);
    assert(ncma_plugin_get_api(1,4,&guiApi,sizeof(guiApi),&error)==NCMA_BUFFER_TOO_SMALL);
    assert(ncma_plugin_get_api(1,5,&toolbarApi,sizeof(toolbarApi),&error)==NCMA_OK&&toolbarApi.base.module.minor==5);
    assert(ncma_plugin_get_api(1,6,&toolbarApi,sizeof(toolbarApi),&error)==NCMA_OK&&toolbarApi.base.module.minor==6);
    assert(ncma_plugin_get_api(1,7,&toolbarApi,sizeof(toolbarApi),&error)==NCMA_ABI_MISMATCH);
    assert(getPlatform(1, 0, &api, sizeof(api), &error) == NCMA_OK);
    uint64_t platform = 0, w = 0;
    assert(api.module.initialize(nullptr, 0, &platform, &error) == NCMA_OK);
    const std::string title = "Native adapter fixture";
    NcmaWindowDescriptionV1 description{sizeof(description), 640, 480, 0,
        reinterpret_cast<const uint8_t*>(title.data()), static_cast<uint32_t>(title.size()), 0};
    assert(api.create_window(platform, &description, &w, &error) == NCMA_OK);
    GLFWwindow* pointer = nullptr;
    assert(ncma_platform_borrow_window_v1(platform, w, &pointer, &error) == NCMA_OK);
    assert(api.destroy_window(platform, w, &error) == NCMA_BUSY);
    std::array<NcmaInputEventV1, 4096> events{}; uint32_t count = 0; NcmaWindowStateV1 state{};
    assert(api.poll_events(platform, w, events.data(), 4096, &count, &state, &error) == NCMA_OK);
    auto key = glfwSetKeyCallback(pointer, nullptr); glfwSetKeyCallback(pointer, key);
    auto text = glfwSetCharCallback(pointer, nullptr); glfwSetCharCallback(pointer, text);
    auto focus = glfwSetWindowFocusCallback(pointer, nullptr); glfwSetWindowFocusCallback(pointer, focus);
    assert(key && text && focus);
    key(pointer, GLFW_KEY_A, 0, GLFW_PRESS, 0); key(pointer, GLFW_KEY_A, 0, GLFW_RELEASE, 0); text(pointer, 0x4E2D);
    assert(api.poll_events(platform, w, events.data(), 1, &count, &state, &error) == NCMA_BUFFER_TOO_SMALL);
    assert(api.poll_events(platform, w, events.data(), 4096, &count, &state, &error) == NCMA_OK);
    assert(count >= 3 && events[0].kind == NCMA_KEY && events[0].action == GLFW_PRESS &&
        events[1].action == GLFW_RELEASE && events[2].codepoint == 0x4E2D);
    for (uint32_t i = 1; i < count; i++) assert(events[i].sequence > events[i - 1].sequence);
    for (int i = 0; i < 10000; i++) key(pointer, GLFW_KEY_B, 0, GLFW_PRESS, 0);
    assert(api.poll_events(platform, w, events.data(), 4096, &count, &state, &error) == NCMA_OK && !count && state.overflow == 1);
    focus(pointer, 0); focus(pointer, 1);
    assert(api.poll_events(platform, w, events.data(), 4096, &count, &state, &error) == NCMA_OK && state.input_reset == 1);
    assert(api.poll_events(platform, w, events.data(), 4096, &count, &state, &error) == NCMA_OK && state.input_reset == 0 && state.overflow == 0);
    assert(api.set_icon(platform, w, reinterpret_cast<const uint8_t*>(argv[2]), static_cast<uint32_t>(std::strlen(argv[2])), &error) == NCMA_OK);
    HWND hwnd = glfwGetWin32Window(pointer);
    assert(SendMessageW(hwnd, WM_GETICON, ICON_SMALL, 0) && SendMessageW(hwnd, WM_GETICON, ICON_BIG, 0));
    assert(ncma_platform_release_window_v1(platform, w, &error) == NCMA_OK);
    std::cout << "PASS native callback queue: 10000 events, bounded overflow/refocus, icon resources\n";

    uint64_t gm = 0, g = 0; assert(Initialize(nullptr, 0, &gm, &error) == NCMA_OK);
    NcmaGuiDescriptionV1 gd{sizeof(gd), 0, platform, w, nullptr, 0, 18, nullptr, 0, 0};
    assert(Create(gm, &gd, &g, &error) == NCMA_OK);
    state.struct_size = sizeof(state); state.focused = 1; state.input_reset = 0; state.overflow = 0;
    const std::string labels = "PanelExecute";
    std::array<NcmaGuiItemV1, 3> items{};
    items[0].kind = NCMA_GUI_PANEL_BEGIN; items[0].enabled = 1; items[0].widget_high = 1; items[0].widget_low = 1;
    items[0].label_length = 5; items[0].rect[2] = 300; items[0].rect[3] = 200;
    items[1].kind = NCMA_GUI_BUTTON; items[1].enabled = 1; items[1].widget_high = 1; items[1].widget_low = 2;
    items[1].label_offset = 5; items[1].label_length = 7; items[2].kind = NCMA_GUI_PANEL_END;
    NcmaGuiFrameV1 frame{sizeof(frame), 3, static_cast<uint32_t>(labels.size()), 0, 1, 1, 1, 0};
    NcmaGuiCaptureV1 capture{}; NcmaGuiStatsV1 stats{};
    std::array<NcmaGuiEventV1, 256> guiEvents{}; std::array<uint8_t, 65536> outputText{};
    auto draw = [&]() {
        assert(Draw(gm, g, &frame, items.data(), reinterpret_cast<const uint8_t*>(labels.data()),
            guiEvents.data(), 256, &count, outputText.data(), 65536, &stats, &error) == NCMA_OK);
        assert(stats.vertices && stats.indices);
        frame.frame++;
    };
    assert(Begin(gm, g, &state, 0, &capture, &error) == NCMA_OK && capture.keyboard && capture.mouse);
    assert(Draw(gm, g, &frame, items.data(), reinterpret_cast<const uint8_t*>(labels.data()), guiEvents.data(), 1, &count,
        outputText.data(), 65536, &stats, &error) == NCMA_BUFFER_TOO_SMALL);
    draw();
    auto& io = ImGui::GetIO();
    io.AddMousePosEvent((recordedButtonMin.x + recordedButtonMax.x) / 2, (recordedButtonMin.y + recordedButtonMax.y) / 2);
    assert(Begin(gm, g, &state, 1.0 / 60, &capture, &error) == NCMA_OK); draw();
    io.AddMouseButtonEvent(0, true);
    assert(Begin(gm, g, &state, 1.0 / 60, &capture, &error) == NCMA_OK && capture.mouse);
    draw();
    io.AddMouseButtonEvent(0, false);
    assert(Begin(gm, g, &state, 1.0 / 60, &capture, &error) == NCMA_OK);
    draw();
    assert(count == 1 && guiEvents[0].phase == 3 && guiEvents[0].widget_low == 2 &&
        guiEvents[0].document_generation == 1 && guiEvents[0].view_generation == 1);
    std::cout << "PASS GUI synthetic button intent + current-frame mouse capture\n";
    items[1].kind = NCMA_GUI_TEXT; frame.view_generation = 2;
    assert(Begin(gm, g, &state, 1.0 / 60, &capture, &error) == NCMA_OK); draw();
    io.AddMousePosEvent((recordedButtonMin.x + recordedButtonMax.x) / 2, (recordedButtonMin.y + recordedButtonMax.y) / 2);
    assert(Begin(gm, g, &state, 1.0 / 60, &capture, &error) == NCMA_OK); draw();
    io.AddMouseButtonEvent(0, true);
    assert(Begin(gm, g, &state, 1.0 / 60, &capture, &error) == NCMA_OK); draw();
    io.AddMouseButtonEvent(0, false);
    assert(Begin(gm, g, &state, 1.0 / 60, &capture, &error) == NCMA_OK); draw();
    io.AddInputCharacter(0x4E2D);
    assert(Begin(gm, g, &state, 1.0 / 60, &capture, &error) == NCMA_OK && capture.keyboard); draw();
    bool changedText = false;
    for (uint32_t i = 0; i < count; i++) if (guiEvents[i].phase == 2 && guiEvents[i].text_length == 3)
        changedText = std::memcmp(outputText.data() + guiEvents[i].text_offset, "\xE4\xB8\xAD", 3) == 0;
    assert(changedText);
    io.AddMousePosEvent(270, 170); io.AddMouseButtonEvent(0, true);
    assert(Begin(gm, g, &state, 1.0 / 60, &capture, &error) == NCMA_OK); draw();
    bool committedText = false;
    for (uint32_t i = 0; i < count; i++) if (guiEvents[i].phase == 3 && guiEvents[i].text_length == 3)
        committedText = std::memcmp(outputText.data() + guiEvents[i].text_offset, "\xE4\xB8\xAD", 3) == 0;
    assert(committedText);
    io.AddMouseButtonEvent(0, false);
    assert(Begin(gm, g, &state, 1.0 / 60, &capture, &error) == NCMA_OK); draw();
    io.AddKeyEvent(ImGuiKey_Escape, true);
    assert(Begin(gm, g, &state, 1.0 / 60, &capture, &error) == NCMA_OK && capture.cancel_interaction); draw();
    assert(count == 0);
    io.AddKeyEvent(ImGuiKey_Escape, false);
    std::cout << "PASS GUI synthetic UTF-8 change/commit retains draft, Escape cancels without intents\n";
    for (float scale : {1.0f, 1.5f, 2.0f}) {
        state.scale_x = scale; state.scale_y = scale;
        state.framebuffer_width = static_cast<uint32_t>(state.width * scale);
        state.framebuffer_height = static_cast<uint32_t>(state.height * scale);
        assert(Begin(gm, g, &state, 1.0 / 60, &capture, &error) == NCMA_OK);
        assert(std::abs(io.FontGlobalScale - 1 / scale) < 0.001f);
        draw();
    }
    // ABI 1.2 display-only copied canvas: validate complete batch BEFORE touching ImGui.
    std::array<NcmaGuiItemV1,6> canvas{};
    canvas[0]=items[0];canvas[1].kind=NCMA_GUI_THEME;canvas[1].value=1;
    canvas[2].kind=NCMA_GUI_CANVAS_BEGIN;canvas[2].rect[3]=100;
    canvas[3].kind=NCMA_GUI_CANVAS_LINES;canvas[3].value=4294967295.0;canvas[3].minimum=canvas[3].maximum=1;
    canvas[3].text_offset=5;
    canvas[4].kind=NCMA_GUI_CANVAS_END;canvas[5].kind=NCMA_GUI_PANEL_END;
    for(std::size_t i=1;i<5;i++) {canvas[i].widget_high=2;canvas[i].widget_low=i;canvas[i].enabled=1;}
    frame.item_count=6;frame.view_generation++;
    assert(Begin(gm,g,&state,1.0/60,&capture,&error)==NCMA_OK);
    std::string payload="Panelnan 0 1 1 ";frame.text_bytes=static_cast<uint32_t>(payload.size());canvas[3].text_length=frame.text_bytes-5;
    assert(Draw(gm,g,&frame,canvas.data(),reinterpret_cast<const uint8_t*>(payload.data()),guiEvents.data(),256,&count,outputText.data(),65536,&stats,&error)==NCMA_INVALID_ARGUMENT);
    payload="Panel0.1 0.2 0.8 0.9 ";frame.text_bytes=static_cast<uint32_t>(payload.size());canvas[3].text_length=frame.text_bytes-5;
    assert(Draw(gm,g,&frame,canvas.data(),reinterpret_cast<const uint8_t*>(payload.data()),guiEvents.data(),256,&count,outputText.data(),65536,&stats,&error)==NCMA_OK);
    assert(count==0 && stats.vertices>0 && ImGui::GetStyle().Colors[ImGuiCol_WindowBg].x>.5f);
    const LONG_PTR originalStyle=GetWindowLongPtrW(hwnd,GWL_STYLE);
    canvas[1].value=3;frame.frame++;
    assert(Begin(gm,g,&state,1.0/60,&capture,&error)==NCMA_OK);
    assert(Draw(gm,g,&frame,canvas.data(),reinterpret_cast<const uint8_t*>(payload.data()),guiEvents.data(),256,&count,outputText.data(),65536,&stats,&error)==NCMA_OK);
    HIGHCONTRASTW contrast{};contrast.cbSize=sizeof(contrast);
    assert(SystemParametersInfoW(SPI_GETHIGHCONTRAST,sizeof(contrast),&contrast,0));
    const bool highContrast=(contrast.dwFlags&HCF_HIGHCONTRASTON)!=0;
    if(gui->chromeMask&1){BOOL dark=FALSE;assert(SUCCEEDED(DwmGetWindowAttribute(hwnd,DWMWA_USE_IMMERSIVE_DARK_MODE,&dark,sizeof(dark)))&&dark==(highContrast?FALSE:TRUE));}
    for(const auto check:std::array<std::pair<uint32_t,COLORREF>,3>{{{2,RGB(9,19,31)},{4,RGB(223,233,243)},{8,RGB(43,67,90)}}}) {
        if(!(gui->chromeMask&check.first))continue;
        const DWORD attribute=check.first==2?DWMWA_CAPTION_COLOR:check.first==4?DWMWA_TEXT_COLOR:DWMWA_BORDER_COLOR;
        COLORREF actual=0;assert(SUCCEEDED(DwmGetWindowAttribute(hwnd,attribute,&actual,sizeof(actual))));
        assert(actual==(highContrast?DWMWA_COLOR_DEFAULT:check.second));
    }
    assert(GetWindowLongPtrW(hwnd,GWL_STYLE)==originalStyle);
    wchar_t windowTitle[64]{};assert(GetWindowTextW(hwnd,windowTitle,64)>0&&std::wstring(windowTitle)==L"Native adapter fixture");
    assert(SendMessageW(hwnd,WM_GETICON,ICON_SMALL,0)&&SendMessageW(hwnd,WM_GETICON,ICON_BIG,0));
    std::cout<<"PASS native chrome DWM readback mask="<<gui->chromeMask<<"; native decorations/icon retained; highContrast="<<highContrast<<"\n";
    canvas[1].value=1;frame.frame++;
    assert(Begin(gm,g,&state,1.0/60,&capture,&error)==NCMA_OK);
    assert(Draw(gm,g,&frame,canvas.data(),reinterpret_cast<const uint8_t*>(payload.data()),guiEvents.data(),256,&count,outputText.data(),65536,&stats,&error)==NCMA_OK);
    if(gui->chromeMask&2){COLORREF reset=0;assert(SUCCEEDED(DwmGetWindowAttribute(hwnd,DWMWA_CAPTION_COLOR,&reset,sizeof(reset)))&&reset==DWMWA_COLOR_DEFAULT);}
    if(gui->chromeMask&1){BOOL dark=TRUE;assert(SUCCEEDED(DwmGetWindowAttribute(hwnd,DWMWA_USE_IMMERSIVE_DARK_MODE,&dark,sizeof(dark)))&&dark==FALSE);}
    assert(NcmaChrome::Apply(nullptr,true)==0);
    std::cout<<"PASS bounded copied canvas and theme; invalid batch leaves frame recoverable, no mutation intents\\n";
    assert(Destroy(gm, g, &error) == NCMA_OK && Shutdown(gm, &error) == NCMA_OK);
    assert(api.destroy_window(platform, w, &error) == NCMA_OK && api.module.shutdown(platform, &error) == NCMA_OK);
    FreeLibrary(platformLibrary);
    std::cout << "PASS CPU font atlas 100/150/200% and complete native cleanup; not manual/GPU acceptance\n";
    } catch (const std::exception& exception) { std::cerr << exception.what() << "\n"; return 1; }
}
