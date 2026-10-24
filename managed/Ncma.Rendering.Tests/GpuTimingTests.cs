using System.Numerics;
using System.Text.Json;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;

internal static unsafe partial class Program
{
    private static int GpuTimingTests(string plugins,string output)
    {
        output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
        int checks=0;void Pass(bool ok,string label){Check(ok,"M7.7 timing: "+label);checks++;}
        GpuTimingSample copied=default;long warmAllocation=0;
        using(var loader=new PluginLoader())
        {
            loader.Load(Path.GetFullPath(plugins),Specs().Take(2).ToArray());
            var native=loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer);
            using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"GPU source frame",64,64,false);
            using(var renderer=new RendererSession(native,window,64,64,pureUi:true))
            {
                var empty=renderer.ReadGpuTiming();
                Pass(empty.State==GpuSampleState.Unavailable&&empty.Sequence==0&&empty.OriginFrame==0&&empty.Milliseconds==0&&empty.Generation==renderer.ReadFrameState().Generation,"fresh sample is not a zero-cost result");
                foreach(var domain in Enum.GetValues<GpuTimingDomain>())
                {
                    var snapshot=renderer.ReadGpuTimingSnapshot(domain);
                    Pass(snapshot.Current.Timing.State==GpuSampleState.Unavailable&&snapshot.LastResolved.Timing.State==GpuSampleState.Unavailable&&snapshot.Current.Timing.Generation==empty.Generation&&snapshot.LastResolved.Timing.Domain==domain,"fresh bounded snapshot");
                }
                Reject(()=>renderer.ReadGpuTimingSnapshot((GpuTimingDomain)0));
                Reject(()=>renderer.ReadGpuTimingSnapshot((GpuTimingDomain)4));
                Pass(Task.Run(()=>{try{renderer.ReadGpuTimingSnapshot(GpuTimingDomain.MainSubmissionBeforePresent);return false;}catch(PluginException e){return e.Result==PluginResult.WrongThread;}}).GetAwaiter().GetResult(),"snapshot owner guard");
                Pass(Task.Run(()=>{try{renderer.ReadGpuTiming();return false;}catch(PluginException e){return e.Result==PluginResult.WrongThread;}}).GetAwaiter().GetResult(),"native owner guard");
                var graph=new ClearPipeline(.2f,.3f,.4f).Build(64,64).Compile(renderer.Capabilities);
                renderer.Submit(graph,null,RenderFrame.Reference(41,64,64));
                var recording=renderer.ReadGpuTiming();
                Pass(recording.State==GpuSampleState.Recording&&recording.Sequence==1&&recording.OriginFrame==41&&recording.Milliseconds==0,"recording has identity, not fabricated duration");
                renderer.Capture(new byte[64*64*4]);renderer.Present();renderer.WaitIdle();
                var first=renderer.ReadGpuTiming();
                Pass(first.IsValid&&first.OriginFrame==41&&first.Sequence==1&&first.ObservedFrame==41&&first.Domain==GpuTimingDomain.MainSubmissionBeforePresent,"actual completed main submission sample");
                Pass(renderer.ReadGpuTiming()==first,"repeat reads do not invent another sample");
                Pass(renderer.ReadGpuTimingSnapshot(GpuTimingDomain.MainSubmissionBeforePresent)==new GpuTimingSnapshot(new(first,0,0),new(first,0,0)),"resolved snapshot copied twice with same identity");
                using(var white=renderer.CreateUiImage(1,1,[255,255,255,255]))
                {
                    var builder=new UiDisplayListBuilder(renderer);
                    builder.Quad(new(Guid.NewGuid(),new(0,0,20,20),Matrix3x2.Identity,new(0,0,64,64),1,true,-1),white,new(1,0,0,1));
                    using var list=builder.Build();
                    renderer.SubmitUi(list,57,Vector4.Zero);
                    Pass(renderer.ReadGpuTiming() is{State:GpuSampleState.Recording,OriginFrame:57,Sequence:2},"pure UI starts its own main sample without 3D");
                    var retained=renderer.ReadGpuTimingSnapshot(GpuTimingDomain.MainSubmissionBeforePresent);
                    Pass(retained.Current.Timing.State==GpuSampleState.Recording&&retained.LastResolved.Timing==(first with{ObservedFrame=57}),"new main sample retains prior completed source with current read horizon");
                    renderer.Present();renderer.WaitIdle();copied=renderer.ReadGpuTiming();
                    Pass(copied.IsValid&&copied.OriginFrame==57&&copied.Sequence==2&&copied.Generation==first.Generation,"actual pure UI query resolves");
                    var before=renderer.UiStats;
                    for(int i=0;i<32;i++)renderer.ReadGpuTiming();
                    long allocation=GC.GetAllocatedBytesForCurrentThread();
                    for(int i=0;i<128;i++)renderer.ReadGpuTiming();
                    warmAllocation=GC.GetAllocatedBytesForCurrentThread()-allocation;
                    Pass(warmAllocation==0,"128 warmed read-only samples allocate zero on owner thread");
                    for(int i=0;i<32;i++)renderer.ReadGpuTimingSnapshot(GpuTimingDomain.MainSubmissionBeforePresent);
                    long snapshotBefore=GC.GetAllocatedBytesForCurrentThread();
                    for(int i=0;i<128;i++)renderer.ReadGpuTimingSnapshot(GpuTimingDomain.MainSubmissionBeforePresent);
                    Pass(GC.GetAllocatedBytesForCurrentThread()==snapshotBefore,"128 warmed snapshot reads allocate zero on owner thread");
                    Pass(renderer.UiStats.UploadedBytes==before.UploadedBytes&&renderer.UiStats.ResidentBytes==before.ResidentBytes&&renderer.Stats.SubmittedFrames==2,"polling does not upload/draw/grow resources");
                    renderer.Submit(graph,null,RenderFrame.Reference(65,64,64));
                    renderer.SubmitUi(list,65,Vector4.Zero,overlay:true);
                    Pass(renderer.ReadGpuTiming() is{State:GpuSampleState.Recording,OriginFrame:65,Sequence:3},"overlay stays in original main sample");
                    renderer.AbandonFrameForShutdown(65);
                    Pass(renderer.ReadGpuTiming() is{State:GpuSampleState.Abandoned,OriginFrame:65,Sequence:3,Milliseconds:0},"explicit abandon invalidates cost");
                    var abandoned=renderer.ReadGpuTimingSnapshot(GpuTimingDomain.MainSubmissionBeforePresent);
                    Pass(abandoned.Current.Timing.State==GpuSampleState.Abandoned&&abandoned.LastResolved.Timing.OriginFrame==57&&abandoned.LastResolved.Timing.IsValid,"abandon visible beside previous historical GPU result");
                }
                Pass(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"healthy actual DX11 API0/0");
                renderer.Dispose();Reject(()=>renderer.ReadGpuTiming());
                Reject(()=>renderer.ReadGpuTimingSnapshot(GpuTimingDomain.MainSubmissionBeforePresent));
            }
            using(var fresh=new RendererSession(native,window,64,64,pureUi:true))
            {
                var sample=fresh.ReadGpuTiming();
                Pass(sample.Generation!=copied.Generation&&sample.State==GpuSampleState.Unavailable&&sample.Sequence==0&&copied.IsValid,"new device and copied old result distinguished");
                Pass(fresh.ReadGpuTimingSnapshot(GpuTimingDomain.MainSubmissionBeforePresent).LastResolved.Timing.State==GpuSampleState.Unavailable,"new device clears resolution cache");
            }
            Pass(native.Status.LiveResources==0,"native renderer resources drained");
        }
        Pass(copied.IsValid&&copied.OriginFrame==57,"copy survives plugin unload");
        File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{checks,copied,warmAllocation,wholeFrameTiming=false,fullPerformanceAccepted=false}));
        Console.WriteLine($"PASS M7.7 timing {checks} checks; {output}");return 0;
    }
}
