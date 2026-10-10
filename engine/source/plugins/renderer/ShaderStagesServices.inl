// Closed binding kernels only. No shader policy, World, filesystem or Agent permission.
namespace RegisteredStages {
bool Code(const uint8_t* bytes,uint32_t size){return bytes&&size>=4&&size<=1048576;}
bool Pair(const NcmaShaderPairV1& p){return p.struct_size==32&&p.version==1&&Code(p.vertex,p.vertex_bytes)&&Code(p.pixel,p.pixel_bytes);}
bool Empty(const NcmaShaderPairV1& p){return !p.struct_size&&!p.version&&!p.vertex_bytes&&!p.pixel_bytes&&!p.vertex&&!p.pixel;}
bool SameSignature(ID3D11ShaderReflection* a,ID3D11ShaderReflection* b,bool output,uint32_t count){
 for(uint32_t i=0;i<count;i++){D3D11_SIGNATURE_PARAMETER_DESC x{},y{};
  if(FAILED(output?a->GetOutputParameterDesc(i,&x):a->GetInputParameterDesc(i,&x))||FAILED(output?b->GetOutputParameterDesc(i,&y):b->GetInputParameterDesc(i,&y))||
   !x.SemanticName||!y.SemanticName||_stricmp(x.SemanticName,y.SemanticName)||x.SemanticIndex!=y.SemanticIndex||x.Mask!=y.Mask||x.ComponentType!=y.ComponentType||x.SystemValueType!=y.SystemValueType||x.Stream!=y.Stream||x.MinPrecision!=y.MinPrecision)return false;
 }return true;
}
bool SameType(ID3D11ShaderReflectionType* actual,ID3D11ShaderReflectionType* expected,uint32_t depth,uint32_t& nodes){
 if(!actual||!expected||depth>8||++nodes>64)return false;D3D11_SHADER_TYPE_DESC a{},b{};
 if(FAILED(actual->GetDesc(&a))||FAILED(expected->GetDesc(&b))||a.Type!=b.Type||a.Class!=b.Class||a.Rows!=b.Rows||a.Columns!=b.Columns||a.Elements!=b.Elements||a.Members!=b.Members||a.Offset!=b.Offset)return false;
 for(uint32_t i=0;i<b.Members;i++){auto* x=actual->GetMemberTypeName(i);auto* y=expected->GetMemberTypeName(i);if(!x||!y||std::strcmp(x,y)||!SameType(actual->GetMemberTypeByIndex(i),expected->GetMemberTypeByIndex(i),depth+1,nodes))return false;}
 return true;
}
bool SameBindings(ID3D11ShaderReflection* actual,ID3D11ShaderReflection* expected,const char*& reason){
 reason="registered_stage_shape";
 D3D11_SHADER_DESC a{},b{};if(FAILED(actual->GetDesc(&a))||FAILED(expected->GetDesc(&b))||a.Version!=b.Version||a.InputParameters!=b.InputParameters||a.OutputParameters!=b.OutputParameters||a.BoundResources!=b.BoundResources||a.ConstantBuffers!=b.ConstantBuffers||actual->GetNumInterfaceSlots()||a.PatchConstantParameters)return false;
 reason="registered_stage_signature";if(!SameSignature(actual,expected,false,b.InputParameters)||!SameSignature(actual,expected,true,b.OutputParameters))return false;
 reason="registered_stage_resource";
 for(uint32_t i=0;i<b.BoundResources;i++){D3D11_SHADER_INPUT_BIND_DESC x{},y{};if(FAILED(expected->GetResourceBindingDesc(i,&y))||!y.Name||FAILED(actual->GetResourceBindingDescByName(y.Name,&x))||x.Type!=y.Type||x.BindPoint!=y.BindPoint||x.BindCount!=y.BindCount||x.Dimension!=y.Dimension||x.ReturnType!=y.ReturnType||((x.uFlags^y.uFlags)&D3D_SIF_COMPARISON_SAMPLER))return false;}
 for(uint32_t i=0;i<b.ConstantBuffers;i++){
  reason="registered_stage_cbuffer";auto* eb=expected->GetConstantBufferByIndex(i);D3D11_SHADER_BUFFER_DESC y{};if(!eb||FAILED(eb->GetDesc(&y))||!y.Name)return false;
  auto* ab=actual->GetConstantBufferByName(y.Name);D3D11_SHADER_BUFFER_DESC x{};if(!ab||FAILED(ab->GetDesc(&x))||x.Type!=y.Type||x.Size!=y.Size||x.Variables!=y.Variables)return false;
  reason="registered_stage_member";for(uint32_t k=0;k<y.Variables;k++){auto* ev=eb->GetVariableByIndex(k);D3D11_SHADER_VARIABLE_DESC v{},w{};
   if(!ev||FAILED(ev->GetDesc(&v))||!v.Name)return false;auto* av=ab->GetVariableByName(v.Name);uint32_t nodes=0;
   if(!av||FAILED(av->GetDesc(&w))||w.StartOffset!=v.StartOffset||w.Size!=v.Size||!SameType(av->GetType(),ev->GetType(),0,nodes))return false;
  }
 }return true;
}
uint32_t ValidateCode(const uint8_t* bytes,uint32_t size,std::string_view source,const char* entry,const char* target,bool skin,NcmaErrorV1* error,bool environment=false){
 if(!Code(bytes,size))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"registered_stage_bytecode_budget");
 ComPtr<ID3D11ShaderReflection> actual,expected;
 auto& cache=environment?renderer->environmentShaderReferences:renderer->shaderReferences;
 for(const auto& cached:cache)if(cached.source==source&&cached.entry==entry&&cached.target==target){expected=cached.reflection;break;}
 if(!expected){
  if(cache.size()>=(environment?4u:10u))return NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,"registered_reference_budget");
  ComPtr<ID3DBlob> reference;
  if(FAILED(D3DCompile(source.data(),source.size(),"Ncma.ClosedStage.v1",nullptr,nullptr,entry,target,ShaderCompilation::Flags,0,&reference,nullptr)))return NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,"registered_internal_contract");
  if(FAILED(D3DReflect(reference->GetBufferPointer(),reference->GetBufferSize(),__uuidof(ID3D11ShaderReflection),&expected)))return NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,"registered_internal_reflection");
  cache.push_back({std::string(source),entry,target,expected});
 }
 if(FAILED(D3DReflect(bytes,size,__uuidof(ID3D11ShaderReflection),&actual)))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"registered_stage_bytecode");
 const char* reason=nullptr;if(!SameBindings(actual.Get(),expected.Get(),reason))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,reason);
 if(skin){
  UINT x=0,y=0,z=0;if(actual->GetThreadGroupSize(&x,&y,&z)!=64||x!=64||y!=1||z!=1)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"registered_skin_dispatch");
  ComPtr<ID3DBlob> assembly;if(FAILED(D3DDisassemble(bytes,size,0,nullptr,&assembly))||!assembly||assembly->GetBufferSize()>4194304)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"registered_skin_stride");
  uint32_t first=0,second=0;std::string_view text{static_cast<const char*>(assembly->GetBufferPointer()),assembly->GetBufferSize()-1};
  if(!ShaderCompilation::StructuredStride(text,0,false,first)||!ShaderCompilation::StructuredStride(text,1,false,second)||first!=80||second!=128)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"registered_skin_stride");
 }return NCMA_OK;
}
uint32_t Scene(const NcmaSceneShadersV1* shaders,bool shadow,NcmaErrorV1* error){
 if(!shaders)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
 if(shaders->struct_size!=104||shaders->version!=1)return NcmaPlugin::Error(error,NCMA_ABI_MISMATCH);
 if(!Pair(shaders->geometry)||!Pair(shaders->tone)||(shadow?!Pair(shaders->shadow):!Empty(shaders->shadow)))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"registered_scene_complete_group");
 auto source=Rendering::ScenePipelineKernel::ShaderSource();std::string variant=shadow?std::string(source):"#define NCMA_NO_SCENE_SHADOW\n"+std::string(source);uint32_t result;
 if((result=ValidateCode(shaders->geometry.vertex,shaders->geometry.vertex_bytes,variant,"VSMain","vs_5_0",false,error)))return result;
 if((result=ValidateCode(shaders->geometry.pixel,shaders->geometry.pixel_bytes,variant,"PSMain","ps_5_0",false,error)))return result;
 if(shadow){if((result=ValidateCode(shaders->shadow.vertex,shaders->shadow.vertex_bytes,source,"VSShadowAlpha","vs_5_0",false,error)))return result;
  if((result=ValidateCode(shaders->shadow.pixel,shaders->shadow.pixel_bytes,source,"PSShadow","ps_5_0",false,error)))return result;}
 return RegisteredTone::Validate(&shaders->tone,error);
}
uint32_t Skin(const NcmaComputeShaderV1* shader,NcmaErrorV1* error){
 if(!shader)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
 if(shader->struct_size!=24||shader->version!=1)return NcmaPlugin::Error(error,NCMA_ABI_MISMATCH);
 if(shader->reserved)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
 return ValidateCode(shader->bytecode,shader->bytes,Rendering::SkinKernel::ShaderSource(),"CSMain","cs_5_0",true,error);
}
}
uint32_t NCMA_CALL CopyStageSource(uint64_t context,uint64_t handle,uint32_t kind,uint8_t* bytes,uint32_t capacity,uint32_t* required,NcmaErrorV1* error) noexcept {
 return NcmaPlugin::Guard(error,[&]()->uint32_t{auto v=ValidateShaderPreparation(context,handle,error);if(v)return v;if(kind>1||!required||capacity>262144)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
  auto source=kind?Rendering::SkinKernel::ShaderSource():Rendering::ScenePipelineKernel::ShaderSource();auto count=static_cast<uint32_t>(source.size());
  if(!bytes||capacity<count){NcmaPlugin::Error(error,NCMA_BUFFER_TOO_SMALL);error->required_bytes=count;return NCMA_BUFFER_TOO_SMALL;}std::memcpy(bytes,source.data(),count);*required=count;return NCMA_OK;});
}
uint32_t NCMA_CALL CreateRegisteredStages(uint64_t context,uint64_t handle,const NcmaScenePipelineDescriptionV4* scene,const NcmaSceneShadersV1* shaders,NcmaGpuResourceV3* output,NcmaErrorV1* error) noexcept {
 return NcmaPlugin::Guard(error,[&]()->uint32_t{auto v=ValidateShaderPreparation(context,handle,error);if(v)return v;if(!scene||!output)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);v=RegisteredStages::Scene(shaders,scene->shadow_resolution!=0,error);if(v)return v;
  return CreateScenePipelinePrepared(context,handle,scene,output,error,nullptr,shaders);});
}
uint32_t NCMA_CALL ReplaceRegisteredStages(uint64_t context,uint64_t handle,NcmaGpuResourceV3 key,const NcmaSceneShadersV1* shaders,NcmaErrorV1* error) noexcept {
 return NcmaPlugin::Guard(error,[&]()->uint32_t{auto v=ValidateShaderPreparation(context,handle,error);if(v)return v;if(key.generation!=handle||!renderer->scenePipelines.contains(key.value))return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);
  auto& scene=*renderer->scenePipelines.at(key.value);if(scene.environmentCapable)return NcmaPlugin::Error(error,NCMA_ABI_MISMATCH,"Environment scene requires query15.");v=RegisteredStages::Scene(shaders,scene.resolution!=0,error);if(v)return v;
  BusyScope busyScope;Rendering::ScenePipelineKernel::Programs candidate{};struct Scope{Rendering::ScenePipelineKernel& scene;Rendering::ScenePipelineKernel::Programs& candidate;~Scope(){scene.ReleasePrograms(candidate);}} retained{scene,candidate};std::string message;
  if(!scene.PrepareShaders(*shaders,candidate,message))return FAILED(renderer->backend->GetDevice()->GetDeviceRemovedReason())?Failure(error,"registered_scene_device"):NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,"registered_scene_candidate");
 #ifdef NCMA_RENDERER_TEST_WAIT
  if(testTonePreparationThrow)throw std::runtime_error("Injected pre-publication diagnostic failure");
 #endif
  Validation(*renderer);if(!Wait(*renderer,message))return FAILED(renderer->backend->GetDevice()->GetDeviceRemovedReason())?Failure(error,"registered_scene_device"):NcmaPlugin::Error(error,NCMA_SHUTDOWN_TIMEOUT,"registered_scene_drain; old_group_retained");
  if(FAILED(renderer->backend->GetDevice()->GetDeviceRemovedReason()))return Failure(error,"registered_scene_device");
  renderer->backend->GetDeviceContext()->ClearState();scene.PublishShaders(candidate);return NCMA_OK;});
}
uint32_t NCMA_CALL CreateRegisteredSkin(uint64_t context,uint64_t handle,const NcmaSkinMeshV5* mesh,const NcmaComputeShaderV1* shader,NcmaGpuMeshV1* output,NcmaErrorV1* error) noexcept {
 return NcmaPlugin::Guard(error,[&]()->uint32_t{auto v=ValidateShaderPreparation(context,handle,error);if(v)return v;v=RegisteredStages::Skin(shader,error);if(v)return v;return CreateSkinPrepared(context,handle,mesh,output,error,shader);});
}
uint32_t NCMA_CALL ReplaceRegisteredSkin(uint64_t context,uint64_t handle,const NcmaComputeShaderV1* shader,NcmaErrorV1* error) noexcept {
 return NcmaPlugin::Guard(error,[&]()->uint32_t{auto v=ValidateShaderPreparation(context,handle,error);if(v)return v;if(!renderer->skinKernel)return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE,"registered_skin_missing_kernel");v=RegisteredStages::Skin(shader,error);if(v)return v;
  BusyScope busyScope;std::string message;auto candidate=renderer->skinKernel->PrepareShader(*shader,message);if(!candidate)return FAILED(renderer->backend->GetDevice()->GetDeviceRemovedReason())?Failure(error,"registered_skin_device"):NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,"registered_skin_candidate");
  std::vector<uint8_t> code(shader->bytecode,shader->bytecode+shader->bytes);
 #ifdef NCMA_RENDERER_TEST_WAIT
  if(testTonePreparationThrow)throw std::runtime_error("Injected pre-publication diagnostic failure");
 #endif
  Validation(*renderer);if(!Wait(*renderer,message))return FAILED(renderer->backend->GetDevice()->GetDeviceRemovedReason())?Failure(error,"registered_skin_device"):NcmaPlugin::Error(error,NCMA_SHUTDOWN_TIMEOUT,"registered_skin_drain; old_program_retained");
  if(FAILED(renderer->backend->GetDevice()->GetDeviceRemovedReason()))return Failure(error,"registered_skin_device");
  renderer->backend->GetDeviceContext()->ClearState();renderer->skinKernel->PublishShader(candidate,code);return NCMA_OK;});
}
