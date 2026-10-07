using System.Numerics;
using System.Text;
using Ncma.Editor.App;
using Ncma.Editor.Services;
using Ncma.Gui;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;

internal static unsafe class WorkspaceTests
{
    private static void Check(bool value){if(!value)throw new Exception("Workspace assertion failed.");}
    private static GuiEvent Event(EditorPresenter view,ulong id,uint? kind=null)=>new(){Kind=kind??view.Items.ToArray().Single(i=>i.WidgetHigh==24&&i.WidgetLow==id).Kind,WidgetHigh=24,WidgetLow=id,Phase=3,
        Frame=view.Frame.Frame,ViewGeneration=view.Frame.ViewGeneration,DocumentGeneration=view.Frame.DocumentGeneration,Revision=view.Frame.Revision};
    public static void Run(string root,string plugins,string output)
    {
        using var owner=new EditorSessionOwner("Actual scene");var workspace=new EditorWorkspace(owner);
        workspace.CreateObject(workspace.Stamp);
        var presenter=new EditorPresenter(workspace,null,workspaceStyle:true,projectName:"动作测试项目");
        byte[] unchanged=owner.Document.CaptureBytes();ulong revision=owner.Document.Revision;
        foreach(var size in new (uint W,uint H)[]{(520,360),(800,600),(1280,720),(1920,1080)}) {
            presenter.Build(1,size.W,size.H);var items=presenter.Items.ToArray();
            Check(presenter.Viewport.Y<110&&presenter.Viewport.Width>0&&presenter.Viewport.Height>0);
            foreach(var i in items.Where(i=>i.Kind==(uint)GuiItemKind.MenuButton||i.Kind==(uint)GuiItemKind.MenuBrand))
                Check(i.Rect[0]>=0&&i.Rect[0]+i.Rect[2]<=size.W&&i.Rect[1]+i.Rect[3]<=48);
            foreach(var i in items.Where(i=>i.Kind==(uint)GuiItemKind.PanelBegin&&i.WidgetLow!=90))
                Check(i.Rect[0]>=0&&i.Rect[1]>=48&&i.Rect[0]+i.Rect[2]<=size.W+.1f&&i.Rect[1]+i.Rect[3]<=size.H+.1f);
            Check(!items.Any(i=>i.Kind==(uint)GuiItemKind.MenuBrand));
            var menus=items.Where(i=>i.Kind==(uint)GuiItemKind.MenuButton&&i.WidgetHigh==24&&i.WidgetLow is >=10 and <=17).ToArray();
            Check(menus[0].WidgetLow==17&&menus[1].WidgetLow==10&&menus[0].Rect[0]+menus[0].Rect[2]<=menus[1].Rect[0]);
            Check(menus[0].Rect[0]==8);
            for(int i=1;i<menus.Length;i++)Check(menus[i].Rect[0]==menus[i-1].Rect[0]+menus[i-1].Rect[2]+4);
            Check(Encoding.UTF8.GetString(presenter.Text.Slice((int)menus[0].LabelOffset,(int)menus[0].LabelLength))=="文件");
            if(size.W>=1100)Check(menus.Select(i=>Encoding.UTF8.GetString(presenter.Text.Slice((int)i.LabelOffset,(int)i.LabelLength))).SequenceEqual(new[]{"文件","项目","编辑","工具","游戏","AI","窗口","帮助"}));
            Check(items.Where(i=>i.WidgetHigh==24&&i.WidgetLow is >=60 and <=63).All(i=>i.Enabled==0));
            if(size.W>=1200)CheckAiHeader(presenter);
        }
        presenter.Build(13,1280,720);presenter.Apply([Event(presenter,17)],[]);presenter.Build(14,1280,720);
        Check(presenter.Items.ToArray().Where(i=>i.WidgetHigh==25).Select(i=>i.WidgetLow).SequenceEqual(new ulong[]{3,40,5,41}));
        presenter.Apply([Event(presenter,10)],[]);presenter.Build(15,1280,720);
        Check(!presenter.Items.ToArray().Any(i=>i.WidgetHigh==25&&i.WidgetLow==3));presenter.CancelInteraction();
        presenter.Build(11,1100,600);presenter.Apply([Event(presenter,14)],[]);presenter.Build(12,1100,600);
        CheckAiHeader(presenter);presenter.CancelInteraction();
        Check(owner.Document.Revision==revision&&unchanged.AsSpan().SequenceEqual(owner.Document.CaptureBytes()));
        presenter.Build(2,1280,720);presenter.Apply([Event(presenter,15,(uint)GuiItemKind.Button)],[]);
        Check(!presenter.Items.ToArray().Any(i=>i.WidgetLow==91&&i.Kind==1));
        presenter.Apply([Event(presenter,15)],[]);presenter.Build(3,1280,720);Check(presenter.Items.ToArray().Any(i=>i.Kind==1&&i.WidgetLow==91));
        var toggle=Event(presenter,44);presenter.Apply([toggle],[]);presenter.Build(4,1280,720);
        Check(!presenter.Items.ToArray().Any(i=>i.Kind==1&&i.WidgetLow==93));presenter.Apply([toggle],[]);
        presenter.Build(41,1280,720);presenter.Apply([Event(presenter,15)],[]);presenter.CancelInteraction();presenter.Build(42,1280,720);
        Check(!presenter.Items.ToArray().Any(i=>i.Kind==1&&i.WidgetLow==91)); // Escape/focus loss closes the overlay, no command.
        workspace.PlayControl(workspace.Stamp,"start");workspace.PlayControl(workspace.Stamp,"pause");
        var play=owner.Play!;ulong tick=play.Tick;Guid session=play.SessionId,world=play.Status.WorldId;
        presenter.Build(5,1280,720);presenter.Apply([Event(presenter,15)],[]);presenter.Build(6,1280,720);presenter.Apply([Event(presenter,44)],[]);
        Check(play.Tick==tick&&play.SessionId==session&&play.Status.WorldId==world);workspace.PlayControl(workspace.Stamp,"stop");
        foreach(string? project in new string?[]{null," ","  动作测试项目  "}) {
            var fallback=new EditorPresenter(workspace,null,workspaceStyle:true,projectName:project);fallback.Build(1,1280,720);
            Check(!fallback.Items.ToArray().Any(i=>i.Kind==(uint)GuiItemKind.MenuBrand));
            Check(EditorPresenter.WindowTitle(project)=="NcmaEngine - "+(string.IsNullOrWhiteSpace(project)?"未命名项目":"动作测试项目"));
        }
        string longTitle=EditorPresenter.WindowTitle(new string('项',199)+"😀"+new string('项',300));
        Check(Encoding.UTF8.GetByteCount(longTitle)<=1024&&!char.IsHighSurrogate(longTitle[^1]));

        using var loader=new PluginLoader();loader.Load(plugins,[new("platform",ModuleKind.Platform,"NcmaPlatform.dll","NcmaPlatform.dll",1,0,[]),
            new("renderer",ModuleKind.Renderer,"NcmaRenderer.dll","NcmaRenderer.dll",1,2,["platform"]),new("gui",ModuleKind.Gui,"NcmaGui.dll","NcmaGui.dll",1,5,["platform","renderer"])]);
        using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),EditorPresenter.WindowTitle("动作测试项目"),1280,720,false);
        using var renderer=new RendererSession(loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer),window,1280,720);
        using var gui=new GuiSession(loader.Modules.Single(m=>m.Kind==ModuleKind.Gui),window,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts),"msyh.ttc"));gui.AttachRenderer(renderer);
        gui.SetToolbarIcon(File.ReadAllBytes(Path.Combine(root,"engine/build/resources/NcmaEngine.ico")));
        presenter.Build(7,1280,720);var viewport=presenter.Viewport;
        using var target=renderer.CreateViewTarget((uint)viewport.Width,(uint)viewport.Height);
        byte[] pixels=new byte[1280*720*4];
        for(ulong frame=8;frame<=10;frame++){
            gui.Begin(window.Poll(),1.0/60);presenter.Build(frame,1280,720);var view=presenter.AttachViewport(target.ImageToken);
            var stats=gui.Draw(view,presenter.Items,presenter.Text);Check(stats.EventOverflow==0&&stats.Vertices>0);
            renderer.SubmitResources(frame,[],new(0,0,target.Width,target.Height),new(.018f,.028f,.045f,1),new(new(0,0,5),-Vector3.UnitY,Vector4.One),target);
            gui.RenderGpu();if(frame==10)renderer.Capture(pixels);renderer.Present();
        }
        Check(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0);
        int pixel=(350*1280+20)*4;Console.WriteLine($"Workspace background sample: {pixels[pixel]},{pixels[pixel+1]},{pixels[pixel+2]}");
        string file=Path.Combine(output,"workspace-review.bmp");using var bmp=new BinaryWriter(File.Create(file));
        bmp.Write((ushort)0x4d42);bmp.Write(54+pixels.Length);bmp.Write(0);bmp.Write(54);bmp.Write(40);bmp.Write(1280);bmp.Write(-720);bmp.Write((ushort)1);bmp.Write((ushort)32);bmp.Write(0);bmp.Write(pixels.Length);bmp.Write(2835);bmp.Write(2835);bmp.Write(0);bmp.Write(0);
        for(int i=0;i<pixels.Length;i+=4){bmp.Write(pixels[i+2]);bmp.Write(pixels[i+1]);bmp.Write(pixels[i]);bmp.Write(pixels[i+3]);}
        Console.WriteLine("Workspace review image: "+file);
        Check(Math.Abs(pixels[pixel]-20)<3&&Math.Abs(pixels[pixel+1]-33)<3&&Math.Abs(pixels[pixel+2]-49)<3);
    }
    private static void CheckAiHeader(EditorPresenter presenter)
    {
        var items=presenter.Items.ToArray();var header=items.Single(i=>i.Kind==(uint)GuiItemKind.PanelBegin&&i.WidgetLow==93);
        Check(Encoding.UTF8.GetString(presenter.Text.Slice((int)header.TextOffset,(int)header.TextLength))=="未接入推理服务");
        Check(!items.Any(i=>i.Kind==(uint)GuiItemKind.Label&&Encoding.UTF8.GetString(presenter.Text.Slice((int)i.LabelOffset,(int)i.LabelLength))=="未接入推理服务"));
    }
}
