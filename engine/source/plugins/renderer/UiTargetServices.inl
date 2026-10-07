// Color-only UI target numerics. Managed host owns content, cache and workspace policy.
uint32_t NCMA_CALL CreateUiTarget(uint64_t context,uint64_t handle,const NcmaUiTargetDescriptionV1* d,NcmaUiKeyV1* output,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t {
        auto valid=Instance(context,handle,error);if(valid)return valid;
        if(renderer->failed)return NcmaPlugin::Error(error,renderer->faultResult);
        if(!d||!output||d->struct_size!=sizeof(*d)||d->reserved||!d->width||!d->height||d->width>4096||d->height>4096)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        valid=EnsureUi(error);if(valid)return valid;
        const uint64_t bytes=static_cast<uint64_t>(d->width)*d->height*4;
        if(renderer->uiTargets.size()>=4||bytes+renderer->uiTargetBytes+renderer->ui->Stats(handle,renderer->pureUi).resident_bytes>128ull*1024*1024)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"UI target resident budget.");
        BusyScope scope;std::string message;Rhi::TextureDescription desc{};desc.Width=d->width;desc.Height=d->height;
        desc.Usage=Rhi::TextureUsage::RenderTarget|Rhi::TextureUsage::Sampled;
        auto texture=renderer->backend->CreateTexture(desc,message);
        if(!texture)return FAILED(renderer->backend->GetDevice()->GetDeviceRemovedReason())?Failure(error,message):NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,message);
        const uint64_t id=0x5554000000000000ull|nextResource++;
        try {renderer->uiTargets.emplace(id,Renderer::UiTarget{texture,d->width,d->height});}
        catch(...) {renderer->backend->DestroyTexture(texture);throw;}
        renderer->uiTargetBytes+=bytes;*output={id,handle};Validation(*renderer);return NCMA_OK;
    });
}
uint32_t NCMA_CALL DestroyUiTarget(uint64_t context,uint64_t handle,NcmaUiKeyV1 key,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t {
        auto valid=Instance(context,handle,error);if(valid)return valid;
        auto it=renderer->uiTargets.find(key.value);
        if(key.generation!=handle||it==renderer->uiTargets.end())return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);
        if(renderer->active||it->second.leases)return NcmaPlugin::Error(error,NCMA_BUSY);
        BusyScope scope;std::string message;
        if(SUCCEEDED(renderer->backend->GetDevice()->GetDeviceRemovedReason())&&!Wait(*renderer,message))return NcmaPlugin::Error(error,NCMA_SHUTDOWN_TIMEOUT,message);
        renderer->backend->GetDeviceContext()->ClearState();renderer->backend->DestroyTexture(it->second.color);
        renderer->uiTargetBytes-=static_cast<uint64_t>(it->second.width)*it->second.height*4;renderer->uiTargets.erase(it);Validation(*renderer);return NCMA_OK;
    });
}
uint32_t NCMA_CALL SubmitUiTarget(uint64_t context,uint64_t handle,const NcmaUiTargetFrameV1* input,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t {
        auto valid=Instance(context,handle,error);if(valid)return valid;
        if(!input||input->struct_size!=sizeof(*input)||input->reserved||!input->frame||!input->content_revision||input->target.generation!=handle||input->list.generation!=handle||!renderer->ui||!renderer->ui->HasList(input->list.value))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        auto it=renderer->uiTargets.find(input->target.value);if(it==renderer->uiTargets.end())return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);
        for(float c:input->clear)if(!std::isfinite(c)||c<0||c>1)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        if(renderer->failed)return NcmaPlugin::Error(error,renderer->faultResult);
        auto& target=it->second;
        if(renderer->active||target.leases||input->frame<=renderer->lastFrame||input->frame<=target.produced||input->content_revision<=target.content)return NcmaPlugin::Error(error,NCMA_BUSY,"Stale/pinned UI production.");
        BusyScope scope;ExecutionScope execution;std::string message;Rhi::RenderPassDescription pass{};
        pass.ColorTarget=target.color;pass.ClearColor=true;std::copy_n(input->clear,4,pass.ClearColorValue);
        if(!renderer->backend->BeginRenderPass(pass,message))return Failure(error,message);
        renderer->ui->Draw(input->list.value,target.width,target.height);renderer->backend->EndRenderPass();
        if(FAILED(renderer->backend->GetDevice()->GetDeviceRemovedReason()))return Failure(error,"UI target device lost.");
        target.produced=input->frame;target.content=input->content_revision;renderer->uiProductions++;Validation(*renderer);execution.success=true;return NCMA_OK;
    });
}
uint32_t NCMA_CALL CaptureUiTarget(uint64_t context,uint64_t handle,NcmaUiKeyV1 key,uint8_t* output,uint32_t capacity,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t {
        auto valid=Instance(context,handle,error);if(valid)return valid;auto it=renderer->uiTargets.find(key.value);
        if(key.generation!=handle||it==renderer->uiTargets.end())return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);
        if(renderer->failed)return NcmaPlugin::Error(error,renderer->faultResult);
        if(renderer->active||!it->second.content)return NcmaPlugin::Error(error,NCMA_BUSY);
        const uint64_t bytes=static_cast<uint64_t>(it->second.width)*it->second.height*4;
        if(!output||capacity<bytes){NcmaPlugin::Error(error,NCMA_BUFFER_TOO_SMALL);error->required_bytes=static_cast<uint32_t>(bytes);return NCMA_BUFFER_TOO_SMALL;}
        BusyScope scope;std::string message;if(!renderer->backend->CaptureRgba8(it->second.color,output,capacity,message))return Failure(error,message);Validation(*renderer);return NCMA_OK;
    });
}
uint32_t NCMA_CALL AcquireUiTarget(uint64_t context,uint64_t handle,NcmaUiKeyV1 key,uint64_t content,uint64_t frame,NcmaUiKeyV1* output,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t {
        auto valid=Instance(context,handle,error);if(valid)return valid;auto it=renderer->uiTargets.find(key.value);
        if(!output||key.generation!=handle||it==renderer->uiTargets.end())return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);
        if(renderer->failed)return NcmaPlugin::Error(error,renderer->faultResult);
        if(renderer->active||!content||it->second.content!=content||!frame||frame<=renderer->lastFrame||it->second.produced>frame||renderer->uiLeases.size()>=64)return NcmaPlugin::Error(error,NCMA_BUSY,"Stale UI presentation lease.");
        const uint64_t id=0x554c000000000000ull|nextResource++;
        renderer->uiLeases.emplace(id,Renderer::UiLease{key.value,content,it->second.produced,frame});it->second.leases++;renderer->uiPresentations++;*output={id,handle};return NCMA_OK;
    });
}
uint32_t NCMA_CALL ReleaseUiTarget(uint64_t context,uint64_t handle,NcmaUiKeyV1 key,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t {
        auto valid=Instance(context,handle,error);if(valid)return valid;auto it=renderer->uiLeases.find(key.value);
        if(key.generation!=handle||it==renderer->uiLeases.end())return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);
        if(it->second.pins)return NcmaPlugin::Error(error,NCMA_BUSY,"UI lease pinned by GUI.");
        renderer->uiTargets.at(it->second.target).leases--;renderer->uiLeases.erase(it);return NCMA_OK;
    });
}
uint32_t NCMA_CALL UiTargetStats(uint64_t context,uint64_t handle,NcmaUiTargetStatsV1* output,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]()->uint32_t {auto valid=Instance(context,handle,error);if(valid)return valid;if(!output)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        *output={sizeof(*output),0,handle,renderer->uiTargets.size(),renderer->uiLeases.size(),renderer->uiTargetBytes,renderer->uiProductions,renderer->uiPresentations};return NCMA_OK;});
}
