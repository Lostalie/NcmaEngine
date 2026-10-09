// Independent UI shaders. Generic reflection compares ONLY the UI source, never a 3D kernel.
uint32_t NCMA_CALL CopyUiShaderSource(uint64_t context,uint64_t handle,uint8_t* bytes,uint32_t capacity,uint32_t* required,NcmaErrorV1* error) noexcept {
 return NcmaPlugin::Guard(error,[&]()->uint32_t{auto v=ValidateShaderPreparation(context,handle,error);if(v)return v;
  if(!required||capacity>262144)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);auto source=Rendering::UiKernel::ShaderSource();auto count=static_cast<uint32_t>(source.size());
  if(!bytes||capacity<count){NcmaPlugin::Error(error,NCMA_BUFFER_TOO_SMALL);error->required_bytes=count;return NCMA_BUFFER_TOO_SMALL;}
  std::memcpy(bytes,source.data(),count);*required=count;return NCMA_OK;});
}
uint32_t InstallUiShaders(uint64_t context,uint64_t handle,const NcmaShaderPairV1* shaders,bool create,NcmaErrorV1* error){
 auto v=ValidateShaderPreparation(context,handle,error);if(v)return v;
 if(!shaders)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
 if(shaders->struct_size!=32||shaders->version!=1)return NcmaPlugin::Error(error,NCMA_ABI_MISMATCH);
 if(!RegisteredStages::Pair(*shaders))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
 if(create?renderer->ui!=nullptr:renderer->ui==nullptr)return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE,"ui_shader_kernel_state");
 if(!renderer->uiLeases.empty())return NcmaPlugin::Error(error,NCMA_BUSY,"ui_shader_presentation_leases");
 auto source=Rendering::UiKernel::ShaderSource();
 if((v=RegisteredStages::ValidateCode(shaders->vertex,shaders->vertex_bytes,source,"VSMain","vs_5_0",false,error)))return v;
 if((v=RegisteredStages::ValidateCode(shaders->pixel,shaders->pixel_bytes,source,"PSMain","ps_5_0",false,error)))return v;
 BusyScope scope;std::string message;std::unique_ptr<Rendering::UiKernel> kernel;Rendering::UiKernel::Programs programs;
 if(create){kernel=std::make_unique<Rendering::UiKernel>(renderer->backend->GetDevice(),renderer->backend->GetDeviceContext());if(!kernel->Initialize(message,shaders))return FAILED(renderer->backend->GetDevice()->GetDeviceRemovedReason())?Failure(error,"ui_shader_device"):NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,"ui_shader_candidate");}
 else if(!renderer->ui->PrepareShaders(*shaders,programs,message))return FAILED(renderer->backend->GetDevice()->GetDeviceRemovedReason())?Failure(error,"ui_shader_device"):NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,"ui_shader_candidate");
 #ifdef NCMA_RENDERER_TEST_WAIT
 if(testTonePreparationThrow)throw std::runtime_error("Injected UI pre-publication diagnostic failure");
 #endif
 Validation(*renderer);
 if(!Wait(*renderer,message))return FAILED(renderer->backend->GetDevice()->GetDeviceRemovedReason())?Failure(error,"ui_shader_device"):NcmaPlugin::Error(error,NCMA_SHUTDOWN_TIMEOUT,"ui_shader_drain; old_program_retained");
 if(FAILED(renderer->backend->GetDevice()->GetDeviceRemovedReason()))return Failure(error,"ui_shader_device");
 renderer->backend->GetDeviceContext()->ClearState();if(create)renderer->ui=std::move(kernel);else renderer->ui->PublishShaders(programs);return NCMA_OK;
}
uint32_t NCMA_CALL CreateUiShaders(uint64_t c,uint64_t h,const NcmaShaderPairV1* s,NcmaErrorV1* e) noexcept {return NcmaPlugin::Guard(e,[&]{return InstallUiShaders(c,h,s,true,e);});}
uint32_t NCMA_CALL ReplaceUiShaders(uint64_t c,uint64_t h,const NcmaShaderPairV1* s,NcmaErrorV1* e) noexcept {return NcmaPlugin::Guard(e,[&]{return InstallUiShaders(c,h,s,false,e);});}
