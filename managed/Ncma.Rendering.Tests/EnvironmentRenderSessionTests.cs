using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Animation.Native;
using Ncma.Asset.Import;
using Ncma.Assets;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Scene;
using Ncma.Scene.Rendering;

internal static unsafe partial class Program
{
    private readonly record struct EnvironmentBoundaryProbe(int Value):Ncma.Runtime.IComponent;
    private static int EnvironmentRenderSessionTests(string root,string output)
    {
        Directory.CreateDirectory(output);int cases=0,maxError=0;ulong frame=1;
        void Pass(bool value,string label){Check(value,"M7.3-C2-B "+label);cases++;}
        void Bad(Action action){Reject(action);cases++;}
        using var loader=new PluginLoader();loader.Load(Path.GetFullPath(root),Specs().Take(2).ToArray());
        var native=loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer);
        using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"Shared scene IBL",256,256,false);
        using var renderer=new RendererSession(native,window,256,256);
        using var cook=new EnvironmentCookService(native,()=>true);
        // Constant linear radiance allows an independent material/position pixel oracle.
        var source=HdrEnvironmentSource.Prepare(Guid.NewGuid(),8,4,Enumerable.Range(0,32).SelectMany(_=>new[]{.18f,.18f,.18f,1f}).ToArray());
        var package=cook.Cook(Guid.NewGuid(),1,source,new(8,4,8,128));
        var config=new EnvironmentLightingData(1,package.AssetId,package.Generation,package.ContentHash,1,0,true);
        void Publish(string projectRoot,Guid project,EnvironmentPackage p){var d=EnvironmentAssetDescriptor.FromPackage(project,p);string path=Path.Combine(projectRoot,d.Path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,p.CopyBytes());File.WriteAllBytes(Path.Combine(projectRoot,"assets/environment.ncenv"),d.Encode());}
        var fixture=new SceneGpuFixture(output);var (document,camera,_,_)=fixture.Scene();
        var light=document.World.GetObjects().Single(o=>o.Has<DirectionalLightData>());
        light.Set(light.Get<DirectionalLightData>() with{Intensity=0,CastShadow=false});
        var env=document.World.CreateObject("Environment");env.Set(config);Publish(fixture.Root,fixture.Project,package);
        using var assets=SceneAssetPreparation.Prepare(fixture.Root,fixture.Project,document.CaptureSnapshot(),true);
        using var cache=new RenderResourceCache(renderer);using var target=renderer.CreateViewTarget(256,256);
        byte[] image=new byte[256*256*4];
        Bad(()=>new SceneRenderSession(renderer,cache,document.World,assets,document.CaptureSnapshot()).Dispose());
        Bad(()=>new SceneRenderSession(renderer,cache,document.World,assets,document.CaptureSnapshot(),environmentPreparationAllowed:()=>false).Dispose());
        Pass(renderer.EnvironmentStats.Live==0,"constructor failures publish no environment");
        bool allowed=true;
        using var session=new SceneRenderSession(renderer,cache,document.World,assets,document.CaptureSnapshot(),environmentPreparationAllowed:()=>allowed);
        void Prepare()=>session.PrepareEnvironmentView(256,256,camera,ambient:0);
        void Tick(){Pass(session.Submit(frame++,256,256,camera,ambient:0,target:target),"prepared actual submit");renderer.CaptureTarget(target,image);renderer.Present();}
        Bad(()=>session.Submit(frame,256,256,camera,ambient:0,target:target));Prepare();Tick();
        byte[] baseline=(byte[])image.Clone();var oracle=new IblImageOracle(package);
        Bad(()=>cache.AcquireEnvironment(package,()=>false).Dispose());
        using(var shared=cache.AcquireEnvironment(package,()=>true))Bad(()=>shared.Resource.Replace(package,()=>true));
        Pass(renderer.EnvironmentStats.Live==1,"cache hit checks approval and cannot mutate immutable generation");
        foreach(int y in new[]{104,128,152})foreach(int x in new[]{104,128,152}){
            var p=new Vector3((x+.5f)/256*4-2,2-(y+.5f)/256*4,-4);
            var expected=oracle.Evaluate(p,Vector3.UnitZ,new(.6f,.25f,.1f),0,.6f,1,1,0);
            int error=0;for(int c=0;c<3;c++)error=Math.Max(error,Math.Abs(image[(y*256+x)*4+c]-(int)MathF.Round(expected[c]*255)));
            maxError=Math.Max(maxError,error);Pass(error<=3,"shared scene software pixel oracle error="+error);
        }
        File.WriteAllBytes(Path.Combine(output,"shared-ibl.png"),Png(256,256,baseline));
        var before=renderer.EnvironmentStats;ulong plans=renderer.PipelineStats.Creates;
        env.Set(config with{Strength=.5f,RotationRadians=.7f});Bad(()=>session.Submit(frame,256,256,camera,ambient:0,target:target));
        allowed=false;Bad(()=>session.RefreshEnvironment(assets,()=>allowed));Pass(session.PreparedEnvironment==config,"revoked refresh retains installed configuration");
        allowed=true;session.RefreshEnvironment(assets,()=>allowed);Prepare();Tick();
        byte[] halfImage=(byte[])image.Clone();
        Pass(!image.SequenceEqual(baseline)&&renderer.EnvironmentStats.Publications==before.Publications&&renderer.EnvironmentStats.UploadedBytes==before.UploadedBytes&&renderer.PipelineStats.Creates==plans,"scalar refresh rebinds without allocation or upload");
        env.Set(config);session.RefreshEnvironment(assets,()=>true);Prepare();Tick();Pass(image.SequenceEqual(baseline),"restored committed values reproduce image");
        env.Set(config with{Generation=2});Bad(()=>session.RefreshEnvironment(assets,()=>true));Pass(session.PreparedEnvironment==config,"wrong generation retains last configuration");env.Set(config);Tick();Pass(image.SequenceEqual(baseline),"failed generation preserves image after document restoration");
        Bad(()=>session.RefreshEnvironment(assets,()=>{session.Dispose();return true;}));
        Bad(()=>session.RefreshEnvironment(assets,()=>{session.Submit(frame,256,256,camera,ambient:0,target:target);return true;}));
        Bad(()=>session.RefreshEnvironment(assets,()=>{env.Set(config with{Strength=2});return true;}));
        Pass(env.Get<EnvironmentLightingData>()==config,"approval callback cannot mutate live World");
        var runner=new Ncma.Runtime.WorldRunner(document.World);runner.AddSystem(new GraphFailure(_=>{
            Bad(()=>session.RefreshEnvironment(assets,()=>true));Bad(()=>session.PrepareEnvironmentView(256,256,camera));Bad(()=>session.Submit(frame,256,256,camera,target:target));
        }));runner.Advance(1d/60);Pass(!runner.IsFaulted&&document.World.Tick==1,"simulation boundary rejected without faulting runner");
        Exception? foreign=null;var thread=new Thread(()=>{try{session.RefreshEnvironment(assets,()=>true);}catch(Exception e){foreign=e;}});thread.Start();thread.Join();Pass(foreign is InvalidOperationException,"foreign-thread refresh rejected");
        renderer.SubmitResources(frame++,[],new(0,0,256,256),Vector4.Zero,new(Vector3.Zero,Vector3.UnitY,Vector4.Zero));Bad(()=>session.RefreshEnvironment(assets,()=>true));renderer.Present();
        light.Set(light.Get<DirectionalLightData>() with{CastShadow=true});Bad(()=>session.Submit(frame,256,256,camera,ambient:0,target:target));Prepare();Tick();
        Pass(session.Costs.ShadowDraws==2&&renderer.EnvironmentStats.Publications==before.Publications,"shadow variant keeps exact environment without upload");
        session.PrepareEnvironmentView(128,128,camera,ambient:0);Pass(session.Submit(frame++,128,128,camera,ambient:0,target:target),"resize retains binding");renderer.Present();Prepare();
        light.Set(light.Get<DirectionalLightData>() with{CastShadow=false});Prepare();
        // Source-free asset closure is distinct from author metadata; both sessions share immutable GPU generations.
        string packed="assets/scene.ncpak";File.WriteAllBytes(Path.Combine(fixture.Root,packed),SceneAssetPreparation.CreateRuntimePackage(fixture.Root,fixture.Project,document.CaptureSnapshot(),[]));
        using(var packedAssets=SceneAssetPreparation.Prepare(fixture.Root,fixture.Project,document.CaptureSnapshot(),true,packed))
        using(var second=new SceneRenderSession(renderer,cache,document.World,packedAssets,document.CaptureSnapshot(),environmentPreparationAllowed:()=>true)){
            second.PrepareEnvironmentView(256,256,camera,ambient:0);Pass(second.Submit(frame++,256,256,camera,ambient:0,target:target),"source-free NCP shared scene");renderer.CaptureTarget(target,image);renderer.Present();
            Pass(image.SequenceEqual(baseline)&&renderer.EnvironmentStats.Publications==before.Publications&&renderer.EnvironmentStats.Live==1,"source-free pixels and GPU generation sharing");
        }
        env.Set(EnvironmentLightingData.Off);session.RefreshEnvironment(assets,()=>true);Prepare();Tick();Pass(image.Where((_,i)=>i%4!=3).All(b=>b==0),"explicit Off removes IBL contribution");cache.Trim();Pass(renderer.EnvironmentStats.Live==0,"Off releases scene pins before final environment retirement");
        var next=cook.Cook(package.AssetId,2,source,new(8,4,8,128));Publish(fixture.Root,fixture.Project,next);
        var nextConfig=config with{Generation=2,ContentHash=next.ContentHash};env.Set(nextConfig);
        using var nextAssets=SceneAssetPreparation.Prepare(fixture.Root,fixture.Project,document.CaptureSnapshot(),true);
        session.RefreshEnvironment(nextAssets,()=>true);Prepare();Tick();Pass(image.SequenceEqual(baseline)&&renderer.EnvironmentStats.Live==1,"exact second generation refresh");
        for(int i=0;i<16;i++){session.Submit(frame++,256,256,camera,ambient:0,target:target);renderer.Present();}
        before=renderer.EnvironmentStats;var resources=renderer.ResourceStats;plans=renderer.PipelineStats.Creates;ulong calls=renderer.SubmitCalls,cookCalls=cook.NativeCalls;
        long allocated=GC.GetAllocatedBytesForCurrentThread();var started=System.Diagnostics.Stopwatch.GetTimestamp();
        for(int i=0;i<64;i++){session.Submit(frame++,256,256,camera,ambient:0,target:target);renderer.Present();}
        double elapsed=System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
        Pass(allocated==0,"64 warmed shared submissions allocation zero="+allocated);
        Pass(renderer.EnvironmentStats.Publications==before.Publications&&renderer.EnvironmentStats.UploadedBytes==before.UploadedBytes&&renderer.EnvironmentStats.Captures==before.Captures&&renderer.ResourceStats.Creates==resources.Creates&&renderer.ResourceStats.UploadedBytes==resources.UploadedBytes&&renderer.PipelineStats.Creates==plans&&cook.NativeCalls==cookCalls&&renderer.SubmitCalls==calls+64,"warm submit no cook/readback/upload/rebuild; one scene batch");
        session.Dispose();cache.Trim();Pass(renderer.EnvironmentStats.Live==0&&renderer.PipelineStats.Pipelines==0,"final ordered close clears resources");
        // An empty scene never requests an environment GPU or shader, even if metadata selects one.
        var empty=new SceneDocument("Empty",RenderComponentRegistry.CreateRegistry());empty.World.CreateObject("Environment").Set(nextConfig);
        using(var emptyAssets=SceneAssetPreparation.Prepare(fixture.Root,fixture.Project,empty.CaptureSnapshot(),true))
        using(var flat=new SceneRenderSession(renderer,cache,empty.World,emptyAssets,empty.CaptureSnapshot()))
            Pass(!flat.Submit(frame,256,256,Guid.Empty)&&renderer.EnvironmentStats.Live==0&&flat.PreparedEnvironmentShaders is null,"empty avoids unused 3D initialization");
        // Promotion of an already submitted old-profile scene is explicit and preserves resources.
        env.Set(EnvironmentLightingData.Off);
        using(var old=new SceneRenderSession(renderer,cache,document.World,nextAssets,document.CaptureSnapshot())){
            old.Submit(frame++,256,256,camera,ambient:0,target:target);renderer.Present();env.Set(nextConfig);
            old.RefreshEnvironment(nextAssets,()=>true);old.PrepareEnvironmentView(256,256,camera,ambient:0);
            Pass(old.Submit(frame++,256,256,camera,ambient:0,target:target),"old profile explicit promotion");renderer.CaptureTarget(target,image);renderer.Present();Pass(image.SequenceEqual(baseline),"promotion actual image parity");
            var restored=document.CaptureSnapshot();document.RestoreSnapshot(restored);Bad(()=>old.RefreshEnvironment(nextAssets,()=>true));Bad(()=>old.Submit(frame,256,256,camera,target:target));
        }
        // Real ASCII/binary imported NCA characters use the same shared skin/shadow/IBL path.
        string build=Directory.GetParent(root)!.Parent!.FullName;var repo=new DirectoryInfo(Path.GetFullPath(root));while(!Directory.Exists(Path.Combine(repo.FullName,"tests/assets/fbx")))repo=repo.Parent??throw new Exception("fixtures missing");
        string posePath=Path.Combine(build,"NcmaAnimationKernel.dll"),importPath=Path.Combine(build,"NcmaImportKernel.dll");
        using var kernel=new PoseKernel(posePath,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(posePath))));
        foreach(string f in new[]{"synthetic","blender_279_sausage_6100_ascii.fbx","blender_279_sausage_7400_binary.fbx"}){
            SkinFixture skinFixture;if(f=="synthetic")skinFixture=new(output);else{using var importer=new ImportKernel(importPath,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(importPath))));skinFixture=new(output,importer.LoadAndCopy(Path.Combine(repo.FullName,"tests/assets/fbx",f),30));}
            var (skinDocument,skinCamera,objects)=skinFixture.Scene();skinDocument.World.CreateObject("Environment").Set(config);Publish(skinFixture.Root,skinFixture.Project,package);
            using var skinAssets=SceneAssetPreparation.Prepare(skinFixture.Root,skinFixture.Project,skinDocument.CaptureSnapshot(),true);
            using var skinSession=new SceneRenderSession(renderer,cache,skinDocument.World,skinAssets,skinDocument.CaptureSnapshot(),poseKernel:kernel,environmentPreparationAllowed:()=>true);
            skinSession.PrepareEnvironmentView(256,256,skinCamera,ambient:0);
            foreach(double t in new[]{0,.5,.999999}){skinSession.Animation!.SetPreviewTime(t);Pass(skinSession.Submit(frame++,256,256,skinCamera,ambient:0,target:target),"shared skin actual submit "+f+"/"+t);renderer.CaptureTarget(target,image);renderer.Present();Pass(skinSession.Costs.GeometryDraws==1&&skinSession.Costs.ShadowDraws==1&&skinSession.ReadAnimationPresentation() is not null,"same animated geometry/shadow publication");}
            File.WriteAllBytes(Path.Combine(output,"shared-ibl-"+f+".png"),Png(256,256,image));
        }
        cache.Trim();Pass(renderer.EnvironmentStats.Live==0&&renderer.SkinStats.Meshes==0&&renderer.PipelineStats.Pipelines==0,"all animation/environment pins retire");
        Pass(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"DX11 API validation 0/0");
        // Actual custom bytecode through v2 file admission and this exact shared service.
        string shaderRoot=Path.Combine(output,"user-shaders");Directory.CreateDirectory(Path.Combine(shaderRoot,"assets/shaders"));
        var shaderFiles=new List<ShaderPackageFile>();using(var compiler=new ShaderCompilerService(renderer,()=>true))foreach(bool shadow in new[]{false,true}){
            var selected=DefaultEnvironmentSceneShaders.Select(DefaultEnvironmentSceneShaders.CopyCatalog(renderer,shadow),shadow);
            var descriptors=shadow?new[]{selected.GeometryVertex,selected.GeometryPixel,selected.ToneVertex,selected.TonePixel,selected.ShadowVertex!,selected.ShadowPixel!}:
                new[]{selected.GeometryVertex,selected.GeometryPixel,selected.ToneVertex,selected.TonePixel};
            var inputs=new List<RuntimeShaderInput>();for(int i=0;i<descriptors.Length;i++){
                var d=descriptors[i];if(i==1){var definition=d.CopyDefinition();string text=definition.Source.Replace("*ao*EnvironmentSettings.x;","*ao*EnvironmentSettings.x*.5;",StringComparison.Ordinal);Pass(text!=definition.Source,"custom actual pixel bytecode");d=ShaderDescriptor.Prepare(definition with{AssetId=Guid.NewGuid(),Source=text,SourceHash=ShaderContractCodec.HashSource(text)});}
                inputs.Add(new(i<2?(i==0?RuntimeShaderRole.EnvironmentGeometryVertex:RuntimeShaderRole.EnvironmentGeometryPixel):(RuntimeShaderRole)(i+2),compiler.Prepare(d)));
            }
            var p=RuntimeShaderPackage.Cook(ShaderProfile.SceneEnvironment,shadow,false,inputs);string relative="assets/shaders/"+shadow+".ncshader";File.WriteAllBytes(Path.Combine(shaderRoot,relative),p.CopyBytes());shaderFiles.Add(new(relative,p.ContentHash,p.Profile.ToString(),shadow,false));
        }
        var finalStats=renderer.Stats;target.Dispose();cache.Dispose();renderer.Dispose();
        using(var files=RuntimeShaderFileSet.Load(shaderRoot,new(2,shaderFiles.ToArray())))
        using(var fileRenderer=new RendererSession(native,window,256,256))
        using(var fileCache=new RenderResourceCache(fileRenderer))
        using(var fileTarget=fileRenderer.CreateViewTarget(256,256)){
            files.InstallSelection(fileRenderer,()=>true);
            Bad(()=>fileRenderer.DefaultRuntimeShaders.Prepare(ShaderProfile.Scene3D,false,false,()=>true));
            using(var fileSession=new SceneRenderSession(fileRenderer,fileCache,document.World,nextAssets,document.CaptureSnapshot(),environmentPreparationAllowed:()=>true)){
                fileSession.PrepareEnvironmentView(256,256,camera,ambient:0);Pass(fileSession.Submit(1,256,256,camera,ambient:0,target:fileTarget),"source-free custom file shared submit");fileRenderer.CaptureTarget(fileTarget,image);fileRenderer.Present();
                Pass(image.SequenceEqual(halfImage),"custom file bytecode equals independent half-strength shared image");
            }
            fileCache.Trim();Pass(fileRenderer.EnvironmentStats.Live==0&&fileRenderer.Stats.ValidationErrors==0&&fileRenderer.Stats.ValidationWarnings==0,"file-backed resource lifetime/API0/0");
        }
        var duplicate=document.World.CreateObject("Duplicate environment");duplicate.Set(nextConfig);Bad(()=>SceneEnvironmentState.Read(document.World,document.World.Identity));
        Ncma.Runtime.World? probeWorld=null;var registry=Ncma.Runtime.ComponentRegistry.CreateDefault();
        registry.Register<EnvironmentBoundaryProbe>("test.environment_boundary",1,"{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"value\"],\"properties\":{\"value\":{\"type\":\"integer\"}}}",v=>{
            if(probeWorld is not null)Bad(()=>SceneEnvironmentState.VerifyBoundary(probeWorld,probeWorld.Identity));return v;
        });
        probeWorld=new("Boundary probe",registry);probeWorld.CreateObject("Probe").Set(new EnvironmentBoundaryProbe(1));
        var probeSnapshot=probeWorld.CaptureSnapshot();probeWorld.RestoreSnapshot(probeSnapshot);Pass(probeWorld.Count==1,"component validation/restore reject preparation reentry without changing document");
        File.WriteAllText(Path.Combine(output,"shared-environment-results.json"),JsonSerializer.Serialize(new{cases,maxError,warmFrames=64,allocatedBytes=allocated,elapsedMilliseconds=elapsed,formalHostsEnabled=false,validationErrors=finalStats.ValidationErrors,validationWarnings=finalStats.ValidationWarnings},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"PASS M7.3-C2-B shared SceneRenderSession cases={cases} pixelError={maxError} warmAllocation={allocated} API0/0; formal hosts remain pending");return 0;
    }
}
