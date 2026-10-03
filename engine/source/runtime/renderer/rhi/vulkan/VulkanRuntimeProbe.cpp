#include "renderer/rhi/vulkan/VulkanRuntimeProbe.h"

#if defined(_WIN32)
#if !defined(WIN32_LEAN_AND_MEAN)
#define WIN32_LEAN_AND_MEAN
#endif
#include <Windows.h>
#endif

namespace NcmaEngine::Rhi
{
    VulkanRuntimeStatus ProbeVulkanRuntime()
    {
        VulkanRuntimeStatus status;
#if defined(_WIN32)
        HMODULE loader = LoadLibraryExW(L"vulkan-1.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
        if (loader == nullptr)
        {
            status.Diagnostic = "Vulkan loader vulkan-1.dll was not found in Windows System32";
            return status;
        }

        status.LoaderAvailable = GetProcAddress(loader, "vkGetInstanceProcAddr") != nullptr;
        status.LoaderPath = "vulkan-1.dll";
        status.Diagnostic = status.LoaderAvailable
            ? "Vulkan loader is available; the Ncma Vulkan RHI is not built yet"
            : "vulkan-1.dll does not export vkGetInstanceProcAddr";
        FreeLibrary(loader);
#else
        status.Diagnostic = "Vulkan runtime probing is not implemented on this platform";
#endif
        return status;
    }
}
