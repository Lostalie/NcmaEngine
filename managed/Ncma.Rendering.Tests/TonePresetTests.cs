using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Ncma.Assets;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Ui;

internal static unsafe partial class Program
{
    private static int TonePresetTests(string root,string output)
    {
        Directory.CreateDirectory(output);int cases=0,maxError=0;ulong frame=1;bool allowed=true;
        var costs=new List<object>();
        void Pass(bool value,string message){Check(value,"M7.4-A "+message);cases++;}
        void Bad(Action action){Reject(action);cases++;}
        foreach(var curve in Enum.GetValues<SceneToneCurve>())foreach(var profile in new[]{ShaderProfile.Scene3D,ShaderProfile.SceneEnvironment}){
            var a=SceneTonePresets.Create(curve,profile);var b=SceneTonePresets.Create(curve,profile);
            Pass(a.Pixel.ContentHash==b.Pixel.ContentHash&&a.Catalog.CopyPage(0).All(m=>!m.Compiled),"deterministic declaration without GPU credential");
            var copy=a.Pixel.CopyDefinition();copy.Constants[0].Members[0]=copy.Constants[0].Members[0] with{Name="mutated"};
            Pass(a.Pixel.CopyDefinition().Constants[0].Members[0].Name=="MVP","owned copy");
        }
        foreach(double ev in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity,-6.001,4.001})Bad(()=>SceneTonePresets.ExposureFromStops(ev));
        Pass(SceneTonePresets.ExposureFromStops(-6)==.015625f&&SceneTonePresets.ExposureFromStops(0)==1&&SceneTonePresets.ExposureFromStops(4)==16,"EV exact boundaries");
        Bad(()=>SceneTonePresets.Create((SceneToneCurve)99));
        Bad(()=>SceneTonePresets.Create(SceneToneCurve.Reinhard,ShaderProfile.Flat2D));
        Bad(()=>SceneTonePresets.Create(SceneToneCurve.Reinhard,ShaderProfile.Skinning));
        // CPU-only declaration work above precedes any native plugin load.
        using var loader=new PluginLoader();loader.Load(Path.GetFullPath(root),Specs().Take(2).ToArray());
        var native=loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer);
        using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"Tone presets HDR acceptance",256,256,false);
        using(var renderer=new RendererSession(native,window,256,256)){
            using var mesh=renderer.CreateStaticMesh(Quad());using var target=renderer.CreateViewTarget(256,256);
            var light=new ResourceLighting(Vector3.Zero,Vector3.UnitZ,new(1,1,1,0));
            // Overscan the entire target so EVERY pixel has the independently known emissive value.
            var model=Matrix4x4.CreateScale(4,4,1)*Matrix4x4.CreateTranslation(0,0,-4.5f);var vp=Matrix4x4.CreateOrthographic(4,4,.1f,50);
            byte[] image=new byte[256*256*4];var range=Quad().Ranges[0];
            var source=DefaultSceneShaders.CopyCatalog(renderer,false);var stages=DefaultSceneShaders.Select(source,false);
            var official=SceneTonePresets.Create(SceneToneCurve.AcesFitted);
            var composed=SceneToneComposition.Compose(source,stages,official);
            Pass(composed.Selection.GeometryPixel.ContentHash==stages.GeometryPixel.ContentHash&&composed.Catalog.Count==4&&source.Require(stages.TonePixel.AssetId,stages.TonePixel.ContentHash)==stages.TonePixel,"composition preserves geometry and original immutable catalog");
            Bad(()=>SceneToneComposition.Compose(source,stages,SceneTonePresets.Create(SceneToneCurve.Reinhard,ShaderProfile.SceneEnvironment)));
            Bad(()=>SceneToneComposition.Compose(source,stages with{ShadowVertex=stages.GeometryVertex},official));
            Bad(()=>SceneToneComposition.Compose(source,stages with{TonePixel=official.Pixel},official));
            Bad(()=>SceneToneComposition.Compose(source,stages,official with{Pixel=official.Vertex}));
            var collision=official.Pixel.CopyDefinition() with{AssetId=stages.GeometryPixel.AssetId};
            var colliding=ShaderDescriptor.Prepare(collision);var collisionCatalog=ShaderCatalog.Create(ShaderProfile.Scene3D,[official.Vertex.CopyDefinition(),collision]);
            Bad(()=>SceneToneComposition.Compose(source,stages,new(collisionCatalog,official.Vertex,colliding)));
            int Error(SceneToneCurve curve,float exposure,Vector3 linear,bool bgr=false){
                byte[] expected=[ToneOracle(linear.X,exposure,curve),ToneOracle(linear.Y,exposure,curve),ToneOracle(linear.Z,exposure,curve),255];
                int error=0;for(int i=0;i<image.Length;i++)error=Math.Max(error,Math.Abs(image[i]-expected[bgr&&i%4<3?2-i%4:i%4]));maxError=Math.Max(maxError,error);return error;
            }
            void Draw(ScenePipelineSession scene,SceneGpuDraw[] batch){scene.Submit(frame++,batch,[],light,Matrix4x4.Identity,new(),target);renderer.CaptureTarget(target,image);renderer.Present();}
            using var white=renderer.CreateUiImage(1,1,[255,255,255,255]);var builder=new UiDisplayListBuilder(renderer);
            builder.Quad(new(Guid.NewGuid(),new(0,0,32,32),Matrix3x2.CreateTranslation(16,16),new(0,0,256,256),1,true,-1),white,new(.2f,.4f,.8f,1));
            using var ui=builder.Build();
            foreach(var profile in new[]{ShaderProfile.Scene3D,ShaderProfile.SceneEnvironment})foreach(bool shadows in new[]{false,true})foreach(var curve in Enum.GetValues<SceneToneCurve>()){
                var src=profile==ShaderProfile.Scene3D?DefaultSceneShaders.CopyCatalog(renderer,shadows):DefaultEnvironmentSceneShaders.CopyCatalog(renderer,shadows);
                var selected=profile==ShaderProfile.Scene3D?DefaultSceneShaders.Select(src,shadows):DefaultEnvironmentSceneShaders.Select(src,shadows);
                var preset=SceneTonePresets.Create(curve,profile);var c=SceneToneComposition.Compose(src,selected,preset);
                RegisteredSceneShaders? programs=null;RegisteredEnvironmentSceneShaders? environment=null;RuntimeShaderPackage package;
                if(profile==ShaderProfile.Scene3D){
                    programs=RegisteredSceneShaders.Prepare(renderer,c.Catalog,c.Selection,()=>allowed);
                    using var compiler=new ShaderCompilerService(renderer,()=>allowed);
                    var inputs=new List<RuntimeShaderInput>{new(RuntimeShaderRole.GeometryVertex,compiler.Prepare(c.Selection.GeometryVertex)),new(RuntimeShaderRole.GeometryPixel,compiler.Prepare(c.Selection.GeometryPixel)),new(RuntimeShaderRole.ToneVertex,compiler.Prepare(c.Selection.ToneVertex)),new(RuntimeShaderRole.TonePixel,compiler.Prepare(c.Selection.TonePixel))};
                    if(shadows){inputs.Add(new(RuntimeShaderRole.ShadowVertex,compiler.Prepare(c.Selection.ShadowVertex!)));inputs.Add(new(RuntimeShaderRole.ShadowPixel,compiler.Prepare(c.Selection.ShadowPixel!)));}
                    package=RuntimeShaderPackage.Cook(profile,shadows,false,inputs);
                }else{environment=RegisteredEnvironmentSceneShaders.Prepare(renderer,c.Catalog,c.Selection,()=>allowed);package=environment.CookPackage();}
                using var scene=new ScenePipelineSession(renderer,new Scene3DPipeline(shadows:shadows,ambient:0,shadowResolution:256),256,256,shaders:programs,environmentShaders:environment);
                foreach(float value in new[]{0,.0001f,.003f,.18f,1,4,16}){
                    Vector3 linear=new(value,value*.5f,value*.125f);var d=MaterialDefinition.Default(Guid.NewGuid()) with{BaseColor=new(0,0,0,1),Emissive=new(linear.X,linear.Y,linear.Z,1)};
                    using var m=renderer.CreateMaterial(d,new GpuTexture?[6],false);SceneGpuDraw[] batch=[SceneGpuDraw.Create(mesh,m,range,model,vp,directLight:false)];
                    foreach(float exposure in new[]{.01f,SceneTonePresets.ExposureFromStops(-6),1f,4f,16f}){
                        scene.Configure(new Scene3DPipeline(exposure,0,256,shadows));Draw(scene,batch);int error=Error(curve,exposure,linear);
                        Pass(error<=1,$"full-image HDR oracle {profile}/{shadows}/{curve} L={value} E={exposure} error={error}");
                    }
                    if(value==4){
                        File.WriteAllBytes(Path.Combine(output,$"{profile}-{shadows}-{curve}.png"),Png(256,256,image));
                        var baseline=(byte[])image.Clone();var read=RuntimeShaderPackage.Preflight(package.CopyBytes(),package.ContentHash);
                        Pass(!read.GpuValidated,"source-free preflight not GPU proof");
                        if(profile==ShaderProfile.Scene3D)scene.ReplaceShaders(RuntimeShaderPreparation.Prepare(renderer,read,()=>allowed).Scene!);
                        else scene.ReplaceEnvironmentShaders(EnvironmentShaderPreparation.Prepare(renderer,read,()=>allowed).Scene);
                        Draw(scene,batch);Pass(image.SequenceEqual(baseline),"source-free actual reflection and exact full image");
                        // Display-domain UI is composed after Tone on the actual backbuffer, not a separate image assertion.
                        ulong f=frame++;scene.Submit(f,batch,[],light,Matrix4x4.Identity,new(),null);
                        renderer.SubmitUi(ui,f,Vector4.Zero,overlay:true);renderer.Capture(image);renderer.Present();
                        Pass(Math.Abs(image[(24*256+24)*4]-51)<=1&&Math.Abs(image[(24*256+24)*4+1]-102)<=1&&Math.Abs(image[(24*256+24)*4+2]-204)<=1,"UI display color unchanged by HDR/exposure/Tone");
                        Pass(image[(128*256+128)*4]==baseline[(128*256+128)*4],"UI outside rectangle retains scene Tone");
                        var active=scene.Plan;Bad(()=>scene.Configure(new Scene3DPipeline(float.NaN,0,256,shadows)));Pass(ReferenceEquals(active,scene.Plan),"invalid exposure keeps active plan");
                        if(!shadows){
                            void Tick(){scene.Submit(frame++,batch,[],light,Matrix4x4.Identity,new(),target);renderer.Present();}
                            for(int i=0;i<32;i++)Tick();renderer.WaitIdle();var before=renderer.PipelineStats;var resources=renderer.ResourceStats;var meshes=renderer.SceneStats;
                            long bytes=GC.GetAllocatedBytesForCurrentThread(),start=Stopwatch.GetTimestamp();for(int i=0;i<64;i++)Tick();double ms=Stopwatch.GetElapsedTime(start).TotalMilliseconds;bytes=GC.GetAllocatedBytesForCurrentThread()-bytes;renderer.WaitIdle();
                            Pass(bytes==0&&before.Creates==renderer.PipelineStats.Creates&&resources.UploadedBytes==renderer.ResourceStats.UploadedBytes&&resources.Creates==renderer.ResourceStats.Creates&&meshes.UploadedBytes==renderer.SceneStats.UploadedBytes,"warmed64 no allocations/rebuild/immutable upload");
                            costs.Add(new{profile,curve,frames=64,bytes,milliseconds=ms,constantBytes=renderer.PipelineStats.ConstantUploadBytes-before.ConstantUploadBytes});
                        }
                    }
                }
            }
            // User-authored curve takes the SAME composition path; no privileged preset bypass.
            var userDef=official.Pixel.CopyDefinition();string userSource=userDef.Source.Replace("return float4(c,1);","return float4(c.bgr,1);",StringComparison.Ordinal);
            userDef=userDef with{AssetId=Guid.NewGuid(),Name="User display BGR",Source=userSource,SourceHash=ShaderContractCodec.HashSource(userSource)};
            var user=ShaderDescriptor.Prepare(userDef);var uc=ShaderCatalog.Create(ShaderProfile.Scene3D,[official.Vertex.CopyDefinition(),userDef]);
            var composite=SceneToneComposition.Compose(source,stages,new(uc,official.Vertex,user));
            var good=RegisteredSceneShaders.Prepare(renderer,composite.Catalog,composite.Selection,()=>allowed);
            using(var scene=new ScenePipelineSession(renderer,new Scene3DPipeline(shadows:false,ambient:0),256,256,shaders:good)){
                var d=MaterialDefinition.Default(Guid.NewGuid()) with{BaseColor=new(0,0,0,1),Emissive=new(.3f,.1f,.05f,1)};using var m=renderer.CreateMaterial(d,new GpuTexture?[6],false);
                SceneGpuDraw[] batch=[SceneGpuDraw.Create(mesh,m,range,model,vp,directLight:false)];Draw(scene,batch);Pass(Error(SceneToneCurve.AcesFitted,1,new(.3f,.1f,.05f),true)<=1,"user composition independent full image");var old=(byte[])image.Clone();
                string wrong=userDef.Source.Replace(":SV_TARGET",":SV_TARGET1",StringComparison.Ordinal);var badDef=userDef with{Source=wrong,SourceHash=ShaderContractCodec.HashSource(wrong)};var bad=ShaderDescriptor.Prepare(badDef);
                var bc=SceneToneComposition.Compose(source,stages,new(ShaderCatalog.Create(ShaderProfile.Scene3D,[official.Vertex.CopyDefinition(),badDef]),official.Vertex,bad));
                var candidate=RegisteredSceneShaders.Prepare(renderer,bc.Catalog,bc.Selection,()=>allowed);Bad(()=>scene.ReplaceShaders(candidate));
                allowed=false;Bad(()=>scene.ReplaceShaders(good));allowed=true;
                scene.Submit(frame++,batch,[],light,Matrix4x4.Identity,new(),target);Bad(()=>scene.ReplaceShaders(good));renderer.Present();
                Exception? threadError=null;var thread=new Thread(()=>{try{scene.ReplaceShaders(good);}catch(Exception e){threadError=e;}});thread.Start();thread.Join();Pass(threadError is PluginException{Result:PluginResult.WrongThread},"owner guard");
                Draw(scene,batch);Pass(ReferenceEquals(scene.RegisteredShaders,good)&&image.SequenceEqual(old),"bad output/approval/frame/owner preserve old published scene and pixels");
            }
            foreach(uint width in new uint[]{320,192,256}){
                renderer.Resize(width,192);using var t=renderer.CreateViewTarget(width,192);using var s=new ScenePipelineSession(renderer,new Scene3DPipeline(shadows:false),width,192,shaders:good);
                s.Submit(frame++,[],[],light,Matrix4x4.Identity,new(),t,clear:new(.25f,.5f,.75f,1));byte[] pixels=new byte[width*192*4];renderer.CaptureTarget(t,pixels);renderer.Present();
                byte[] expected=[ToneOracle(.75f,1,SceneToneCurve.AcesFitted),ToneOracle(.5f,1,SceneToneCurve.AcesFitted),ToneOracle(.25f,1,SceneToneCurve.AcesFitted),255];
                Pass(pixels.Select((p,i)=>Math.Abs(p-expected[i%4])).Max()<=1,"resize/recreate full image");
            }
            Pass(renderer.PipelineStats.Pipelines==0&&renderer.PipelineStats.ResidentBytes==0,"scene resources released after all closes");
            Pass(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"DX11 validation0/0");
        }
        using(var renderer=new RendererSession(native,window,256,256,pureUi:true)){
            var preset=SceneTonePresets.Create(SceneToneCurve.Reinhard);
            var prepared=RegisteredSceneTone.Prepare(renderer,preset.Catalog,preset.Vertex,preset.Pixel,()=>true);
            Bad(()=>new ScenePipelineSession(renderer,new Scene3DPipeline(shadows:false),256,256,prepared));
            Pass(renderer.PipelineStats.Pipelines==0&&renderer.SkinStats.Meshes==0&&renderer.EnvironmentStats.ResidentBytes==0&&renderer.ResourceStats.ResidentBytes==0,"pureUI no 3D resources");
        }
        File.WriteAllText(Path.Combine(output,"tone-preset-results.json"),JsonSerializer.Serialize(new{schema=1,cases,maxError,costs,apiErrors=0,apiWarnings=0,bloom=false,formalSettings=false,fullPerformanceAccepted=false,manualAccepted=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"PASS M7.4-A {cases} Tone preset HDR/independent full-image/source-free/UI/atomic/lifetime/cost cases; maxError={maxError}; API0/0");return 0;
    }
    private static byte ToneOracle(float linear,float exposure,SceneToneCurve curve)
    {
        // Independent double scalar oracle; scene storage first rounds to IEEE binary16.
        double x=(float)(Half)linear*(double)exposure;
        double display=curve switch{SceneToneCurve.AcesFitted=>Math.Clamp((2.51*x*x+.03*x)/(2.43*x*x+.59*x+.14),0,1),SceneToneCurve.Reinhard=>1-1/(1+x),SceneToneCurve.LinearClamp=>Math.Min(x,1),_=>throw new ArgumentOutOfRangeException(nameof(curve))};
        double srgb=display<=.0031308?12.92*display:1.055*Math.Pow(display,1/2.4)-.055;
        return (byte)Math.Clamp(Math.Round(srgb*255),0,255);
    }
}
