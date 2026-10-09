uint32_t NCMA_CALL ValidateRuntimeShaders(uint64_t context,uint64_t handle,const NcmaRuntimeShadersV1* input,NcmaErrorV1* error) noexcept {
 return NcmaPlugin::Guard(error,[&]()->uint32_t {
  auto v=ValidateShaderPreparation(context,handle,error);if(v)return v;
  if(!input)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
  const auto d=*input;
  if(d.struct_size!=176||d.version!=1)return NcmaPlugin::Error(error,NCMA_ABI_MISMATCH);
  if(d.profile>1||d.flags>3)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
  const bool emptySkin=!d.skin.struct_size&&!d.skin.version&&!d.skin.bytes&&!d.skin.reserved&&!d.skin.bytecode;
  if(d.profile==0){
   if(d.flags||!emptySkin||d.scene.struct_size||d.scene.version||!RegisteredStages::Empty(d.scene.geometry)||
      !RegisteredStages::Empty(d.scene.shadow)||!RegisteredStages::Empty(d.scene.tone)||!RegisteredStages::Pair(d.ui))
    return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"runtime_ui_closure");
   auto source=Rendering::UiKernel::ShaderSource();
   if((v=RegisteredStages::ValidateCode(d.ui.vertex,d.ui.vertex_bytes,source,"VSMain","vs_5_0",false,error)))return v;
   return RegisteredStages::ValidateCode(d.ui.pixel,d.ui.pixel_bytes,source,"PSMain","ps_5_0",false,error);
  }
  if(renderer->pureUi||!RegisteredStages::Empty(d.ui)||(!(d.flags&2)&&!emptySkin))
   return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"runtime_scene_closure");
  if((v=RegisteredStages::Scene(&d.scene,(d.flags&1)!=0,error)))return v;
  return d.flags&2?RegisteredStages::Skin(&d.skin,error):NCMA_OK;
 });
}
