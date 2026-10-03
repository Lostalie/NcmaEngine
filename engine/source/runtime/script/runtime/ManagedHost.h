#pragma once
#include <filesystem>
namespace NcmaEngine::Scripting
{
    // Bootstrap only: opaque managed handles and caller-owned C ABI buffers.
    class ManagedHost final
    {
    public:
        static void Configure(const std::filesystem::path& runtimeConfig, const std::filesystem::path& hostAssembly);
        static void* Resolve(const wchar_t* method);
    };
}
