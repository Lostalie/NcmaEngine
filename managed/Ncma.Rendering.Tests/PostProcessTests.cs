using System.Numerics;
using System.Diagnostics;
using System.Reflection;
using System.Buffers.Binary;
using System.Text.Json;
using System.Security.Cryptography;
using Ncma.Assets;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Ui;

internal static unsafe partial class Program
{
    private static int PostProcessChild(string path,string hash)
    {
        Check(new FileInfo(path).Length<=PostProcessPackage.MaxBytes,"bounded post child input");var package=PostProcessPackage.Preflight(File.ReadAllBytes(path),hash);
        Check(!package.GpuValidated&&package.CopyPage().Length==5,"post CPU preflight not GPU admission");
        var modules=Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Select(m=>m.ModuleName).ToArray();
        Check(!modules.Any(n=>new[]{"NcmaRenderer.dll","NcmaPlatform.dll","NcmaNative.dll","NcmaPhysics.dll","NcmaGui.dll","NcmaAnimationKernel.dll","d3dcompiler_47.dll"}.Contains(n,StringComparer.OrdinalIgnoreCase)),"post preflight loaded native/compiler");
        Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name!.StartsWith("Ncma.Editor",StringComparison.Ordinal)),"post preflight loaded Editor");
        Console.WriteLine("PASS NPH1 native/compiler/Editor=0");return 0;
    }
    private static int PostProcessTests(string root,string output)
    {
        Directory.CreateDirectory(output);int cases=0,maxError=0;bool allowed=true;ulong frame=1;var costs=new List<object>();
        void Pass(bool ok,string text){Check(ok,"M7.4-B "+text);cases++;}void Bad(Action a){Reject(a);cases++;}
        var definitions=BloomFeature.CreatePrograms();var rows=definitions.CopyPrograms();
        Pass(rows.Length==5&&definitions.Catalog.Count==6&&definitions.Catalog.CopyPage(0).All(r=>!r.Compiled),"public immutable declarations no fake GPU support");
        Pass(BloomFeature.Build(1,1,new(Enabled:false),null) is null&&BloomFeature.Build(64,64,new(Intensity:0),null) is null,"Off requires no shader package");
        foreach(var s in new[]{new BloomSettings(Levels:0),new(Levels:7),new(Threshold:float.NaN),new(SoftKnee:2),new(Intensity:5),new(Scatter:-1)})Bad(()=>s.Validate());
        using var loader=new PluginLoader();loader.Load(Path.GetFullPath(root),Specs().Take(2).ToArray());var native=loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer);
        using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"HDR post Bloom",256,256,false);
        PreparedPostProcessPrograms retained;
        using(var renderer=new RendererSession(native,window,256,256)){
            var package=PostProcessPackage.Cook(renderer,definitions.Catalog,rows,()=>allowed);var read=PostProcessPackage.Preflight(package.CopyBytes(),package.ContentHash);
            Pass(read.CopyPage().SequenceEqual(package.CopyPage())&&!read.GpuValidated,"source-free copied package");retained=PreparedPostProcessPrograms.Prepare(renderer,read,()=>allowed);
            Pass(renderer.PostStats.Bindings==0&&renderer.PostStats.ResidentBytes==0,"preparation allocates no effect resources");
            Bad(()=>PostProcessPackage.Preflight(package.CopyBytes(),new string('0',64)));var trailing=package.CopyBytes().Concat(new byte[]{0}).ToArray();Bad(()=>PostProcessPackage.Preflight(trailing,Convert.ToHexString(SHA256.HashData(trailing))));
            Bad(()=>RuntimeShaderPackage.Preflight(read.CopyBytes(),read.ContentHash));
            var malformed=read.CopyBytes();BinaryPrimitives.WriteUInt32LittleEndian(malformed.AsSpan(116+28),17);Bad(()=>PostProcessPackage.Preflight(malformed,Convert.ToHexString(SHA256.HashData(malformed))));
            var ownedCopy=read.CopyBytes();ownedCopy[0]=0;Pass(read.CopyBytes()[0]!=0,"owned immutable package bytes");
            string packagePath=Path.GetFullPath(Path.Combine(output,"bloom.nph"));File.WriteAllBytes(packagePath,read.CopyBytes());
            var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);start.ArgumentList.Add("--post-process-preflight");start.ArgumentList.Add(packagePath);start.ArgumentList.Add(read.ContentHash);
            using(var child=Process.Start(start)!){var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();if(!child.WaitForExit(30000)){child.Kill(true);throw new Exception("post child deadline");}Pass(child.ExitCode==0&&stdout.Result.Contains("native/compiler/Editor=0",StringComparison.Ordinal),"source-free separate process "+stderr.Result);}
            var tone=SceneTonePresets.Create(SceneToneCurve.LinearClamp);var preparedTone=RegisteredSceneTone.Prepare(renderer,tone.Catalog,tone.Vertex,tone.Pixel,()=>allowed);
            using var mesh=renderer.CreateStaticMesh(Quad());var definition=MaterialDefinition.Default(Guid.NewGuid()) with{BaseColor=new(0,0,0,1),Emissive=new(4,1,.25f,1)};
            using var material=renderer.CreateMaterial(definition,new GpuTexture?[6],false);
            var light=new ResourceLighting(Vector3.Zero,Vector3.UnitZ,new(1,1,1,0));var vp=Matrix4x4.CreateOrthographic(4,4,.1f,50);
            SceneGpuDraw[] geometry=[SceneGpuDraw.Create(mesh,material,Quad().Ranges[0],Matrix4x4.CreateScale(.625f,.625f,1)*Matrix4x4.CreateTranslation(0,0,-4.5f),vp,directLight:false)];
            foreach(var size in new[]{(64,64),(63,47),(1,1),(2,3),(129,65)}){
                uint w=(uint)size.Item1,h=(uint)size.Item2;renderer.Resize(Math.Max(64,w),Math.Max(64,h));using var target=renderer.CreateViewTarget(w,h);using var scene=new ScenePipelineSession(renderer,new Scene3DPipeline(.1f,0,shadows:false),w,h,preparedTone);
                byte[] image=new byte[w*h*4];void Draw(){scene.Submit(frame++,geometry,[],light,Matrix4x4.Identity,new(),target);renderer.CaptureTarget(target,image);renderer.Present();}
                Vector3[] basePixels=new Vector3[w*h];for(int y=0;y<h;y++)for(int x=0;x<w;x++){double px=(x+.5)/w*4-2,py=2-(y+.5)/h*4;basePixels[y*w+x]=Math.Abs(px)<.5&&Math.Abs(py)<.5?new(4,1,.25f):Vector3.Zero;}
                var oracle=new BloomOracle((int)w,(int)h,basePixels);Draw();byte[] off=(byte[])image.Clone();
                int Error(byte[] expected){int e=0;for(int i=0;i<expected.Length;i++)e=Math.Max(e,Math.Abs(expected[i]-image[i]));maxError=Math.Max(maxError,e);return e;}
                Pass(Error(oracle.Display(.1f))<=1,"independent geometry coverage/linear tone baseline");
                foreach(var settings in new[]{new BloomSettings(Levels:1),new(Levels:3,Intensity:.5f),new(Levels:6,Threshold:0,SoftKnee:0,Intensity:.8f,Scatter:1),new(Levels:2,Threshold:16),new(Levels:3,Threshold:2,SoftKnee:0,Scatter:0)}){
                    var plan=BloomFeature.Build(w,h,settings,read)!;scene.ConfigurePostProcess(plan,retained,()=>allowed);Draw();var expected=oracle.Bloom(settings).Display(.1f);int error=Error(expected);
                    Pass(error<=3,$"independent full-image Bloom {w}x{h} levels={settings.Levels} threshold={settings.Threshold} error={error}");
                    Pass(renderer.PostStats.ResidentBytes==plan.ResidentBytes&&renderer.PostStats.Targets==(ulong)plan.CopyTargets().Length,"actual GPU target budget");
                    if(settings.Threshold==16)Pass(image.SequenceEqual(off),"below threshold creates no light");
                    if(settings.Threshold==1&&w==64){Pass(image.Where((v,i)=>off[i]==0&&i%4<3).Any(v=>v>0),"bright area spreads into dark pixels");File.WriteAllBytes(Path.Combine(output,$"bloom-{settings.Levels}.png"),Png(w,h,image));}
                }
                var scalar=new BloomSettings(Levels:3,Threshold:.7f,SoftKnee:.8f,Intensity:.9f,Scatter:.5f);var scalarPlan=BloomFeature.Build(w,h,scalar,read)!;
                scene.ConfigurePostProcess(scalarPlan,retained,()=>allowed);Draw();Pass(Error(oracle.Bloom(scalar).Display(.1f))<=3,"same-layout scalar publication image");
                Bad(()=>scene.Configure(new Scene3DPipeline(.1f,0,shadows:false,features:[new SceneExposureFeature(.1f)])));
                void Tick(){scene.Submit(frame++,geometry,[],light,Matrix4x4.Identity,new(),target);renderer.Present();}
                for(int i=0;i<32;i++)Tick();renderer.WaitIdle();var before=renderer.PostStats;var resources=renderer.ResourceStats;var sceneStats=renderer.PipelineStats;
                long allocated=GC.GetAllocatedBytesForCurrentThread(),time=Stopwatch.GetTimestamp();for(int i=0;i<64;i++)Tick();double ms=Stopwatch.GetElapsedTime(time).TotalMilliseconds;allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;renderer.WaitIdle();var after=renderer.PostStats;
                Pass(allocated==0&&after.Publications==before.Publications&&after.ResidentBytes==before.ResidentBytes&&sceneStats.Creates==renderer.PipelineStats.Creates&&resources.UploadedBytes==renderer.ResourceStats.UploadedBytes,"warmed64 zero managed allocations/rebuild/immutable upload");
                Pass(after.Draws-before.Draws==(ulong)scalarPlan.PassCount*64&&after.ConstantUploadBytes-before.ConstantUploadBytes==(ulong)scalarPlan.PassCount*64*16,"exact post dispatch and constant bytes");
                costs.Add(new{width=w,height=h,frames=64,allocated,milliseconds=ms,passCount=scalarPlan.PassCount,residentBytes=after.ResidentBytes,draws=after.Draws-before.Draws,constantBytes=after.ConstantUploadBytes-before.ConstantUploadBytes});
                var oldPlan=scene.PostProcess;Draw();byte[] baseline=(byte[])image.Clone();allowed=false;Bad(()=>scene.ConfigurePostProcess(oldPlan,retained,()=>true));allowed=true;
                Bad(()=>scene.ConfigurePostProcess(oldPlan,retained,()=>{scene.Dispose();return true;}));
                Bad(()=>scene.ConfigurePostProcess(oldPlan,retained,()=>{scene.Configure(new Scene3DPipeline(shadows:false));return true;}));
                Bad(()=>scene.ConfigurePostProcess(oldPlan,retained,()=>{scene.ConfigurePostProcess(null,null,()=>true);return true;}));
                Exception? threadError=null;var worker=new Thread(()=>{try{scene.ConfigurePostProcess(null,null,()=>true);}catch(Exception e){threadError=e;}});worker.Start();worker.Join();Pass(threadError is PluginException{Result:PluginResult.WrongThread},"post owner thread before local state");
                scene.Submit(frame++,geometry,[],light,Matrix4x4.Identity,new(),target);Bad(()=>scene.ConfigurePostProcess(null,null,()=>true));renderer.Present();
                Pass(ReferenceEquals(scene.PostProcess,oldPlan),"revoke/reentry/frame failure keeps plan");Draw();Pass(image.SequenceEqual(baseline),"failed candidate preserves entire image");
                scene.ConfigurePostProcess(null,null,()=>true);Draw();Pass(image.SequenceEqual(off)&&renderer.PostStats.Bindings==0&&renderer.PostStats.ResidentBytes==0&&renderer.PostStats.Targets==0,"Off exact original image and zero effect resources");
            }
            // Graph policy errors, independent of native handles.
            PostProcessGraph One(out PostResource target){var g=new PostProcessGraph();target=g.AddTarget(new(64,64));return g;}
            var bad=One(out var dest);bad.AddPass(new(BloomFeature.BlurProgram,dest,null,dest,new(1,0,0,0)));Bad(()=>bad.Compile(64,64,dest,read));
            var display=new PostProcessGraph();var displayTarget=display.AddTarget(new(64,64,ColorSpace:PostColorSpace.Display));display.AddPass(new(BloomFeature.BlurProgram,new(0),null,displayTarget,Vector4.One));Bad(()=>display.Compile(64,64,displayTarget,read));
            var format=new PostProcessGraph();var fmt=format.AddTarget(new(64,64,PostFormat.Rgba8));format.AddPass(new(BloomFeature.BlurProgram,new(0),null,fmt,Vector4.One));Bad(()=>format.Compile(64,64,fmt,read));
            var uninitialized=One(out dest);var missingInput=uninitialized.AddTarget(new(64,64));uninitialized.AddPass(new(BloomFeature.BlurProgram,missingInput,null,dest,Vector4.One));Bad(()=>uninitialized.Compile(64,64,dest,read));
            var unused=One(out dest);var extra=unused.AddTarget(new(64,64));unused.AddPass(new(BloomFeature.BlurProgram,new(0),null,dest,Vector4.One));unused.AddPass(new(BloomFeature.BlurProgram,new(0),null,extra,Vector4.One));Bad(()=>unused.Compile(64,64,dest,read));
            var duplicate=One(out dest);duplicate.AddPass(new(BloomFeature.BlurProgram,new(0),null,dest,Vector4.One));duplicate.AddPass(new(BloomFeature.BlurProgram,new(0),null,dest,Vector4.One));Bad(()=>duplicate.Compile(64,64,dest,read));
            var cycle=One(out var a);var b=cycle.AddTarget(new(64,64));cycle.AddPass(new(BloomFeature.BlurProgram,a,null,b,Vector4.One));cycle.AddPass(new(BloomFeature.BlurProgram,b,null,a,Vector4.One));Bad(()=>cycle.Compile(64,64,b,read));
            var nan=One(out dest);nan.AddPass(new(BloomFeature.BlurProgram,new(0),null,dest,new(float.NaN)));Bad(()=>nan.Compile(64,64,dest,read));
            var missing=One(out dest);missing.AddPass(new(Guid.NewGuid(),new(0),null,dest,Vector4.One));Bad(()=>missing.Compile(64,64,dest,read));
            var arity=One(out dest);arity.AddPass(new(BloomFeature.CombineProgram,new(0),null,dest,Vector4.One));Bad(()=>arity.Compile(64,64,dest,read));
            var big=new PostProcessGraph();var huge=big.AddTarget(new(4096,4096));big.AddPass(new(BloomFeature.BlurProgram,new(0),null,huge,Vector4.One));Bad(()=>big.Compile(4096,4096,huge,read));
            // A user authored stage uses exactly the same public catalog/package/graph/actual admission.
            const string custom="cbuffer Post:register(b0){float4 Params;};Texture2D Input0:register(t0);SamplerState LinearClamp:register(s0);struct Full{float4 p:SV_POSITION;float2 uv:TEXCOORD0;};float4 Custom(Full i):SV_TARGET{return float4(Input0.SampleLevel(LinearClamp,i.uv,0).rgb*Params.x,1);}";
            var userDef=rows[0].Pixel.CopyDefinition() with{AssetId=Guid.NewGuid(),Name="User linear scale",EntryPoint="Custom",Source=custom,SourceHash=ShaderContractCodec.HashSource(custom)};
            var userProgram=new PostProgramDefinition(1,rows[0].Vertex,ShaderDescriptor.Prepare(userDef));var userCatalog=ShaderCatalog.Create(ShaderProfile.HdrPostProcess,[rows[0].Vertex.CopyDefinition(),userDef]);
            var userPackage=PostProcessPackage.Cook(renderer,userCatalog,[userProgram],()=>allowed);var userPrepared=PreparedPostProcessPrograms.Prepare(renderer,PostProcessPackage.Preflight(userPackage.CopyBytes(),userPackage.ContentHash),()=>allowed);
            var userGraph=One(out var first);var last=userGraph.AddTarget(new(64,64));
            // Intentionally submit in reverse dependency order: framework sorts, native consumes a ready batch.
            userGraph.AddPass(new(userDef.AssetId,first,null,last,new(.5f,0,0,0)));userGraph.AddPass(new(userDef.AssetId,new(0),null,first,new(.5f,0,0,0)));
            var userPlan=userGraph.Compile(64,64,last,userPackage);Pass(userPlan.CopyPasses()[0].Input.Value==0&&userPlan.CopyPasses()[1].Input==first,"public dependency sorting");
            using(var target=renderer.CreateViewTarget(64,64))using(var scene=new ScenePipelineSession(renderer,new Scene3DPipeline(.1f,0,shadows:false),64,64,preparedTone)){
                scene.ConfigurePostProcess(userPlan,userPrepared,()=>allowed);byte[] image=new byte[64*64*4];
                void Draw(){scene.Submit(frame++,geometry,[],light,Matrix4x4.Identity,new(),target);renderer.CaptureTarget(target,image);renderer.Present();}
                Draw();var linear=new Vector3[64*64];for(int y=24;y<40;y++)for(int x=24;x<40;x++)linear[y*64+x]=new(1,.25f,.0625f);var expected=new BloomOracle(64,64,linear).Display(.1f);
                int error=image.Select((v,i)=>Math.Abs(v-expected[i])).Max();maxError=Math.Max(maxError,error);Pass(error<=1,"user source-free two-stage actual independent full image");
                var original=(byte[])image.Clone();Bad(()=>scene.ConfigurePostProcess(userPlan,retained,()=>true));Draw();Pass(image.SequenceEqual(original)&&ReferenceEquals(scene.PostProcess,userPlan),"exact package hash mismatch retains custom plan/image");
                File.WriteAllBytes(Path.Combine(output,"user-two-stage.png"),Png(64,64,image));
            }
            // Late actual PS output validation cannot be forged with truthful binding metadata.
            var pd=rows[0].Pixel.CopyDefinition();string text=pd.Source.Replace("Threshold(Full i):SV_TARGET","Threshold(Full i):SV_TARGET1",StringComparison.Ordinal);
            pd=pd with{Source=text,SourceHash=ShaderContractCodec.HashSource(text)};var forged=new PostProgramDefinition(1,rows[0].Vertex,ShaderDescriptor.Prepare(pd));
            var wrong=PostProcessPackage.Cook(renderer,ShaderCatalog.Create(ShaderProfile.HdrPostProcess,[rows[0].Vertex.CopyDefinition(),pd]),[forged],()=>true);
            Bad(()=>PreparedPostProcessPrograms.Prepare(renderer,PostProcessPackage.Preflight(wrong.CopyBytes(),wrong.ContentHash),()=>true));
            // Analytic constant field: normalized pyramid weights must not multiply energy by level count.
            using(var target=renderer.CreateViewTarget(64,64))using(var scene=new ScenePipelineSession(renderer,new Scene3DPipeline(1,0,shadows:false),64,64,preparedTone)){
                byte[] image=new byte[64*64*4];var constant=Enumerable.Repeat(BloomOracle.HalfRound(new(.6f,.3f,.15f)),64*64).ToArray();var expected=new BloomOracle(64,64,constant).Display(1);
                foreach(int levels in new[]{1,2,4,6}){
                    scene.ConfigurePostProcess(BloomFeature.Build(64,64,new(Levels:levels,Threshold:.25f,SoftKnee:0,Intensity:.4f),read),retained,()=>true);
                    scene.Submit(frame++,[],[],light,Matrix4x4.Identity,new(),target,clear:new(.5f,.25f,.125f,1));renderer.CaptureTarget(target,image);renderer.Present();
                    int error=image.Select((v,i)=>Math.Abs(v-expected[i])).Max();maxError=Math.Max(maxError,error);Pass(error<=1,"analytic constant bloom energy independent of pyramid levels");
                    scene.Submit(frame++,[],[],light,Matrix4x4.Identity,new(),target,clear:Vector4.UnitW);renderer.CaptureTarget(target,image);renderer.Present();
                    Pass(image.Where((_,i)=>i%4<3).All(v=>v==0),"black frame after light has no phantom brightness or stale target");
                }
            }
            // Same numerical post executor with the complete Environment + Shadow shader variant.
            renderer.Resize(128,128);var environmentCatalog=DefaultEnvironmentSceneShaders.CopyCatalog(renderer,true);var environmentSelection=DefaultEnvironmentSceneShaders.Select(environmentCatalog,true);
            var environmentTone=SceneTonePresets.Create(SceneToneCurve.LinearClamp,ShaderProfile.SceneEnvironment);var composition=SceneToneComposition.Compose(environmentCatalog,environmentSelection,environmentTone);
            var environmentPrograms=RegisteredEnvironmentSceneShaders.Prepare(renderer,composition.Catalog,composition.Selection,()=>true);
            using(var target=renderer.CreateViewTarget(128,128))using(var scene=new ScenePipelineSession(renderer,new Scene3DPipeline(.1f,0,256),128,128,environmentShaders:environmentPrograms)){
                var settings=new BloomSettings(Levels:4,Intensity:.4f);scene.ConfigurePostProcess(BloomFeature.Build(128,128,settings,read),retained,()=>true);byte[] image=new byte[128*128*4];
                scene.Submit(frame++,geometry,geometry,light,Matrix4x4.Identity,new(),target);renderer.CaptureTarget(target,image);renderer.Present();
                var linear=new Vector3[128*128];for(int y=48;y<80;y++)for(int x=48;x<80;x++)linear[y*128+x]=new(4,1,.25f);var expected=new BloomOracle(128,128,linear).Bloom(settings).Display(.1f);
                int error=image.Select((v,i)=>Math.Abs(v-expected[i])).Max();maxError=Math.Max(maxError,error);Pass(error<=3,"Environment profile with shadow variant independent full image (IBL Off)");
                using var white=renderer.CreateUiImage(1,1,[255,255,255,255]);var builder=new UiDisplayListBuilder(renderer);builder.Quad(new(Guid.NewGuid(),new(0,0,16,16),Matrix3x2.CreateTranslation(4,4),new(0,0,128,128),1,true,-1),white,new(.2f,.4f,.8f,1));using var ui=builder.Build();
                ulong f=frame++;scene.Submit(f,geometry,geometry,light,Matrix4x4.Identity,new());renderer.SubmitUi(ui,f,Vector4.Zero,overlay:true);renderer.Capture(image);renderer.Present();
                Pass(Math.Abs(image[(8*128+8)*4]-51)<=1&&Math.Abs(image[(8*128+8)*4+1]-102)<=1&&Math.Abs(image[(8*128+8)*4+2]-204)<=1,"actual display UI after Bloom/Tone, no second color transform");
            }
            Pass(renderer.PostStats.Bindings==0&&renderer.PostStats.ResidentBytes==0&&renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"closure and real DX11 API0/0");
        }
        using(var pure=new RendererSession(native,window,256,256,pureUi:true)){
            Bad(()=>PreparedPostProcessPrograms.Prepare(pure,retained.Package,()=>true));Pass(pure.PostStats.Bindings==0&&pure.PostStats.ResidentBytes==0&&pure.PipelineStats.Pipelines==0,"pureUI no post or scene resources");
        }
        File.WriteAllText(Path.Combine(output,"post-process-results.json"),JsonSerializer.Serialize(new{schema=1,cases,maxError,costs,apiErrors=0,apiWarnings=0,formalHost=false,manualAcceptance=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"PASS M7.4-B {cases} actual Bloom/full-image oracle/Off/graph/source-free/atomic cases; maxError={maxError}; API0/0");return 0;
    }
}
