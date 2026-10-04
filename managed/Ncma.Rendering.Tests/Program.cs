using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Ncma.Gui;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Scene;
internal static unsafe class Program
{
    private static long _cachedThreadBytes, _cachedProcessBytes;
    private static double _cachedElapsedMs;
    static void Check(bool value,string message) { if(!value) throw new Exception(message); }
    static void Reject(Action action,string? code=null)
    {
        try {action();} catch(RenderGraphException e) { Check(code is null || e.Code==code,"Unexpected diagnostic "+e.Code);return; }
        catch(Exception e) when(code is null && e is ArgumentException or InvalidOperationException or PluginException) {return;}
        throw new Exception("Expected rejection "+code);
    }
    static PluginSpecification[] Specs() => [
        new("platform",ModuleKind.Platform,"NcmaPlatform.dll","NcmaPlatform.dll",1,0,[]),
        new("renderer",ModuleKind.Renderer,"NcmaRenderer.dll","NcmaRenderer.dll",1,0,["platform"]),
        new("gui",ModuleKind.Gui,"NcmaGui.dll","NcmaGui.dll",1,0,["platform","renderer"])];
    static void ExportBmp(string path,byte[] pixels,uint width,uint height)
    {
        using var file=new BinaryWriter(File.Create(path));
        file.Write((ushort)0x4d42);file.Write(54+pixels.Length);file.Write(0);file.Write(54);file.Write(40);
        file.Write(width);file.Write(-(int)height);file.Write((ushort)1);file.Write((ushort)32);file.Write(0);
        file.Write(pixels.Length);file.Write(2835);file.Write(2835);file.Write(0);file.Write(0);
        for(int i=0;i<pixels.Length;i+=4){file.Write(pixels[i+2]);file.Write(pixels[i+1]);file.Write(pixels[i]);file.Write(pixels[i+3]);}
    }
    static void TestConfiguration(RendererSession renderer,ref ulong frame,byte[] baseline,byte[] image)
    {
        using var owner=new EditorSessionOwner("Rendering commands",components:RenderConfiguration.CreateRegistry());
        var edit=owner.Edit!; Guid objectId=Guid.NewGuid(),configId=Guid.NewGuid();
        var permission=new CapabilityPermissions(["ncma.scene.transaction","ncma.history.undo","ncma.history.redo"],objectScope:[objectId],createScope:[objectId],componentScope:[RenderConfiguration.ComponentType],allowDocumentHistory:false);
        CapabilityRequest Request(string capability,JsonElement input)=>new(EditSession.ContractVersion,Guid.NewGuid(),edit.SessionId,edit.Revision,capability,input);
        var create=Request("ncma.scene.transaction",JsonSerializer.SerializeToElement(new{operations=new[]{new{op="create",objectId,name="Rendering settings"}}}));
        Check(edit.Invoke(create,permission).Changed,"Create configuration object via Core.");
        var config=RenderConfiguration.Default(configId);var settings=RenderingEditorAdapter.SettingsRequest(edit,objectId,config,edit.Revision);
        Check(edit.Invoke(settings).Status=="denied","Agent defaults read-only.");
        Check(edit.Invoke(settings,permission).Changed,"Configure via Core.");
        byte[] saved=owner.Document.CaptureBytes();
        var copy=new SceneDocument("Copy",RenderConfiguration.CreateRegistry());copy.RestoreBytes(saved);
        Check(copy.World.FindObject(objectId).Get<RenderConfiguration>()==config,"Persisted configuration value/UUID.");
        using var service=new RenderPipelineService(renderer);service.Configure(config,256,256);
        var generation=service.Generation;service.Configure(config,256,256);Check(service.Generation==generation,"Unchanged plan not reused.");
        RenderingEditorAdapter.RegisterInspections(edit,service,renderer);
        foreach(string capability in new[]{"ncma.render.inspect_pipeline","ncma.render.inspect_graph","ncma.render.get_profile"})
            Check(edit.Invoke(Request(capability,JsonSerializer.SerializeToElement(new{}))).Status=="ok","Rendering inspection "+capability);
        Reject(()=>edit.RegisterInspection(new("illegal","Illegal mutation",MutationRisk.Reversible,JsonSerializer.SerializeToElement(new{}),JsonSerializer.SerializeToElement(new{})),_=>new{}));
        var changed=config with{Exposure=2};
        var request=RenderingEditorAdapter.SettingsRequest(edit,objectId,changed,edit.Revision);
        var wrongScope=new CapabilityPermissions(["ncma.scene.transaction"],objectScope:[Guid.NewGuid()]);
        Check(edit.Invoke(request,wrongScope).Status=="denied","Scoped settings mutation denied.");
        Check(edit.Invoke(request,permission).Changed,"Settings commit.");
        Check(edit.Invoke(RenderingEditorAdapter.SettingsRequest(edit,objectId,config,0),permission).Status=="conflict","Stale configuration revision.");
        ulong builds=service.PlanBuilds;
        service.Configure(RenderingEditorAdapter.ReadConfiguration(edit,objectId),256,256);
        Check(service.PlanBuilds==builds,"Uniform update rebuilt topology.");
        service.Submit(frame++);renderer.Capture(image);renderer.Present();Check(!image.SequenceEqual(baseline),"Settings produced no pixels.");
        Check(edit.Invoke(Request("ncma.history.undo",JsonSerializer.SerializeToElement(new{})),permission).Changed,"Shared Undo.");
        service.Configure(RenderingEditorAdapter.ReadConfiguration(edit,objectId),256,256);
        service.Submit(frame++);renderer.Capture(image);renderer.Present();Check(image.SequenceEqual(baseline),"Settings Undo reference mismatch.");
        Check(edit.Invoke(Request("ncma.history.redo",JsonSerializer.SerializeToElement(new{})),permission).Changed,"Shared Redo.");
        var active=service.Plan; generation=service.Generation;
        Reject(()=>service.Configure(changed with{PipelineType="arbitrary.dll"},256,256));
        Check(ReferenceEquals(active,service.Plan)&&generation==service.Generation,"Failed candidate destroyed active plan.");
        Reject(()=>RenderingEditorAdapter.SettingsRequest(edit,objectId,changed with{Exposure=float.NaN},edit.Revision));
        for(int i=0;i<8;i++){service.Submit(frame++);renderer.Present();}
        long processAllocated=GC.GetTotalAllocatedBytes(true);
        long allocated=GC.GetAllocatedBytesForCurrentThread();long started=Stopwatch.GetTimestamp();
        for(int i=0;i<64;i++){service.Submit(frame++);renderer.Present();}
        long bytes=GC.GetAllocatedBytesForCurrentThread()-allocated;
        _cachedThreadBytes=bytes;_cachedProcessBytes=GC.GetTotalAllocatedBytes(true)-processAllocated;
        _cachedElapsedMs=Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        Console.WriteLine($"Managed cached graph encode/submit: frames=64 allocation={bytes} bytes elapsed_ms={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F3}; GPU rendering included, VSync off");
        Check(bytes<=1024,"Cached submit allocated unexpectedly.");
        // Same one-call submission with a 4096-object managed document; no per-object native call.
        var objects=Enumerable.Range(0,4096).Select(i=>new SceneObjectData(Guid.NewGuid(),"Object "+i,[],[])).ToArray();
        var many=new SceneDocument();many.RestoreSnapshot(new(1,"4096 objects",objects));
        ulong calls=renderer.SubmitCalls;service.Submit(frame++);renderer.Present();
        Check(renderer.SubmitCalls==calls+1,"Per-object ABI regression.");
    }
    public static int Main(string[] args)
    {
        try {
            if(args.Length is not (3 or 4))return 2;
            string root=Path.GetFullPath(args[0]),baselinePath=Path.GetFullPath(args[1]),output=Path.GetFullPath(args[2]);Directory.CreateDirectory(output);
            Check(Marshal.SizeOf<RenderFrame>()==112 && Marshal.SizeOf<RendererStats>()==88 && Marshal.SizeOf<RenderPass>()==32,"Renderer layout");
            var frameView=new RenderFrameView(Guid.NewGuid(),1,Guid.NewGuid(),new Ncma.Runtime.TransformData(new(1,2,3),System.Numerics.Quaternion.Identity,new(2,3,4)));
            Span<float> matrix=stackalloc float[16];frameView.CopyColumnMajorModel(matrix);
            Check(matrix[0]==2 && matrix[5]==3 && matrix[10]==4 && matrix[12]==1 && matrix[13]==2 && matrix[14]==3,"Matrix ABI transpose/translation.");
            var emptyWorld=new Ncma.Runtime.World();var nonSpatial=emptyWorld.CreateObject("Empty");
            Check(!RenderFrameView.Capture(emptyWorld,nonSpatial.PersistentId).HasReferenceModel,"Empty object silently gained a transform.");
            var caps=RenderCapabilities.ReferenceDx11;
            var graph=new ReferencePreviewPipeline().Build(256,256);var compiled=graph.Compile(caps);
            Check(ReferenceEquals(compiled,graph.Compile(caps)) && compiled.PassCount==3,"Cached default graph");
            Reject(()=>graph.Compile(caps with {ReferencePbr=false}),"capability");
            Reject(()=>graph.Compile(caps with {Arrays=false}),"capability");
            Reject(()=>new ReferencePreviewPipeline().Build(0,256).Compile(caps),"resource_dimensions");
            Reject(()=>new ReferencePreviewPipeline().Build(256,256).Compile(caps with{MaxPasses=2}),"graph_budget");
            Reject(()=>new ReferencePreviewPipeline(toneExposureOverride:float.NaN).Build(256,256).Compile(caps),"parameter");
            var cycle=new RenderGraph();var a=cycle.AddResource(new("A",RenderRole.Output,RenderFormat.Rgba8,RenderUsage.ColorTarget,256,256,Imported:true));
            var b=cycle.AddResource(new("B",RenderRole.Output,RenderFormat.Rgba8,RenderUsage.ColorTarget,256,256,Imported:true));
            cycle.AddPass("A",RenderOperation.Clear,[b],[a],0);cycle.AddPass("B",RenderOperation.Clear,[a],[b],0);cycle.SetOutput(b);
            Reject(()=>cycle.Compile(caps),"cycle");
            var missing=new RenderGraph();var m=missing.AddResource(new("Output",RenderRole.Output,RenderFormat.Rgba8,RenderUsage.ColorTarget,256,256,Imported:true));
            var uninit=missing.AddResource(new("Input",RenderRole.Output,RenderFormat.Rgba8,RenderUsage.ColorTarget,256,256,Imported:true));
            missing.AddPass("Uninitialized",RenderOperation.Clear,[uninit],[m],0);missing.SetOutput(m);
            Reject(()=>missing.Compile(caps),"uninitialized_read");
            var mismatch=new RenderGraph();var r=mismatch.AddResource(new("Output",RenderRole.Output,RenderFormat.Rgba16Float,RenderUsage.ColorTarget,256,256,Imported:true));
            mismatch.AddPass("Invalid",RenderOperation.Clear,[],[r],0);mismatch.SetOutput(r);Reject(()=>mismatch.Compile(caps),"resource_format");
            var shader=new RenderGraph();var o=shader.AddResource(new("Output",RenderRole.Output,RenderFormat.Rgba8,RenderUsage.ColorTarget,256,256,Imported:true));
            shader.AddPass("Invalid shader",RenderOperation.Clear,[],[o],1);shader.SetOutput(o);Reject(()=>shader.Compile(caps),"shader_contract");
            var absent=new RenderGraph();absent.AddResource(new("Empty",RenderRole.Output,RenderFormat.Rgba8,RenderUsage.ColorTarget,256,256,Imported:true));Reject(()=>absent.Compile(caps),"graph_budget");
            Console.WriteLine("PASS graph validation/cache/capabilities");

            using var loader=new PluginLoader();loader.Load(root,Specs());
            var platform=loader.Modules.Single(m=>m.Kind==ModuleKind.Platform);var native=loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer);var guiModule=loader.Modules.Single(m=>m.Kind==ModuleKind.Gui);
            byte[] baseline=File.ReadAllBytes(baselinePath);Check(baseline.Length==256*256*4,"Kernel fixture dimension");
            int legacyMax=0;double legacyMean=0;
            if(args.Length==4) {
                byte[] legacy=FrozenReference.Load(Path.GetFullPath(args[3]));
                Check(legacy.Length==baseline.Length,"Legacy comparison fixture dimension");
                long sum=0;
                for(int i=0;i<baseline.Length;i++){int delta=Math.Abs(baseline[i]-legacy[i]);legacyMax=Math.Max(legacyMax,delta);sum+=delta;}
                legacyMean=sum/(double)baseline.Length;
                Check(legacyMax<=4 && legacyMean<=.1,$"Kernel/legacy mismatch max={legacyMax} mean={legacyMean}");
                Console.WriteLine($"PASS kernel/legacy reference parity max={legacyMax}; mean={legacyMean}");
            }
            byte[] image=new byte[baseline.Length];double mean=0;int max=0;ulong calls=0,bytes=0;double submit=0,gpu=0;
            string hardware="";
            for(int cycleIndex=0;cycleIndex<32;cycleIndex++) {
                using var window=new PlatformWindow(platform,"Renderer reference test",256,256,false);
                Reject(()=>new RendererSession(native,window,256,256,backend:2));
                using var renderer=new RendererSession(native,window,256,256);
                using var resources=renderer.CreateReferenceResources();
                using var gui=new GuiSession(guiModule,window,cycleIndex==0?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts),"msyh.ttc"):"");
                gui.AttachRenderer(renderer);
                ulong frame=1;
                renderer.Submit(compiled,resources,RenderFrame.Reference(frame++,256,256));
                renderer.Capture(image);
                if(cycleIndex==0) {
                    long total=0;
                    for(int i=0;i<image.Length;i++){int d=Math.Abs(image[i]-baseline[i]);max=Math.Max(max,d);total+=d;}
                    mean=total/(double)image.Length;
                    // Declared before execution: max <=4/255, mean <=.1/255, includes alpha.
                    Check(max<=4 && mean<=.1,$"Kernel reference mismatch max={max} mean={mean}");
                    Check(image.Where((_,i)=>i%4!=3).Distinct().Count()>32,"Reference rendered no scene.");
                    ExportBmp(Path.Combine(output,"reference.bmp"),image,256,256);
                    hardware=native.ReadDiagnostics();
                }
                renderer.Present();Reject(renderer.Present);
                var invalid=RenderFrame.Reference(frame,256,256);invalid.Metallic=float.NaN;
                Reject(()=>renderer.Submit(compiled,resources,invalid));Check(renderer.Stats.SubmittedFrames==1,"Invalid batch mutated frame");
                Reject(()=>renderer.Capture(new byte[8]));Reject(()=>renderer.Resize(0,256));
                Reject(()=>renderer.Dispose());Reject(()=>window.Dispose());

