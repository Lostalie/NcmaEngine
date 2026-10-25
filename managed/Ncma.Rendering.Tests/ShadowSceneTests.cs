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
    private static int ShadowSceneTests(string plugins,string output)
    {
        output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);int cases=0;ulong frame=1;
        void Pass(bool value,string label){Check(value,"M7.5 shared shadow: "+label);cases++;}
        void Bad(Action action){Reject(action);cases++;}
        bool invoked=false;RenderPipeline InvalidFactory(float e,float a,bool s){invoked=true;return new Scene3DPipeline(e,a,shadows:s);}
        Bad(()=>SceneShadowPipelinePolicy.Create(InvalidFactory,1,.03f,false,null,ShaderProfile.Flat2D));
        Pass(!invoked,"invalid profile rejects before arbitrary custom factory callback");
        var repo=new DirectoryInfo(Path.GetFullPath(plugins));while(!File.Exists(Path.Combine(repo.FullName,"Build.bat")))repo=repo.Parent??throw new Exception("Repo missing");
        string config=new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        using var staged=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(repo.FullName,"out/verification/m2-7",config,"packages.json")));string packageRoot=staged.RootElement.GetProperty("editor").GetString()!;
        Ncma.Application.DeploymentManifest.Validate(packageRoot);
        using var files=RuntimeShaderFileSet.ForHost(packageRoot,null,null)!;using var highFiles=RuntimeShadowLibrary.LoadDeployment(packageRoot);using var postFiles=RuntimePostProcessLibrary.LoadDeployment(packageRoot);
        using var loader=new PluginLoader();loader.Load(Path.GetFullPath(plugins),Specs().Take(2).ToArray());using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"Scene shadow selection",128,128,false);
        using var renderer=new RendererSession(loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer),window,128,128);files.InstallSelection(renderer,()=>true);
        bool approved=true;Action? callback=null;bool Approval(){callback?.Invoke();return approved;}
        var high=highFiles.Prepare(renderer,files,Approval);using var post=new PostProcessRuntime(renderer,files,postFiles,PostProcessModules.Official(),Approval);
        var fixture=new SceneGpuFixture(output);var(source,camera,_,_)=fixture.Scene();
        var authoring=new SceneDocument("Shadow selection",global::Ncma.Runtime.CompositionRegistry.Build(global::Ncma.Scene.Rendering.Generated.ComponentModule.Module, global::Ncma.Rendering.Generated.ComponentModule.Module),SceneRenderValidation.RequireComposition);authoring.RestoreSnapshot(source.CaptureSnapshot());
        var authoringBytes=authoring.CaptureBytes();var doc=BakeRenderScene(authoring);
        global::Ncma.Runtime.Entity setting=doc.World.CreateEntity("Shadows");doc.World.Set(setting, new SceneShadowData(1,SceneShadowQuality.Off));
        using var assets=SceneAssetPreparation.Prepare(fixture.Root,fixture.Project,doc.CaptureSnapshot(),true);using var cache=new RenderResourceCache(renderer);using var target=renderer.CreateViewTarget(128,128);byte[] image=new byte[128*128*4];
        using var session=new SceneRenderSession(renderer,cache,doc.World);session.Prepare(assets,doc.CaptureSnapshot(),highShadows:high,environmentPreparationAllowed:Approval);
        void Prepare(uint size=128)=>session.PrepareEnvironmentView(size,size,camera);
        void Tick(uint size=128){Pass(session.Submit(frame++,size,size,camera,target:target),"ready submit");renderer.CaptureTarget(target,image);renderer.Present();}
        Bad(()=>session.Submit(frame++,128,128,camera,target:target));
        foreach(var quality in new[]{SceneShadowQuality.Off,SceneShadowQuality.Low,SceneShadowQuality.Medium,SceneShadowQuality.High}){
            doc.World.Set(setting, new SceneShadowData(1,quality));if(quality!=SceneShadowQuality.Off)Bad(()=>session.Submit(frame++,128,128,camera,target:target));
            Prepare();Tick();var preset=SceneShadowPresets.Get(quality);var shadow=session.Plan!.Resources.SingleOrDefault(r=>r.Role==RenderRole.SceneShadow);
            Pass((shadow?.Width??0)==preset.Budget.Resolution&&session.PreparedShadowSelection?.Quality==quality,"selected exact shadow allocation "+quality);
            Pass(quality==SceneShadowQuality.Off?session.Costs.ShadowDraws==0:session.Costs.ShadowDraws>0,"Off skips caster encode "+quality);
            Pass(renderer.PipelineStats.ResidentBytes==128UL*128*12+408+preset.Budget.DepthTextureBytes,"actual resident budget "+quality);
            File.WriteAllBytes(Path.Combine(output,$"{quality}.png"),Png(128,128,image));
        }
        for(int i=0;i<8;i++){Prepare();session.Submit(frame++,128,128,camera,target:target);renderer.Present();}
        ulong creates=renderer.PipelineStats.Creates;long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<64;i++){session.Submit(frame++,128,128,camera,target:target);renderer.Present();}long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        Pass(allocated==0&&renderer.PipelineStats.Creates==creates,"warm High Submit allocation0/no pipeline creation");
        Bad(()=>session.Submit(frame++,128,128,camera,target:target,shadow:new SceneShadowSettings()));Bad(()=>session.Submit(frame++,127,127,camera,target:target));Prepare(127);Tick(127);Prepare();
        doc.World.Set(setting, new SceneShadowData(1,SceneShadowQuality.Low));approved=false;Bad(()=>Prepare());Pass(session.PreparedShadowSelection?.Quality==SceneShadowQuality.High,"revoked swap retains installed High");approved=true;
        foreach(Action action in new Action[]{()=>doc.World.CreateEntity("not allowed"),()=>session.Dispose(),()=>renderer.Dispose()}){callback=action;Bad(()=>Prepare());callback=null;}
        Prepare();Tick();Pass(session.PreparedShadowSelection?.Quality==SceneShadowQuality.Low,"failed approval recovery");
        var geometry=doc.World.GetEntities().Where(o=>doc.World.Has<StaticMeshData>(o)).ToArray();var original=geometry.Select(o=>doc.World.Get<StaticMeshData>(o)).ToArray();
        for(int i=0;i<geometry.Length;i++)doc.World.Set(geometry[i], original[i] with{Visible=false,CastShadow=false});doc.World.Set(setting, new SceneShadowData(1,SceneShadowQuality.High));Prepare();
        Pass(!session.Submit(frame++,128,128,camera,target:target)&&session.PreparedShadowSelection?.Quality==SceneShadowQuality.Low,"culled view never stamps old graph as High");
        for(int i=0;i<geometry.Length;i++)doc.World.Set(geometry[i], original[i]);Bad(()=>session.Submit(frame++,128,128,camera,target:target));Prepare();Tick();
        var highSnapshot=doc.CaptureSnapshot();Bad(()=>{using var denied=new SceneRenderSession(renderer,cache,doc.World);denied.Prepare(assets,highSnapshot);});
        doc.World.Remove<SceneShadowData>(setting);Bad(()=>session.Submit(frame++,128,128,camera,target:target));Prepare();Tick();Pass(session.PreparedShadowSelection is null&&session.Plan!.Resources.Single(r=>r.Role==RenderRole.SceneShadow).Width==1024,"remove explicit selection restores default Medium");
        for(int i=0;i<8;i++)Prepare();long defaultBefore=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<64;i++)Prepare();long defaultPreparationBytes=GC.GetAllocatedBytesForCurrentThread()-defaultBefore;
        Pass(defaultPreparationBytes==0,"unselected plain default keeps original no-extra-extraction/no-read-scope preparation path");
        session.Dispose();cache.Trim();
        // Explicit presets constrain public custom graphs; absence preserves user-owned policy.
        RenderPipeline Factory(float e,float a,bool s)=>new Scene3DPipeline(e,a,256,s);
        using(var custom=new SceneRenderSession(renderer,cache,doc.World)){custom.Prepare(assets,doc.CaptureSnapshot(),pipelineFactory:Factory,highShadows:high,environmentPreparationAllowed:Approval);
            custom.PrepareEnvironmentView(128,128,camera);Pass(custom.Plan!.Resources.Single(r=>r.Role==RenderRole.SceneShadow).Width==256,"unselected custom graph retains explicit user policy, no claimed Medium budget");
            doc.World.Set(setting, SceneShadowData.Default);Bad(()=>custom.PrepareEnvironmentView(128,128,camera));Pass(custom.PreparedShadowSelection is null&&custom.Plan.Resources.Single(r=>r.Role==RenderRole.SceneShadow).Width==256,"mismatched explicit preset cannot overwrite prior custom graph");
        }
        doc.World.Set(setting, new SceneShadowData(1,SceneShadowQuality.High));global::Ncma.Runtime.Entity postObject=doc.World.CreateEntity("Tone/Bloom");var postValue=PostProcessData.Off with{Enabled=true,ToneModule=PostProcessLibrarySelection.ReinhardModule,StackJson=PostProcessData.EncodeStack([PostProcessModules.Bloom(new(Levels:2,Threshold:0,Intensity:.5f))])};doc.World.Set(postObject, postValue);
        using(var joint=new SceneRenderSession(renderer,cache,doc.World)){joint.Prepare(assets,doc.CaptureSnapshot(),postProcessing:post,highShadows:high,environmentPreparationAllowed:Approval);
            void Joint(){joint.PrepareEnvironmentView(128,128,camera);Pass(joint.Submit(frame++,128,128,camera,target:target),"joint High ready");renderer.CaptureTarget(target,image);renderer.Present();}
            Joint();Pass(joint.PreparedShadowSelection?.Quality==SceneShadowQuality.High&&joint.PreparedPostProcess==postValue&&renderer.PostStats.Bindings==1,"High retains selected Tone/Bloom");
            using var presets=EnvironmentPresetLibrary.LoadDeployment(packageRoot);var environment=presets.Package.CloneForAsset(Guid.NewGuid(),1);var descriptor=EnvironmentAssetDescriptor.FromPackage(fixture.Project,environment);
            string path=Path.Combine(fixture.Root,descriptor.Path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,environment.CopyBytes());File.WriteAllBytes(Path.Combine(fixture.Root,"assets/shadow-environment.ncenv"),descriptor.Encode());
            global::Ncma.Runtime.Entity env=doc.World.CreateEntity("Environment");var value=new EnvironmentLightingData(1,environment.AssetId,1,environment.ContentHash,1,0,true);doc.World.Set(env, value);
            using var environmentAssets=SceneAssetPreparation.Prepare(fixture.Root,fixture.Project,doc.CaptureSnapshot(),true);joint.RefreshEnvironment(environmentAssets,Approval);Joint();
            Pass(joint.PreparedEnvironment==value&&joint.Plan!.RequiredSceneGeometryPixelHash==SceneShadowShaders.CreateHighPixel(ShaderProfile.SceneEnvironment).ContentHash,"IBL High exact pixel dependency");
            doc.World.Set(setting, new SceneShadowData(1,SceneShadowQuality.Off));Joint();Pass(joint.Costs.ShadowDraws==0&&!joint.Plan!.Resources.Any(r=>r.Role==RenderRole.SceneShadow)&&renderer.PostStats.Bindings==1,"Off keeps IBL/Bloom but no shadow resources");
        }
        cache.Trim();Pass(renderer.PipelineStats.Pipelines==0&&renderer.PostStats.Bindings==0&&renderer.EnvironmentStats.Live==0&&renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"all leases close API0/0");
        Pass(authoringBytes.SequenceEqual(authoring.CaptureBytes()),"runtime shadow changes leave authoring source unchanged");
        File.WriteAllText(Path.Combine(output,"shadow-scene-results.json"),JsonSerializer.Serialize(new{cases,allocated,defaultPreparationBytes,apiErrors=0,apiWarnings=0,formalHosts=false,animatedOracle=false}));Console.WriteLine($"PASS M7.5 shared shadow: {cases} checks; warm64 allocation={allocated}; default preparation={defaultPreparationBytes}; API0/0; {output}");return 0;
    }
}
