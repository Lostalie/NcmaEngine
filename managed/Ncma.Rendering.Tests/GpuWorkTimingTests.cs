using System.Numerics;
using System.Text.Json;
using Ncma.Assets;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;

internal static unsafe partial class Program
{
    private static int GpuWorkTimingTests(string plugins,string output)
    {
        output=Path.Combine(Path.GetFullPath(output),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
        int checks=0;void Pass(bool ok,string label){Check(ok,"M7.7 work timing: "+label);checks++;}
        GpuWorkTimingSample skinCopy=default,uiCopy=default;long allocated=0;
        using(var loader=new PluginLoader())
        {
            loader.Load(Path.GetFullPath(plugins),Specs().Take(2).ToArray());
            var native=loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer);
            using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"GPU work source",64,64,false);
            using(var renderer=new RendererSession(native,window,64,64))
            {
                var skinEmpty=renderer.ReadGpuWorkTiming(GpuTimingDomain.SkinDispatch);
                var uiEmpty=renderer.ReadGpuWorkTiming(GpuTimingDomain.UiProduction);
                Pass(skinEmpty.Timing.State==GpuSampleState.Unavailable&&uiEmpty.Timing.State==GpuSampleState.Unavailable&&skinEmpty.Resource==0&&uiEmpty.ContentRevision==0,"missing domains report no invented work");
                Reject(()=>renderer.ReadGpuWorkTiming(GpuTimingDomain.MainSubmissionBeforePresent));
                Pass(Task.Run(()=>{try{renderer.ReadGpuWorkTiming(GpuTimingDomain.SkinDispatch);return false;}catch(PluginException e){return e.Result==PluginResult.WrongThread;}}).GetAwaiter().GetResult(),"work sample owner boundary");
                var source=SyntheticModel();var mesh=source.Meshes[0];
                var upload=SkinUploadData.Prepare(new MeshPayload(true,source.Bones.Length,mesh.Vertices,[],mesh.Indices,mesh.TriangleMaterials,mesh.Bindings,mesh.Materials.Length));
                var palette=Enumerable.Repeat(GpuSkinPalette.Create(Matrix4x4.Identity),mesh.Bindings.Length).ToArray();
                using(var gpu=renderer.CreateSkinnedMesh(upload))
                {
                    renderer.UpdateSkins(11,[new(gpu,0)],palette);renderer.WaitIdle();
                    skinCopy=renderer.ReadGpuWorkTiming(GpuTimingDomain.SkinDispatch);
                    Pass(skinCopy.Timing.IsValid&&skinCopy.Timing.OriginFrame==11&&skinCopy.Timing.ObservedFrame==11&&skinCopy.Timing.Sequence==1&&skinCopy.Resource==0&&skinCopy.ContentRevision==0,"actual skin dispatch attributed separately");
                    Pass(renderer.ReadGpuTiming().State==GpuSampleState.Unavailable&&renderer.Stats.SubmittedFrames==0,"skin cost is not a main frame");
                }
                Pass(renderer.ReadGpuWorkTiming(GpuTimingDomain.SkinDispatch).Timing.State==GpuSampleState.Abandoned,"explicit skin owner retirement invalidates live sample");
                var retired=renderer.ReadGpuTimingSnapshot(GpuTimingDomain.SkinDispatch);
                Pass(retired.Current.Timing.State==GpuSampleState.Abandoned&&retired.LastResolved==skinCopy,"skin owner retirement retains copied resolution");
                using(var gpu=renderer.CreateSkinnedMesh(upload))
                {
                    renderer.UpdateSkins(13,[new(gpu,0)],palette);renderer.WaitIdle();
                    var next=renderer.ReadGpuWorkTiming(GpuTimingDomain.SkinDispatch);
                    Pass(next.Timing.IsValid&&next.Timing.Sequence==2&&next.Timing.Generation==skinCopy.Timing.Generation,"skin owner recreation retains per-device monotonic sequence");
                }
                using(var white=renderer.CreateUiImage(1,1,[255,255,255,255]))
                {
                    var builder=new UiDisplayListBuilder(renderer);
                    builder.Quad(new(Guid.NewGuid(),new(0,0,24,24),Matrix3x2.Identity,new(0,0,32,32),1,true,-1),white,new(1,0,0,1));
                    using var list=builder.Build();
                    using(var a=renderer.CreateUiTarget(32,32))
                    using(var b=renderer.CreateUiTarget(32,32))
                    {
                        renderer.SubmitUiTarget(a,list,21,3,Vector4.Zero);renderer.WaitIdle();
                        var first=renderer.ReadGpuWorkTiming(GpuTimingDomain.UiProduction);var pixels=renderer.CaptureUiTarget(a);
                        renderer.SubmitUiTarget(b,list,21,7,Vector4.Zero);renderer.WaitIdle();uiCopy=renderer.ReadGpuWorkTiming(GpuTimingDomain.UiProduction);
                        Pass(first.Timing.IsValid&&uiCopy.Timing.IsValid&&first.Timing.OriginFrame==21&&uiCopy.Timing.OriginFrame==21&&uiCopy.Timing.Sequence==2&&first.Resource!=0&&uiCopy.Resource!=0&&first.Resource!=uiCopy.Resource&&first.ContentRevision==3&&uiCopy.ContentRevision==7,"same-frame UI productions retain target and exact content identity");
                        Pass(renderer.ReadGpuTimingSnapshot(GpuTimingDomain.UiProduction).LastResolved==uiCopy,"UI snapshot preserves exact source metadata");
                        Pass(renderer.CaptureUiTarget(b).SequenceEqual(pixels)&&pixels[(8*32+8)*4]==255,"actual timed target pixels unchanged");
                        using(var lease=renderer.AcquireUiPresentation(b,7,23))
                            Pass(renderer.ReadGpuWorkTiming(GpuTimingDomain.UiProduction)==uiCopy,"cached lease acquisition does not pretend to produce new work");
                        renderer.SubmitUi(list,25,Vector4.Zero);renderer.Present();renderer.WaitIdle();
                        Pass(renderer.ReadGpuTiming() is{State:GpuSampleState.Valid,OriginFrame:25}&&renderer.ReadGpuWorkTiming(GpuTimingDomain.UiProduction)==uiCopy,"separate main/UI scope attribution");
                        for(int i=0;i<32;i++){renderer.ReadGpuWorkTiming(GpuTimingDomain.UiProduction);renderer.ReadGpuWorkTiming(GpuTimingDomain.SkinDispatch);}
                        long before=GC.GetAllocatedBytesForCurrentThread();
                        for(int i=0;i<128;i++){renderer.ReadGpuWorkTiming(GpuTimingDomain.UiProduction);renderer.ReadGpuWorkTiming(GpuTimingDomain.SkinDispatch);}
                        allocated=GC.GetAllocatedBytesForCurrentThread()-before;Pass(allocated==0,"256 warmed work timing reads allocate zero on owner thread");
                        for(int i=0;i<32;i++){renderer.ReadGpuTimingSnapshot(GpuTimingDomain.UiProduction);renderer.ReadGpuTimingSnapshot(GpuTimingDomain.SkinDispatch);}
                        long snapshotBefore=GC.GetAllocatedBytesForCurrentThread();
                        for(int i=0;i<128;i++){renderer.ReadGpuTimingSnapshot(GpuTimingDomain.UiProduction);renderer.ReadGpuTimingSnapshot(GpuTimingDomain.SkinDispatch);}
                        Pass(GC.GetAllocatedBytesForCurrentThread()==snapshotBefore,"256 warmed work snapshots allocate zero on owner thread");
                    }
                    Pass(renderer.ReadGpuWorkTiming(GpuTimingDomain.UiProduction).Timing.State==GpuSampleState.Abandoned,"last UI target retires sample owner");
                    using var again=renderer.CreateUiTarget(32,32);renderer.SubmitUiTarget(again,list,29,1,Vector4.Zero);renderer.WaitIdle();
                    var recreated=renderer.ReadGpuWorkTiming(GpuTimingDomain.UiProduction);
                    Pass(recreated.Timing.IsValid&&recreated.Timing.Sequence==3&&recreated.Resource!=uiCopy.Resource&&recreated.ContentRevision==1,"new UI owner preserves sequence but changes resource identity");
                }
                Pass(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"actual work timing API0/0");
            }
            using(var pure=new RendererSession(native,window,64,64,pureUi:true))
            {
                var sample=pure.ReadGpuWorkTiming(GpuTimingDomain.SkinDispatch);
                Pass(sample.Timing.Generation!=skinCopy.Timing.Generation&&sample.Timing.Sequence==0&&sample.Timing.State==GpuSampleState.Unavailable&&pure.ReadGpuWorkTiming(GpuTimingDomain.UiProduction).Timing.State==GpuSampleState.Unavailable,"new pure UI renderer does not inherit/initialize workloads");
                Pass(pure.SkinStats.Meshes==0&&pure.Stats.LiveGroups==0&&pure.UiTargetStats.Targets==0,"query did not create skin/3D/offscreen resources");
            }
            Pass(native.Status.LiveResources==0,"all work owners released");
        }
        Pass(skinCopy.Timing.OriginFrame==11&&uiCopy.ContentRevision==7,"copied observations survive DLL unload");
        File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{checks,skinCopy,uiCopy,allocated,wholeFrameTiming=false,fullPerformanceAccepted=false}));
        Console.WriteLine($"PASS M7.7 work timing {checks} checks; {output}");return 0;
    }
}
