using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Animation.Native;
using Ncma.Asset.Import;
using Ncma.Editor.App;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Player.App;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Scene.Rendering;

internal static unsafe partial class Program
{
    private static int SceneConstructionSafetyTests(string plugins,string output)
    {
        plugins=Path.GetFullPath(plugins);output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
        var repo=new DirectoryInfo(plugins);while(!File.Exists(Path.Combine(repo.FullName,"Build.bat")))repo=repo.Parent??throw new Exception("Repo missing");
        string native=Directory.GetParent(plugins)!.Parent!.FullName,animationPath=Path.Combine(native,"NcmaAnimationKernel.dll"),importPath=Path.Combine(native,"NcmaImportKernel.dll");
        int cases=0;void Pass(bool ok,string label){Check(ok,"M7.6 scene construction: "+label);cases++;}
        FieldInfo Field(object owner,string name)=>owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)??throw new Exception("Missing field "+name);
        using var kernel=new PoseKernel(animationPath,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(animationPath))));
        using(var loader=new PluginLoader()) {
            loader.Load(plugins,Specs().Take(2).ToArray());
            using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"Scene construction safety",64,64,false);
            using var renderer=new RendererSession(loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer),window,64,64);
            ulong frame=1;
            foreach(string file in new[]{"blender_279_sausage_6100_ascii.fbx","blender_279_sausage_7400_binary.fbx"}) {
                SkinFixture fixture;using(var importer=new ImportKernel(importPath,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(importPath)))))
                    fixture=new(output,importer.LoadAndCopy(Path.Combine(repo.FullName,"tests/assets/fbx",file),30));
                var(authoring,camera,_)=fixture.Scene(2);var authoringBytes=authoring.CaptureBytes();
                var document=BakeRenderScene(authoring);var snapshot=document.CaptureSnapshot();var original=document.CaptureCheckpoint().CopyBytes();
                using var assets=SceneAssetPreparation.Prepare(fixture.Root,fixture.Project,snapshot,true);
                using var cache=new RenderResourceCache(renderer);using var animation=new SceneAnimationSession(document.World,kernel);
                Pass(animation.PreparationPhase==ScenePreparationPhase.Empty,"animation owner precedes acquisition");
                Reject(()=>animation.Evaluate(0,false));animation.Prepare(assets,snapshot);Reject(()=>animation.Prepare(assets,snapshot));
                foreach(bool wholeScene in new[]{false,true})foreach(int fault in new[]{0,1,2}) {
                    using var resources=new SceneGpuResources();using var scene=new SceneRenderSession(renderer,cache,document.World);
                    Pass(resources.PreparationPhase==ScenePreparationPhase.Empty&&scene.PreparationPhase==ScenePreparationPhase.Empty&&renderer.SkinStats.Meshes==0,"caller owns empty holders before GPU work");
                    Reject(()=>scene.Submit(frame,64,64,camera));Reject(()=>{_=resources.Metadata;});
                    bool armed=false,fired=false,pending=false;SceneRuntimeShaders? shaders=null;
                    void Prepare(){if(wholeScene)scene.Prepare(assets,snapshot,poseKernel:kernel,shaders:shaders);else resources.Prepare(cache,assets,snapshot,renderer,animation,shaders!.Unshadowed.Skin);}
                    void Close(){if(wholeScene)scene.Dispose();else resources.Dispose();}
                    ScenePreparationPhase Phase()=>wholeScene?scene.PreparationPhase:resources.PreparationPhase;
                    bool Allowed(){if(armed&&!fired&&renderer.SkinStats.Meshes==1){fired=true;
                        if(fault==0){renderer.SubmitResources(frame++,[],new(0,0,64,64),Vector4.Zero,new(Vector3.Zero,Vector3.UnitY,Vector4.Zero));pending=true;return false;}
                        try{if(fault==1)Prepare();else Close();}catch(InvalidOperationException){}
                    }return true;}
                    shaders=SceneRuntimeShaders.PrepareDefault(renderer,true,Allowed);
                    try {
                        armed=true;Reject(Prepare);
                        Pass(fired&&Phase()==ScenePreparationPhase.Failed&&renderer.SkinStats.Meshes>0,"second imported FBX instance refusal or caught reentry retains owner");
                        ulong count=renderer.SkinStats.Meshes;Reject(Prepare);Reject(()=>scene.Submit(frame,64,64,camera));
                        Pass(renderer.SkinStats.Meshes==count,"failed owner cannot be overwritten or submitted");
                        if(fault==0){
                            PluginException? close=null;try{Close();}catch(PluginException error){close=error;}
                            Pass(close is{Result:PluginResult.Busy,Phase:"destroy_mesh"}&&Phase()==ScenePreparationPhase.Closing&&renderer.SkinStats.Meshes==1,"exact native Busy retains closing owner and first skin");
                            renderer.Present();pending=false;
                        }
                        Close();cache.Trim();Pass(Phase()==ScenePreparationPhase.Closed&&renderer.SkinStats.Meshes==0&&cache.Count==0,"explicit same-owner close drains resources");
                    }finally{if(pending)renderer.Present();Close();}
                }
                using(var fresh=new SceneRenderSession(renderer,cache,document.World)) {
                    Task.Run(()=>Reject(()=>fresh.Prepare(assets,snapshot,poseKernel:kernel))).GetAwaiter().GetResult();
                    Pass(fresh.PreparationPhase==ScenePreparationPhase.Empty,"foreign thread cannot consume owner");
                    fresh.Prepare(assets,snapshot,poseKernel:kernel);using var target=renderer.CreateViewTarget(64,64);
                    Pass(fresh.Submit(frame++,64,64,camera,target:target),"fresh owner renders after failures drain");renderer.Present();
                }
                cache.Trim();Pass(original.SequenceEqual(document.CaptureCheckpoint().CopyBytes())&&authoringBytes.SequenceEqual(authoring.CaptureBytes())&&renderer.SkinStats.Meshes==0&&renderer.PipelineStats.Pipelines==0&&renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"runtime and authoring unchanged, resources drained, actual API0/0");
            }
        }
        // Test-only owner attachment tests actual product-class close/retry order,
        // not full PlayerRunner.Run/CLI recovery or a physical device loss.
        foreach(bool editor in new[]{false,true}) {
            IDisposable host;
            if(editor){var value=new CandidatePresentation(plugins,null,true,false,false);value.Start();host=value;}
            else{var value=new PlayerPresentation(plugins,false);value.Start();host=value;}
            var renderer=(RendererSession)Field(host,"_renderer").GetValue(host)!;
            var cache=(RenderResourceCache)Field(host,editor?"_sceneCache":"_cache").GetValue(host)!;
            var fixture=new SkinFixture(output);var(authoring,_,_)=fixture.Scene(2);var document=BakeRenderScene(authoring);var snapshot=document.CaptureSnapshot();
            using var assets=SceneAssetPreparation.Prepare(fixture.Root,fixture.Project,snapshot,true);
            string slot=editor?"_pendingScene":"_scene";Pass(Field(host,slot).GetValue(host) is null,"test never overwrites existing owner");
            var scene=new SceneRenderSession(renderer,cache,document.World);Field(host,slot).SetValue(host,scene);
            bool armed=false,fired=false,pending=false;
            bool Allowed(){if(armed&&!fired&&renderer.SkinStats.Meshes==1){fired=true;renderer.SubmitResources(1,[],new(0,0,64,64),Vector4.Zero,new(Vector3.Zero,Vector3.UnitY,Vector4.Zero));pending=true;return false;}return true;}
            var shaders=SceneRuntimeShaders.PrepareDefault(renderer,true,Allowed);
            try {
                armed=true;Reject(()=>scene.Prepare(assets,snapshot,poseKernel:kernel,shaders:shaders));
                PluginException? close=null;try{host.Dispose();}catch(PluginException error){close=error;}
                Pass(fired&&close is{Result:PluginResult.Busy,Phase:"destroy_mesh"}&&ReferenceEquals(scene,Field(host,slot).GetValue(host)),"actual host retains failed scene through native Busy close");
                Pass(ReferenceEquals(renderer,Field(host,"_renderer").GetValue(host))&&renderer.SkinStats.Meshes==1,"host retains downstream native dependencies");
                renderer.Present();pending=false;Pass(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"host API0/0 before unload");
                host.Dispose();Pass(scene.PreparationPhase==ScenePreparationPhase.Closed&&Field(host,slot).GetValue(host) is null&&Field(host,"_renderer").GetValue(host) is null,"host close retry drains scene before unload");
            }finally{if(pending)renderer.Present();host.Dispose();}
        }
        File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{cases,realAsciiAndBinaryFbx=true,hostFaultAttachment="test-only owner field",deviceLossRecovery=false,fullStageAccepted=false}));
        Console.WriteLine($"PASS M7.6 scene construction {cases} checks; {output}");return 0;
    }
}
