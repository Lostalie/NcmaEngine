using System.Numerics;
using System.Text.Json;
using Ncma.Assets;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;
internal static unsafe partial class Program
{
    private static int PostSceneTests(string plugins,string output)
    {
        output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);int cases=0;ulong frame=1;
        void Pass(bool ok,string label){Check(ok,"C2 shared post "+label);cases++;}
        void Bad(Action action){Reject(action);cases++;}
        var repo=new DirectoryInfo(Path.GetFullPath(plugins));while(!File.Exists(Path.Combine(repo.FullName,"Build.bat")))repo=repo.Parent??throw new Exception("Repo missing");
        using var shaderFiles=RuntimeShaderFileSet.ForHost(Path.Combine(repo.FullName,"out/bin"),null,null)??throw new Exception("Deployed shader files required.");
        using var library=RuntimePostProcessLibrary.LoadDeployment(Path.Combine(repo.FullName,"out/bin"));
        using var loader=new PluginLoader();loader.Load(Path.GetFullPath(plugins),Specs().Take(2).ToArray());
        using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"Shared post",128,128,false);
        using var renderer=new RendererSession(loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer),window,128,128);shaderFiles.InstallSelection(renderer,()=>true);
        bool approved=true;Action? onApproval=null;
        using var runtime=new PostProcessRuntime(renderer,shaderFiles,library,PostProcessModules.Official(),()=>{onApproval?.Invoke();return approved;});
        var fixture=new SceneGpuFixture(output);var(source,camera,_,_)=fixture.Scene();
        var registry=global::Ncma.Runtime.CompositionRegistry.Build(global::Ncma.Scene.Rendering.Generated.ComponentModule.Module, global::Ncma.Rendering.Generated.ComponentModule.Module);
        var authoring=new SceneDocument("Post scene",registry,s=>{SceneRenderValidation.RequireComposition(s);_=ScenePostProcessState.Read(s);});authoring.RestoreSnapshot(source.CaptureSnapshot());
        var authoringBytes=authoring.CaptureBytes();var document=BakeRenderScene(authoring);
        global::Ncma.Runtime.Entity light=document.World.GetEntities().Single(o=>document.World.Has<DirectionalLightData>(o));document.World.Set(light, document.World.Get<DirectionalLightData>(light) with{CastShadow=false});
        global::Ncma.Runtime.Entity settings=document.World.CreateEntity("Post settings");document.World.Set(settings, PostProcessData.Off);
        using var assets=SceneAssetPreparation.Prepare(fixture.Root,fixture.Project,document.CaptureSnapshot(),true);
        using var cache=new RenderResourceCache(renderer);using var target=renderer.CreateViewTarget(128,128);byte[] image=new byte[128*128*4];
        using(var baseline=new SceneRenderSession(renderer,cache,document.World)){baseline.Prepare(assets,document.CaptureSnapshot());Pass(baseline.Submit(frame++,128,128,camera,target:target),"old path baseline");renderer.CaptureTarget(target,image);renderer.Present();}
        byte[] original=image.ToArray();
        using var session=new SceneRenderSession(renderer,cache,document.World);session.Prepare(assets,document.CaptureSnapshot(),postProcessing:runtime,environmentPreparationAllowed:()=>approved);
        void Prepare(uint size=128,float exposure=1)=>session.PrepareEnvironmentView(size,size,camera,exposure:exposure);
        void Tick(uint size=128,float exposure=1){Pass(session.Submit(frame++,size,size,camera,target:target,exposure:exposure),"ready scene submit");renderer.CaptureTarget(target,image);renderer.Present();}
        Bad(()=>session.Submit(frame++,128,128,camera,target:target));Prepare();Tick();Pass(image.SequenceEqual(original)&&renderer.PostStats.Bindings==0,"Off uses original image without post targets");
        var active=PostProcessData.Off with{Enabled=true,StackJson=PostProcessData.EncodeStack([PostProcessModules.Bloom(new(Levels:2,Threshold:0,Intensity:.8f))])};document.World.Set(settings, active);
        Bad(()=>session.Submit(frame++,128,128,camera,target:target));Prepare();Tick();Pass(!image.SequenceEqual(original)&&renderer.PostStats.Bindings==1,"actual shared Bloom changes pixels");
        byte[] bloom=image.ToArray();ulong publishes=renderer.PostStats.Publications,creates=renderer.PipelineStats.Creates;
        for(int i=0;i<4;i++){Prepare();session.Submit(frame++,128,128,camera,target:target);renderer.Present();}
        Pass(renderer.PostStats.Publications==publishes&&renderer.PipelineStats.Creates==creates,"unchanged prepare does not rebuild");
        long allocated=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<64;i++){session.Submit(frame++,128,128,camera,target:target);renderer.Present();}
        allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;Pass(allocated==0&&renderer.PostStats.Publications==publishes,"warm submit allocation0/no rebuild");
        document.World.Set(settings, active with{ToneModule=Guid.NewGuid()});Bad(()=>Prepare());Bad(()=>session.Submit(frame++,128,128,camera,target:target));Pass(session.PreparedPostProcess==active&&renderer.PostStats.Publications==publishes,"unknown module retains old installed binding");
        document.World.Set(settings, active);Prepare();Tick();Pass(image.SequenceEqual(bloom),"failed candidate recovery");
        approved=false;document.World.Set(settings, active with{ExposureEv=-1});Bad(()=>Prepare());Pass(session.PreparedPostProcess==active,"revocation retains old configuration");approved=true;
        onApproval=()=>document.World.CreateEntity("forbidden callback write");Bad(()=>Prepare());onApproval=null;
        onApproval=()=>session.Dispose();Bad(()=>Prepare());onApproval=null;
        onApproval=()=>renderer.Dispose();Bad(()=>Prepare());onApproval=null;
        onApproval=()=>runtime.Dispose();Bad(()=>Prepare());onApproval=null;
        Prepare();Tick();Pass(!image.SequenceEqual(bloom),"EV applied before Tone");
        document.World.Set(settings, active with{ToneModule=PostProcessLibrarySelection.ReinhardModule});Prepare();Tick();Pass(session.PreparedPostProcess.ToneModule==PostProcessLibrarySelection.ReinhardModule,"exact Tone variant switch");
        var meshes=document.World.GetEntities().Where(o=>document.World.Has<StaticMeshData>(o)).ToArray();var meshValues=meshes.Select(o=>document.World.Get<StaticMeshData>(o)).ToArray();
        for(int i=0;i<meshes.Length;i++)document.World.Set(meshes[i], meshValues[i] with{Visible=false,CastShadow=false});document.World.Set(settings, active);Prepare();
        Pass(!session.Submit(frame++,128,128,camera,target:target)&&session.PreparedPostProcess.ToneModule==PostProcessLibrarySelection.ReinhardModule,"culled view does not publish uninstalled settings");
        Pass(!session.IsPostProcessReady,"culled view cannot report post ready");
        for(int i=0;i<meshes.Length;i++)document.World.Set(meshes[i], meshValues[i]);Bad(()=>session.Submit(frame++,128,128,camera,target:target));Prepare();Tick();Pass(image.SequenceEqual(bloom),"visible-again prepares requested graph");
        Bad(()=>session.Submit(frame++,127,127,camera,target:target));Prepare(127);Pass(session.Submit(frame++,127,127,camera,target:target),"odd resize off-frame");renderer.Present();Prepare();
        document.World.Set(light, document.World.Get<DirectionalLightData>(light) with{CastShadow=true});Bad(()=>session.Submit(frame++,128,128,camera,target:target));Prepare();Tick();Pass(session.Plan!.Resources.Any(r=>r.Role==RenderRole.SceneShadow),"shadow variant paired");
        document.World.Set(settings, active with{ExposureEv=4});Bad(()=>Prepare(exposure:16));Pass(session.PreparedPostProcess==active,"combined exposure refuses overflow without clamping");
        document.World.Set(settings, PostProcessData.Off);document.World.Set(light, document.World.Get<DirectionalLightData>(light) with{CastShadow=false});Prepare();Tick();Pass(image.SequenceEqual(original)&&renderer.PostStats.Bindings==0&&renderer.PostStats.ResidentBytes==0,"Off releases post and restores original shaders");
        document.World.Set(settings, active);Bad(()=>{using var denied=new SceneRenderSession(renderer,cache,document.World);denied.Prepare(assets,document.CaptureSnapshot());});
        using(var presets=EnvironmentPresetLibrary.LoadDeployment(Path.Combine(repo.FullName,"out/bin"))) {
            var environment=presets.Package.CloneForAsset(Guid.NewGuid(),1);var descriptor=EnvironmentAssetDescriptor.FromPackage(fixture.Project,environment);
            string path=Path.Combine(fixture.Root,descriptor.Path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,environment.CopyBytes());File.WriteAllBytes(Path.Combine(fixture.Root,"assets/post-environment.ncenv"),descriptor.Encode());
            global::Ncma.Runtime.Entity env=document.World.CreateEntity("Environment");var value=new EnvironmentLightingData(1,environment.AssetId,1,environment.ContentHash,1,0,true);document.World.Set(env, value);
            using var environmentAssets=SceneAssetPreparation.Prepare(fixture.Root,fixture.Project,document.CaptureSnapshot(),true);
            session.RefreshEnvironment(environmentAssets,()=>approved);Bad(()=>session.Submit(frame++,128,128,camera,target:target));Prepare();Tick();
            Pass(session.PreparedEnvironment==value&&renderer.EnvironmentStats.Live==1&&renderer.PostStats.Bindings==1,"real environment and Bloom share completed candidate");
            byte[] ibl=image.ToArray();document.World.Set(env, value with{Generation=2});Bad(()=>session.RefreshEnvironment(environmentAssets,()=>approved));Bad(()=>session.Submit(frame++,128,128,camera,target:target));
            Pass(session.PreparedEnvironment==value,"bad generation retains installed environment and post");document.World.Set(env, value);Prepare();Tick();Pass(image.SequenceEqual(ibl),"environment failed refresh recovery");
            document.World.Set(env, EnvironmentLightingData.Off);session.RefreshEnvironment(assets,()=>approved);Prepare();Tick();cache.Trim();Pass(renderer.EnvironmentStats.Live==0&&renderer.PostStats.Bindings==1,"IBL Off keeps post, releases environment");
        }
        Prepare();Tick();runtime.Dispose();document.World.Set(settings, active with{ExposureEv=-2});Bad(()=>Prepare());Bad(()=>session.Submit(frame++,128,128,camera,target:target));
        session.Dispose();cache.Trim();Pass(renderer.PostStats.Bindings==0&&renderer.PipelineStats.Pipelines==0&&renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"close returns baseline API0/0");
        Pass(authoringBytes.SequenceEqual(authoring.CaptureBytes()),"runtime post changes leave authoring source unchanged");
        File.WriteAllText(Path.Combine(output,"post-scene-results.json"),JsonSerializer.Serialize(new{cases,allocated,apiErrors=0,apiWarnings=0,formalHosts=false,manualAccepted=false}));
        Console.WriteLine($"PASS M7.4-C2 shared scene: {cases} checks; allocation={allocated}; API0/0; {output}");return 0;
    }
}
