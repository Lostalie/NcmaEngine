using System.Diagnostics;
using Ncma.Rendering;
using Ncma.Rendering.Scene.Examples;

internal static unsafe partial class Program
{
    // Test-only wrapping of the SAME public graph contracts. Even a full custom graph
    // must declare the exact High pixel requirement, not rely on a selected name alone.
    private sealed class CostJointRequiredPixel(RenderPipeline source,ShaderDescriptor pixel):RenderPipeline
    {public override RenderGraph Build(uint width,uint height){var graph=source.Build(width,height);graph.RequireSceneGeometryPixel(pixel);return graph;}}

    private static RenderPipeline CostJointPipeline(SceneShadowPreset preset,int mode,float exposure)
    {
        if(mode is 0 or 1)return preset.CreatePipeline(true,exposure,0,
            geometry:mode==1?new ExampleAmbientStage(0):null,profile:ShaderProfile.SceneEnvironment);
        RenderPipeline full=ShadowJointPipelines.Create(2,exposure,0,preset.Enabled,preset.Budget.Resolution);
        return preset.RequiresHighShader?new CostJointRequiredPixel(full,SceneShadowShaders.CreateHighPixel(ShaderProfile.SceneEnvironment)):full;
    }

    private readonly record struct JointCostFrame(bool Submitted,PresentationCostReport Report,
        ulong GeometryDraws,ulong ShadowDraws);

    private static object MeasureSceneJointCosts(RendererSession renderer,Func<ulong,bool> draw,
        string label,int width,int height,int mode,SceneShadowPreset preset,int geometry,int casters,
        int skinRequests,int palettes,ref ulong frame)
    {
        var observer=new PresentationCostCapture(renderer);
        JointCostFrame Record(ulong f){var before=renderer.PipelineStats;observer.Begin(f);bool submitted;
            try{submitted=draw(f);}catch{observer.Abort();throw;}
            var report=observer.End();var after=renderer.PipelineStats;
            return new(submitted,report,after.GeometryDraws-before.GeometryDraws,after.ShadowDraws-before.ShadowDraws);}
        for(int i=0;i<8;i++)Record(frame++);
        // One explicit diagnostic drain BEFORE the interval; no Wait/Flush/readback,
        // shader preparation, retries, JSON, resource creation or image oracle inside.
        renderer.WaitIdle();var samples=new JointCostFrame[32];ulong first=frame;
        var resources=renderer.ResourceStats;var skin=renderer.SkinStats;var ui=renderer.UiStats;
        var post=renderer.PostStats;var environment=renderer.EnvironmentStats;var pipeline=renderer.PipelineStats;
        long allocated=GC.GetAllocatedBytesForCurrentThread(),started=Stopwatch.GetTimestamp();
        for(int i=0;i<samples.Length;i++)samples[i]=Record(frame++);
        double elapsed=Stopwatch.GetElapsedTime(started).TotalMilliseconds;allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
        Check(allocated==0,"M7.7 complete warm Skin/scene/Bloom/UI/Present plus observation loop allocated");
        Check(samples.Any(s=>s.Submitted),"M7.7 no joint frame submitted");
        foreach(var sample in samples){
            var report=sample.Report;var c=report.Counters??throw new Exception("Missing completed counters");ulong submitted=sample.Submitted?1UL:0;
            Check(report.Completed&&!report.IncludesDiagnosticCapture&&report.OwnerThreadAllocatedBytes==0,"M7.7 measured scope incorrectly labeled");
            Check(c.MainSubmissions==submitted&&c.Presents==submitted&&c.UiDrawSubmissions==submitted&&c.UiProductions==0,
                "M7.7 exact main/Present/direct HUD batches, busy never fabricates submission");
            Check(c.SkinBatches==(skinRequests==0?0:submitted)&&c.PaletteBytes==(skinRequests==0?0:submitted*(ulong)palettes*128),"M7.7 exact skin batch/palette bytes");
            Check(sample.GeometryDraws==submitted*(ulong)geometry&&sample.ShadowDraws==submitted*(ulong)casters,"M7.7 exact geometry/shadow submissions");
            Check(c.UiUploadedBytes==0&&c.MeshUploadedBytes==0&&c.ResourceUploadedBytes==0&&c.EnvironmentUploadedBytes==0,
                "M7.7 immutable assets/HUD not reuploaded");
            Check(report.Ui?.Attribution==CostSampleAttribution.NoValidSample,"M7.7 direct HUD has no imaginary offscreen production cost");
            foreach(var timing in new[]{report.Main,report.Skin}){
                if(timing is not {} t)throw new Exception("Missing timing observation");
                Check(t.Sample.Timing.Generation==report.Generation&&t.Sample.Timing.OriginFrame<=report.HostFrame,"M7.7 timing device/origin");
                if(t.Attribution==CostSampleAttribution.CurrentSourceFrame)Check(sample.Submitted,"M7.7 busy frame incorrectly owns valid GPU work");
                if(!t.Sample.Timing.IsValid)Check(t.Sample.Timing.Milliseconds==0,"M7.7 invalid timing not zero");
            }
        }
        Check(renderer.ResourceStats.ResidentBytes==resources.ResidentBytes&&renderer.SkinStats.ResidentBytes==skin.ResidentBytes&&
            renderer.UiStats.ResidentBytes==ui.ResidentBytes&&renderer.PostStats.ResidentBytes==post.ResidentBytes&&
            renderer.PipelineStats.ResidentBytes==pipeline.ResidentBytes&&renderer.EnvironmentStats.Live==environment.Live&&
            renderer.PostStats.Publications==post.Publications,"M7.7 warm resources/publications grow");
        return new{label,width,height,mode,quality=preset.Quality.ToString(),firstFrame=first,lastFrame=frame-1,
            elapsedMilliseconds=elapsed,ownerThreadAllocatedBytes=allocated,attempts=samples.Length,submitted=samples.Count(s=>s.Submitted),
            scope="precomputed pose/palette Skin upload+dispatch, scene, Bloom, direct HUD, Present; excludes animation sampling, gameplay, startup, input and preparation",
            gpuScope="separate elapsed intervals; not additive; a sample outside [firstFrame,lastFrame] is earlier warm/diagnostic work, not this measured workload",
            samples=samples.Select(s=>new{s.Submitted,s.Report,s.GeometryDraws,s.ShadowDraws,
                mainSourceInMeasurement=s.Report.Main is {} m&&m.Sample.Timing.IsValid&&m.Sample.Timing.OriginFrame>=first,
                skinSourceInMeasurement=s.Report.Skin is {} k&&k.Sample.Timing.IsValid&&k.Sample.Timing.OriginFrame>=first}).ToArray(),
            fullPerformanceAccepted=false,relocatedProductsAccepted=false};
    }
}
