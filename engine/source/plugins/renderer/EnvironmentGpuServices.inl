uint32_t NCMA_CALL ValidateEnvironmentGpu(uint64_t context,uint64_t handle,NcmaErrorV1* error) noexcept {
 return NcmaPlugin::Guard(error,[&]()->uint32_t{auto valid=Instance(context,handle,error);if(valid)return valid;
 if(renderer->pureUi)return NcmaPlugin::Error(error,NCMA_UNSUPPORTED_FEATURE,"Pure UI excludes environment GPU resources.");
 if(renderer->failed)return NcmaPlugin::Error(error,renderer->faultResult);
 if(renderer->active)return NcmaPlugin::Error(error,NCMA_BUSY,"Environment off-frame boundary required.");return NCMA_OK;});
}
#ifdef NCMA_RENDERER_TEST_WAIT
bool testEnvironmentGpuPublicationFault=false;
#endif
uint32_t NCMA_CALL PublishEnvironmentGpu(uint64_t context,uint64_t handle,NcmaGpuResourceV3 old,const NcmaEnvironmentGpuDescriptionV1* input,NcmaGpuResourceV3* output,NcmaErrorV1* error) noexcept {
 return NcmaPlugin::Guard(error,[&]()->uint32_t{auto valid=ValidateEnvironmentGpu(context,handle,error);if(valid)return valid;
 if(!input||!output||!input->values)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
 const auto d=*input;if(!Rendering::EnvironmentGpuKernel::Describe(d))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Environment layout.");
 auto previous=renderer->environments.find(old.value);
 if(old.value?(old.generation!=handle||previous==renderer->environments.end()):old.generation!=0)return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);
 const uint64_t bytes=static_cast<uint64_t>(d.float_count)*4;
 if((!old.value&&renderer->environments.size()>=8)||renderer->environmentStats.resident_bytes+bytes>renderer->environmentStats.budget_bytes)return NcmaPlugin::Error(error,NCMA_BUSY,"Environment resource budget including candidate.");
 BusyScope scope;const std::vector<float> copied(d.values,d.values+d.float_count);
 if(!Rendering::EnvironmentGpuKernel::ValidValues(d,copied))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Environment complete numerical values.");
 auto candidate=std::make_unique<Rendering::EnvironmentGpuKernel>(*renderer->backend);std::string message;
 if(!candidate->Initialize(d,copied,message))return NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,message);
#ifdef NCMA_RENDERER_TEST_WAIT
 if(testEnvironmentGpuPublicationFault)throw std::runtime_error("Environment diagnostics prepublication fault");
#endif
 Validation(*renderer);if(renderer->stats.validation_errors||renderer->stats.validation_warnings)return NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,"Environment GPU validation.");
 if(old.value&&!Wait(*renderer,message))return NcmaPlugin::Error(error,NCMA_SHUTDOWN_TIMEOUT,message);
 Validation(*renderer);if(renderer->stats.validation_errors||renderer->stats.validation_warnings)return NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,"Environment completion validation.");
 if(old.value) {
     const uint64_t oldBytes=static_cast<uint64_t>(previous->second->layout.float_count)*4;
     renderer->backend->GetDeviceContext()->ClearState();previous->second.swap(candidate);
     renderer->environmentStats.resident_bytes-=oldBytes;renderer->environmentStats.retirements++;
 } else {
     const uint64_t key=nextResource;renderer->environments.emplace(key,std::move(candidate));nextResource++;
     old={key,handle};renderer->environmentStats.live++;
 }
 renderer->environmentStats.resident_bytes+=bytes;renderer->environmentStats.publications++;renderer->environmentStats.uploaded_bytes+=bytes;
 *output=old;return NCMA_OK;});
}
uint32_t NCMA_CALL DestroyEnvironmentGpu(uint64_t context,uint64_t handle,NcmaGpuResourceV3 key,NcmaErrorV1* error) noexcept {
 return NcmaPlugin::Guard(error,[&]()->uint32_t{auto valid=Instance(context,handle,error);if(valid)return valid;
 auto found=renderer->environments.find(key.value);if(key.generation!=handle||found==renderer->environments.end())return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);
 if(renderer->active)return NcmaPlugin::Error(error,NCMA_BUSY);
 BusyScope scope;std::string message;Validation(*renderer);
 if(SUCCEEDED(renderer->backend->GetDevice()->GetDeviceRemovedReason())&&!Wait(*renderer,message))return NcmaPlugin::Error(error,NCMA_SHUTDOWN_TIMEOUT,message);
 renderer->backend->GetDeviceContext()->ClearState();renderer->environmentStats.resident_bytes-=static_cast<uint64_t>(found->second->layout.float_count)*4;
 renderer->environments.erase(found);renderer->environmentStats.live--;renderer->environmentStats.retirements++;return NCMA_OK;});
}
uint32_t NCMA_CALL CaptureEnvironmentGpu(uint64_t context,uint64_t handle,NcmaGpuResourceV3 key,float* output,uint32_t capacity,NcmaErrorV1* error) noexcept {
 return NcmaPlugin::Guard(error,[&]()->uint32_t{auto valid=ValidateEnvironmentGpu(context,handle,error);if(valid)return valid;
 auto found=renderer->environments.find(key.value);if(key.generation!=handle||found==renderer->environments.end())return NcmaPlugin::Error(error,NCMA_INVALID_HANDLE);
 const uint32_t floats=found->second->layout.float_count;
 if(capacity<floats){NcmaPlugin::Error(error,NCMA_BUFFER_TOO_SMALL);if(error)error->required_bytes=floats*4;return NCMA_BUFFER_TOO_SMALL;}
 if(!output||capacity!=floats)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
 BusyScope scope;std::vector<float> candidate(floats);Rendering::EnvironmentGpuKernel::Capture capture;std::string message;
 if(!found->second->PrepareCapture(capture,message))return NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,message);
 if(!Wait(*renderer,message))return NcmaPlugin::Error(error,NCMA_SHUTDOWN_TIMEOUT,message);
 if(!found->second->ReadCapture(capture,candidate,message))return NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,message);
#ifdef NCMA_RENDERER_TEST_WAIT
 if(testEnvironmentGpuPublicationFault)throw std::runtime_error("Environment capture prepublication fault");
#endif
 Validation(*renderer);if(renderer->stats.validation_errors||renderer->stats.validation_warnings)return NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,"Environment capture validation.");
 std::memcpy(output,candidate.data(),static_cast<size_t>(floats)*4);renderer->environmentStats.captures++;return NCMA_OK;});
}
uint32_t NCMA_CALL EnvironmentGpuStats(uint64_t context,uint64_t handle,NcmaEnvironmentGpuStatsV1* output,NcmaErrorV1* error) noexcept {
 return NcmaPlugin::Guard(error,[&]()->uint32_t{auto valid=Instance(context,handle,error);if(valid)return valid;
 if(!output)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);*output=renderer->environmentStats;return NCMA_OK;});
}
