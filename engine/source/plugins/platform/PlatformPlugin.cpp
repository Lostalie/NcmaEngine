#include "PlatformPrivate.h"
#include "../PluginSupport.h"
#include "../WindowsHandle.h"
#include "../Utf8.h"
#define GLFW_INCLUDE_NONE
#include <GLFW/glfw3.h>
#define GLFW_EXPOSE_NATIVE_WIN32
#include <GLFW/glfw3native.h>
#include <windows.h>
#include <array>
#include <cmath>
#include <deque>
#include <memory>
#include <mutex>
#include <string>
namespace {
const auto loadingThread = std::this_thread::get_id();
std::mutex diagnosticsGate;
std::deque<std::string> diagnostics;
uint64_t module = 0;
bool busy = false;
HANDLE precisionTimer = nullptr;
uint64_t precisionIdentity = 0;
struct Window {
    GLFWwindow* pointer = nullptr;
    uint64_t handle = 0, sequence = 0;
    uint32_t borrowers = 0;
    HICON smallIcon = nullptr, largeIcon = nullptr;
    bool overflow = false, focusLost = false;
    std::array<uint64_t, 8> held{};
    std::deque<NcmaInputEventV1> events;
};
std::unique_ptr<Window> window;
void Record(Window& item, NcmaInputEventV1 event) noexcept
{
    try {
    event.sequence = ++item.sequence;
    if (item.overflow) return;
    if (item.events.size() == NCMA_MAX_INPUT_EVENTS) { item.events.clear(); item.overflow = true; return; }
    item.events.push_back(event);
    } catch (...) { item.events.clear(); item.overflow = true; }
}
Window& From(GLFWwindow* value) { return *static_cast<Window*>(glfwGetWindowUserPointer(value)); }
void Held(Window& item, uint32_t key, bool down)
{
    if (key >= 512) return;
    uint64_t bit = uint64_t{1} << (key % 64);
    if (down) item.held[key / 64] |= bit; else item.held[key / 64] &= ~bit;
}
void Key(GLFWwindow* value, int key, int, int action, int)
{
    auto& item = From(value);
    if (key >= 0) Held(item, static_cast<uint32_t>(key), action != GLFW_RELEASE);
    Record(item, {NCMA_KEY, static_cast<uint32_t>(key), static_cast<uint32_t>(action), 0, 0, 0, 0});
}
void Mouse(GLFWwindow* value, int button, int action, int)
{
    auto& item = From(value); Held(item, static_cast<uint32_t>(384 + button), action != GLFW_RELEASE);
    double x=0,y=0;glfwGetCursorPos(value,&x,&y);
    Record(item, {NCMA_MOUSE_BUTTON, static_cast<uint32_t>(button), static_cast<uint32_t>(action), 0, x, y, 0});
}
void Pointer(GLFWwindow* value, double x, double y) { Record(From(value), {NCMA_POINTER, 0, 0, 0, x, y, 0}); }
void Scroll(GLFWwindow* value, double x, double y) { Record(From(value), {NCMA_SCROLL, 0, 0, 0, x, y, 0}); }
void Text(GLFWwindow* value, unsigned int scalar) { Record(From(value), {NCMA_TEXT, 0, 0, scalar, 0, 0, 0}); }
void Focus(GLFWwindow* value, int focused)
{
    auto& item = From(value); if (!focused) { item.held.fill(0); item.focusLost = true; }
    Record(item, {NCMA_FOCUS, 0, static_cast<uint32_t>(focused), 0, 0, 0, 0});
}
void Resize(GLFWwindow* value, int width, int height)
{
    Record(From(value), {NCMA_RESIZE, 0, 0, 0, static_cast<double>(width), static_cast<double>(height), 0});
}
void GlfwError(int code, const char* message) noexcept
{
    try {
    std::lock_guard lock(diagnosticsGate);
    if (diagnostics.size() == 32) diagnostics.pop_front();
    size_t length = 0; if (message) while (length < 1024 && message[length]) ++length;
    diagnostics.push_back(std::to_string(code) + ": " + std::string(message ? message : "", length));
    } catch (...) { }
}
uint32_t Validate(uint64_t context, NcmaErrorV1* error)
{
    if (std::this_thread::get_id() != loadingThread) return NcmaPlugin::Error(error, NCMA_WRONG_THREAD);
    if (context == 0 || context != module) return NcmaPlugin::Error(error, NCMA_INVALID_HANDLE);
    if (busy) return NcmaPlugin::Error(error, NCMA_BUSY);
    return NCMA_OK;
}
uint32_t ValidateWindow(uint64_t context, uint64_t handle, NcmaErrorV1* error)
{
    auto result = Validate(context, error); if (result) return result;
    if (!window || window->handle != handle) return NcmaPlugin::Error(error, NCMA_INVALID_HANDLE);
    return NCMA_OK;
}
struct BusyScope { BusyScope() { busy = true; } ~BusyScope() { busy = false; } };
NcmaWindowStateV1 State()
{
    int width = 0, height = 0, fw = 0, fh = 0;
    NcmaWindowStateV1 state{}; state.struct_size = sizeof(state);
    glfwGetWindowSize(window->pointer, &width, &height); glfwGetFramebufferSize(window->pointer, &fw, &fh);
    state.width = static_cast<uint32_t>(std::max(width, 0)); state.height = static_cast<uint32_t>(std::max(height, 0));
    state.framebuffer_width = static_cast<uint32_t>(std::max(fw, 0)); state.framebuffer_height = static_cast<uint32_t>(std::max(fh, 0));
    glfwGetWindowContentScale(window->pointer, &state.scale_x, &state.scale_y);
    glfwGetCursorPos(window->pointer, &state.pointer_x, &state.pointer_y);
    state.focused = glfwGetWindowAttrib(window->pointer, GLFW_FOCUSED) ? 1u : 0u;
    state.minimized = glfwGetWindowAttrib(window->pointer, GLFW_ICONIFIED) ? 1u : 0u;
    state.close_requested = glfwWindowShouldClose(window->pointer) ? 1u : 0u;
    state.input_reset = window->focusLost ? 1u : 0u;
    state.overflow = window->overflow ? 1u : 0u; state.sequence = window->sequence;
    if (!state.focused) window->held.fill(0);
    std::copy(window->held.begin(), window->held.end(), state.held);
    return state;
}
uint32_t NCMA_CALL Initialize(const uint8_t* input, uint32_t length, uint64_t* output, NcmaErrorV1* error) noexcept
{
    return NcmaPlugin::Guard(error, [&]() -> uint32_t {
        if (std::this_thread::get_id() != loadingThread) return NcmaPlugin::Error(error, NCMA_WRONG_THREAD);
        if (!output || input || length) return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT);
        if (module || busy) return NcmaPlugin::Error(error, NCMA_BUSY);
        *output = 0; const uint64_t identity=NcmaPlugin::NewWindowsHandle(); BusyScope scope; glfwSetErrorCallback(GlfwError);
        if (!glfwInit()) { glfwSetErrorCallback(nullptr); return NcmaPlugin::Error(error, NCMA_INTERNAL_ERROR, "GLFW initialization failed."); }
        module = identity; *output = module; return NCMA_OK;
    });
}
uint32_t NCMA_CALL Shutdown(uint64_t context, NcmaErrorV1* error) noexcept
{
    return NcmaPlugin::Guard(error, [&]() -> uint32_t {
        auto valid = Validate(context, error); if (valid) return valid;
        if (window || precisionTimer) return NcmaPlugin::Error(error, NCMA_BUSY, "Platform resource still owned.");
        BusyScope scope; glfwTerminate(); glfwSetErrorCallback(nullptr); module = 0;
        std::lock_guard lock(diagnosticsGate); diagnostics.clear(); return NCMA_OK;
    });
}
uint32_t NCMA_CALL Status(uint64_t context, NcmaModuleStatusV1* output, NcmaErrorV1* error) noexcept
{
    return NcmaPlugin::Guard(error, [&]() -> uint32_t {
        if (!output) return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT);
        auto valid = Validate(context, error); if (valid) return valid;
        *output = {sizeof(*output), 1, (window ? 1u : 0u) + (precisionTimer ? 1u : 0u), 0, window ? window->sequence : 0}; return NCMA_OK;
    });
}
uint32_t NCMA_CALL ReadDiagnostic(uint64_t context, uint8_t* output, uint32_t capacity, uint32_t* required, NcmaErrorV1* error) noexcept
{
    return NcmaPlugin::Guard(error, [&]() -> uint32_t {
        auto valid = Validate(context, error); if (valid) return valid;
        if (!required || (capacity && !output)) return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT);
        std::lock_guard lock(diagnosticsGate);
        std::string text; for (const auto& entry : diagnostics) { text += entry; text += '\n'; }
        *required = static_cast<uint32_t>(text.size());
        if (capacity < text.size()) return NcmaPlugin::Error(error, NCMA_BUFFER_TOO_SMALL);
        if (!text.empty()) std::memcpy(output, text.data(), text.size());
        return NCMA_OK; // Non-consuming, repeatable bounded inspection.
    });
}
uint32_t CreateWindowCore(uint64_t context, const NcmaWindowDescriptionV1* desc, uint64_t* output, NcmaErrorV1* error, bool pixels)
{
    return NcmaPlugin::Guard(error, [&]() -> uint32_t {
        auto valid = Validate(context, error); if (valid) return valid;
        std::string title;
        if (!desc || !output || desc->struct_size != sizeof(*desc) || !desc->width || !desc->height ||
            desc->width > 16384 || desc->height > 16384 || desc->visible > 1 || desc->reserved ||
            !NcmaPlugin::CopyUtf8(desc->title, desc->title_length, 1024, title)) return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT);
        if (window) return NcmaPlugin::Error(error, NCMA_BUSY);
        *output = 0; BusyScope scope;
        auto candidate = std::make_unique<Window>();
        candidate->handle = NcmaPlugin::NewWindowsHandle();
        glfwDefaultWindowHints(); glfwWindowHint(GLFW_CLIENT_API, GLFW_NO_API);
        glfwWindowHint(GLFW_VISIBLE, static_cast<int>(desc->visible));
        glfwWindowHint(GLFW_SCALE_TO_MONITOR, pixels ? GLFW_FALSE : GLFW_TRUE);
        if (pixels) {
            glfwWindowHint(GLFW_DECORATED, GLFW_FALSE);
            glfwWindowHint(GLFW_RESIZABLE, GLFW_FALSE);
            GLFWmonitor* primary=glfwGetPrimaryMonitor();
            if(!primary)return NcmaPlugin::Error(error,NCMA_UNSUPPORTED_FEATURE,"Primary monitor unavailable.");
            int x=0,y=0;glfwGetMonitorPos(primary,&x,&y);
            glfwWindowHint(GLFW_POSITION_X,x);glfwWindowHint(GLFW_POSITION_Y,y);
        }
        candidate->pointer = glfwCreateWindow(static_cast<int>(desc->width), static_cast<int>(desc->height), title.c_str(), nullptr, nullptr);
        if (!candidate->pointer) return NcmaPlugin::Error(error, NCMA_INTERNAL_ERROR, "Window creation failed.");
        if (pixels) {
            int width=0,height=0;glfwGetFramebufferSize(candidate->pointer,&width,&height);
            if(width!=static_cast<int>(desc->width)||height!=static_cast<int>(desc->height)) {
                glfwDestroyWindow(candidate->pointer);candidate->pointer=nullptr;
                return NcmaPlugin::Error(error,NCMA_UNSUPPORTED_FEATURE,"Requested framebuffer pixel size unavailable.");
            }
        }
        glfwSetWindowUserPointer(candidate->pointer, candidate.get());
        glfwSetKeyCallback(candidate->pointer, Key); glfwSetMouseButtonCallback(candidate->pointer, Mouse);
        glfwSetCursorPosCallback(candidate->pointer, Pointer); glfwSetScrollCallback(candidate->pointer, Scroll);
        glfwSetCharCallback(candidate->pointer, Text); glfwSetWindowFocusCallback(candidate->pointer, Focus);
        glfwSetFramebufferSizeCallback(candidate->pointer, Resize);
        *output = candidate->handle; window = std::move(candidate); return NCMA_OK;
    });
}
uint32_t NCMA_CALL PlatformCreateWindow(uint64_t context, const NcmaWindowDescriptionV1* desc, uint64_t* output, NcmaErrorV1* error) noexcept
{
    return CreateWindowCore(context,desc,output,error,false);
}
uint32_t NCMA_CALL CreatePixelWindow(uint64_t context,const NcmaPixelWindowDescriptionV1* desc,uint64_t* output,NcmaErrorV1* error) noexcept
{
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {
        auto valid=Validate(context,error);if(valid)return valid;
        if(!desc||desc->window.struct_size!=sizeof(*desc)||desc->mode!=NCMA_BORDERLESS_PIXELS||desc->reserved)
            return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        auto base=desc->window;base.struct_size=sizeof(base);
        return CreateWindowCore(context,&base,output,error,true);
    });
}
uint32_t NCMA_CALL DestroyWindow(uint64_t context, uint64_t handle, NcmaErrorV1* error) noexcept
{
    return NcmaPlugin::Guard(error, [&]() -> uint32_t {
        auto valid = ValidateWindow(context, handle, error); if (valid) return valid;
        if (window->borrowers) return NcmaPlugin::Error(error, NCMA_BUSY, "Native GUI/Renderer still borrows this window.");
        BusyScope scope; glfwDestroyWindow(window->pointer);
        if (window->smallIcon) DestroyIcon(window->smallIcon);
        if (window->largeIcon) DestroyIcon(window->largeIcon);
        window.reset(); return NCMA_OK;
    });
}
uint32_t NCMA_CALL GetWindowState(uint64_t context, uint64_t handle, NcmaWindowStateV1* output, NcmaErrorV1* error) noexcept
{
    return NcmaPlugin::Guard(error, [&]() -> uint32_t {
        if (!output) return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT);
        auto valid = ValidateWindow(context, handle, error); if (valid) return valid;
        BusyScope scope; *output = State(); return NCMA_OK;
    });
}
uint32_t NCMA_CALL Poll(uint64_t context, uint64_t handle, NcmaInputEventV1* events, uint32_t capacity, uint32_t* count,
    NcmaWindowStateV1* state, NcmaErrorV1* error) noexcept
{
    return NcmaPlugin::Guard(error, [&]() -> uint32_t {
        auto valid = ValidateWindow(context, handle, error); if (valid) return valid;
        if (!events || capacity < NCMA_MAX_INPUT_EVENTS || !count || !state) return NcmaPlugin::Error(error, NCMA_BUFFER_TOO_SMALL);
        BusyScope scope; glfwPollEvents(); *state = State();
        if (!std::isfinite(state->pointer_x) || !std::isfinite(state->pointer_y)) return NcmaPlugin::Error(error, NCMA_INTERNAL_ERROR);
        *count = static_cast<uint32_t>(window->events.size());
        std::copy(window->events.begin(), window->events.end(), events); window->events.clear(); window->overflow = false; window->focusLost = false;
        return NCMA_OK;
    });
}
uint32_t NCMA_CALL SetTitle(uint64_t context, uint64_t handle, const uint8_t* bytes, uint32_t length, NcmaErrorV1* error) noexcept
{
    return NcmaPlugin::Guard(error, [&]() -> uint32_t {
        auto valid = ValidateWindow(context, handle, error); if (valid) return valid;
        std::string title; if (!NcmaPlugin::CopyUtf8(bytes, length, 1024, title)) return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT);
        BusyScope scope; glfwSetWindowTitle(window->pointer, title.c_str()); return NCMA_OK;
    });
}
uint32_t NCMA_CALL SetIcon(uint64_t context, uint64_t handle, const uint8_t* bytes, uint32_t length, NcmaErrorV1* error) noexcept
{
    return NcmaPlugin::Guard(error, [&]() -> uint32_t {
        auto valid = ValidateWindow(context, handle, error); if (valid) return valid;
        std::string path; if (!length || !NcmaPlugin::CopyUtf8(bytes, length, 4096, path)) return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT);
        int required = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, path.data(), static_cast<int>(path.size()), nullptr, 0);
        if (!required) return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT);
        std::wstring wide(static_cast<size_t>(required), L'\0');
        MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, path.data(), static_cast<int>(path.size()), wide.data(), required);
        HICON small = static_cast<HICON>(LoadImageW(nullptr, wide.c_str(), IMAGE_ICON, GetSystemMetrics(SM_CXSMICON), GetSystemMetrics(SM_CYSMICON), LR_LOADFROMFILE));
        HICON large = static_cast<HICON>(LoadImageW(nullptr, wide.c_str(), IMAGE_ICON, GetSystemMetrics(SM_CXICON), GetSystemMetrics(SM_CYICON), LR_LOADFROMFILE));
        if (!small || !large) { if (small) DestroyIcon(small); if (large) DestroyIcon(large); return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT, "Icon cannot be loaded."); }
        HWND hwnd = glfwGetWin32Window(window->pointer);
        SendMessageW(hwnd, WM_SETICON, ICON_SMALL, reinterpret_cast<LPARAM>(small));
        SendMessageW(hwnd, WM_SETICON, ICON_BIG, reinterpret_cast<LPARAM>(large));
        if (window->smallIcon) DestroyIcon(window->smallIcon);
        if (window->largeIcon) DestroyIcon(window->largeIcon);
        window->smallIcon = small; window->largeIcon = large;
        return NCMA_OK;
    });
}
uint32_t NCMA_CALL RequestClose(uint64_t context, uint64_t handle, NcmaErrorV1* error) noexcept
{
    return NcmaPlugin::Guard(error, [&]() -> uint32_t {
        auto valid = ValidateWindow(context, handle, error); if (valid) return valid;
        glfwSetWindowShouldClose(window->pointer, GLFW_TRUE); return NCMA_OK;
    });
}
uint32_t NCMA_CALL Wait(uint64_t context, double seconds, NcmaErrorV1* error) noexcept
{
    return NcmaPlugin::Guard(error, [&]() -> uint32_t {
        auto valid = Validate(context, error); if (valid) return valid;
        if (!std::isfinite(seconds) || seconds <= 0 || seconds > 0.1) return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT);
        BusyScope scope; glfwWaitEventsTimeout(seconds); return NCMA_OK;
    });
}
uint32_t NCMA_CALL CreatePrecisionWait(uint64_t context, uint64_t* output, NcmaErrorV1* error) noexcept
{
    return NcmaPlugin::Guard(error, [&]() -> uint32_t {
        auto valid=Validate(context,error); if(valid)return valid;
        if(!output)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        if(precisionTimer)return NcmaPlugin::Error(error,NCMA_BUSY);
        *output=0;
        const uint64_t identity=NcmaPlugin::NewWindowsHandle();
        BusyScope scope;
        HANDLE created=CreateWaitableTimerExW(nullptr,nullptr,CREATE_WAITABLE_TIMER_HIGH_RESOLUTION,
            TIMER_MODIFY_STATE|SYNCHRONIZE);
        if(!created) {
            const DWORD code=GetLastError();
            return NcmaPlugin::Error(error,(code==ERROR_INVALID_PARAMETER||code==ERROR_NOT_SUPPORTED)?
                NCMA_UNSUPPORTED_FEATURE:NCMA_INTERNAL_ERROR,"High-resolution wait timer creation failed.");
        }
        precisionTimer=created;precisionIdentity=identity;*output=identity;return NCMA_OK;
    });
}
uint32_t NCMA_CALL PrecisionWait(uint64_t context, uint64_t identity, uint32_t microseconds, NcmaErrorV1* error) noexcept
{
    return NcmaPlugin::Guard(error, [&]() -> uint32_t {
        auto valid=Validate(context,error); if(valid)return valid;
        if(!precisionTimer||identity!=precisionIdentity)return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);
        if(microseconds==0||microseconds>8000)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        BusyScope scope;LARGE_INTEGER due{};
        due.QuadPart=-static_cast<LONGLONG>(microseconds)*10; // Relative 100ns units.
        if(!SetWaitableTimerEx(precisionTimer,&due,0,nullptr,nullptr,nullptr,0))
            return NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,"High-resolution timer arm failed.");
        const DWORD result=WaitForSingleObject(precisionTimer,1000);
        if(result!=WAIT_OBJECT_0) {
            // Preserve resource on failure for explicit owner-thread destruction.
            CancelWaitableTimer(precisionTimer);
            return NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,"High-resolution timer wait failed or timed out.");
        }
        return NCMA_OK;
    });
}
uint32_t NCMA_CALL DestroyPrecisionWait(uint64_t context, uint64_t identity, NcmaErrorV1* error) noexcept
{
    return NcmaPlugin::Guard(error, [&]() -> uint32_t {
        auto valid=Validate(context,error); if(valid)return valid;
        if(!precisionTimer||identity!=precisionIdentity)return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);
        BusyScope scope;
        if(!CloseHandle(precisionTimer))return NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,"Timer close failed; ownership retained.");
        precisionTimer=nullptr;precisionIdentity=0;return NCMA_OK;
    });
}
}
extern "C" uint32_t NCMA_CALL ncma_platform_borrow_window_v1(uint64_t context, uint64_t handle, GLFWwindow** output, NcmaErrorV1* error) noexcept
{
    return NcmaPlugin::Guard(error, [&]() -> uint32_t {
        auto valid = ValidateWindow(context, handle, error); if (valid) return valid;
        if (!output || window->borrowers == UINT32_MAX) return NcmaPlugin::Error(error, NCMA_INVALID_ARGUMENT);
        ++window->borrowers; *output = window->pointer; return NCMA_OK;
    });
}
extern "C" uint32_t NCMA_CALL ncma_platform_release_window_v1(uint64_t context, uint64_t handle, NcmaErrorV1* error) noexcept
{
    return NcmaPlugin::Guard(error, [&]() -> uint32_t {
        auto valid = ValidateWindow(context, handle, error); if (valid) return valid;
        if (!window->borrowers) return NcmaPlugin::Error(error, NCMA_INVALID_HANDLE);
        --window->borrowers; return NCMA_OK;
    });
}
extern "C" NCMA_EXPORT uint32_t NCMA_CALL ncma_plugin_get_api(uint32_t major, uint32_t minor, void* output, uint32_t capacity, NcmaErrorV1* error) noexcept
{
    const NcmaPlatformApiV1 api{{sizeof(NcmaPlatformApiV1), 1, 0, NCMA_PLATFORM, 0, Initialize, Shutdown, Status, ReadDiagnostic},
        PlatformCreateWindow, DestroyWindow, Poll, GetWindowState, SetTitle, SetIcon, RequestClose, Wait};
    if(minor==1||minor==2) {
        NcmaPlatformApiV1_1 extended{api,CreatePrecisionWait,PrecisionWait,DestroyPrecisionWait};
        extended.base.module.struct_size=sizeof(extended);extended.base.module.minor=1;
        if(minor==2) {
            NcmaPlatformApiV1_2 pixels{extended,CreatePixelWindow};
            pixels.base.base.module.struct_size=sizeof(pixels);pixels.base.base.module.minor=2;
            return NcmaPlugin::CopyApi(major,minor,output,capacity,error,pixels,2);
        }
        return NcmaPlugin::CopyApi(major,minor,output,capacity,error,extended,1);
    }
    return NcmaPlugin::CopyApi(major, minor, output, capacity, error, api);
}
