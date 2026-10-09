#pragma once
#include "StaticMeshKernel.h"
#include "contracts/NcmaSkin.h"
#include "contracts/NcmaShaderStages.h"
#include "renderer/rhi/d3d11/D3D11RenderBackend.h"
namespace NcmaEngine::Rendering {
class SkinInstance final {
public:
 Microsoft::WRL::ComPtr<ID3D11Buffer> source;
 Microsoft::WRL::ComPtr<ID3D11ShaderResourceView> input;
 Microsoft::WRL::ComPtr<ID3D11UnorderedAccessView> output;
 uint32_t vertices=0,bindings=0;uint64_t frame=0,bytes=0;
 bool Initialize(Rhi::D3D11RenderBackend&,StaticMesh&,const NcmaSkinMeshV5&,std::string&);
};
class SkinKernel final {
 struct Slot {
  Microsoft::WRL::ComPtr<ID3D11Buffer> buffer;
  Microsoft::WRL::ComPtr<ID3D11ShaderResourceView> view;
  Microsoft::WRL::ComPtr<ID3D11Query> event,disjoint,begin,end;
  bool pending=false;
 };
 Rhi::D3D11RenderBackend& backend;
 std::array<Slot,3> slots;
 Microsoft::WRL::ComPtr<ID3D11ComputeShader> shader;
 Microsoft::WRL::ComPtr<ID3D11Buffer> constants;
 int selected=-1;
 std::vector<uint8_t> registeredCode;
public:
 std::array<NcmaSkinRequestV5,32> copiedJobs{};
 std::array<NcmaSkinPaletteV5,32768> copiedPalettes{};
 static constexpr uint64_t ResidentBytes=3ull*32768*128+16;
 static constexpr uint64_t CpuScratchBytes=32ull*24+32768ull*128;
 explicit SkinKernel(Rhi::D3D11RenderBackend& b):backend(b){}
 static std::string_view ShaderSource();
 bool Initialize(std::string&,const NcmaComputeShaderV1* = nullptr);
 bool Matches(const NcmaComputeShaderV1& d) const noexcept { return registeredCode.size()==d.bytes&&std::equal(registeredCode.begin(),registeredCode.end(),d.bytecode); }
 Microsoft::WRL::ComPtr<ID3D11ComputeShader> PrepareShader(const NcmaComputeShaderV1&,std::string&);
 void PublishShader(Microsoft::WRL::ComPtr<ID3D11ComputeShader>& candidate,std::vector<uint8_t>& code) noexcept { shader.Swap(candidate);registeredCode.swap(code); }
 // Nonblocking. Called after whole-batch CPU preflight, before any GPU work.
 uint32_t SelectSlot(NcmaSkinStatsV5&,std::string&);
 void Begin(const NcmaSkinPaletteV5*,uint32_t);
 void Dispatch(const SkinInstance&,uint32_t);
 void End();
 void Resolve(NcmaSkinStatsV5&);
 bool Capture(const StaticMesh&,uint8_t*,uint32_t,std::string&);
};
}
