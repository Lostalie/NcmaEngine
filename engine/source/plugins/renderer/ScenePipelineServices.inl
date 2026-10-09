// Private numerical GPU batch service. No World, UUID resolver, authoring policy or clock.
uint32_t CreateScenePipelinePrepared(uint64_t context,uint64_t handle,const NcmaScenePipelineDescriptionV4* input,NcmaGpuResourceV3* output,NcmaErrorV1* error,const NcmaShaderPairV1* pair,const NcmaSceneShadersV1* shaders=nullptr) noexcept {
 return NcmaPlugin::Guard(error,[&]()->uint32_t{
  auto valid=ResourceReady(context,handle,error);if(valid)return valid;if(!input||!output)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);const auto d=*input;
  uint64_t bytes=static_cast<uint64_t>(d.width)*d.height*12+static_cast<uint64_t>(d.shadow_resolution)*d.shadow_resolution*4+408;
  if(d.struct_size!=16||!d.width||!d.height||d.width>4096||d.height>4096||(d.shadow_resolution&&(d.shadow_resolution<256||d.shadow_resolution>2048))||
     (d.shadow_resolution&(d.shadow_resolution-1))||renderer->scenePipelines.size()>=8||bytes>512ull*1024*1024-renderer->sceneStats.resident_bytes)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
  BusyScope busyScope;std::string message;auto candidate=std::make_unique<Rendering::ScenePipelineKernel>(*renderer->backend);
  if(!candidate->Initialize(d,message,pair,shaders))return FAILED(renderer->backend->GetDevice()->GetDeviceRemovedReason())?Failure(error,message):NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,message);
#ifdef NCMA_RENDERER_TEST_WAIT
  if(testTonePreparationThrow)throw std::runtime_error("Injected pre-publication diagnostic failure");
#endif
  Validation(*renderer); // All potentially allocating diagnostics BEFORE key/counter publication.
  uint64_t id=0x5343000000000000ull|nextResource++;renderer->scenePipelines.emplace(id,std::move(candidate));renderer->sceneStats.creates++;renderer->sceneStats.resident_bytes+=bytes;*output={id,handle};return NCMA_OK;
 });
}
uint32_t NCMA_CALL CreateScenePipeline(uint64_t context,uint64_t handle,const NcmaScenePipelineDescriptionV4* input,NcmaGpuResourceV3* output,NcmaErrorV1* error) noexcept {
 return CreateScenePipelinePrepared(context,handle,input,output,error,nullptr);
}
uint32_t NCMA_CALL DestroyScenePipeline(uint64_t context,uint64_t handle,NcmaGpuResourceV3 key,NcmaErrorV1* error) noexcept {
 return NcmaPlugin::Guard(error,[&]()->uint32_t{
  auto valid=Instance(context,handle,error);if(valid)return valid;if(key.generation!=handle||!renderer->scenePipelines.contains(key.value))return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);
  if(renderer->active)return NcmaPlugin::Error(error,NCMA_BUSY);BusyScope busyScope;std::string message;
  if(SUCCEEDED(renderer->backend->GetDevice()->GetDeviceRemovedReason())&&!Wait(*renderer,message))return NcmaPlugin::Error(error,NCMA_SHUTDOWN_TIMEOUT,message);
  renderer->backend->GetDeviceContext()->ClearState();const auto& p=*renderer->scenePipelines.at(key.value);
  renderer->sceneStats.resident_bytes-=static_cast<uint64_t>(p.width)*p.height*12+static_cast<uint64_t>(p.resolution)*p.resolution*4+408;renderer->scenePipelines.erase(key.value);Validation(*renderer);return NCMA_OK;
 });
}
uint32_t NCMA_CALL ScenePipelineStats(uint64_t context,uint64_t handle,NcmaScenePipelineStatsV4* output,NcmaErrorV1* error) noexcept {
 return NcmaPlugin::Guard(error,[&]()->uint32_t{auto valid=Instance(context,handle,error);if(valid)return valid;if(!output)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
 renderer->sceneStats.generation=handle;renderer->sceneStats.pipelines=renderer->scenePipelines.size();*output=renderer->sceneStats;return NCMA_OK;});
}
uint32_t NCMA_CALL SubmitScenePipeline(uint64_t context,uint64_t handle,const NcmaSceneFrameV4* input,const NcmaSceneDrawV4* geometry,const NcmaSceneDrawV4* casters,const NcmaScenePassV4* passes,NcmaErrorV1* error) noexcept {
 return NcmaPlugin::Guard(error,[&]()->uint32_t{
  auto valid=ResourceReady(context,handle,error);if(valid)return valid;if(!input)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);const auto f=*input;const auto& b=f.base;
  if(b.struct_size!=240||!b.frame||b.frame<=renderer->lastFrame||b.generation!=handle||b.draw_count>4096||f.caster_count>4096||
    (b.draw_count&&!geometry)||(f.caster_count&&!casters)||f.pass_count<2||f.pass_count>16||!passes||b.reserved||b.mode||f.pipeline.generation!=handle||!renderer->scenePipelines.contains(f.pipeline.value))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
  auto& pipeline=*renderer->scenePipelines.at(f.pipeline.value);uint32_t targetWidth=renderer->stats.width,targetHeight=renderer->stats.height;Rhi::TextureHandle target;
  if(b.target.value){if(b.target.generation!=handle||!renderer->targets.contains(b.target.value))return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);const auto& t=*renderer->targets.at(b.target.value);target=t.color;targetWidth=t.width;targetHeight=t.height;}else if(b.target.generation)return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);
  for(float x:b.viewport)if(!std::isfinite(x))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
  if(b.viewport[0]<0||b.viewport[1]<0||b.viewport[2]!=pipeline.width||b.viewport[3]!=pipeline.height||b.viewport[0]+b.viewport[2]>targetWidth||b.viewport[1]+b.viewport[3]>targetHeight)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
  for(float x:b.clear)if(!std::isfinite(x)||x<0||x>1)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
  for(float x:b.camera)if(!std::isfinite(x))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
  if(b.camera[3]!=1||b.light_direction[3]!=0)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
  float length=0;for(int i=0;i<3;++i){float x=b.light_direction[i];if(!std::isfinite(x))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);length+=x*x;}if(!std::isfinite(length)||length<.0001f)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
  for(int i=0;i<4;++i)if(!std::isfinite(b.light_color[i])||b.light_color[i]<0||b.light_color[i]>(i==3?100000.f:16.f))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
  for(float x:f.light_view_projection)if(!std::isfinite(x))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
  const double lightDeterminant=Eigen::Map<const Eigen::Matrix4f>(f.light_view_projection).cast<double>().determinant();
  if(!std::isfinite(lightDeterminant)||std::abs(lightDeterminant)<1e-20)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Singular light projection.");
  for(float x:f.shadow)if(!std::isfinite(x))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
  if(f.shadow[0]<0||f.shadow[0]>.1f||f.shadow[1]<0||f.shadow[1]>.1f||(f.shadow[2]!=0&&f.shadow[2]!=1)||f.shadow[3]<1||f.shadow[3]>16)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
  std::vector<NcmaScenePassV4> operations(passes,passes+f.pass_count);bool shadows=false,hasGeometry=false;uint32_t tones=0;
  for(const auto& p:operations){for(float x:p.parameters)if(!std::isfinite(x))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
   if(p.operation==6){if(shadows||hasGeometry||p.parameters[0]||p.parameters[1]||p.parameters[2])return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);shadows=true;}
   else if(p.operation==7){if(hasGeometry||p.parameters[0]<0||p.parameters[0]>1||p.parameters[1]||p.parameters[2])return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);hasGeometry=true;}
   else if(p.operation==8){if(!hasGeometry||p.parameters[0]<.01f||p.parameters[0]>16||p.parameters[1]||p.parameters[2])return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);tones++;}
   else return NcmaPlugin::Error(error,NCMA_UNSUPPORTED_FEATURE);
  }if(!hasGeometry||!tones||operations.back().operation!=8||(!shadows&&f.caster_count)||(shadows&&!pipeline.resolution))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
  auto validateDraw=[&](const NcmaSceneDrawV4& item)->bool{const auto& d=item.draw;
   if(d.mesh.generation!=handle||!renderer->meshes.contains(d.mesh.value)||d.material.generation!=handle||!renderer->materials.contains(d.material.value))return false;
   if(renderer->skins.contains(d.mesh.value)&&renderer->skins.at(d.mesh.value)->frame!=b.frame)return false;
   auto count=renderer->meshes.at(d.mesh.value)->indexCount;if(d.reserved[0]||d.reserved[1]||!d.index_count||d.index_count%3||d.first_index%3||d.first_index>count||d.index_count>count-d.first_index)return false;
   for(float x:d.model)if(!std::isfinite(x))return false;for(float x:d.model_view_projection)if(!std::isfinite(x))return false;for(float x:d.normal_matrix)if(!std::isfinite(x))return false;
   const double projectionDeterminant=Eigen::Map<const Eigen::Matrix4f>(d.model_view_projection).cast<double>().determinant();
   if(!std::isfinite(projectionDeterminant)||std::abs(projectionDeterminant)<1e-20)return false;
   const auto* m=d.model;const auto* n=d.normal_matrix;double determinant=static_cast<double>(m[0])*(static_cast<double>(m[5])*m[10]-static_cast<double>(m[6])*m[9])-static_cast<double>(m[4])*(static_cast<double>(m[1])*m[10]-static_cast<double>(m[2])*m[9])+static_cast<double>(m[8])*(static_cast<double>(m[1])*m[6]-static_cast<double>(m[2])*m[5]);
   if(m[3]||m[7]||m[11]||m[15]!=1||!std::isfinite(determinant)||determinant<=1e-8)return false;
   for(int row=0;row<3;++row)for(int col=0;col<3;++col){double dot=0;for(int k=0;k<3;++k)dot+=static_cast<double>(m[row*4+k])*n[col*4+k];if(std::abs(dot-(row==col?1.:0.))>.01)return false;}
   for(float x:item.override_surface)if(!std::isfinite(x))return false;
   return item.override_surface[0]>=0&&item.override_surface[0]<=1&&item.override_surface[1]>=.045f&&item.override_surface[1]<=1&&(item.override_surface[2]==0||item.override_surface[2]==1)&&(item.override_surface[3]==0||item.override_surface[3]==1);
  };
  std::vector<NcmaSceneDrawV4> geo,caster;if(b.draw_count)geo.assign(geometry,geometry+b.draw_count);if(f.caster_count)caster.assign(casters,casters+f.caster_count);
  for(const auto& d:geo)if(!validateDraw(d))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Scene geometry batch.");for(const auto& d:caster)if(!validateDraw(d))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Scene shadow batch.");
  BusyScope busyScope;ExecutionScope execution;std::string message;const auto started=std::chrono::steady_clock::now();ResolveTiming(*renderer);
  if(!renderer->timingPending){auto* dc=renderer->backend->GetDeviceContext();dc->Begin(renderer->gpuDisjoint.Get());dc->End(renderer->gpuBegin.Get());renderer->measuring=true;}
  if(!renderer->backend->BeginFrame(message))return Failure(error,message);
  for(const auto& p:operations){
   if(p.operation==6){if(!pipeline.BeginShadow(message))return Failure(error,message);for(const auto& d:caster){const auto& m=*renderer->materials.at(d.draw.material.value);if(!pipeline.Draw(*renderer->meshes.at(d.draw.mesh.value),d,m.description,m.textures,f,true,shadows,0,message))return Failure(error,message);}renderer->backend->EndRenderPass();}
   else if(p.operation==7){if(!pipeline.BeginGeometry(f,message))return Failure(error,message);for(const auto& d:geo){const auto& m=*renderer->materials.at(d.draw.material.value);if(!pipeline.Draw(*renderer->meshes.at(d.draw.mesh.value),d,m.description,m.textures,f,false,shadows,p.parameters[0],message))return Failure(error,message);}renderer->backend->EndRenderPass();}
   else if(!pipeline.Tone(f,target,p.parameters[0],message))return Failure(error,message);
  }
  if(b.target.value)renderer->targets.at(b.target.value)->lastFrame=b.frame;
  renderer->active=true;renderer->lastFrame=b.frame;renderer->stats.submitted_frames++;renderer->sceneStats.geometry_draws+=b.draw_count;renderer->sceneStats.shadow_draws+=f.caster_count;
  renderer->sceneStats.copied_bytes+=240+(static_cast<uint64_t>(b.draw_count)+f.caster_count)*256+static_cast<uint64_t>(f.pass_count)*16;
  renderer->sceneStats.constant_upload_bytes+=(static_cast<uint64_t>(b.draw_count)+f.caster_count+tones)*400;
  renderer->stats.submit_ms=std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-started).count();Validation(*renderer);execution.success=true;return NCMA_OK;
 });
}
