using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Gui;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Ui;
using Ncma.Ui.Rendering;

internal static unsafe partial class Program
{
    private static int SceneUiSurfaceTests(string plugins, string output)
    {
        output = Path.Combine(Path.GetFullPath(output), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
        int checks = 0;
        void Pass(bool value, string name) { Check(value, "D3-C HUD surface " + name); checks++; }
        void Reject(Action action, string name) {
            try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or PluginException) { checks++; return; }
            throw new Exception("HUD surface accepted " + name);
        }
        var definition = UiDefinition.Create(Guid.NewGuid(), "Transparent HUD");
        var rect = UiElement.Create(Guid.NewGuid(), "Half red", UiKind.Rectangle, definition.Root) with {
            Layout = UiLayout.Fixed(16,16,48,48), Style = UiStyle.Default with { Fill = new(1,0,0,.5f) } };
        definition = definition with { Elements = [definition.Elements[0],rect] };
        Guid project = Guid.NewGuid(); byte[] bytes = RuntimeUiPackage.Encode(project,1,definition,[],[]);
        using var package = RuntimeUiPackage.Decode(bytes,project,definition.AssetId,1,Convert.ToHexString(SHA256.HashData(bytes)));
        using var loader = new PluginLoader(); loader.Load(plugins,[
            new("platform",ModuleKind.Platform,"NcmaPlatform.dll","NcmaPlatform.dll",1,0,[]),
            new("renderer",ModuleKind.Renderer,"NcmaRenderer.dll","NcmaRenderer.dll",1,2,["platform"]),
            new("gui",ModuleKind.Gui,"NcmaGui.dll","NcmaGui.dll",1,7,["platform","renderer"])]);
        var native = loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer);
        using var window = new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"HUD transparent surface",128,128,false);
        using var renderer = new RendererSession(native,window,128,128,pureUi:true);
        using var gui = new GuiSession(loader.Modules.Single(m=>m.Kind==ModuleKind.Gui),window); gui.AttachRenderer(renderer);
        using var hud = new SceneUiPresentation(); var viewport = new UiViewport(128,128,1,default);
        Reject(()=>hud.PrepareSurface(viewport),"unprepared");
        hud.Prepare(package,renderer,"no-text-plugin-path",viewport);
        Pass(!hud.TextLoaded && renderer.UiTargetStats.Targets==0,"direct Player path has no surface");
        Reject(()=>hud.AcquireSurface(1),"unprepared surface");
        hud.PrepareSurface(viewport); Reject(()=>hud.CaptureSurface(),"unproduced"); Reject(()=>hud.AcquireSurface(0),"zero frame");
        Task.Run(()=>Reject(()=>hud.PrepareSurface(viewport),"owner thread")).GetAwaiter().GetResult();
        using var white = renderer.CreateUiImage(1,1,[255,255,255,255]);
        var background = new UiDisplayListBuilder(renderer);
        background.Quad(new(Guid.NewGuid(),new(0,0,128,128),Matrix3x2.Identity,new(0,0,128,128),1,true,-1),white,new(0,0,1,1));
        using var list = background.Build();
        var frame = new GuiFrame { StructSize=48,ViewGeneration=1,DocumentGeneration=1,ItemCount=6 };
        GuiItem[] items = [new(){Kind=(uint)GuiItemKind.PanelBegin,Enabled=1,WidgetHigh=901,WidgetLow=1,Value=3},default,
            new(){Kind=(uint)GuiItemKind.OverlayBegin,Enabled=1,WidgetHigh=901,WidgetLow=3},
            new(){Kind=(uint)GuiItemKind.CanvasRect,Enabled=1,WidgetHigh=901,WidgetLow=4,Value=0x8000ff00},
            new(){Kind=(uint)GuiItemKind.CanvasEnd,WidgetHigh=901,WidgetLow=5},new(){Kind=(uint)GuiItemKind.PanelEnd}];
        items[0].Rect[2]=128; items[0].Rect[3]=128;
        items[2].Rect[2]=128;items[2].Rect[3]=128;
        items[3].Rect[0]=.625f;items[3].Rect[1]=.125f;items[3].Rect[2]=.25f;items[3].Rect[3]=.25f;
        byte[] pixels = new byte[128*128*4]; ulong uploads=0,productions=0;
        for(ulong id=1;id<=33;id++) {
            hud.PrepareSurface(viewport); var token=hud.AcquireSurface(id);
            if(id==1) {
                Reject(()=>hud.AcquireSurface(id),"double acquisition"); Reject(()=>hud.PrepareSurface(viewport),"prepare while leased");
                Reject(()=>hud.PrepareView(viewport),"direct prepare while leased");
                byte[] surface=hud.CaptureSurface(); File.WriteAllBytes(Path.Combine(output,"transparent.rgba"),surface);
                int at=(32*128+32)*4; Pass(Math.Abs(surface[at]-128)<=1&&surface[at+1]==0&&surface[at+2]==0&&Math.Abs(surface[at+3]-128)<=1,"premultiplied target pixels");
                Pass(surface[(100*128+100)*4+3]==0,"transparent uncovered target");
            }
            gui.Begin(window.Poll(),1d/60); frame.Frame=id; items[1]=GuiItem.CachedImage(901,2,token,0,0,128,128);
            gui.Draw(frame,items,[]);
            if(id==1){Reject(hud.ReleaseSurface,"GUI retains lease");Reject(hud.Dispose,"close cannot unload GUI dependency");}
            renderer.SubmitUi(list,id,Vector4.Zero); gui.RenderGpu(); renderer.Capture(pixels); renderer.Present(); hud.ReleaseSurface();
            if(id==1) {
                File.WriteAllBytes(Path.Combine(output,"composited.rgba"),pixels); int at=(32*128+32)*4;
                File.WriteAllText(Path.Combine(output,"pixel.json"),JsonSerializer.Serialize(new{actual=pixels.Skip(at).Take(4),expected=new[]{128,0,128,255}}));
                Pass(Math.Abs(pixels[at]-128)<=1&&pixels[at+1]==0&&Math.Abs(pixels[at+2]-128)<=1,"single alpha composite over scene blue");
                int ordinary=(32*128+96)*4;Pass(pixels[ordinary]==0&&Math.Abs(pixels[ordinary+1]-128)<=1&&Math.Abs(pixels[ordinary+2]-127)<=1,"ordinary GUI restores straight alpha");
                Pass(pixels[(100*128+100)*4+2]==255,"uncovered scene preserved"); uploads=renderer.UiStats.UploadedBytes;productions=renderer.UiTargetStats.Productions;
            }
        }
        Pass(renderer.UiStats.UploadedBytes==uploads&&renderer.UiTargetStats.Productions==productions,"warm32 no upload or target production");
        hud.Runtime.SetFlags(rect.Id,false,false);Reject(()=>hud.AcquireSurface(34),"widget mutation invalidates cached surface");Reject(()=>hud.CaptureSurface(),"widget mutation invalidates capture");
        hud.Runtime.SetFlags(rect.Id,true,true);
        hud.PrepareView(new(96,80,1,default));Reject(()=>hud.AcquireSurface(34),"direct view resize invalidates surface");Reject(()=>hud.CaptureSurface(),"stale capture");
        viewport=new(96,80,1,default);hud.PrepareSurface(viewport);hud.AcquireSurface(34);hud.ReleaseSurface();
        Pass(hud.CaptureSurface().Length==96*80*4&&renderer.UiTargetStats.Targets==1,"resized target replaces old owner");
        hud.Dispose(); Pass(renderer.UiTargetStats.Leases==0&&renderer.UiTargetStats.Targets==0,"all surface leases closed");
        Pass(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0&&renderer.Stats.LiveGroups==0,"API0/0 and pure2D remains no3D");
        File.WriteAllText(Path.Combine(output,"surface-results.json"),JsonSerializer.Serialize(new{checks,wholeD3Accepted=false,formalEditorHudAccepted=false}));
        Console.WriteLine($"PASS D3-C HUD surfaces: {checks} checks; {output}"); return 0;
    }
}
