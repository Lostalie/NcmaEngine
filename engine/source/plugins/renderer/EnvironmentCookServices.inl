#ifdef NCMA_RENDERER_TEST_WAIT
bool testEnvironmentPublicationFault=false; // Test TU only, no production control/export.
#endif
uint32_t NCMA_CALL ValidateEnvironmentCook(uint64_t context,NcmaErrorV1* error) noexcept {
 return NcmaPlugin::Guard(error,[&]()->uint32_t{auto valid=Validate(context,error);if(valid)return valid;
 if(renderer){if(renderer->pureUi)return NcmaPlugin::Error(error,NCMA_UNSUPPORTED_FEATURE,"Pure UI excludes environment cook.");
 if(renderer->failed)return NcmaPlugin::Error(error,renderer->faultResult);
 if(renderer->active)return NcmaPlugin::Error(error,NCMA_BUSY,"Offline environment boundary required.");}return NCMA_OK;});
}
uint32_t NCMA_CALL CookEnvironment(uint64_t context,const NcmaEnvironmentCookV1* input,NcmaEnvironmentCookOutputV1* output,float* values,uint32_t capacity,NcmaErrorV1* error) noexcept {
 return NcmaPlugin::Guard(error,[&]()->uint32_t{const auto valid=ValidateEnvironmentCook(context,error);if(valid)return valid;
 if(!input||!output||!input->input)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
 const auto description=*input;Rendering::EnvironmentCook::Layout layout{};
 if(!Rendering::EnvironmentCook::Describe(description,layout))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Environment budget/layout.");
 if(capacity<layout.floats){NcmaPlugin::Error(error,NCMA_BUFFER_TOO_SMALL);if(error)error->required_bytes=layout.floats*4;return NCMA_BUFFER_TOO_SMALL;}
 if(!values)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
 BusyScope scope;const std::vector<float> source(description.input,description.input+description.input_floats);
 if(!Rendering::EnvironmentCook::ValidInput(source))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"Linear HDR input required.");
 const auto result=Rendering::EnvironmentCook::Run(description,source,layout);
#ifdef NCMA_RENDERER_TEST_WAIT
 if(testEnvironmentPublicationFault)throw std::runtime_error("Environment publication fault.");
#endif
 const NcmaEnvironmentCookOutputV1 metadata{32,1,1,layout.floats,layout.levels,description.input_floats*4,0,0};
 std::memcpy(values,result.data(),result.size()*sizeof(float));*output=metadata;return NCMA_OK;});
}
