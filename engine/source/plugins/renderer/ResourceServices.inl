// Included in RendererPlugin's private namespace; no separate registry or scene ownership.
uint32_t ResourceReady(uint64_t context,uint64_t handle,NcmaErrorV1* error) {
    auto valid=Instance(context,handle,error);if(valid)return valid;
    if(renderer->failed)return NcmaPlugin::Error(error,renderer->faultResult);
    return renderer->active?NcmaPlugin::Error(error,NCMA_BUSY):NCMA_OK;
}
uint32_t NCMA_CALL CreateTexture(uint64_t context,uint64_t handle,const NcmaTextureDescriptionV3* input,NcmaGpuResourceV3* output,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {
        auto valid=ResourceReady(context,handle,error);if(valid)return valid;
        if(!input||!output)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);const auto d=*input;
        if(d.struct_size!=48||!d.width||!d.height||d.width>4096||d.height>4096||d.format<1||d.format>2||!d.mips||!d.pixels||!d.mip_count||d.mip_count>13||
           d.reserved[0]||d.reserved[1]||d.data_bytes>64u*1024*1024||renderer->textures.size()>=256||
           d.data_bytes>256ull*1024*1024-renderer->resourceStats.resident_bytes)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Texture format/budget.");
        Rhi::TextureDescription t{};t.Width=d.width;t.Height=d.height;t.MipLevels=d.mip_count;t.Format=d.format==2?Rhi::TextureFormat::Rgba8Srgb:Rhi::TextureFormat::Rgba8Unorm;
        uint32_t width=d.width,height=d.height;uint64_t offset=0;
        for(uint32_t i=0;i<d.mip_count;++i) {
            const auto m=d.mips[i];uint64_t size=static_cast<uint64_t>(width)*height*4;
            if(m.width!=width||m.height!=height||m.offset!=offset||m.row_pitch!=width*4||offset+size>d.data_bytes||(width==1&&height==1&&i+1!=d.mip_count))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Noncanonical texture mip.");
            t.InitialMips.push_back({d.pixels+offset,m.row_pitch,static_cast<uint32_t>(size)});offset+=size;
            if(i+1==d.mip_count&&(width!=1||height!=1))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Incomplete mip chain.");
            width=std::max(1u,width/2);height=std::max(1u,height/2);
        }
        if(offset!=d.data_bytes)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Trailing texture data.");
        BusyScope scope;std::string message;auto candidate=std::make_unique<TextureGpu>(*renderer->backend);
        candidate->texture=renderer->backend->CreateTexture(t,message);if(!candidate->texture)return FAILED(renderer->backend->GetDevice()->GetDeviceRemovedReason())?Failure(error,message):NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,message);
        candidate->bytes=d.data_bytes;candidate->format=d.format;Validation(*renderer);
        const uint64_t id=0x5458000000000000ull|nextResource++;renderer->textures.emplace(id,std::move(candidate));
        renderer->resourceStats.creates++;renderer->resourceStats.uploaded_bytes+=d.data_bytes;renderer->resourceStats.resident_bytes+=d.data_bytes;*output={id,handle};return NCMA_OK;
    });
}
uint32_t NCMA_CALL CreateMaterial(uint64_t context,uint64_t handle,const NcmaMaterialDescriptionV3* input,NcmaGpuResourceV3* output,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {
        auto valid=ResourceReady(context,handle,error);if(valid)return valid;
        if(!input||!output)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);const auto d=*input;
        if(d.struct_size!=160||d.flags>7||((d.flags&4)&&!(d.flags&2))||d.channels[1]||(d.channels[0]&0xff000000)||
           (d.channels[0]&255)>3||((d.channels[0]>>8)&255)>3||((d.channels[0]>>16)&255)>3||renderer->materials.size()>=256)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        for(float x:d.base_color)if(!std::isfinite(x)||x<0||x>1)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        for(float x:d.emissive)if(!std::isfinite(x)||x<0||x>16)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        for(float x:d.surface)if(!std::isfinite(x))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        if(d.surface[0]<0||d.surface[0]>1||d.surface[1]<.045f||d.surface[1]>1||d.surface[2]<0||d.surface[2]>4||d.surface[3]<0||d.surface[3]>1)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        auto candidate=std::make_unique<MaterialGpu>();candidate->description=d;
        for(size_t i=0;i<6;++i) {
            const auto ref=d.textures[i];
            if(!ref.value){if(ref.generation)return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);continue;}
            const auto it=renderer->textures.find(ref.value);
            if(ref.generation!=handle||it==renderer->textures.end())return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);
            if(it->second->format!=((i==0||i==5)?2u:1u))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Texture color-space role mismatch.");
            candidate->textures[i]=it->second->texture;
        }
        BusyScope scope;std::string message;std::unique_ptr<Rendering::ResourceKernel> kernel;
        if(!renderer->resourceKernel){kernel=std::make_unique<Rendering::ResourceKernel>(*renderer->backend);if(!kernel->Initialize(message))return FAILED(renderer->backend->GetDevice()->GetDeviceRemovedReason())?Failure(error,message):NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,message);}
        Validation(*renderer);const uint64_t id=0x4D54000000000000ull|nextResource++;renderer->materials.emplace(id,std::move(candidate));
        if(kernel)renderer->resourceKernel=std::move(kernel);renderer->resourceStats.creates++;*output={id,handle};return NCMA_OK;
    });
}
uint32_t NCMA_CALL CreateTarget(uint64_t context,uint64_t handle,const NcmaTargetDescriptionV3* input,NcmaGpuResourceV3* output,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {
        auto valid=ResourceReady(context,handle,error);if(valid)return valid;if(!input||!output)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);const auto d=*input;
        const uint64_t bytes=static_cast<uint64_t>(d.width)*d.height*8;
        if(d.struct_size!=16||d.reserved||!d.width||!d.height||d.width>4096||d.height>4096||renderer->targets.size()>=16||bytes>256ull*1024*1024-renderer->resourceStats.resident_bytes)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        BusyScope scope;std::string message;auto candidate=std::make_unique<TargetGpu>(*renderer->backend);candidate->width=d.width;candidate->height=d.height;
        Rhi::TextureDescription t{};t.Width=d.width;t.Height=d.height;t.Usage=Rhi::TextureUsage::RenderTarget|Rhi::TextureUsage::Sampled;
        candidate->color=renderer->backend->CreateTexture(t,message);if(!candidate->color)return FAILED(renderer->backend->GetDevice()->GetDeviceRemovedReason())?Failure(error,message):NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,message);
        t.Format=Rhi::TextureFormat::D32Float;t.Usage=Rhi::TextureUsage::DepthStencil;candidate->depth=renderer->backend->CreateTexture(t,message);
        if(!candidate->depth)return FAILED(renderer->backend->GetDevice()->GetDeviceRemovedReason())?Failure(error,message):NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,message);
        Validation(*renderer);const uint64_t id=0x5447000000000000ull|nextResource++;renderer->targets.emplace(id,std::move(candidate));
        renderer->resourceStats.creates++;renderer->resourceStats.resident_bytes+=bytes;*output={id,handle};return NCMA_OK;
    });
}
uint32_t NCMA_CALL DestroyResource(uint64_t context,uint64_t handle,NcmaGpuResourceV3 resource,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {
        auto valid=Instance(context,handle,error);if(valid)return valid;
        if(resource.generation!=handle||(!renderer->textures.contains(resource.value)&&!renderer->materials.contains(resource.value)&&!renderer->targets.contains(resource.value)))return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);
        if(renderer->active)return NcmaPlugin::Error(error,NCMA_BUSY);
        if(renderer->textures.contains(resource.value))for(const auto& pair:renderer->materials)for(const auto& t:pair.second->description.textures)if(t.value==resource.value)return NcmaPlugin::Error(error,NCMA_BUSY,"Texture pinned by material.");
        BusyScope scope;std::string message;if(SUCCEEDED(renderer->backend->GetDevice()->GetDeviceRemovedReason())&&!Wait(*renderer,message))return NcmaPlugin::Error(error,NCMA_SHUTDOWN_TIMEOUT,message);
        renderer->backend->GetDeviceContext()->ClearState();
        if(renderer->textures.contains(resource.value)){renderer->resourceStats.resident_bytes-=renderer->textures.at(resource.value)->bytes;renderer->textures.erase(resource.value);}
        else if(renderer->targets.contains(resource.value)){const auto& t=*renderer->targets.at(resource.value);renderer->resourceStats.resident_bytes-=static_cast<uint64_t>(t.width)*t.height*8;renderer->targets.erase(resource.value);}
        else {renderer->materials.erase(resource.value);if(renderer->materials.empty())renderer->resourceKernel.reset();}
        Validation(*renderer);return NCMA_OK;
    });
}
uint32_t NCMA_CALL SubmitResources(uint64_t context,uint64_t handle,const NcmaResourceFrameV3* input,const NcmaResourceDrawV3* draws,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {
        auto valid=ResourceReady(context,handle,error);if(valid)return valid;if(!input)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);const auto f=*input;
        if(f.struct_size!=136||!f.frame||f.frame<=renderer->lastFrame||f.generation!=handle||f.draw_count>4096||(f.draw_count&&!draws)||f.reserved||f.mode>2||
           !std::isfinite(f.exposure)||f.exposure<.01f||f.exposure>16||!std::isfinite(f.ambient)||f.ambient<0||f.ambient>1)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        uint32_t width=renderer->stats.width,height=renderer->stats.height;TargetGpu* target=nullptr;
        if(f.target.value){if(f.target.generation!=handle||!renderer->targets.contains(f.target.value))return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);target=renderer->targets.at(f.target.value).get();width=target->width;height=target->height;}
        else if(f.target.generation)return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);
        for(float x:f.viewport)if(!std::isfinite(x))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        if(f.viewport[0]<0||f.viewport[1]<0||f.viewport[2]<=0||f.viewport[3]<=0||f.viewport[0]+f.viewport[2]>width||f.viewport[1]+f.viewport[3]>height)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        for(float x:f.clear)if(!std::isfinite(x)||x<0||x>1)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        for(float x:f.camera)if(!std::isfinite(x))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        float length=0;for(float x:f.light_direction){if(!std::isfinite(x))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);length+=x*x;}
        if(!std::isfinite(length)||length<.0001f)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        for(float x:f.light_color)if(!std::isfinite(x)||x<0||x>16)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        std::vector<NcmaResourceDrawV3> batch;if(f.draw_count)batch.assign(draws,draws+f.draw_count);
        for(const auto& d:batch){
            if(d.mesh.generation!=handle||!renderer->meshes.contains(d.mesh.value)||d.material.generation!=handle||!renderer->materials.contains(d.material.value))return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);
            const uint32_t count=renderer->meshes.at(d.mesh.value)->indexCount;
            if(d.reserved[0]||d.reserved[1]||!d.index_count||d.index_count%3||d.first_index%3||d.first_index>count||d.index_count>count-d.first_index)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
            for(float x:d.model)if(!std::isfinite(x))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
            for(float x:d.model_view_projection)if(!std::isfinite(x))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
            for(float x:d.normal_matrix)if(!std::isfinite(x))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
            const auto* m=d.model;const auto* n=d.normal_matrix;
            const double determinant=static_cast<double>(m[0])*(static_cast<double>(m[5])*m[10]-static_cast<double>(m[6])*m[9])-static_cast<double>(m[4])*(static_cast<double>(m[1])*m[10]-static_cast<double>(m[2])*m[9])+static_cast<double>(m[8])*(static_cast<double>(m[1])*m[6]-static_cast<double>(m[2])*m[5]);
            if(m[3]!=0||m[7]!=0||m[11]!=0||m[15]!=1||!std::isfinite(determinant)||determinant<=1e-8)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Positive affine model required.");
            for(int row=0;row<3;++row)for(int col=0;col<3;++col){double dot=0;for(int k=0;k<3;++k)dot+=static_cast<double>(m[row*4+k])*n[col*4+k];if(std::abs(dot-(row==col?1.0:0.0))>.01)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Normal inverse-transpose mismatch.");}
        }
        BusyScope scope;ExecutionScope execution;std::string message;const auto started=std::chrono::steady_clock::now();ResolveTiming(*renderer);
        if(!renderer->timingPending){auto* dc=renderer->backend->GetDeviceContext();dc->Begin(renderer->gpuDisjoint.Get());dc->End(renderer->gpuBegin.Get());renderer->measuring=true;}
        const auto encode=[](float x){return x<=.0031308f?12.92f*x:1.055f*std::pow(x,1.0f/2.4f)-.055f;};
        renderer->backend->SetClearColor(encode(f.clear[0]),encode(f.clear[1]),encode(f.clear[2]),f.clear[3]);
        bool begun=renderer->backend->BeginFrame(message);renderer->backend->SetClearColor(.035f,.039f,.052f,1);if(!begun)return Failure(error,message);
        if(target){Rhi::RenderPassDescription p{};p.ColorTarget=target->color;p.DepthTarget=target->depth;p.ClearColor=true;p.ClearDepth=true;
            for(int i=0;i<4;++i)p.ClearColorValue[i]=i==3?f.clear[i]:encode(f.clear[i]);if(!renderer->backend->BeginRenderPass(p,message))return Failure(error,message);}
        for(const auto& d:batch){const auto& material=*renderer->materials.at(d.material.value);
            if(!renderer->resourceKernel->Draw(*renderer->meshes.at(d.mesh.value),d,material.description,material.textures,f,message))return Failure(error,message);}
        renderer->backend->EndRenderPass();renderer->active=true;renderer->lastFrame=f.frame;renderer->stats.submitted_frames++;renderer->resourceStats.draws+=f.draw_count;
        renderer->stats.submit_ms=std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-started).count();Validation(*renderer);execution.success=true;return NCMA_OK;
    });
}
uint32_t NCMA_CALL ResourceStats(uint64_t context,uint64_t handle,NcmaResourceStatsV3* output,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {auto valid=Instance(context,handle,error);if(valid)return valid;if(!output)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        renderer->resourceStats.generation=handle;renderer->resourceStats.textures=renderer->textures.size();renderer->resourceStats.materials=renderer->materials.size();renderer->resourceStats.targets=renderer->targets.size();*output=renderer->resourceStats;return NCMA_OK;});
}
uint32_t NCMA_CALL CaptureTarget(uint64_t context,uint64_t handle,NcmaGpuResourceV3 ref,uint8_t* output,uint32_t capacity,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {auto valid=Instance(context,handle,error);if(valid)return valid;
        if(ref.generation!=handle||!renderer->targets.contains(ref.value))return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);
        const auto& target=*renderer->targets.at(ref.value);const uint32_t bytes=target.width*target.height*4;
        if(!output||capacity<bytes){NcmaPlugin::Error(error,NCMA_BUFFER_TOO_SMALL);error->required_bytes=bytes;return NCMA_BUFFER_TOO_SMALL;}
        if(capacity!=bytes)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        if(renderer->failed)return NcmaPlugin::Error(error,renderer->faultResult);if(!renderer->active)return NcmaPlugin::Error(error,NCMA_BUSY);
        BusyScope scope;std::string message;if(!renderer->backend->CaptureRgba8(target.color,output,capacity,message))return Failure(error,message);
        Validation(*renderer);return NCMA_OK;});
}
