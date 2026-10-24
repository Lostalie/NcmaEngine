using System.Numerics;
using System.Reflection;
using System.Text.Json;
using Ncma.Editor.App;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Player.App;
using Ncma.Rendering;
using Ncma.Rendering.Scene;

internal static unsafe partial class Program
{
    private static void PipelineStartupSafetyTests(string plugins,string output)
    {
        output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);int cases=0;
        void Pass(bool ok,string label){Check(ok,"M7.6 pipeline startup: "+label);cases++;}
        plugins=Path.GetFullPath(plugins);var fixture=new SceneGpuFixture(output);var(authoring,_,_,_)=fixture.Scene();var authoringBytes=authoring.CaptureBytes();var document=BakeRenderScene(authoring);
        var snapshot=document.CaptureSnapshot();var original=document.CaptureCheckpoint().CopyBytes();
        FieldInfo Field(object owner,string name)=>owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)??throw new Exception("Missing owner field "+name);
        using(var loader=new PluginLoader()) {
            loader.Load(plugins,Specs().Take(2).ToArray());
            using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"Pipeline startup safety",64,64,false);
            using var renderer=new RendererSession(loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer),window,64,64);
            using var startup=new ScenePipelineStartup(renderer);int builds=0;ulong frame=1;
            RenderPipeline Factory(float e,float a,bool s){builds++;return new Scene3DPipeline(e,a,shadows:s);}
            void Prepare(Func<bool> allowed)=>startup.Prepare(document.World,snapshot,Factory,null,allowed);
            Pass(!startup.HasRetainedProbe&&Field(renderer,"_publishEnvironment").GetValue(renderer) is null,"creating owner does not query optional GPU environment API");
            startup.PreparePost(document.World,snapshot,null);
            Pass(Field(renderer,"_publishEnvironment").GetValue(renderer) is null,"post Off stays GPU-environment lazy");
            int approvals=0;Task.Run(()=>Reject(()=>Prepare(()=>{approvals++;return true;}))).GetAwaiter().GetResult();
            Pass(approvals==0&&builds==0,"foreign thread rejected before approval/factory");
            Reject(()=>Prepare(()=>false));Pass(builds==0&&!startup.HasRetainedProbe,"denial before factory has no retained GPU work");
            Prepare(()=>true);Pass(builds==2&&renderer.PipelineStats.Pipelines==0&&!startup.HasRetainedProbe,"two real shadow variants prepare and retire without post runtime");
            foreach(bool afterCreate in new[]{false,true})foreach(bool nested in new[]{false,true}) {
                bool called=false;
                Reject(()=>Prepare(()=>{
                    if(!called&&(!afterCreate||renderer.PipelineStats.Pipelines==1)) {
                        called=true;try{if(nested)Prepare(()=>true);else startup.Dispose();}catch(InvalidOperationException){}
                    }return true;
                }));
                Pass(called&&!startup.HasRetainedProbe&&renderer.PipelineStats.Pipelines==0,"caught close/reentry poisons startup including post-create approval");
                Prepare(()=>true);
            }
            renderer.SubmitResources(frame++,[],new(0,0,64,64),Vector4.Zero,new(Vector3.Zero,Vector3.UnitY,Vector4.Zero));
            int before=builds;try{Reject(()=>Prepare(()=>true));}finally{renderer.Present();}
            Pass(before==builds&&!startup.HasRetainedProbe,"active frame rejects before factory");
            bool pending=false,fired=false;AggregateException? failure=null;
            try {
                try{Prepare(()=>{if(!fired&&renderer.PipelineStats.Pipelines==1){fired=true;renderer.SubmitResources(frame++,[],new(0,0,64,64),Vector4.Zero,new(Vector3.Zero,Vector3.UnitY,Vector4.Zero));pending=true;return false;}return true;});}
                catch(AggregateException error){failure=error;}
                Pass(fired&&failure?.InnerExceptions.Count==2&&failure.InnerExceptions[1] is PluginException{Result:PluginResult.Busy,Phase:"destroy_scene_pipeline"}&&startup.HasRetainedProbe,"no-post final approval/retirement double failure retains probe");
                Reject(startup.Dispose);before=builds;Reject(()=>Prepare(()=>true));
                Pass(before==builds&&renderer.PipelineStats.Pipelines==1,"failed close blocks overwrite and preserves exactly one native probe");
            }finally{if(pending)renderer.Present();}
            startup.Dispose();Pass(!startup.HasRetainedProbe&&renderer.PipelineStats.Pipelines==0,"explicit no-post close retry drains retained probe");
            Reject(()=>Prepare(()=>true));
            using var fresh=new ScenePipelineStartup(renderer);fresh.Prepare(document.World,snapshot,Factory,null,()=>true);
            Pass(!fresh.HasRetainedProbe&&renderer.PipelineStats.Pipelines==0&&original.SequenceEqual(document.CaptureCheckpoint().CopyBytes())&&authoringBytes.SequenceEqual(authoring.CaptureBytes())&&renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"fresh owner retains complete validation and immutable World/API0/0");
        }
        foreach(bool editorHost in new[]{false,true}) {
            IDisposable host;
            if(editorHost){var editor=new CandidatePresentation(plugins,null,true,false,false);host=editor;editor.Start();}
            else{var player=new PlayerPresentation(plugins,false);host=player;player.Start();}
            var renderer=(RendererSession)Field(host,"_renderer").GetValue(host)!;
            Check(Field(host,"_pipelineStartup").GetValue(host) is null,"never overwrite actual host owner");
            // Test-only owner attachment. Product shutdown/retry methods are unchanged.
            var startup=new ScenePipelineStartup(renderer);Field(host,"_pipelineStartup").SetValue(host,startup);bool pending=false,fired=false;
            try {
                AggregateException? failure=null;
                try{startup.Prepare(document.World,snapshot,(e,a,s)=>new Scene3DPipeline(e,a,shadows:s),null,()=>{
                    if(!fired&&renderer.PipelineStats.Pipelines==1){fired=true;renderer.SubmitResources(1,[],new(0,0,64,64),Vector4.Zero,new(Vector3.Zero,Vector3.UnitY,Vector4.Zero));pending=true;return false;}return true;});}
                catch(AggregateException error){failure=error;}
                Pass(fired&&failure?.InnerExceptions.Count==2&&startup.HasRetainedProbe,"actual host retains injected failed no-post startup probe");
                PluginException? close=null;try{host.Dispose();}catch(PluginException error){close=error;}
                Pass(close is{Result:PluginResult.Busy,Phase:"destroy_scene_pipeline"}&&ReferenceEquals(startup,Field(host,"_pipelineStartup").GetValue(host))&&ReferenceEquals(renderer,Field(host,"_renderer").GetValue(host)),"host failed close retains startup owner and native dependencies");
                renderer.Present();pending=false;Pass(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"host fault API0/0");
                host.Dispose();Pass(!startup.HasRetainedProbe&&Field(host,"_pipelineStartup").GetValue(host) is null&&Field(host,"_renderer").GetValue(host) is null,"host explicit close retry drains then unloads");
            }finally{if(pending)renderer.Present();host.Dispose();}
        }
        File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{cases,actualGpu=true,hostFaultAttachment="test-only owner field",deviceLossRecovery=false,fullStageAccepted=false}));
        Console.WriteLine($"PASS M7.6 pipeline startup {cases} checks; {output}");
    }
}
