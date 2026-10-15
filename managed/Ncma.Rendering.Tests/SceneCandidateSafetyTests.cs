using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Assets;
using System.Numerics;
using System.Text.Json;

internal static unsafe partial class Program
{
    private sealed class CandidateCallbackPipeline(Action callback, RenderPipeline? inner=null) : RenderPipeline
    {
        public override RenderGraph Build(uint width,uint height) { callback();return (inner??new Scene3DPipeline(shadows:false)).Build(width,height); }
    }
    private sealed class WrongSizeCandidatePipeline : RenderPipeline
    { public override RenderGraph Build(uint width,uint height)=>new Scene3DPipeline(shadows:false).Build(width+1,height); }
    private static int SceneCandidateSafetyTests(string plugins,string output)
    {
        output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
        using var loader=new PluginLoader();loader.Load(Path.GetFullPath(plugins),Specs().Take(2).ToArray());
        using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"Scene candidate safety",64,64,false);
        using var renderer=new RendererSession(loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer),window,64,64);
        using var scene=new ScenePipelineSession(renderer,new Scene3DPipeline(shadows:false),64,64);
        int cases=0;void Pass(bool ok,string label){Check(ok,"M7.6 scene candidate: "+label);cases++;}
        static void Swallow(Action operation){try{operation();}catch(InvalidOperationException){}}
        using var target=renderer.CreateViewTarget(64,64);ulong frame=1;
        var lighting=new ResourceLighting(Vector3.Zero,Vector3.UnitZ,Vector4.One);Vector4 clear=new(.125f,.25f,.5f,1);
        byte[] Draw(ScenePipelineSession selected){byte[] pixels=new byte[64*64*4];selected.Submit(frame++,[],[],lighting,Matrix4x4.Identity,new(),target,clear:clear);renderer.CaptureTarget(target,pixels);renderer.Present();return pixels;}
        var originalPlan=scene.Plan;var original=Draw(scene);ulong creates=renderer.PipelineStats.Creates;
        Reject(()=>scene.Configure(new CandidateCallbackPipeline(scene.Dispose)));
        Pass(ReferenceEquals(scene.Plan,originalPlan)&&original.SequenceEqual(Draw(scene)),"self-close in graph Build refused before installed state changes");
        foreach(Action mutation in new Action[]{scene.Dispose,()=>scene.Configure(new Scene3DPipeline(2,shadows:false)),
            ()=>scene.Submit(frame++,[],[],lighting,Matrix4x4.Identity,new(),target),
            ()=>scene.ConfigurePostProcess(null,null,()=>true),()=>scene.ReplaceTone(null!),()=>scene.ReplaceShaders(null!),
            ()=>scene.ReplaceEnvironmentShaders(null!),()=>scene.ConfigureEnvironment(null,EnvironmentLightingConfiguration.Off,()=>true)}) {
            Reject(()=>scene.Configure(new CandidateCallbackPipeline(()=>Swallow(mutation))));
            Pass(ReferenceEquals(scene.Plan,originalPlan)&&original.SequenceEqual(Draw(scene)),"caught scene mutation poisons graph candidate and preserves exact GPU image");
        }
        Reject(()=>scene.Configure(new CandidateCallbackPipeline(()=>throw new InvalidOperationException("fixture graph failure"))));
        scene.Configure(new Scene3DPipeline(2,shadows:false));
        Pass(!original.SequenceEqual(Draw(scene)),"explicit valid retry installs requested exposure");
        scene.Configure(new Scene3DPipeline(shadows:false));
        Pass(original.SequenceEqual(Draw(scene)),"valid retry restores original image");
        originalPlan=scene.Plan;
        Reject(()=>scene.Configure(new WrongSizeCandidatePipeline()));
        Reject(()=>{using var invalid=new ScenePipelineSession(renderer,new WrongSizeCandidatePipeline(),64,64);});
        Reject(()=>scene.Configure(new Scene3DPipeline(shadows:true,shadowResolution:256)));
        Pass(ReferenceEquals(scene.Plan,originalPlan)&&renderer.PipelineStats.Creates==creates,"target/shadow allocation mismatch never publishes or replaces graph");
        using(var replacement=new ScenePipelineSession(renderer,new Scene3DPipeline(shadows:true,shadowResolution:256),64,64))
            Pass(replacement.Shadows&&original.SequenceEqual(Draw(replacement)),"allocation-changing graph is allowed as a separate complete candidate");
        int builds=0;
        Task.Run(()=>Reject(()=>{using var invalid=new ScenePipelineSession(renderer,new CandidateCallbackPipeline(()=>builds++),64,64);})).GetAwaiter().GetResult();
        Pass(builds==0,"constructor checks renderer owner before calling Build");
        scene.Configure(new CandidateCallbackPipeline(()=>Task.Run(()=>Reject(scene.Dispose)).GetAwaiter().GetResult()));
        Pass(original.SequenceEqual(Draw(scene)),"foreign-thread refusal does not poison owner preparation state");
        scene.Submit(frame++,[],[],lighting,Matrix4x4.Identity,new(),target);
        try { Reject(()=>scene.Configure(new CandidateCallbackPipeline(()=>builds++)));Pass(builds==0,"active frame rejects graph preparation before user callback"); }
        finally{renderer.Present();}

        // Each program approval is trusted C#, but cannot mutate the target session
        // while native bindings are being prepared, even if it catches the refusal.
        Action? approvalMutation=null;bool Approve(){if(approvalMutation is {} action)Swallow(action);return true;}
        var catalog=DefaultSceneTone.CopyCatalog(renderer);var rows=catalog.CopyPage(0);
        var vertex=catalog.Require(DefaultSceneTone.VertexId,rows.Single(r=>r.Stage==ShaderStage.Vertex).ContentHash);
        var pixel=catalog.Require(DefaultSceneTone.PixelId,rows.Single(r=>r.Stage==ShaderStage.Pixel).ContentHash);
        var tone=RegisteredSceneTone.Prepare(renderer,catalog,vertex,pixel,Approve);
        var stageCatalog=DefaultSceneShaders.CopyCatalog(renderer,false);
        var stages=RegisteredSceneShaders.Prepare(renderer,stageCatalog,DefaultSceneShaders.Select(stageCatalog,false),Approve);
        scene.ReplaceTone(tone);var beforeTone=Draw(scene);
        foreach(Action mutation in new Action[]{scene.Dispose,()=>scene.Configure(new Scene3DPipeline(2,shadows:false)),()=>scene.ReplaceTone(tone)}) {
            approvalMutation=mutation;Reject(()=>scene.ReplaceTone(tone));approvalMutation=null;
            Pass(ReferenceEquals(scene.RegisteredTone,tone)&&beforeTone.SequenceEqual(Draw(scene)),"Tone approval caught reentry preserves installed bytecode and graph");
            approvalMutation=mutation;Reject(()=>scene.ReplaceShaders(stages));approvalMutation=null;
            Pass(ReferenceEquals(scene.RegisteredTone,tone)&&scene.RegisteredShaders is null&&beforeTone.SequenceEqual(Draw(scene)),"complete shader group approval preserves old registered contract");
        }
        scene.ReplaceShaders(stages);Pass(ReferenceEquals(scene.RegisteredShaders,stages)&&beforeTone.SequenceEqual(Draw(scene)),"valid group replacement succeeds after refusal");
        var environmentCatalog=DefaultEnvironmentSceneShaders.CopyCatalog(renderer,false);
        var environmentShaders=RegisteredEnvironmentSceneShaders.Prepare(renderer,environmentCatalog,DefaultEnvironmentSceneShaders.Select(environmentCatalog,false),Approve);
        using(var environmentScene=new ScenePipelineSession(renderer,new Scene3DPipeline(shadows:false),64,64,environmentShaders:environmentShaders)) {
            var baseline=Draw(environmentScene);
            approvalMutation=environmentScene.Dispose;Reject(()=>environmentScene.ReplaceEnvironmentShaders(environmentShaders));approvalMutation=null;
            Pass(ReferenceEquals(environmentScene.RegisteredEnvironmentShaders,environmentShaders)&&baseline.SequenceEqual(Draw(environmentScene)),"environment shader reentry preserves exact old output");
            Reject(()=>environmentScene.ConfigureEnvironment(null,EnvironmentLightingConfiguration.Off,()=>{Swallow(environmentScene.Dispose);return true;}));
            Pass(environmentScene.EnvironmentLighting==EnvironmentLightingConfiguration.Off&&baseline.SequenceEqual(Draw(environmentScene)),"environment binding approval cannot close target scene");
            environmentScene.ConfigureEnvironment(null,EnvironmentLightingConfiguration.Off,()=>true);
            Pass(baseline.SequenceEqual(Draw(environmentScene)),"environment binding recovers by explicit approved retry");
        }
        var bloom=BloomFeature.CreatePrograms();var package=PostProcessPackage.Cook(renderer,bloom.Catalog,bloom.CopyPrograms(),()=>true);
        var postPrograms=PreparedPostProcessPrograms.Prepare(renderer,package,Approve);
        var post=BloomFeature.Build(64,64,new BloomSettings(Levels:2,Threshold:0,Intensity:1),package)!;
        scene.ConfigurePostProcess(post,postPrograms,()=>true);var postImage=Draw(scene);
        Pass(!postImage.SequenceEqual(beforeTone),"actual post targets change reference image");
        foreach(Action mutation in new Action[]{scene.Dispose,()=>scene.Configure(new Scene3DPipeline(2,shadows:false)),()=>scene.ConfigurePostProcess(null,null,()=>true)}) {
            var stats=renderer.PostStats;
            Reject(()=>scene.ConfigurePostProcess(null,null,()=>{Swallow(mutation);return true;}));
            Pass(ReferenceEquals(scene.PostProcess,post)&&stats.Publications==renderer.PostStats.Publications&&postImage.SequenceEqual(Draw(scene)),"post disable approval cannot bypass guard or change old targets");
            approvalMutation=mutation;Reject(()=>scene.ConfigurePostProcess(post,postPrograms,()=>true));approvalMutation=null;
            Pass(ReferenceEquals(scene.PostProcess,post)&&stats.Publications==renderer.PostStats.Publications&&postImage.SequenceEqual(Draw(scene)),"post program approval cannot publish after caught target mutation");
        }
        scene.ConfigurePostProcess(null,null,()=>true);Pass(beforeTone.SequenceEqual(Draw(scene)),"explicit approved post disable restores original GPU image");
        scene.Dispose();target.Dispose();
        Pass(renderer.PipelineStats.Pipelines==0&&renderer.PostStats.Bindings==0&&renderer.PostStats.ResidentBytes==0&&renderer.ResourceStats.Targets==0&&renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"all scene/post/target resources drained; API0/0");
        File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{cases,apiErrors=0,apiWarnings=0,deviceLossRecovery=false,fullStageAccepted=false}));
        Console.WriteLine($"PASS M7.6 scene candidate {cases} checks; API0/0; {output}");return 0;
    }
}
