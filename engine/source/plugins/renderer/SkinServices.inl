// Owner-thread bounded GPU numerics; no character policy or World.
uint32_t CreateSkinPrepared(uint64_t context,uint64_t handle,const NcmaSkinMeshV5* input,NcmaGpuMeshV1* output,NcmaErrorV1* error,const NcmaComputeShaderV1* registered) noexcept {
 return NcmaPlugin::Guard(error,[&]()->uint32_t{
  auto valid=ResourceReady(context,handle,error);if(valid)return valid;if(!input||!output)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);const auto d=*input;const auto& m=d.mesh;
  uint64_t bytes=static_cast<uint64_t>(m.vertex_count)*128+m.index_bytes,extra=renderer->skinKernel?0:Rendering::SkinKernel::ResidentBytes;
  if(m.struct_size!=56||m.layout!=3||m.stride!=80||m.reserved||d.reserved||!m.vertices||!m.indices||!m.vertex_count||!m.index_count||m.index_count%3||
     static_cast<uint64_t>(m.vertex_count)*80!=m.vertex_bytes||static_cast<uint64_t>(m.index_count)*4!=m.index_bytes||!d.binding_count||d.binding_count>1024||bytes>64ull*1024*1024||
     renderer->skins.size()>=64||renderer->meshes.size()>=128||bytes+extra>512ull*1024*1024-renderer->skinStats.resident_bytes||
     static_cast<uint64_t>(m.vertex_count)*48+m.index_bytes>256ull*1024*1024-renderer->meshStats.resident_bytes)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Skin source layout/budget.");
  std::vector<uint8_t> vertices(m.vertices,m.vertices+m.vertex_bytes);std::vector<uint32_t> indices(m.indices,m.indices+m.index_count);
  for(uint32_t i=0;i<m.vertex_count;i++){const auto* row=vertices.data()+static_cast<size_t>(i)*80;float a[12],w[4];uint32_t joints[4];std::memcpy(a,row,48);std::memcpy(joints,row+48,16);std::memcpy(w,row+64,16);
   for(float x:a)if(!std::isfinite(x))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);for(int k=0;k<3;k++)if(std::abs(a[k])>1e6f)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Skin coordinate budget.");
   float n=0,t=0,dot=0,total=0;for(int k=0;k<3;k++){n+=a[k+3]*a[k+3];t+=a[k+8]*a[k+8];dot+=a[k+3]*a[k+8];}
   if(!std::isfinite(n)||!std::isfinite(t)||std::abs(n-1)>.002f||std::abs(t-1)>.002f||std::abs(dot)>.002f||std::abs(a[11])!=1)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Skin vertex basis.");
   for(int k=0;k<4;k++){if(joints[k]>=d.binding_count||!std::isfinite(w[k])||w[k]<0||w[k]>1)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Skin influence.");total+=w[k];}
   if(std::abs(total-1)>.001f)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Normalized four weights required.");
  }
  for(auto index:indices)if(index>=m.vertex_count)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Skin index.");
  BusyScope scope;std::string message;auto mesh=std::make_unique<Rendering::StaticMesh>(*renderer->backend);auto skin=std::make_unique<Rendering::SkinInstance>();std::unique_ptr<Rendering::SkinKernel> kernel;
  if(registered&&renderer->skinKernel&&!renderer->skinKernel->Matches(*registered))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"skin_existing_program_requires_explicit_replace");
  if(!renderer->skinKernel){kernel=std::make_unique<Rendering::SkinKernel>(*renderer->backend);if(!kernel->Initialize(message,registered))return FAILED(renderer->backend->GetDevice()->GetDeviceRemovedReason())?Failure(error,message):NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,message);}
  auto owned=d;owned.mesh.vertices=vertices.data();owned.mesh.indices=indices.data();if(!skin->Initialize(*renderer->backend,*mesh,owned,message))return FAILED(renderer->backend->GetDevice()->GetDeviceRemovedReason())?Failure(error,message):NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,message);
 #ifdef NCMA_RENDERER_TEST_WAIT
  if(registered&&testTonePreparationThrow)throw std::runtime_error("Injected pre-publication diagnostic failure");
 #endif
  Validation(*renderer);uint64_t id=0x534b000000000000ull|nextMesh++;uint64_t meshBytes=mesh->bytes;
  renderer->meshes.emplace(id,std::move(mesh));try{renderer->skins.emplace(id,std::move(skin));}catch(...){renderer->meshes.erase(id);throw;}
  if(kernel)renderer->skinKernel=std::move(kernel);renderer->skinStats.resident_bytes+=bytes+extra;renderer->skinStats.creates++;
  renderer->meshStats.resident_bytes+=meshBytes;renderer->meshStats.uploaded_bytes+=m.vertex_bytes+m.index_bytes;renderer->meshStats.mesh_creates++;*output={id,handle};return NCMA_OK;
 });
}
uint32_t NCMA_CALL CreateSkin(uint64_t context,uint64_t handle,const NcmaSkinMeshV5* input,NcmaGpuMeshV1* output,NcmaErrorV1* error) noexcept {
 return CreateSkinPrepared(context,handle,input,output,error,nullptr);
}
uint32_t NCMA_CALL DestroySkin(uint64_t context,uint64_t handle,NcmaGpuMeshV1 key,NcmaErrorV1* error) noexcept {
 return NcmaPlugin::Guard(error,[&]()->uint32_t{auto valid=Instance(context,handle,error);if(valid)return valid;
  if(key.generation!=handle||!renderer->skins.contains(key.value))return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);if(renderer->active)return NcmaPlugin::Error(error,NCMA_BUSY);
  BusyScope scope;std::string message;if(SUCCEEDED(renderer->backend->GetDevice()->GetDeviceRemovedReason())&&!Wait(*renderer,message))return NcmaPlugin::Error(error,NCMA_SHUTDOWN_TIMEOUT,message);
  renderer->backend->GetDeviceContext()->ClearState();renderer->skinStats.resident_bytes-=renderer->skins.at(key.value)->bytes;renderer->meshStats.resident_bytes-=renderer->meshes.at(key.value)->bytes;
  renderer->skins.erase(key.value);renderer->meshes.erase(key.value);if(renderer->meshes.empty())renderer->meshKernel.reset();if(renderer->skins.empty()){renderer->skinKernel.reset();renderer->skinStats.resident_bytes-=Rendering::SkinKernel::ResidentBytes;renderer->skinStats.gpu_sample_valid=0;renderer->skinStats.gpu_ms=0;}Validation(*renderer);return NCMA_OK;
 });
}
uint32_t NCMA_CALL UpdateSkin(uint64_t context,uint64_t handle,const NcmaSkinBatchV5* input,const NcmaSkinRequestV5* requests,const NcmaSkinPaletteV5* palettes,NcmaErrorV1* error) noexcept {
 return NcmaPlugin::Guard(error,[&]()->uint32_t{auto valid=ResourceReady(context,handle,error);if(valid)return valid;if(!input)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);auto batch=*input;
  if(batch.struct_size!=32||batch.generation!=handle||!batch.frame||batch.frame<=renderer->lastFrame||batch.frame<=renderer->lastSkinFrame||!batch.request_count||batch.request_count>32||
     !requests||!palettes||!batch.palette_count||batch.palette_count>32768||batch.reserved||!renderer->skinKernel)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Skin batch/frame.");
  // Fixed context scratch avoids per-frame native heap allocation. Failed preflight never publishes it.
  auto jobs=std::span(renderer->skinKernel->copiedJobs).first(batch.request_count);auto data=std::span(renderer->skinKernel->copiedPalettes).first(batch.palette_count);
  std::copy_n(requests,batch.request_count,jobs.begin());std::copy_n(palettes,batch.palette_count,data.begin());uint32_t end=0;uint64_t vertices=0;
  for(size_t i=0;i<jobs.size();i++){const auto& j=jobs[i];if(j.mesh.generation!=handle||!renderer->skins.contains(j.mesh.value))return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);
   for(size_t k=0;k<i;k++)if(jobs[k].mesh.value==j.mesh.value)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Duplicate skin output.");
   const auto& skin=*renderer->skins.at(j.mesh.value);if(j.palette_offset!=end||j.palette_count!=skin.bindings||j.palette_count>batch.palette_count-end)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Skin palette range.");end+=j.palette_count;vertices+=skin.vertices;
  }if(end!=batch.palette_count)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Unused palette data.");
  for(const auto& p:data){for(float x:p.model)if(!std::isfinite(x)||std::abs(x)>1e8f)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Skin model numerical budget.");for(float x:p.normal)if(!std::isfinite(x)||std::abs(x)>1e8f)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Skin normal numerical budget.");
   auto model=Eigen::Map<const Eigen::Matrix4f>(p.model).cast<double>().eval();double determinant=model.block<3,3>(0,0).determinant();if(p.model[3]||p.model[7]||p.model[11]||p.model[15]!=1||!std::isfinite(determinant)||determinant<=1e-15)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Positive affine skin palette required.");
   for(int row=0;row<3;row++)for(int col=0;col<3;col++){double value=0;for(int k=0;k<3;k++)value+=static_cast<double>(p.model[row*4+k])*p.normal[col*4+k];if(std::abs(value-(row==col?1.:0.))>.01)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Skin inverse-transpose mismatch.");}
  }
  std::string message;auto slot=renderer->skinKernel->SelectSlot(renderer->skinStats,message);if(slot)return slot==NCMA_DEVICE_LOST?Failure(error,message):NcmaPlugin::Error(error,slot,message);
  BusyScope scope;ExecutionScope execution;auto started=std::chrono::steady_clock::now();renderer->skinKernel->Begin(data.data(),batch.palette_count);
  for(const auto& j:jobs)renderer->skinKernel->Dispatch(*renderer->skins.at(j.mesh.value),j.palette_offset);renderer->skinKernel->End();
  if(FAILED(renderer->backend->GetDevice()->GetDeviceRemovedReason()))return Failure(error,"Skin dispatch device lost.");Validation(*renderer);
  for(const auto& j:jobs)renderer->skins.at(j.mesh.value)->frame=batch.frame;renderer->lastSkinFrame=batch.frame;renderer->skinStats.batches++;renderer->skinStats.vertices+=vertices;renderer->skinStats.palette_bytes+=static_cast<uint64_t>(batch.palette_count)*128;
  renderer->skinStats.cpu_ms=std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-started).count();execution.success=true;return NCMA_OK;
 });
}
uint32_t NCMA_CALL CaptureSkin(uint64_t context,uint64_t handle,NcmaGpuMeshV1 key,uint8_t* output,uint32_t bytes,NcmaErrorV1* error) noexcept {
 return NcmaPlugin::Guard(error,[&]()->uint32_t{auto valid=Instance(context,handle,error);if(valid)return valid;if(key.generation!=handle||!renderer->skins.contains(key.value))return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);
  uint32_t required=renderer->skins.at(key.value)->vertices*48;if(!output||bytes<required){NcmaPlugin::Error(error,NCMA_BUFFER_TOO_SMALL);error->required_bytes=required;return NCMA_BUFFER_TOO_SMALL;}
  if(bytes!=required||!renderer->skins.at(key.value)->frame)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);if(renderer->failed)return NcmaPlugin::Error(error,renderer->faultResult);
  BusyScope scope;std::string message;if(!Wait(*renderer,message))return NcmaPlugin::Error(error,NCMA_SHUTDOWN_TIMEOUT,message);
  if(!renderer->skinKernel->Capture(*renderer->meshes.at(key.value),output,bytes,message))return Failure(error,message);renderer->skinStats.captures++;Validation(*renderer);return NCMA_OK;
 });
}
uint32_t NCMA_CALL SkinStats(uint64_t context,uint64_t handle,NcmaSkinStatsV5* output,NcmaErrorV1* error) noexcept {
 return NcmaPlugin::Guard(error,[&]()->uint32_t{auto valid=Instance(context,handle,error);if(valid)return valid;if(!output)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
  if(renderer->skinKernel)renderer->skinKernel->Resolve(renderer->skinStats);renderer->skinStats.generation=handle;renderer->skinStats.meshes=renderer->skins.size();*output=renderer->skinStats;return NCMA_OK;});
}
