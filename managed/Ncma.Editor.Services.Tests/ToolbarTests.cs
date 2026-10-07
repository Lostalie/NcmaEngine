using System.Text.Json;
using Ncma.Editor.App;
using Ncma.Editor.Services;
using Ncma.Gui;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;

internal static unsafe class ToolbarTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Toolbar assertion failed."); }
    private static void Reject(Action action) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return; } throw new Exception("Invalid toolbar accepted."); }
    private static GuiEvent Event(EditorPresenter view,ulong id,uint? kind=null) => new() { Kind=kind??(uint)GuiItemKind.ToolbarButton,WidgetHigh=23,WidgetLow=id,Phase=3,
        Frame=view.Frame.Frame,ViewGeneration=view.Frame.ViewGeneration,DocumentGeneration=view.Frame.DocumentGeneration,Revision=view.Frame.Revision };
    public static void Run(string root,string plugins,string output)
    {
        using var owner=new EditorSessionOwner("Toolbar preview");var workspace=new EditorWorkspace(owner);
        var original=new EditorPresenter(workspace,null);var toolbar=new EditorPresenter(workspace,null,toolbarStyle:true);
        byte[] unchanged=owner.Document.CaptureBytes();ulong revision=owner.Document.Revision;
        foreach(uint width in new uint[]{520,800,1280,1920}) {
            original.Build(1,width,720);toolbar.Build(1,width,720);Check(toolbar.Viewport==original.Viewport);
            var items=toolbar.Items.ToArray();var header=items.Single(i=>i.Kind==(uint)GuiItemKind.ToolbarBegin);Check(header.Rect[3]==64);
            foreach(var i in items.Where(i=>i.Kind==(uint)GuiItemKind.ToolbarButton))Check(i.Rect[0]>=0&&i.Rect[0]+i.Rect[2]<=width&&i.Rect[1]+i.Rect[3]<=64);
            foreach(ulong panel in new ulong[]{2,3,4}) {
                var a=original.Items.ToArray().Single(i=>i.Kind==1&&i.WidgetHigh==1&&i.WidgetLow==panel);var b=items.Single(i=>i.Kind==1&&i.WidgetHigh==1&&i.WidgetLow==panel);
                for(int axis=0;axis<4;axis++)Check(a.Rect[axis]==b.Rect[axis]);
            }
        }
        Check(unchanged.AsSpan().SequenceEqual(owner.Document.CaptureBytes())&&revision==owner.Document.Revision);
        workspace.CreateObject(workspace.Stamp);toolbar.Build(2,1280,720);
        toolbar.Apply([Event(toolbar,5,(uint)GuiItemKind.Button)],[]);Check(owner.Document.World.Count==1);
        var undo=Event(toolbar,5);toolbar.Apply([undo],[]);Check(owner.Document.World.Count==0);toolbar.Apply([undo],[]);Check(owner.Document.World.Count==0);
        owner.Edit!.SetFrozen(true);toolbar.Build(3,1280,720);Check(toolbar.Items.ToArray().Single(i=>i.WidgetHigh==23&&i.WidgetLow==4).Enabled==0);owner.Edit.SetFrozen(false);

        using var loader=new PluginLoader();loader.Load(plugins,[new("platform",ModuleKind.Platform,"NcmaPlatform.dll","NcmaPlatform.dll",1,0,[]),
            new("renderer",ModuleKind.Renderer,"NcmaRenderer.dll","NcmaRenderer.dll",1,2,["platform"]),new("gui",ModuleKind.Gui,"NcmaGui.dll","NcmaGui.dll",1,4,["platform","renderer"])]);
        using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"Toolbar visual review",1280,720,false);
        using var renderer=new RendererSession(loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer),window,1280,720);
        using var pipeline=new RenderPipelineService(renderer);pipeline.Configure(RenderConfiguration.Default(Guid.NewGuid()),1280,720);
        using var gui=new GuiSession(loader.Modules.Single(m=>m.Kind==ModuleKind.Gui),window,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts),"msyh.ttc"));gui.AttachRenderer(renderer);
        Reject(()=>gui.SetToolbarIcon(new byte[32]));gui.SetToolbarIcon(File.ReadAllBytes(Path.Combine(root,"engine/build/resources/NcmaEngine.ico")));
        byte[] pixels=new byte[1280*720*4];
        for(ulong frame=4;frame<=6;frame++) {
            var state=window.Poll();gui.Begin(state,1.0/60);var view=toolbar.Build(frame,1280,720);
            if(frame==4){var bad=toolbar.Items.ToArray();int index=Array.FindIndex(bad,i=>i.Kind==(uint)GuiItemKind.ToolbarButton);bad[index].Rect[0]=-100;Reject(()=>gui.Draw(view,bad,toolbar.Text));}
            var stats=gui.Draw(view,toolbar.Items,toolbar.Text);Check(stats.EventOverflow==0&&stats.Vertices>0);
            pipeline.Submit(frame);gui.RenderGpu();if(frame==6)renderer.Capture(pixels);renderer.Present();
        }
        Check(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0);
        int p=(2*1280+1200)*4;Check(pixels[p]<25&&pixels[p+1]<35&&pixels[p+2]<50); // Toolbar, not default panel background.
        string file=Path.Combine(output,"toolbar-review.bmp");using var bmp=new BinaryWriter(File.Create(file));
        bmp.Write((ushort)0x4d42);bmp.Write(54+pixels.Length);bmp.Write(0);bmp.Write(54);bmp.Write(40);bmp.Write(1280);bmp.Write(-720);bmp.Write((ushort)1);bmp.Write((ushort)32);bmp.Write(0);bmp.Write(pixels.Length);bmp.Write(2835);bmp.Write(2835);bmp.Write(0);bmp.Write(0);
        for(int i=0;i<pixels.Length;i+=4){bmp.Write(pixels[i+2]);bmp.Write(pixels[i+1]);bmp.Write(pixels[i]);bmp.Write(pixels[i+3]);}
        Console.WriteLine("Toolbar review image: "+file);
    }
}
