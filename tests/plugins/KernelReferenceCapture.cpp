// Test-only reference composition. No Editor, World, CLR, GUI or legacy host dependency.
// Shares numerical shaders with the renderer: not an independent algorithm oracle.
#include "renderer/ReferenceKernel.h"
#include "renderer/rhi/d3d11/D3D11RenderBackend.h"
#include <d3d11sdklayers.h>
#define GLFW_EXPOSE_NATIVE_WIN32
#include <GLFW/glfw3.h>
#include <GLFW/glfw3native.h>
#include <array>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <vector>

namespace {
void Check(bool ok, const std::string& message) {
    if (!ok) throw std::runtime_error(message);
}
struct Window final {
    GLFWwindow* Value = nullptr;
    Window() {
        Check(glfwInit() == GLFW_TRUE, "Kernel fixture GLFW initialization failed.");
        glfwWindowHint(GLFW_CLIENT_API, GLFW_NO_API);
        glfwWindowHint(GLFW_VISIBLE, GLFW_FALSE);
        Value = glfwCreateWindow(256, 256, "Kernel reference fixture", nullptr, nullptr);
        if (!Value) { glfwTerminate(); throw std::runtime_error("Kernel fixture window failed."); }
    }
    ~Window() { if (Value) glfwDestroyWindow(Value); glfwTerminate(); }
    Window(const Window&) = delete;
    Window& operator=(const Window&) = delete;
};
std::filesystem::path EvidenceDirectory(const char* root, const char* configuration) {
    const std::string config(configuration);
    Check(config == "Debug" || config == "Release", "Unsupported fixture configuration.");
    const auto base = std::filesystem::absolute(root).lexically_normal();
    Check(std::filesystem::is_regular_file(base / "CMakeLists.txt"), "Expected project root.");
    const auto directory = base / "out" / "verification" / "m2-8" / ("kernel-" + config);
    std::filesystem::path current;
    for (const auto& part : directory) {
        current /= part;
        const DWORD attributes = GetFileAttributesW(current.c_str());
        if (attributes != INVALID_FILE_ATTRIBUTES) {
            Check((attributes & FILE_ATTRIBUTE_REPARSE_POINT) == 0, "Linked fixture output rejected.");
            Check((attributes & FILE_ATTRIBUTE_DIRECTORY) != 0, "Fixture directory is a file.");
        }
    }
    std::filesystem::create_directories(directory);
    return directory;
}
void CheckOutput(const std::filesystem::path& file) {
    const DWORD attributes = GetFileAttributesW(file.c_str());
    if (attributes != INVALID_FILE_ATTRIBUTES)
        Check((attributes & (FILE_ATTRIBUTE_REPARSE_POINT | FILE_ATTRIBUTE_DIRECTORY)) == 0,
              "Invalid fixture output file.");
}
std::vector<uint8_t> Capture(NcmaEngine::Rhi::D3D11RenderBackend& backend) {
    Microsoft::WRL::ComPtr<ID3D11Texture2D> source, staging;
    Check(SUCCEEDED(backend.GetSwapChain()->GetBuffer(0, IID_PPV_ARGS(&source))), "Fixture swapchain capture.");
    D3D11_TEXTURE2D_DESC description{}; source->GetDesc(&description);
    Check(description.Width == 256 && description.Height == 256 && description.Format == DXGI_FORMAT_R8G8B8A8_UNORM,
          "Unexpected kernel fixture dimensions/format.");
    description.BindFlags = 0; description.MiscFlags = 0;
    description.Usage = D3D11_USAGE_STAGING; description.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    Check(SUCCEEDED(backend.GetDevice()->CreateTexture2D(&description, nullptr, &staging)), "Fixture staging resource.");
    auto* context = backend.GetDeviceContext(); context->CopyResource(staging.Get(), source.Get());
    // Allocate before Map so allocation failure cannot leave the resource mapped.
    std::vector<uint8_t> pixels(256 * 256 * 4);
    D3D11_MAPPED_SUBRESOURCE mapped{};
    Check(SUCCEEDED(context->Map(staging.Get(), 0, D3D11_MAP_READ, 0, &mapped)), "Fixture readback Map.");
    for (size_t y = 0; y < 256; ++y)
        std::memcpy(pixels.data() + y * 256 * 4, static_cast<const uint8_t*>(mapped.pData) + y * mapped.RowPitch, 256 * 4);
    context->Unmap(staging.Get(), 0);
    return pixels;
}
void Validate(ID3D11InfoQueue& queue) {
    const auto count = queue.GetNumStoredMessagesAllowedByRetrievalFilter();
    for (UINT64 index = 0; index < count; ++index) {
        SIZE_T bytes = 0;
        Check(SUCCEEDED(queue.GetMessage(index, nullptr, &bytes)), "Fixture validation size.");
        std::vector<uint8_t> storage(bytes);
        auto* message = reinterpret_cast<D3D11_MESSAGE*>(storage.data());
        Check(SUCCEEDED(queue.GetMessage(index, message, &bytes)), "Fixture validation message.");
        if (message->Severity <= D3D11_MESSAGE_SEVERITY_WARNING)
            throw std::runtime_error(std::string("Kernel DX11 validation: ") + message->pDescription);
    }
}
}

