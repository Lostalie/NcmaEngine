#include <d3d11.h>
#include <d3d11sdklayers.h>
#include <wrl/client.h>
#include <iostream>
int main()
{
    Microsoft::WRL::ComPtr<ID3D11Device> device;
    Microsoft::WRL::ComPtr<ID3D11DeviceContext> context;
    D3D_FEATURE_LEVEL feature{};
    HRESULT result = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, D3D11_CREATE_DEVICE_DEBUG,
        nullptr, 0, D3D11_SDK_VERSION, &device, &feature, &context);
    if (FAILED(result)) {
        std::cout << "d3d11_validation_unavailable HRESULT=0x" << std::hex << static_cast<unsigned long>(result) << "\n";
        return 77; // Explicitly UNVERIFIED/SKIPPED, never a renderer pass.
    }
    Microsoft::WRL::ComPtr<ID3D11InfoQueue> queue;
    if (FAILED(device.As(&queue))) return 1;
    std::cout << "d3d11_validation_available feature=0x" << std::hex << feature << "\n";
    context->ClearState(); context->Flush();
    return 0;
}
