// Test-only runtime-compiled oracle. Kept independently from generated HLSL;
// these are the former backend's transform, image and R8 font expressions.
struct GuiShaderReferenceScope
{
    Microsoft::WRL::ComPtr<ID3D11PixelShader> font;
    GuiShaderReferenceScope() { assert(!referenceGuiImage && !referenceGuiFont); }
    GuiShaderReferenceScope(const GuiShaderReferenceScope&) = delete;
    GuiShaderReferenceScope& operator=(const GuiShaderReferenceScope&) = delete;
    ~GuiShaderReferenceScope() { referenceGuiImage = nullptr; referenceGuiFont = nullptr; }
};
static void UseRuntimeGuiShaderReference(ID3D11Device* device, GuiShaderReferenceScope& scope)
{
    using Microsoft::WRL::ComPtr;
    constexpr const char* vertex =
        "cbuffer vertexBuffer : register(b0) { float4x4 ProjectionMatrix; };"
        "struct VS_INPUT { float2 pos : POSITION; float4 col : COLOR0; float2 uv : TEXCOORD0; };"
        "struct PS_INPUT { float4 pos : SV_POSITION; float4 col : COLOR0; float2 uv : TEXCOORD0; };"
        "PS_INPUT main(VS_INPUT input) { PS_INPUT output;"
        "output.pos = mul(ProjectionMatrix, float4(input.pos.xy, 0.f, 1.f));"
        "output.col = input.col; output.uv = input.uv; return output; }";
    constexpr const char* image =
        "struct PS_INPUT { float4 pos : SV_POSITION; float4 col : COLOR0; float2 uv : TEXCOORD0; };"
        "sampler sampler0; Texture2D texture0;"
        "float4 main(PS_INPUT input) : SV_Target {"
        "float4 out_col = input.col * texture0.Sample(sampler0, input.uv); return out_col; }";
    constexpr const char* font =
        "struct PS_INPUT { float4 pos : SV_POSITION; float4 col : COLOR0; float2 uv : TEXCOORD0; };"
        "sampler sampler0; Texture2D texture0;"
        "float4 main(PS_INPUT input) : SV_Target {"
        "return input.col * float4(1, 1, 1, texture0.Sample(sampler0, input.uv).r); }";
    auto compile = [](const char* source, const char* profile) {
        ComPtr<ID3DBlob> code, errors;
        assert(SUCCEEDED(D3DCompile(source, strlen(source), nullptr, nullptr, nullptr,
            "main", profile, 0, 0, &code, &errors)) && code && !errors);
        return code;
    };
    auto* bd = ImGui_ImplDX11_GetBackendData();
    auto code = compile(vertex, "vs_4_0");
    ComPtr<ID3D11VertexShader> vs;
    assert(SUCCEEDED(device->CreateVertexShader(code->GetBufferPointer(), code->GetBufferSize(), nullptr, &vs)));
    // The independent old VS has three inputs, not the candidate's mode stream.
    const D3D11_INPUT_ELEMENT_DESC elements[]{
        {"POSITION",0,DXGI_FORMAT_R32G32_FLOAT,0,static_cast<UINT>(offsetof(ImDrawVert,pos)),D3D11_INPUT_PER_VERTEX_DATA,0},
        {"TEXCOORD",0,DXGI_FORMAT_R32G32_FLOAT,0,static_cast<UINT>(offsetof(ImDrawVert,uv)),D3D11_INPUT_PER_VERTEX_DATA,0},
        {"COLOR",0,DXGI_FORMAT_R8G8B8A8_UNORM,0,static_cast<UINT>(offsetof(ImDrawVert,col)),D3D11_INPUT_PER_VERTEX_DATA,0}
    };
    ComPtr<ID3D11InputLayout> layout;
    assert(SUCCEEDED(device->CreateInputLayout(elements,3,code->GetBufferPointer(),code->GetBufferSize(),&layout)));
    bd->pInputLayout->Release(); bd->pInputLayout = layout.Detach();
    bd->pVertexShader->Release(); bd->pVertexShader = vs.Detach();
    code = compile(image, "ps_4_0");
    ComPtr<ID3D11PixelShader> ps;
    assert(SUCCEEDED(device->CreatePixelShader(code->GetBufferPointer(), code->GetBufferSize(), nullptr, &ps)));
    bd->pPixelShader->Release(); bd->pPixelShader = ps.Detach();
    referenceGuiImage = bd->pPixelShader;
    if (bd->FontAlphaOnly) {
        code = compile(font, "ps_4_0");
        assert(SUCCEEDED(device->CreatePixelShader(code->GetBufferPointer(), code->GetBufferSize(), nullptr, &scope.font)));
        referenceGuiFont = scope.font.Get();
    }
}
