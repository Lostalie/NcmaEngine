#pragma once

#include <string>

namespace NcmaEngine::Rhi
{
    struct VulkanRuntimeStatus final
    {
        bool LoaderAvailable = false;
        std::string LoaderPath;
        std::string Diagnostic;
    };

    [[nodiscard]] VulkanRuntimeStatus ProbeVulkanRuntime();
}