                if(cycleIndex==0) {
                    TestConfiguration(renderer,ref frame,baseline,image);
                    var variations=new RenderPipeline[]{
                        new ReferencePreviewPipeline(toneExposureOverride:2),
                        new ReferencePreviewPipeline(features:[new ExposureFeature(2)]),
                        new ReferencePreviewPipeline(toneMapping:new ExposureToneStage(2)),
                        new ClearPipeline(.2f,.3f,.4f)};
                    foreach(var variant in variations){
                        var plan=variant.Build(256,256).Compile(caps);
                        renderer.Submit(plan,resources,RenderFrame.Reference(frame++,256,256));renderer.Capture(image);
                        Check(!image.SequenceEqual(baseline),"Customization produced no pixel change.");renderer.Present();
                    }
                    renderer.Submit(compiled,resources,RenderFrame.Reference(frame++,256,256));renderer.Capture(image);renderer.Present();
                    Check(image.SequenceEqual(baseline),"Restored pipeline differs from reference.");
                    ExportBmp(Path.Combine(output,"reference-restored.bmp"),image,256,256);
                    renderer.Resize(320,200);var resized=new ReferencePreviewPipeline().Build(320,200).Compile(caps);
                    renderer.Submit(resized,resources,RenderFrame.Reference(frame++,320,200));renderer.Capture(new byte[320*200*4]);renderer.Present();
                    renderer.Resize(256,256);
                }
                WindowState state=window.Poll();state.Focused=1;
                for(int scaleIndex=1;scaleIndex<=3;scaleIndex++) {
                    state.ScaleX=state.ScaleY=scaleIndex==1?1:scaleIndex==2?1.5f:2;
                    gui.Begin(state,1.0/60);
                    byte[] text=System.Text.Encoding.UTF8.GetBytes(cycleIndex==0?"中文 / Renderer":"GUI / Renderer");
                    GuiItem[] items=new GuiItem[3];
                    items[0]=new(){Kind=1,Enabled=1,WidgetHigh=1,WidgetLow=1,LabelLength=(uint)text.Length};
                    items[0].Rect[2]=150;items[0].Rect[3]=80;
                    items[1]=new(){Kind=3,Enabled=1,WidgetHigh=1,WidgetLow=2,LabelLength=(uint)text.Length};
                    items[2].Kind=2;
                    gui.Draw(new(){StructSize=48,Frame=(ulong)scaleIndex,ViewGeneration=1,DocumentGeneration=1,Revision=0,ItemCount=3,TextBytes=(uint)text.Length},items,text);
                    Reject(gui.RenderGpu); // GUI cannot draw before renderer submit.
                    renderer.Submit(compiled,resources,RenderFrame.Reference(frame++,256,256));gui.RenderGpu();
                    if(cycleIndex==0 && scaleIndex==1){renderer.Capture(image);ExportBmp(Path.Combine(output,"gui-composited.bmp"),image,256,256);Check(!image.SequenceEqual(baseline),"No GUI pixels.");}
                    Reject(gui.RenderGpu);renderer.Present();
                }
                renderer.WaitIdle();
                var stats=renderer.Stats;
                Check(stats.ValidationErrors==0 && stats.ValidationWarnings==0,"DX11 validation: "+native.ReadDiagnostics());
                Check(stats.SubmittedFrames==stats.Presents,"Present count mismatch");
                Check(stats.GpuSampleValid==1 && stats.GpuMilliseconds>=0,"Missing valid GPU timestamp.");gpu+=stats.GpuMilliseconds;
                calls+=renderer.SubmitCalls;bytes+=renderer.CopiedBytes;submit+=stats.SubmitMilliseconds;
                // Explicit order: GUI borrow first, then GPU resources, then renderer, then window.
                gui.Dispose();resources.Dispose();Check(renderer.Stats.LiveGroups==0,"GPU resource group leak");renderer.Dispose();window.Dispose();
                Check(native.Status.LiveResources==0 && guiModule.Status.LiveResources==0 && platform.Status.LiveResources==0,"Native live resource leak");
            }
            File.WriteAllText(Path.Combine(output,"render-results.json"),JsonSerializer.Serialize(new{schema=1,backend="DX11 reference",hardware,cycles=32,width=256,height=256,vsync=false,validation=true,maxChannelError=max,meanChannelError=mean,submitCalls=calls,copiedBytes=bytes,lastSubmitMsAverage=submit/32,gpuMsAverage=gpu/32,kernelReference=true,legacyComparison=args.Length==4,legacyMaxChannelError=legacyMax,legacyMeanChannelError=legacyMean,cachedSubmitFrames=64,cachedOwnerAllocatedBytes=_cachedThreadBytes,cachedProcessAllocatedBytes=_cachedProcessBytes,cachedSubmitElapsedMs=_cachedElapsedMs,manualAcceptance=false},new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine($"PASS real reference/GUI/customization/32 cycles; max={max}; mean={mean}; validation=0/0");
            return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
