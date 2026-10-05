#include "StaticMeshKernel.h"
#include <cstring>
namespace NcmaEngine::Rendering {
StaticMesh::~StaticMesh() {backend.DestroyBuffer(vertices);backend.DestroyBuffer(indices);}
bool StaticMesh::Initialize(const NcmaMeshDescriptionV1& d,std::string& error) {
    Rhi::BufferDescription b{};b.Size=d.vertex_bytes;b.Stride=d.stride;b.Usage=Rhi::BufferUsage::Vertex;
    b.Memory=Rhi::MemoryUsage::GpuOnly;b.DebugName="StaticMesh.Vertices";
    vertices=backend.CreateBuffer(b,d.vertices,error);if(!vertices)return false;
    b.Size=d.index_bytes;b.Stride=4;b.Usage=Rhi::BufferUsage::Index;b.DebugName="StaticMesh.Indices";
    indices=backend.CreateBuffer(b,d.indices,error);if(!indices)return false;
    indexCount=d.index_count;vertexStride=d.stride;bytes=static_cast<uint64_t>(d.vertex_bytes)+d.index_bytes;return true;
}
StaticMeshKernel::~StaticMeshKernel() {backend.DestroyGraphicsPipeline(pipeline);backend.DestroyBuffer(constants);}
bool StaticMeshKernel::Initialize(std::string& error) {
    constexpr std::string_view shader=R"(
cbuffer DrawConstants : register(b0) {column_major float4x4 MVP;float4 Color;};
struct VertexOut {float4 position : SV_POSITION;};
VertexOut VSMain(float3 position : POSITION) {VertexOut o;o.position=mul(MVP,float4(position,1));return o;}
float4 PSMain(VertexOut input) : SV_TARGET {
    float3 c=Color.rgb;
    float3 encoded=lerp(12.92*c,1.055*pow(max(c,0),1.0/2.4)-0.055,step(0.0031308,c));
    return float4(encoded,Color.a);
})";
    Rhi::GraphicsPipelineDescription p{};p.VertexShaderSource=shader;p.PixelShaderSource=shader;
    p.VertexLayout={{"POSITION",0,Rhi::VertexFormat::Float3,0}};
    p.Cull=Rhi::CullMode::None;p.DepthTest=true;p.DepthWrite=true;p.DebugName="StaticMesh.Unlit.v1";
    pipeline=backend.CreateGraphicsPipeline(p,error);if(!pipeline)return false;
    Rhi::BufferDescription b{};b.Size=80;b.Usage=Rhi::BufferUsage::Constant;
    b.Memory=Rhi::MemoryUsage::CpuToGpu;b.DebugName="StaticMesh.DrawConstants";
    constants=backend.CreateBuffer(b,nullptr,error);return static_cast<bool>(constants);
}
bool StaticMeshKernel::Draw(const StaticMesh& mesh,const NcmaMeshDrawV1& draw,const float* viewport,std::string& error) {
    float data[20];std::memcpy(data,draw.model_view_projection,64);std::memcpy(data+16,draw.color,16);
    if(!backend.UpdateBuffer(constants,data,sizeof(data),0,error))return false;
    Rhi::DrawDescription d{};d.Pipeline=pipeline;d.VertexBuffer=mesh.vertices;d.IndexBuffer=mesh.indices;
    d.VertexConstantBuffer=constants;d.PixelConstantBuffer=constants;d.VertexStride=mesh.vertexStride;d.IndexStride=4;
    d.FirstIndex=draw.first_index;d.IndexCount=draw.index_count;
    d.ViewportX=viewport[0];d.ViewportY=viewport[1];d.ViewportWidth=viewport[2];d.ViewportHeight=viewport[3];
    return backend.Draw(d,error);
}
}
