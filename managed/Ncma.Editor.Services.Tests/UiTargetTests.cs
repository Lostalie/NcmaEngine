using System.Numerics;
using System.Runtime.InteropServices;
using Ncma.Gui;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Ui;

internal static unsafe class UiTargetTests
{
    private static void Check(bool value) { if(!value)throw new Exception("UI target assertion failed."); }
    private static void Reject(Action action) { try { action(); } catch(Exception e) when(e is ArgumentException or InvalidOperationException or PluginException){return;}throw new Exception("Invalid UI target operation accepted."); }
    public static void Run(string plugins)
    {
        Check(Marshal.SizeOf<UiTargetStats>()==56&&Marshal.SizeOf<GuiCachedImageToken>()==16);
        using var loader=new PluginLoader();loader.Load(plugins,[new("platform",ModuleKind.Platform,"NcmaPlatform.dll","NcmaPlatform.dll",1,0,[]),
            new("renderer",ModuleKind.Renderer,"NcmaRenderer.dll","NcmaRenderer.dll",1,2,["platform"]),new("gui",ModuleKind.Gui,"NcmaGui.dll","NcmaGui.dll",1,6,["platform","renderer"])]);
        var native=loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer);
        using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"UI target cache test",128,128,false);
        using var renderer=new RendererSession(native,window,128,128,pureUi:true);
        using var gui=new GuiSession(loader.Modules.Single(m=>m.Kind==ModuleKind.Gui),window);gui.AttachRenderer(renderer);
        using var white=renderer.CreateUiImage(1,1,new byte[]{255,255,255,255});
        var builder=new UiDisplayListBuilder(renderer);
        builder.Quad(new(Guid.NewGuid(),new(0,0,64,64),Matrix3x2.CreateTranslation(16,16),new(0,0,128,128),1,true,-1),white,new(1,0,0,1));
        using var list=builder.Build();using var target=renderer.CreateUiTarget(128,128);
        Reject(()=>renderer.AcquireUiPresentation(target,1,1));Reject(()=>renderer.CreateUiTarget(0,10));
        renderer.SubmitUiTarget(target,list,1,1,new(0,0,0,1));
        Check(renderer.Stats.SubmittedFrames==0&&renderer.Stats.Presents==0&&renderer.UiStats.PureUi==1&&renderer.Stats.LiveGroups==0);
        var pixels=renderer.CaptureUiTarget(target);Check(pixels[(20*128+20)*4]==255&&pixels[(110*128+110)*4]==0);
        Reject(()=>renderer.SubmitUiTarget(target,list,1,1,Vector4.Zero));Reject(()=>renderer.AcquireUiPresentation(target,2,1));
        ulong uploaded=renderer.UiStats.UploadedBytes;
        var frame=new GuiFrame{StructSize=48,ViewGeneration=1,DocumentGeneration=1,Revision=0,ItemCount=3};
        GuiItem[] items=[new(){Kind=1,Enabled=1,WidgetHigh=1,WidgetLow=1},default,new(){Kind=2}];items[0].Rect[2]=128;items[0].Rect[3]=128;
        GuiCachedImageToken released=default;
        for(ulong id=1;id<=65;id++) {
            using var lease=renderer.AcquireUiPresentation(target,1,id);released=lease.Token;
            gui.Begin(window.Poll(),1.0/60);frame.Frame=id;items[1]=GuiItem.CachedImage(1,2,lease.Token,0,0,128,128);
            if(id==1){var invalid=items.ToArray();invalid[1]=GuiItem.CachedImage(1,2,new(lease.Token.Value,lease.Token.Generation+1),0,0,128,128);Reject(()=>gui.Draw(frame,invalid,[]));}
            Check(gui.Draw(frame,items,[]).EventOverflow==0);Reject(lease.Dispose);Reject(target.Dispose);
            Reject(()=>renderer.SubmitUiTarget(target,list,id,2,Vector4.Zero));
            renderer.SubmitUi(list,id,new(0,0,0,1));gui.RenderGpu();renderer.Present();lease.Dispose();
        }
        Check(renderer.UiTargetStats.Productions==1&&renderer.UiTargetStats.Presentations==65&&renderer.UiTargetStats.Leases==0);
        Check(renderer.UiStats.UploadedBytes==uploaded&&renderer.Stats.Presents==65);
        gui.Begin(window.Poll(),1.0/60);frame.Frame=66;items[1]=GuiItem.CachedImage(1,2,released,0,0,128,128);Reject(()=>gui.Draw(frame,items,[]));
        using(var lease=renderer.AcquireUiPresentation(target,1,66)) {
            var invalid=items.ToArray();invalid[1]=GuiItem.Image(1,2,new(lease.Token.Value,lease.Token.Generation),0,0,128,128);Reject(()=>gui.Draw(frame,invalid,[]));
            items[1]=GuiItem.CachedImage(1,2,lease.Token,0,0,128,128);gui.Draw(frame,items,[]);renderer.SubmitUi(list,66,new(0,0,0,1));gui.RenderGpu();renderer.Present();
        }
        for(ulong id=67;id<99;id++) {
            using var resized=renderer.CreateUiTarget(64+(uint)(id%2),64);renderer.SubmitUiTarget(resized,list,id,1,new(0,0,0,1));
            using(var lease=renderer.AcquireUiPresentation(resized,1,id)){Check(renderer.UiTargetStats.Targets==2);}
            renderer.SubmitUi(list,id,new(0,0,0,1));renderer.Present();
        }
        Reject(()=>renderer.AcquireUiPresentation(target,1,98));
        Check(renderer.UiTargetStats.Targets==1&&renderer.UiTargetStats.Leases==0&&renderer.UiTargetStats.ResidentBytes==128*128*4);
        Check(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0);
        gui.Dispose();target.Dispose();list.Dispose();white.Dispose();renderer.Dispose();Check(native.Status.LiveResources==0);
    }
}
