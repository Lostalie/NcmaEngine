namespace EnvironmentStages {
uint32_t Scene(const NcmaSceneShadersV1* shaders,bool shadow,NcmaErrorV1* error){
 if(!shaders)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
 if(shaders->struct_size!=104||shaders->version!=2)return NcmaPlugin::Error(error,NCMA_ABI_MISMATCH);
 if(!RegisteredStages::Pair(shaders->geometry)||!RegisteredStages::Pair(shaders->tone)||(shadow?!RegisteredStages::Pair(shaders->shadow):!RegisteredStages::Empty(shaders->shadow)))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"environment_complete_group");
 const auto source=Rendering::ScenePipelineKernel::EnvironmentShaderSource();const std::string variant=shadow?std::string(source):"#define NCMA_NO_SCENE_SHADOW\n"+std::string(source);uint32_t v;
 if((v=RegisteredStages::ValidateCode(shaders->geometry.vertex,shaders->geometry.vertex_bytes,variant,"VSMain","vs_5_0",false,error,true)))return v;
 if((v=RegisteredStages::ValidateCode(shaders->geometry.pixel,shaders->geometry.pixel_bytes,variant,"PSMain","ps_5_0",false,error,true)))return v;
 const auto old=Rendering::ScenePipelineKernel::ShaderSource();
 if(shadow){if((v=RegisteredStages::ValidateCode(shaders->shadow.vertex,shaders->shadow.vertex_bytes,old,"VSShadowAlpha","vs_5_0",false,error)))return v;
 if((v=RegisteredStages::ValidateCode(shaders->shadow.pixel,shaders->shadow.pixel_bytes,old,"PSShadow","ps_5_0",false,error)))return v;}
 return RegisteredTone::Validate(&shaders->tone,error);
}
}
uint32_t NCMA_CALL CopyEnvironmentSceneSource(uint64_t context,uint64_t handle,uint8_t* bytes,uint32_t capacity,uint32_t* required,NcmaErrorV1* error) noexcept {
 return NcmaPlugin::Guard(error,[&]()->uint32_t{auto v=ValidateEnvironmentGpu(context,handle,error);if(v)return v;
 if(!required||capacity>262144)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);auto source=Rendering::ScenePipelineKernel::EnvironmentShaderSource();const auto count=static_cast<uint32_t>(source.size());
 if(!bytes||capacity<count){NcmaPlugin::Error(error,NCMA_BUFFER_TOO_SMALL);if(error)error->required_bytes=count;return NCMA_BUFFER_TOO_SMALL;}
 std::memcpy(bytes,source.data(),count);*required=count;return NCMA_OK;});
}
uint32_t NCMA_CALL ValidateEnvironmentShaders(uint64_t context,uint64_t handle,const NcmaRuntimeShadersV1* input,NcmaErrorV1* error) noexcept {
 return NcmaPlugin::Guard(error,[&]()->uint32_t{auto v=ValidateEnvironmentGpu(context,handle,error);if(v)return v;
 if(!input)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);const auto d=*input;
 if(d.struct_size!=176||d.version!=2||d.profile!=3)return NcmaPlugin::Error(error,NCMA_ABI_MISMATCH);
 const bool emptySkin=!d.skin.struct_size&&!d.skin.version&&!d.skin.bytes&&!d.skin.reserved&&!d.skin.bytecode;
 if(d.flags>3||!RegisteredStages::Empty(d.ui)||(!(d.flags&2)&&!emptySkin))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"environment_runtime_closure");
 if((v=EnvironmentStages::Scene(&d.scene,(d.flags&1)!=0,error)))return v;
 return d.flags&2?RegisteredStages::Skin(&d.skin,error):NCMA_OK;});
}
uint32_t NCMA_CALL CreateEnvironmentScene(uint64_t context,uint64_t handle,const NcmaScenePipelineDescriptionV4* scene,const NcmaSceneShadersV1* shaders,NcmaGpuResourceV3* output,NcmaErrorV1* error) noexcept {
 return NcmaPlugin::Guard(error,[&]()->uint32_t{auto v=ValidateEnvironmentGpu(context,handle,error);if(v)return v;if(!scene||!output)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
 if((v=EnvironmentStages::Scene(shaders,scene->shadow_resolution!=0,error)))return v;return CreateScenePipelinePrepared(context,handle,scene,output,error,nullptr,shaders,true);});
}
uint32_t NCMA_CALL ReplaceEnvironmentScene(uint64_t context,uint64_t handle,NcmaGpuResourceV3 key,const NcmaSceneShadersV1* shaders,NcmaErrorV1* error) noexcept {
 return NcmaPlugin::Guard(error,[&]()->uint32_t{auto v=ValidateEnvironmentGpu(context,handle,error);if(v)return v;
 if(key.generation!=handle||!renderer->scenePipelines.contains(key.value))return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);auto& scene=*renderer->scenePipelines.at(key.value);
 if(!scene.environmentCapable)return NcmaPlugin::Error(error,NCMA_ABI_MISMATCH);if((v=EnvironmentStages::Scene(shaders,scene.resolution!=0,error)))return v;
 BusyScope busyScope;Rendering::ScenePipelineKernel::Programs candidate{};struct Scope{Rendering::ScenePipelineKernel& scene;Rendering::ScenePipelineKernel::Programs& candidate;~Scope(){scene.ReleasePrograms(candidate);}} retained{scene,candidate};std::string message;
 if(!scene.PrepareShaders(*shaders,candidate,message))return NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,"environment_shader_candidate");
#ifdef NCMA_RENDERER_TEST_WAIT
 if(testTonePreparationThrow)throw std::runtime_error("Environment shader prepublication fault");
#endif
 Validation(*renderer);if(!Wait(*renderer,message))return NcmaPlugin::Error(error,NCMA_SHUTDOWN_TIMEOUT,"environment_scene_drain; old_group_retained");
 if(FAILED(renderer->backend->GetDevice()->GetDeviceRemovedReason()))return Failure(error,"environment_scene_device");Validation(*renderer);
 renderer->backend->GetDeviceContext()->ClearState();scene.PublishShaders(candidate);return NCMA_OK;});
}
uint32_t NCMA_CALL BindEnvironmentScene(uint64_t context,uint64_t handle,NcmaGpuResourceV3 key,const NcmaEnvironmentBindingV1* input,NcmaErrorV1* error) noexcept {
 return NcmaPlugin::Guard(error,[&]()->uint32_t{auto v=ValidateEnvironmentGpu(context,handle,error);if(v)return v;if(!input)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);const auto d=*input;
 if(d.struct_size!=40||d.version!=1)return NcmaPlugin::Error(error,NCMA_ABI_MISMATCH);
 if(d.reserved||d.reserved2||!std::isfinite(d.strength)||d.strength<0||d.strength>16||!std::isfinite(d.rotation)||d.rotation<-std::numbers::pi_v<float>||d.rotation>std::numbers::pi_v<float>)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
 if(key.generation!=handle||!renderer->scenePipelines.contains(key.value))return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);auto& scene=*renderer->scenePipelines.at(key.value);
 if(!scene.environmentCapable)return NcmaPlugin::Error(error,NCMA_ABI_MISMATCH);
 Rendering::EnvironmentGpuKernel* resource=nullptr;
 if(d.environment.value){auto found=renderer->environments.find(d.environment.value);if(d.environment.generation!=handle||found==renderer->environments.end())return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);resource=found->second.get();if(resource->scenePins>=8&&scene.environmentKey.value!=d.environment.value)return NcmaPlugin::Error(error,NCMA_BUSY);}
 else if(d.environment.generation||d.strength||d.rotation)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"environment_off_exact");
 const std::array<Rhi::TextureHandle,3> textures=resource?resource->textures:std::array<Rhi::TextureHandle,3>{};const auto sampler=resource?resource->sampler:Rhi::SamplerHandle{};
 const std::array<float,4> settings=resource?std::array<float,4>{d.strength,std::cos(d.rotation),std::sin(d.rotation),static_cast<float>(resource->layout.levels-1)}:std::array<float,4>{0,1,0,0};
 BusyScope busyScope;std::string message;
#ifdef NCMA_RENDERER_TEST_WAIT
 if(testEnvironmentGpuPublicationFault)throw std::runtime_error("Environment bind prepublication fault");
#endif
 Validation(*renderer);if(!Wait(*renderer,message))return NcmaPlugin::Error(error,NCMA_SHUTDOWN_TIMEOUT,"environment_bind_drain; old_binding_retained");
 if(FAILED(renderer->backend->GetDevice()->GetDeviceRemovedReason()))return Failure(error,"environment_bind_device");Validation(*renderer);
 renderer->backend->GetDeviceContext()->ClearState();
 if(scene.environmentKey.value)--renderer->environments.at(scene.environmentKey.value)->scenePins;
 if(resource)++resource->scenePins;
 scene.environmentKey=d.environment;scene.environmentTextures=textures;scene.environmentSampler=sampler;scene.environmentSettings=settings;return NCMA_OK;});
}
