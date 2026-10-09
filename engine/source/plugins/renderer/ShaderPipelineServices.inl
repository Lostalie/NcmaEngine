namespace RegisteredTone {
bool Signature(ID3D11ShaderReflection* reflection,bool output,uint32_t count,const char* const* names,const uint32_t* masks,const D3D_REGISTER_COMPONENT_TYPE* types){
 for(uint32_t i=0;i<count;i++){D3D11_SIGNATURE_PARAMETER_DESC d{};auto hr=output?reflection->GetOutputParameterDesc(i,&d):reflection->GetInputParameterDesc(i,&d);
  const auto system=std::strcmp(names[i],"SV_VertexID")==0?D3D_NAME_VERTEX_ID:std::strcmp(names[i],"SV_POSITION")==0?D3D_NAME_POSITION:std::strcmp(names[i],"SV_TARGET")==0?D3D_NAME_TARGET:D3D_NAME_UNDEFINED;
  if(FAILED(hr)||!d.SemanticName||_stricmp(d.SemanticName,names[i])||d.SemanticIndex||d.Mask!=masks[i]||d.ComponentType!=types[i]||d.SystemValueType!=system||d.Stream)return false;}return true;
}
uint32_t Validate(const NcmaShaderPairV1* input,NcmaErrorV1* error){
 if(!input)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
 if(input->struct_size!=32||input->version!=1)return NcmaPlugin::Error(error,NCMA_ABI_MISMATCH);
 if(!input->vertex||!input->pixel||input->vertex_bytes<4||input->pixel_bytes<4||input->vertex_bytes>1048576||input->pixel_bytes>1048576)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
 ComPtr<ID3D11ShaderReflection> vs,ps;
 if(FAILED(D3DReflect(input->vertex,input->vertex_bytes,__uuidof(ID3D11ShaderReflection),&vs))||FAILED(D3DReflect(input->pixel,input->pixel_bytes,__uuidof(ID3D11ShaderReflection),&ps)))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"tone_bytecode");
 D3D11_SHADER_DESC v{},p{};
 if(FAILED(vs->GetDesc(&v))||FAILED(ps->GetDesc(&p))||v.Version!=0x10050||p.Version!=0x50||v.InputParameters!=1||v.OutputParameters!=2||v.BoundResources||v.ConstantBuffers||p.InputParameters!=2||p.OutputParameters!=1||p.BoundResources!=3||p.ConstantBuffers!=1)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"tone_stage_contract");
 const char* vin[]{"SV_VertexID"};const uint32_t vm[]{1};const D3D_REGISTER_COMPONENT_TYPE vt[]{D3D_REGISTER_COMPONENT_UINT32};
 const char* link[]{"SV_POSITION","TEXCOORD"};const uint32_t lm[]{15,3};const D3D_REGISTER_COMPONENT_TYPE lt[]{D3D_REGISTER_COMPONENT_FLOAT32,D3D_REGISTER_COMPONENT_FLOAT32};
 const char* pout[]{"SV_TARGET"};const uint32_t pm[]{15};const D3D_REGISTER_COMPONENT_TYPE pt[]{D3D_REGISTER_COMPONENT_FLOAT32};
 if(!Signature(vs.Get(),false,1,vin,vm,vt)||!Signature(vs.Get(),true,2,link,lm,lt)||!Signature(ps.Get(),false,2,link,lm,lt)||!Signature(ps.Get(),true,1,pout,pm,pt))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"tone_link_or_output");
 bool found[3]{};
 for(uint32_t i=0;i<p.BoundResources;i++){D3D11_SHADER_INPUT_BIND_DESC d{};if(FAILED(ps->GetResourceBindingDesc(i,&d))||!d.Name||d.BindCount!=1||d.BindPoint)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"tone_resource");
  uint32_t at=3;if(d.Type==D3D_SIT_CBUFFER&&std::strcmp(d.Name,"C")==0)at=0;
  else if(d.Type==D3D_SIT_TEXTURE&&d.Dimension==D3D_SRV_DIMENSION_TEXTURE2D&&d.ReturnType==D3D_RETURN_TYPE_FLOAT&&std::strcmp(d.Name,"BaseTex")==0)at=1;
  else if(d.Type==D3D_SIT_SAMPLER&&!(d.uFlags&D3D_SIF_COMPARISON_SAMPLER)&&std::strcmp(d.Name,"S")==0)at=2;
  if(at==3||found[at])return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"tone_resource");found[at]=true;
 }
 auto* buffer=ps->GetConstantBufferByName("C");D3D11_SHADER_BUFFER_DESC b{};
 if(!buffer||FAILED(buffer->GetDesc(&b))||b.Type!=D3D_CT_CBUFFER||b.Size!=400||b.Variables!=13)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"tone_constant");
 const char* members[]{"MVP","Model","NormalMatrix","LightVP","Base","Emissive","Surface","Camera","LightDirection","LightColor","Settings","Channels","ShadowParameters"};
 for(uint32_t i=0;i<13;i++){auto* variable=buffer->GetVariableByIndex(i);D3D11_SHADER_VARIABLE_DESC d{};D3D11_SHADER_TYPE_DESC t{};
  if(!variable||FAILED(variable->GetDesc(&d))||FAILED(variable->GetType()->GetDesc(&t))||!d.Name||std::strcmp(d.Name,members[i])||d.StartOffset!=(i<4?i*64:256+(i-4)*16)||d.Size!=(i<4?64u:16u)||t.Type!=D3D_SVT_FLOAT||t.Class!=(i<4?D3D_SVC_MATRIX_COLUMNS:D3D_SVC_VECTOR)||t.Rows!=(i<4?4u:1u)||t.Columns!=4||t.Elements||t.Members)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"tone_member");
 }return NCMA_OK;
}
}
uint32_t NCMA_CALL CopyToneSource(uint64_t context,uint64_t handle,uint8_t* bytes,uint32_t capacity,uint32_t* required,NcmaErrorV1* error) noexcept {
 return NcmaPlugin::Guard(error,[&]()->uint32_t{auto valid=ValidateShaderPreparation(context,handle,error);if(valid)return valid;if(!required||capacity>262144)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
  auto source=Rendering::ScenePipelineKernel::ShaderSource();const auto count=static_cast<uint32_t>(source.size());
  if(!bytes||capacity<count){NcmaPlugin::Error(error,NCMA_BUFFER_TOO_SMALL);error->required_bytes=count;return NCMA_BUFFER_TOO_SMALL;}
  std::memcpy(bytes,source.data(),count);*required=count;return NCMA_OK;});
}
uint32_t NCMA_CALL CreateRegisteredScene(uint64_t context,uint64_t handle,const NcmaScenePipelineDescriptionV4* scene,const NcmaShaderPairV1* pair,NcmaGpuResourceV3* output,NcmaErrorV1* error) noexcept {
 return NcmaPlugin::Guard(error,[&]()->uint32_t{auto valid=ValidateShaderPreparation(context,handle,error);if(valid)return valid;valid=RegisteredTone::Validate(pair,error);if(valid)return valid;
  return CreateScenePipelinePrepared(context,handle,scene,output,error,pair);});
}
uint32_t NCMA_CALL ReplaceRegisteredTone(uint64_t context,uint64_t handle,NcmaGpuResourceV3 key,const NcmaShaderPairV1* pair,NcmaErrorV1* error) noexcept {
 return NcmaPlugin::Guard(error,[&]()->uint32_t{auto valid=ValidateShaderPreparation(context,handle,error);if(valid)return valid;
  if(key.generation!=handle||!renderer->scenePipelines.contains(key.value))return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);
  valid=RegisteredTone::Validate(pair,error);if(valid)return valid;BusyScope scope;std::string message;
  auto& scene=*renderer->scenePipelines.at(key.value);auto candidate=scene.PrepareTone(*pair,message);
  if(!candidate)return FAILED(renderer->backend->GetDevice()->GetDeviceRemovedReason())?Failure(error,"tone_device_failure"):NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,"tone_candidate_failure");
  struct CandidateScope { Rhi::D3D11RenderBackend& backend;Rhi::GraphicsPipelineHandle handle;~CandidateScope(){backend.DestroyGraphicsPipeline(handle);} } retained{*renderer->backend,candidate};
#ifdef NCMA_RENDERER_TEST_WAIT
  if(testTonePreparationThrow)throw std::runtime_error("Injected pre-publication diagnostic failure");
#endif
  Validation(*renderer); // No fallible diagnostic allocation after atomic exchange.
  if(!Wait(*renderer,message)){if(FAILED(renderer->backend->GetDevice()->GetDeviceRemovedReason()))return Failure(error,"tone_device_failure");return NcmaPlugin::Error(error,NCMA_SHUTDOWN_TIMEOUT,"tone_drain_failed; old_stage_retained");}
  if(FAILED(renderer->backend->GetDevice()->GetDeviceRemovedReason()))return Failure(error,"tone_device_failure");
  renderer->backend->GetDeviceContext()->ClearState();scene.PublishTone(candidate);retained.handle={};return NCMA_OK;});
}
