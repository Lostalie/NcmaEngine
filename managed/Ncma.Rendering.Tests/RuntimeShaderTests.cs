using System.Numerics;
using System.Text.Json;
using Ncma.Assets;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Ui;

internal static unsafe partial class Program
{
    private static int RuntimeShaderTests(string root,string output)
    {
        Directory.CreateDirectory(output);int cases=0;bool allowed=true;int maxError=0;
        void Pass(bool value,string message){Check(value,"C4-B "+message);cases++;}
        void Bad(Action action){Reject(action);cases++;}
        RuntimeShaderPackage Read(RuntimeShaderPackage package)=>RuntimeShaderPackage.Preflight(package.CopyBytes(),package.ContentHash);
        using var loader=new PluginLoader();loader.Load(Path.GetFullPath(root),Specs().Take(2).ToArray());
        var native=loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer);
        using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"Runtime shader admission",256,256,false);
        RuntimeShaderPreparation retained;
        using(var renderer=new RendererSession(native,window,256,256)){
            var defaults=renderer.DefaultRuntimeShaders;
            retained=defaults.Prepare(ShaderProfile.Flat2D,false,false,()=>allowed);
            Pass(retained.BindingsValidated&&!retained.Package.GpuValidated&&retained.Scene is null&&retained.Skin is null,"actual reflection is separate from CPU package and GPU resources");
            var loaded=RuntimeShaderPreparation.Prepare(renderer,Read(retained.Package),()=>allowed);
            Pass(loaded.CopyMetadata().SequenceEqual(retained.CopyMetadata()),"source-free exact metadata roundtrip");
            Pass(renderer.UiStats.ResidentBytes==0&&renderer.PipelineStats.Pipelines==0&&renderer.SkinStats.Meshes==0,"no GPU resources in whole admission");
            Bad(()=>defaults.Prepare(ShaderProfile.Flat2D,true,false,()=>true));
            allowed=false;Bad(()=>RuntimeShaderPreparation.Prepare(renderer,retained.Package,()=>allowed));Bad(()=>defaults.Prepare(ShaderProfile.Flat2D,false,false,()=>allowed));allowed=true;
            Bad(()=>RuntimeShaderPreparation.Prepare(renderer,retained.Package,()=>{retained.VerifyFor(renderer);return true;}));
            Exception? threadError=null;var worker=new Thread(()=>{try{retained.VerifyFor(renderer);}catch(Exception e){threadError=e;}});worker.Start();worker.Join();
            Pass(threadError is not null,"wrong thread rejected");
            renderer.CreateUiShaders(loaded.Ui!);
            using(var image=renderer.CreateUiImage(1,1,[255,255,255,255])){
                var builder=new UiDisplayListBuilder(renderer);
                builder.Quad(new(Guid.NewGuid(),new(0,0,128,128),Matrix3x2.CreateTranslation(16,16),new(0,0,256,256),1,true,-1),image,new(.8f,.2f,.1f,1));
                using var list=builder.Build();using var target=renderer.CreateUiTarget(256,256);
                renderer.SubmitUiTarget(target,list,1,1,Vector4.Zero);var baseline=renderer.CaptureUiTarget(target);
                renderer.ReplaceUiShaders(retained.Ui!);renderer.SubmitUiTarget(target,list,2,2,Vector4.Zero);var pixels=renderer.CaptureUiTarget(target);
                Pass(pixels.SequenceEqual(baseline)&&baseline[(32*256+32)*4]>150,"loaded UI actual GPU pixels");
                using var compiler=new ShaderCompilerService(renderer,()=>true);
                var selected=DefaultUiShaders.Select(DefaultUiShaders.CopyCatalog(renderer));var pd=selected.Pixel.CopyDefinition();
                string source=pd.Source.Replace("Image.Sample(Linear,v.uv)*v.c","Image.Sample(Linear,v.uv).bgra*v.c.bgra",StringComparison.Ordinal);
                var user=pd with{AssetId=Guid.NewGuid(),Source=source,SourceHash=ShaderContractCodec.HashSource(source)};
                RuntimeShaderPackage CookUi(ShaderDefinition pixel)=>RuntimeShaderPackage.Cook(ShaderProfile.Flat2D,false,false,[
                    new(RuntimeShaderRole.UiVertex,compiler.Prepare(selected.Vertex)),new(RuntimeShaderRole.UiPixel,compiler.Prepare(ShaderDescriptor.Prepare(pixel)))]);
                var userPrepared=RuntimeShaderPreparation.Prepare(renderer,Read(CookUi(user)),()=>allowed);
                renderer.ReplaceUiShaders(userPrepared.Ui!);renderer.SubmitUiTarget(target,list,3,3,Vector4.Zero);pixels=renderer.CaptureUiTarget(target);
                for(int i=0;i<pixels.Length;i++)maxError=Math.Max(maxError,Math.Abs(pixels[i]-baseline[i/4*4+(i%4==3?3:2-i%4)]));
                Pass(maxError<=1&&!pixels.SequenceEqual(baseline),"source-free user channel oracle");
                var wrongSource=pd.Source.Replace(":SV_TARGET",":SV_TARGET1",StringComparison.Ordinal);
                Pass(wrongSource!=pd.Source,"actual wrong output fixture");
                var wrong=CookUi(pd with{AssetId=Guid.NewGuid(),Source=wrongSource,SourceHash=ShaderContractCodec.HashSource(wrongSource)});
                Pass(!Read(wrong).GpuValidated,"structural CPU preflight deliberately accepts wrong output");
                ulong generation=renderer.UiShaderGeneration;
                Bad(()=>RuntimeShaderPreparation.Prepare(renderer,Read(wrong),()=>true));
                Pass(renderer.UiShaderGeneration==generation&&ReferenceEquals(renderer.RegisteredUiShaders,userPrepared.Ui),"wrong final output does not publish metadata");
                renderer.SubmitUiTarget(target,list,4,4,Vector4.Zero);Pass(renderer.CaptureUiTarget(target).SequenceEqual(pixels),"wrong output keeps actual old pixels");
                allowed=false;Bad(()=>renderer.ReplaceUiShaders(retained.Ui!));allowed=true;
                renderer.SubmitUi(list,5,Vector4.Zero);Bad(()=>RuntimeShaderPreparation.Prepare(renderer,retained.Package,()=>true));renderer.Present();
            }
            ulong frame=6;
            using var cache=new RenderResourceCache(renderer);var data=Quad();using var mesh=renderer.CreateStaticMesh(data);
            var material=MaterialDefinition.Default(Guid.NewGuid()) with{BaseColor=new(.6f,.25f,.1f,1),Metallic=.1f,Roughness=.6f};
            using var mat=cache.AcquireMaterial(Version(material),material,_=>null,true,true,out _);using var target3d=renderer.CreateViewTarget(256,256);
            foreach(bool shadows in new[]{false,true})foreach(bool skin in new[]{false,true}){
                var cooked=defaults.Prepare(ShaderProfile.Scene3D,shadows,skin,()=>allowed);var admitted=RuntimeShaderPreparation.Prepare(renderer,Read(cooked.Package),()=>allowed);
                Pass(admitted.CopyMetadata().Length==(shadows?6:4)+(skin?1:0)&&admitted.Scene is not null&&(admitted.Skin is not null)==skin,"exact loaded scene closure");
                using var reference=new ScenePipelineSession(renderer,new Scene3DPipeline(shadows:shadows),256,256);
                using var actual=new ScenePipelineSession(renderer,new Scene3DPipeline(shadows:shadows),256,256,shaders:admitted.Scene);
                var camera=new Ncma.Scene.Rendering.SceneCameraView(Guid.Empty,Matrix4x4.CreateOrthographic(4,4,.1f,50),Vector3.Zero,Ncma.Scene.Rendering.CameraData.Default);
                var light=Vector3.Normalize(new Vector3(1,0,1));var vp=Ncma.Rendering.Scene.ShadowVolume.Create(camera,light);
                SceneGpuDraw[] draws=[SceneGpuDraw.Create(mesh,mat.Resource,data.Ranges[0],Matrix4x4.CreateTranslation(0,0,-4),camera.ViewProjection)];
                byte[] Draw(ScenePipelineSession p){var pixels=new byte[256*256*4];p.Submit(frame++,draws,shadows?draws:[],new(Vector3.Zero,light,new(1,1,1,3)),vp,new(),target3d);renderer.CaptureTarget(target3d,pixels);renderer.Present();return pixels;}
                byte[] baseline=Draw(reference),image=Draw(actual);int error=baseline.Zip(image,(a,b)=>Math.Abs(a-b)).Max();maxError=Math.Max(error,maxError);
                Pass(error<=1,"actual runtime geometry/tone/shadow image "+error);
                if(skin){
                    var model=SyntheticModel().Meshes[0];var upload=SkinUploadData.Prepare(new MeshPayload(true,2,model.Vertices,[],model.Indices,model.TriangleMaterials,model.Bindings,1));
                    using var skinned=renderer.CreateSkinnedMesh(upload,admitted.Skin!);
                    var palette=new[]{GpuSkinPalette.Create(Matrix4x4.Identity),GpuSkinPalette.Create(Matrix4x4.Identity)};renderer.UpdateSkins(frame++,[new(skinned,0)],palette);
                    byte[] vertices=new byte[skinned.VertexCount*48];renderer.CaptureSkinVertices(skinned,vertices);
                    float skinError=0;
                    for(int v=0;v<upload.VertexCount;v++)for(int c=0;c<12;c++)
                        skinError=Math.Max(skinError,Math.Abs(BitConverter.ToSingle(vertices,v*48+c*4)-BitConverter.ToSingle(upload.Vertices.Slice(v*80+c*4,4))));
                    Pass(skinError<.00001f,"source-free compute identity numerical oracle "+skinError);
                }
                allowed=false;Bad(()=>actual.ReplaceShaders(admitted.Scene!));allowed=true;
                Pass(ReferenceEquals(cooked.Package,defaults.Prepare(ShaderProfile.Scene3D,shadows,skin,()=>true).Package),"bounded default package reuse");
            }
            using(var compiler=new ShaderCompilerService(renderer,()=>true)){
                var selection=DefaultSceneShaders.Select(DefaultSceneShaders.CopyCatalog(renderer,true),true);
                var inputs=new List<RuntimeShaderInput>{new(RuntimeShaderRole.GeometryVertex,compiler.Prepare(selection.GeometryVertex)),new(RuntimeShaderRole.GeometryPixel,compiler.Prepare(selection.GeometryPixel)),
                    new(RuntimeShaderRole.ToneVertex,compiler.Prepare(selection.ToneVertex)),new(RuntimeShaderRole.TonePixel,compiler.Prepare(selection.TonePixel)),
                    new(RuntimeShaderRole.ShadowVertex,compiler.Prepare(selection.ShadowVertex!)),new(RuntimeShaderRole.ShadowPixel,compiler.Prepare(selection.ShadowPixel!))};
                var skin=DefaultSkinShader.Select(DefaultSkinShader.CopyCatalog(renderer)).CopyDefinition();
                string source=skin.Source.Replace("numthreads(64,1,1)","numthreads(32,1,1)",StringComparison.Ordinal);
                Pass(source!=skin.Source,"actual final compute fixture");
                inputs.Add(new(RuntimeShaderRole.SkinCompute,compiler.Prepare(ShaderDescriptor.Prepare(skin with{AssetId=Guid.NewGuid(),Source=source,SourceHash=ShaderContractCodec.HashSource(source)}))));
                var wrong=Read(RuntimeShaderPackage.Cook(ShaderProfile.Scene3D,true,true,inputs));
                var stats=renderer.PipelineStats;
                Bad(()=>RuntimeShaderPreparation.Prepare(renderer,wrong,()=>true));
                Pass(renderer.PipelineStats.Pipelines==stats.Pipelines&&renderer.SkinStats.Meshes==0,"wrong last compute before any GPU publication");
            }
            Pass(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"DX11 API0/0");
        }
        using(var renderer=new RendererSession(native,window,256,256,pureUi:true)){
            Bad(()=>renderer.CreateUiShaders(retained.Ui!));Bad(()=>retained.VerifyFor(renderer));
            renderer.PrepareDefaultUiShaders();ulong generation=renderer.UiShaderGeneration;renderer.PrepareDefaultUiShaders();
            Pass(generation==1&&renderer.UiShaderGeneration==1&&renderer.PipelineStats.Pipelines==0&&renderer.SkinStats.Meshes==0,"shared UI startup once and no 3D");
        }
        File.WriteAllText(Path.Combine(output,"runtime-shaders.json"),JsonSerializer.Serialize(new{cases,maxError,nativeQuery=12,actualReflection=true,sourceFreeAdmission=true,formalScenePreparation=true,projectDeployment=false,manualAcceptance=false}));
        Console.WriteLine($"PASS M7.1-C4-B {cases} runtime shader admission/GPU/owner/atomic cases; maxError={maxError}; API0/0");return 0;
    }
}
