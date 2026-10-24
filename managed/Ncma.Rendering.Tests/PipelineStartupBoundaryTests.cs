using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Scene;
using Ncma.Scene.Rendering;

internal static unsafe partial class Program
{
    private static int PipelineStartupBoundaryTests(string plugins)
    {
        int checks=0;void Pass(bool ok,string label){Check(ok,"D3-A boundary "+label);checks++;}void Bad(Action action){Reject(action);checks++;}
        using var loader=new PluginLoader();loader.Load(Path.GetFullPath(plugins),Specs().Take(2).ToArray());
        using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"Pipeline boundary",64,64,false);
        using var renderer=new RendererSession(loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer),window,64,64);
        var authoring=new SceneDocument("Boundary",RenderComponentRegistry.Register(RenderConfiguration.CreateRegistry()),SceneRenderValidation.RequireComposition);
        var authoringBytes=authoring.CaptureBytes();var document=BakeRenderScene(authoring);
        bool approved=true;int calls=0;Action? callback=null;bool inBuild=false;
        Func<float,float,bool,RenderPipeline>? bound=null;
        bound=ScenePipelineStartup.Bind(renderer,document.World,(e,a,s)=>{
            calls++;if(!inBuild)callback?.Invoke();return new StartupCallbackPipeline((w,h)=>{if(inBuild)callback?.Invoke();return new Scene3DPipeline(e,a,shadows:s).Build(w,h);});},()=>approved)!;
        void Build()=>bound(1,.03f,false).Build(16,16).Compile(RenderCapabilities.SceneDx11);
        Build();Pass(calls==1,"exact committed callback");
        foreach(bool build in new[]{false,true}){
            inBuild=build;
            foreach(Action action in new Action[]{()=>document.World.CreateEntity("illegal"),()=>renderer.Dispose(),Build}){
                callback=action;Bad(Build);callback=null;Build();Pass(document.World.Count==0,"factory and Build failure atomic World/renderer/reentry");
            }
        }
        callback=()=>approved=false;Bad(Build);callback=null;approved=true;Build();
        approved=false;int before=calls;Bad(Build);Pass(before==calls,"revoked before callback");approved=true;
        Exception? wrongOwner=null;var thread=new Thread(()=>{try{Build();}catch(Exception e){wrongOwner=e;}});thread.Start();thread.Join();Pass(wrongOwner is InvalidOperationException,"wrong owner rejected");
        using(var clear=new RenderPipelineService(renderer)){
            clear.Configure(RenderConfiguration.Default(Guid.NewGuid()) with{PipelineType="ncma.clear.v1"},64,64);clear.Submit(1);before=calls;Bad(Build);Pass(calls==before,"active renderer frame rejects before callback");renderer.Present();
        }
        using var startup=new ScenePipelineStartup(renderer);
        before=calls;startup.Prepare(document.World,document.CaptureSnapshot(),bound,null,()=>true);
        Pass(before==calls&&renderer.PipelineStats.Pipelines==0&&renderer.EnvironmentStats.Live==0&&renderer.PostStats.Bindings==0,"empty World never initializes custom scene resources");
        document.RestoreCheckpoint(document.CaptureCheckpoint(),document.Revision);Bad(Build);Pass(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"boundary API0/0");Pass(authoringBytes.SequenceEqual(authoring.CaptureBytes()),"runtime boundary and restore leave authoring unchanged");return checks;
    }
    private sealed class StartupCallbackPipeline(Func<uint,uint,RenderGraph> build):RenderPipeline {public override RenderGraph Build(uint w,uint h)=>build(w,h);}
}