int main(int argc, char** argv) {
    try {
        if (argc != 3) return 2;
        const auto directory = EvidenceDirectory(argv[1], argv[2]);
        const auto image = directory / "reference.rgba", report = directory / "fixture.json";
        CheckOutput(image); CheckOutput(report);
        Window window;
        NcmaEngine::Rhi::D3D11RenderBackend backend;
        NcmaEngine::Rhi::BackendCreateInfo description{};
        description.NativeWindow = glfwGetWin32Window(window.Value);
        description.Width = 256; description.Height = 256;
        description.EnableValidation = true; description.EnableVSync = false;
        std::string error;
        Check(backend.Initialize(description, error), error);
        Microsoft::WRL::ComPtr<ID3D11InfoQueue> validation;
        Check(SUCCEEDED(backend.GetDevice()->QueryInterface(IID_PPV_ARGS(&validation))), "DX11 debug layer required, not a skipped acceptance.");
        std::vector<uint8_t> pixels;
        {
            NcmaEngine::Rendering::ReferenceKernel kernel(backend);
            Check(kernel.Initialize(error), error);
            std::array<float, 16> identity{};
            for (size_t index = 0; index < 16; index += 5) identity[index] = 1;
            Check(kernel.Prepare(256, 256, identity.data(), 1, .35F, .28F, error), error);
            Check(backend.BeginFrame(error), error);
            Check(kernel.Shadow(error), error);
            Check(kernel.Geometry(error), error);
            Check(kernel.ToneMap(0, 0, 0, 256, 256, error), error);
            pixels = Capture(backend);
            backend.EndFrame();
        }
        backend.GetDeviceContext()->ClearState(); backend.GetDeviceContext()->Flush();
        Check(backend.GetLiveResourceCount() == 0, "Kernel fixture GPU resource maps leaked.");
        Validate(*validation.Get());
        std::ofstream output(image, std::ios::binary | std::ios::trunc);
        output.write(reinterpret_cast<const char*>(pixels.data()), static_cast<std::streamsize>(pixels.size()));
        output.close(); Check(static_cast<bool>(output), "Kernel reference image write failed.");
        std::ofstream metadata(report, std::ios::trunc);
        metadata << "{\"schemaVersion\":1,\"kind\":\"kernel_only_reference\",\"width\":256,\"height\":256,"
                    "\"sharedNumericalShaders\":true,\"independentAlgorithmOracle\":false,"
                    "\"legacyHostUsed\":false,\"validationErrors\":0,\"validationWarnings\":0,\"liveResources\":0}";
        metadata.close(); Check(static_cast<bool>(metadata), "Kernel fixture report write failed.");
        validation.Reset(); backend.Shutdown();
        std::cout << "Kernel-only reference: " << image.string() << "; validation=0/0; GPU resource maps=0\n";
        return 0;
    } catch (const std::exception& error) { std::cerr << error.what() << '\n'; return 1; }
}
