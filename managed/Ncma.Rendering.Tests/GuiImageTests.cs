using System.Numerics;
using Ncma.Assets;
using Ncma.Gui;
using Ncma.Platform;
using Ncma.Rendering;

internal static unsafe partial class Program
{
    static void TestGuiImages(RendererSession renderer,GuiSession gui,PlatformWindow window,ref ulong frame,string output)
    {
        var baseline=renderer.ResourceStats;var data=Quad();
        using var mesh=renderer.CreateStaticMesh(data);
        using var material=renderer.CreateMaterial(MaterialDefinition.Default(Guid.NewGuid()) with{BaseColor=new(.18f,.4f,.6f,1)},new GpuTexture?[6],false);
        var draw=ResourceDraw.Create(mesh,material,data.Ranges[0],Matrix4x4.Identity,Matrix4x4.Identity);
        var lighting=new ResourceLighting(new(0,0,2),Vector3.UnitZ,Vector4.One);
        byte[] composed=new byte[256*256*4];GuiImageToken stale=default;
        for(int cycle=0;cycle<32;cycle++) {
            uint width=cycle%2==0?128u:192u,height=cycle%2==0?64u:96u;
            using var target=renderer.CreateViewTarget(width,height);byte[] source=new byte[width*height*4];
            GuiItem[] items=new GuiItem[3];items[0]=new(){Kind=(uint)GuiItemKind.PanelBegin,Enabled=1,WidgetHigh=1,WidgetLow=1};
            items[0].Rect[2]=256;items[0].Rect[3]=256;items[1]=GuiItem.Image(1,2,target.ImageToken,8,36,240,212);items[2].Kind=(uint)GuiItemKind.PanelEnd;
            var state=window.Poll();state.Focused=1;gui.Begin(state,1d/60);
            GuiFrame f=new(){StructSize=48,ItemCount=3,Frame=frame,ViewGeneration=1,DocumentGeneration=1};
            var invalid=(GuiItem[])items.Clone();invalid[1]=GuiItem.Image(1,2,target.ImageToken with{Generation=target.ImageToken.Generation+1},8,36,240,212);
            Reject(()=>gui.Draw(f,invalid,[]));
            if(cycle>0){invalid[1]=GuiItem.Image(1,2,stale,8,36,240,212);Reject(()=>gui.Draw(f,invalid,[]));}
            gui.Draw(f,items,[]);Reject(target.Dispose); // CPU draw data already pins target.
            Reject(gui.RenderGpu); // No target submission; cannot show stale/uninitialized pixels.
            renderer.SubmitResources(frame++,[draw],new(0,0,width,height),Vector4.UnitW,lighting,target,ResourceDrawMode.Unlit);
            renderer.CaptureTarget(target,source);renderer.Capture(composed);
            int screen=(142*256+128)*4,center=(int)((height/2*width+width/2)*4);
            Check(composed[screen]==0&&source[center]>100,"Offscreen target must not draw into swapchain");
            gui.RenderGpu();Reject(gui.RenderGpu);renderer.Capture(composed);
            for(int channel=0;channel<3;channel++)Check(Math.Abs(composed[screen+channel]-source[center+channel])<=1,"Opaque GUI token composition pixel mismatch");
            if(cycle==0){ExportBmp(Path.Combine(output,"gui-offscreen-source.bmp"),source,width,height);ExportBmp(Path.Combine(output,"gui-offscreen-composed.bmp"),composed,256,256);}
            renderer.Present();stale=target.ImageToken;
        }
        var after=renderer.ResourceStats;
        Check(after.Targets==baseline.Targets&&after.UploadedBytes==baseline.UploadedBytes,"Target resize/lifecycle must not upload geometry/texture pixels");
        Check(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"GUI image DX11 validation");
        Console.WriteLine("PASS M3.6 GUI 1.3 opaque images: 32 target resize/release cycles, stale/foreign pins, exact frame, offscreen/composition pixels, one Present, zero validation messages");
    }
}
