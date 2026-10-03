#include "EditorApplication.h"

#include <Windows.h>
#include <string_view>

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR commandLine, int)
{
    const std::wstring_view arguments = commandLine != nullptr ? commandLine : L"";
    const bool gameplaySmokeTest = arguments.find(L"--gameplay-smoke-test") != std::wstring_view::npos;
    const bool fbxSmokeTest = arguments.find(L"--fbx-smoke-test") != std::wstring_view::npos;
    const bool smokeTest = gameplaySmokeTest || fbxSmokeTest || arguments.find(L"--smoke-test") != std::wstring_view::npos;
    NcmaEngine::Rhi::BackendType backend = NcmaEngine::Rhi::BackendType::Direct3D11;
    if (arguments.find(L"--renderer=vulkan") != std::wstring_view::npos ||
        arguments.find(L"--renderer=vk") != std::wstring_view::npos)
        backend = NcmaEngine::Rhi::BackendType::Vulkan;
    else if (arguments.find(L"--renderer=null") != std::wstring_view::npos)
        backend = NcmaEngine::Rhi::BackendType::Null;

    int result = 0;
    {
        NcmaEngine::Editor::EditorApplication application(instance, backend, smokeTest, gameplaySmokeTest, fbxSmokeTest);
        result = application.Run();
    }
    if (smokeTest)
        ExitProcess(static_cast<UINT>(result));
    return result;
}
