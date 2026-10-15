using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Ncma.Assets;
using Ncma.Editor.App;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Player.App;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Scene.Rendering;

internal static unsafe partial class Program
{
    private static void EnvironmentStartupHostSafetyTests(string plugins,string output)
    {
        output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);int cases=0;
        void Pass(bool ok,string label){Check(ok,"M7.6 startup host: "+label);cases++;}
        plugins=Path.GetFullPath(plugins);EnvironmentPackage package;
        using(var loader=new PluginLoader()) {
            loader.Load(plugins,Specs().Take(2).ToArray());var native=loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer);
            using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"Startup host fixture",64,64,false);
            using var renderer=new RendererSession(native,window,64,64);using var cook=new EnvironmentCookService(native,()=>true);
            package=cook.Cook(Guid.NewGuid(),1,HdrEnvironmentSource.Neutral(Guid.NewGuid()),new(8,4,8,64));
        }
        var fixture=new SceneGpuFixture(output);var(document,_,_,_)=fixture.Scene();
        var environment=document.World.CreateObject("Environment");var enabled=new EnvironmentLightingData(1,package.AssetId,package.Generation,package.ContentHash,1,0,true);environment.Set(enabled);
        var descriptor=EnvironmentAssetDescriptor.FromPackage(fixture.Project,package);string path=Path.Combine(fixture.Root,descriptor.Path);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path,package.CopyBytes());File.WriteAllBytes(Path.Combine(fixture.Root,"assets/environment.ncenv"),descriptor.Encode());
        using var assets=SceneAssetPreparation.Prepare(fixture.Root,fixture.Project,document.CaptureSnapshot(),true);
        FieldInfo Field(object host,string name)=>host.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)??throw new Exception("Missing host owner field "+name);
        using(var flat=new PlayerPresentation(plugins,false)) {
            flat.Start();environment.Set(EnvironmentLightingData.Off);
            var device=(RendererSession)Field(flat,"_renderer").GetValue(flat)!;
            Pass(Field(device,"_publishEnvironment").GetValue(device) is null,"fresh Player does not query environment API");
            flat.PrepareBeforeGameplay(document,assets);
            Pass(Field(device,"_publishEnvironment").GetValue(device) is null,"Off startup leaves optional environment API uninitialized");
            flat.Dispose();environment.Set(enabled);
        }
        // Ordinary public Player startup refusal, not an injected owner: assignment
        // must precede preparation even when approved CPU generations do not match.
        using(var player=new PlayerPresentation(plugins,false)) {
            player.Start();environment.Set(enabled with{Generation=enabled.Generation+1});
            Reject(()=>player.PrepareBeforeGameplay(document,assets));
            var held=(SceneEnvironmentPreflight?)Field(player,"_environmentPreflight").GetValue(player);
            Pass(held?.Status.Phase==EnvironmentPreflightPhase.Failed,"ordinary Player startup retains failed preflight owner");
            environment.Set(enabled);Reject(()=>player.PrepareBeforeGameplay(document,assets));
            Pass(ReferenceEquals(held,Field(player,"_environmentPreflight").GetValue(player)),"Player refuses overwriting its failed startup owner");
            player.Dispose();Pass(held!.Status.Phase==EnvironmentPreflightPhase.Closed&&Field(player,"_environmentPreflight").GetValue(player) is null,"ordinary failed Player startup closes retained service");
        }
        foreach(int route in new[]{0,1,2}) {
            IDisposable host;string ownerField;RendererSession renderer;RenderResourceCache cache;
            if(route==0){var player=new PlayerPresentation(plugins,false);host=player;ownerField="_environmentPreflight";player.Start();}
            else{var editor=new CandidatePresentation(plugins,null,true,false,false);host=editor;ownerField=route==1?"_environmentStartup":"_playEnvironmentStartup";editor.Start();}
            renderer=(RendererSession)Field(host,"_renderer").GetValue(host)!;
            cache=(RenderResourceCache)Field(host,route==0?"_cache":"_sceneCache").GetValue(host)!;
            Check(Field(host,ownerField).GetValue(host) is null,"test must never replace an existing owner");
            // Explicit test-only fault attachment to the actual host's sole owner
            // slot. No production fault hook or reflection-based recovery exists.
            var preflight=new SceneEnvironmentPreflight(renderer,cache);Field(host,ownerField).SetValue(host,preflight);bool pending=false,fired=false;
            try {
                Reject(()=>preflight.Prepare(document.World,assets,document.CaptureSnapshot(),()=>{
                    if(!fired&&renderer.PipelineStats.Pipelines==1){fired=true;renderer.SubmitResources(1,[],new(0,0,64,64),Vector4.Zero,new(Vector3.Zero,Vector3.UnitY,Vector4.Zero));pending=true;return false;}return true;
                }));
                PluginException? failure=null;try{host.Dispose();}catch(PluginException error){failure=error;}
                Pass(fired&&failure is{Result:PluginResult.Busy,Phase:"destroy_scene_pipeline"}&&preflight.Status.Phase==EnvironmentPreflightPhase.Closing,"actual host close reports native Busy at retained preflight");
                Pass(ReferenceEquals(preflight,Field(host,ownerField).GetValue(host))&&ReferenceEquals(renderer,Field(host,"_renderer").GetValue(host))&&renderer.EnvironmentStats.Live==1&&renderer.PipelineStats.Pipelines==1,"actual host retains preflight and downstream dependencies on failed close");
                renderer.Present();pending=false;
                Pass(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"host failure boundary API0/0");
                host.Dispose();Pass(preflight.Status.Phase==EnvironmentPreflightPhase.Closed&&Field(host,ownerField).GetValue(host) is null&&Field(host,"_renderer").GetValue(host) is null,"actual host explicit close retry releases and unloads in order");
            }finally{if(pending)renderer.Present();host.Dispose();}
        }
        File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{cases,actualHosts=true,faultAttachment="test-only owner field",deviceLossRecovery=false,fullStageAccepted=false}));
        Console.WriteLine($"PASS M7.6 environment startup hosts {cases} checks; {output}");
    }
}
