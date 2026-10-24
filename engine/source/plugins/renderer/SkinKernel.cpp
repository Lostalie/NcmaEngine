#include "SkinKernel.h"
#include "ClosedShaderSources.h"
#include <d3dcompiler.h>
#include <cstring>
#include <memory>
#include <chrono>
#include <thread>
namespace NcmaEngine::Rendering {
namespace {
bool Ok(Rhi::D3D11RenderBackend& backend,HRESULT hr,std::string& error){if(SUCCEEDED(hr))return true;backend.ReportApiFailure(hr);error="DX11 skin resource/dispatch failed: "+std::to_string(static_cast<unsigned long>(hr));return false;}
constexpr auto SkinShader=ClosedShaderSources::Skin;
}
bool SkinInstance::Initialize(Rhi::D3D11RenderBackend& b,StaticMesh& mesh,const NcmaSkinMeshV5& d,std::string& error){
 vertices=d.mesh.vertex_count;bindings=d.binding_count;
 D3D11_BUFFER_DESC inputDesc{};inputDesc.ByteWidth=d.mesh.vertex_bytes;inputDesc.Usage=D3D11_USAGE_IMMUTABLE;inputDesc.BindFlags=D3D11_BIND_SHADER_RESOURCE;
 inputDesc.MiscFlags=D3D11_RESOURCE_MISC_BUFFER_STRUCTURED;inputDesc.StructureByteStride=80;D3D11_SUBRESOURCE_DATA data{d.mesh.vertices,0,0};
 if(!Ok(b,b.GetDevice()->CreateBuffer(&inputDesc,&data,&source),error))return false;
 D3D11_SHADER_RESOURCE_VIEW_DESC sr{};sr.Format=DXGI_FORMAT_UNKNOWN;sr.ViewDimension=D3D11_SRV_DIMENSION_BUFFER;sr.Buffer.NumElements=vertices;
 if(!Ok(b,b.GetDevice()->CreateShaderResourceView(source.Get(),&sr,&this->input),error))return false;
 Rhi::BufferDescription outputDesc{};outputDesc.Size=static_cast<size_t>(vertices)*48;outputDesc.Stride=48;outputDesc.RawGpuWritable=true;
 mesh.vertices=b.CreateBuffer(outputDesc,nullptr,error);if(!mesh.vertices)return false;
 outputDesc.RawGpuWritable=false;outputDesc.Size=d.mesh.index_bytes;outputDesc.Stride=4;outputDesc.Usage=Rhi::BufferUsage::Index;
 mesh.indices=b.CreateBuffer(outputDesc,d.mesh.indices,error);if(!mesh.indices)return false;
 mesh.vertexStride=48;mesh.indexCount=d.mesh.index_count;mesh.bytes=static_cast<uint64_t>(vertices)*48+d.mesh.index_bytes;
 D3D11_UNORDERED_ACCESS_VIEW_DESC ua{};ua.Format=DXGI_FORMAT_R32_TYPELESS;ua.ViewDimension=D3D11_UAV_DIMENSION_BUFFER;ua.Buffer.NumElements=vertices*12;ua.Buffer.Flags=D3D11_BUFFER_UAV_FLAG_RAW;
 if(!Ok(b,b.GetDevice()->CreateUnorderedAccessView(b.BorrowBuffer(mesh.vertices),&ua,&output),error))return false;
 bytes=mesh.bytes+d.mesh.vertex_bytes;return true;
}
std::string_view SkinKernel::ShaderSource(){return SkinShader;}
Microsoft::WRL::ComPtr<ID3D11ComputeShader> SkinKernel::PrepareShader(const NcmaComputeShaderV1& d,std::string& error){
 Microsoft::WRL::ComPtr<ID3D11ComputeShader> result;if(!Ok(backend,backend.GetDevice()->CreateComputeShader(d.bytecode,d.bytes,nullptr,&result),error))return {};return result;
}
bool SkinKernel::Initialize(std::string& error,const NcmaComputeShaderV1* registered){
 if(registered){shader=PrepareShader(*registered,error);if(!shader)return false;registeredCode.assign(registered->bytecode,registered->bytecode+registered->bytes);}
 else {
 Microsoft::WRL::ComPtr<ID3DBlob> code,diagnostic;HRESULT hr=D3DCompile(SkinShader.data(),SkinShader.size(),"Ncma.Skin.v5",nullptr,nullptr,"CSMain","cs_5_0",D3DCOMPILE_WARNINGS_ARE_ERRORS,0,&code,&diagnostic);
 if(FAILED(hr)){error=diagnostic?std::string(static_cast<const char*>(diagnostic->GetBufferPointer()),diagnostic->GetBufferSize()):"Skin shader compilation failed.";return false;}
 if(!Ok(backend,backend.GetDevice()->CreateComputeShader(code->GetBufferPointer(),code->GetBufferSize(),nullptr,&shader),error))return false;
 }
 auto* device=backend.GetDevice();
 D3D11_BUFFER_DESC d{};d.ByteWidth=16;d.Usage=D3D11_USAGE_DEFAULT;d.BindFlags=D3D11_BIND_CONSTANT_BUFFER;if(!Ok(backend,device->CreateBuffer(&d,nullptr,&constants),error))return false;
 for(auto& slot:slots){d.ByteWidth=32768*128;d.BindFlags=D3D11_BIND_SHADER_RESOURCE;d.MiscFlags=D3D11_RESOURCE_MISC_BUFFER_STRUCTURED;d.StructureByteStride=128;
  if(!Ok(backend,device->CreateBuffer(&d,nullptr,&slot.buffer),error))return false;
  D3D11_SHADER_RESOURCE_VIEW_DESC sr{};sr.Format=DXGI_FORMAT_UNKNOWN;sr.ViewDimension=D3D11_SRV_DIMENSION_BUFFER;sr.Buffer.NumElements=32768;
  if(!Ok(backend,device->CreateShaderResourceView(slot.buffer.Get(),&sr,&slot.view),error))return false;
  D3D11_QUERY_DESC q{D3D11_QUERY_EVENT,0};if(!Ok(backend,device->CreateQuery(&q,slot.event.GetAddressOf()),error))return false;
 }return timing.Initialize(error);
}
void SkinKernel::Resolve(NcmaSkinStatsV5& stats){
 auto* dc=backend.GetDeviceContext();auto read=[&](ID3D11Query* query,void* output,UINT bytes){const HRESULT result=dc->GetData(query,output,bytes,D3D11_ASYNC_GETDATA_DONOTFLUSH);backend.ReportApiFailure(result);return result;};
 for(auto& slot:slots)if(slot.pending&&read(slot.event.Get(),nullptr,0)==S_OK)slot.pending=false;
 ResolveTiming(stats);
}
uint32_t SkinKernel::SelectSlot(NcmaSkinStatsV5& stats,std::string& error){
 Resolve(stats);for(int i=0;i<3;i++)if(!slots[static_cast<size_t>(i)].pending){selected=i;return NCMA_OK;}
 if(FAILED(backend.GetDevice()->GetDeviceRemovedReason())){error="GPU skin device lost.";return NCMA_DEVICE_LOST;}error="All three bounded GPU skin palette slots are still in use.";return NCMA_BUSY;
}
void SkinKernel::Begin(const NcmaSkinPaletteV5* palette,uint32_t count,uint64_t frame){
 auto* dc=backend.GetDeviceContext();dc->ClearState();auto& slot=slots[static_cast<size_t>(selected)];
 D3D11_BOX box{0,0,0,count*128,1,1};dc->UpdateSubresource(slot.buffer.Get(),0,&box,palette,0,0);timing.Begin(frame);dc->CSSetShader(shader.Get(),nullptr,0);
}
void SkinKernel::Dispatch(const SkinInstance& instance,uint32_t offset){
 auto* dc=backend.GetDeviceContext();uint32_t settings[4]{instance.vertices,offset,0,0};dc->UpdateSubresource(constants.Get(),0,nullptr,settings,0,0);
 ID3D11Buffer* cb=constants.Get();ID3D11ShaderResourceView* sr[2]{instance.input.Get(),slots[static_cast<size_t>(selected)].view.Get()};ID3D11UnorderedAccessView* ua=instance.output.Get();
 dc->CSSetConstantBuffers(0,1,&cb);dc->CSSetShaderResources(0,2,sr);dc->CSSetUnorderedAccessViews(0,1,&ua,nullptr);dc->Dispatch((instance.vertices+63)/64,1,1);
 ID3D11UnorderedAccessView* none=nullptr;dc->CSSetUnorderedAccessViews(0,1,&none,nullptr);
}
void SkinKernel::End(){auto* dc=backend.GetDeviceContext();auto& slot=slots[static_cast<size_t>(selected)];timing.End();dc->End(slot.event.Get());slot.pending=true;selected=-1;dc->ClearState();}
bool SkinKernel::Capture(const StaticMesh& mesh,uint8_t* output,uint32_t bytes,std::string& error){
 auto* device=backend.GetDevice();auto* dc=backend.GetDeviceContext();D3D11_BUFFER_DESC d{};d.ByteWidth=bytes;d.Usage=D3D11_USAGE_STAGING;d.CPUAccessFlags=D3D11_CPU_ACCESS_READ;Microsoft::WRL::ComPtr<ID3D11Buffer> staging;
 if(!Ok(backend,device->CreateBuffer(&d,nullptr,&staging),error))return false;dc->CopyResource(staging.Get(),backend.BorrowBuffer(mesh.vertices));D3D11_MAPPED_SUBRESOURCE map{};
 dc->Flush();auto deadline=std::chrono::steady_clock::now()+std::chrono::seconds(2);HRESULT hr;
 while((hr=dc->Map(staging.Get(),0,D3D11_MAP_READ,D3D11_MAP_FLAG_DO_NOT_WAIT,&map))==DXGI_ERROR_WAS_STILL_DRAWING){if(std::chrono::steady_clock::now()>=deadline){error="Diagnostic skin capture timed out.";return false;}std::this_thread::yield();}
 if(!Ok(backend,hr,error))return false;std::memcpy(output,map.pData,bytes);dc->Unmap(staging.Get(),0);return true;
}
}
