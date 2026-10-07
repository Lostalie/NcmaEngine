using System.Numerics;
using Ncma.Editor.App;
using Ncma.Editor.Services;
using Ncma.Gui;
using Ncma.Ui;

internal static class UiGeometryTests
{
    private static void Check(bool value){if(!value)throw new Exception("UI geometry/settings assertion failed.");}
    private static void Reject(Action run){try{run();}catch(Exception e)when(e is InvalidOperationException or ArgumentException or IOException){return;}throw new Exception("Expected UI geometry/settings rejection.");}
    public static void Run(string output)
    {
        string path=Path.Combine(output,"workspace-settings.json");var store=new EditorWorkspaceSettingsStore(path);store.Save(0,store.Current with{Left=.2f,Bottom=.35f,ShowAi=false});Check(new EditorWorkspaceSettingsStore(path).Current==store.Current);Reject(()=>store.Save(0,store.Current));
        byte[] invalid=System.Text.Encoding.UTF8.GetBytes("{\"version\":1,\"version\":1,\"left\":0.2}");string bad=Path.Combine(output,"workspace-invalid.json");File.WriteAllBytes(bad,invalid);var preserved=new EditorWorkspaceSettingsStore(bad);Check(preserved.Diagnostic.Length>0);Reject(()=>preserved.Save(0,preserved.Current));Check(invalid.AsSpan().SequenceEqual(File.ReadAllBytes(bad)));
        using var owner=new EditorSessionOwner("Layout only");var workspace=new EditorWorkspace(owner);var presenter=new EditorPresenter(workspace,null,workspaceStyle:true);presenter.AttachLayout(store);byte[] scene=owner.Document.CaptureBytes();Guid identity=owner.Document.World.Identity;
        presenter.Build(1,1280,720);Check(presenter.Items.ToArray().Count(i=>i.Kind==(uint)GuiItemKind.Splitter)==4);
        GuiEvent Event(uint phase,double value)=>new(){Kind=(uint)GuiItemKind.Splitter,WidgetHigh=32,WidgetLow=1,Phase=phase,Value=value,Frame=presenter.Frame.Frame,ViewGeneration=presenter.Frame.ViewGeneration,DocumentGeneration=presenter.Frame.DocumentGeneration,Revision=presenter.Frame.Revision};
        presenter.Apply([Event(1,220),Event(2,250)],[]);presenter.CancelInteraction();presenter.Build(2,1280,720);Check(store.Revision==1);
        ulong generation=presenter.Frame.ViewGeneration;presenter.Apply([Event(1,220),Event(2,260)],[]);presenter.Build(3,1280,720);Check(presenter.Frame.ViewGeneration==generation);presenter.Apply([Event(2,270),Event(3,270)],[]);Check(store.Revision==2&&owner.Edit!.State.UndoCount==0&&scene.AsSpan().SequenceEqual(owner.Document.CaptureBytes())&&identity==owner.Document.World.Identity);
        var d=UiDefinition.Create(Guid.NewGuid(),"Geometry");var a=UiElement.Create(Guid.NewGuid(),"A",UiKind.Rectangle,d.Root)with{Layout=UiLayout.Fixed(20,30,100,60)};var b=UiElement.Create(Guid.NewGuid(),"B",UiKind.Rectangle,d.Root)with{Order=1,Layout=UiLayout.Fixed(200,30,100,60)};
        d=UiEdits.Apply(d,[new(UiEditKind.Add,a.Id,a),new(UiEditKind.Add,b.Id,b)]);var viewport=new UiViewport(640,360,1,default);var boxes=new UiLayoutEngine(new(d)).Layout(viewport).ToArray();var controller=new UiCanvasController();controller.Load(d,boxes);controller.Select(a.Id);controller.Tool=UiCanvasTool.Resize;
        foreach(var handle in Enum.GetValues<UiResizeHandle>()){
            controller.ResizeHandle=handle;var result=controller.Transform(new(10,12),false).Single().Element!;
            bool left=handle is UiResizeHandle.NorthWest or UiResizeHandle.West or UiResizeHandle.SouthWest,right=handle is UiResizeHandle.NorthEast or UiResizeHandle.East or UiResizeHandle.SouthEast,top=handle is UiResizeHandle.NorthWest or UiResizeHandle.North or UiResizeHandle.NorthEast,bottom=handle is UiResizeHandle.SouthWest or UiResizeHandle.South or UiResizeHandle.SouthEast;
            Check(result.Layout.Width==(left?90:right?110:100)&&result.Layout.Height==(top?48:bottom?72:60)&&result.Layout.X==(left?30:20)&&result.Layout.Y==(top?42:30));
        }
        var rotated=a with{Layout=a.Layout with{Rotation=30}};var rd=UiEdits.Apply(d,[new(UiEditKind.Replace,a.Id,rotated)]);controller.Load(rd,new UiLayoutEngine(new(rd)).Layout(viewport));controller.Select(a.Id);controller.ResizeHandle=UiResizeHandle.NorthWest;var edited=UiEdits.Apply(rd,controller.Transform(new(10,12),false));var before=new UiLayoutEngine(new(rd)).Layout(viewport).ToArray().Single(i=>i.Id==a.Id);var after=new UiLayoutEngine(new(edited)).Layout(viewport).ToArray().Single(i=>i.Id==a.Id);
        Check(Vector2.Distance(Vector2.Transform(new(before.Rect.Width,before.Rect.Height),before.Transform),Vector2.Transform(new(after.Rect.Width,after.Rect.Height),after.Transform))<.001f);
        controller.Load(d,boxes);controller.Select(a.Id);var screen=new Vector2(120,110);var point=controller.DocumentPoint(screen,Vector2.Zero);controller.ZoomAt(2,screen,Vector2.Zero);Check(Vector2.Distance(controller.DocumentPoint(screen,Vector2.Zero),point)<.001f);controller.Translate(new(40,10));Check(controller.Selection.Single()==a.Id);
        controller.ZoomAt(100,screen,Vector2.Zero,2);Check(controller.Zoom==2);Reject(()=>controller.ZoomAt(1,screen,Vector2.Zero,.01f));
        var from=new Vector2(-65536,50);var to=new Vector2(65536,50);Check(UiCanvasController.ClipSegment(ref from,ref to,new(0,0,100,100))&&from.X==0&&to.X==100);from=new(-500,-500);to=new(-400,-400);Check(!UiCanvasController.ClipSegment(ref from,ref to,new(0,0,100,100)));
        controller.Select(b.Id,append:true);var group=controller.Group();Check(group.Length==3);controller.Select(d.Root);Reject(()=>controller.Delete(d.Root));Reject(()=>controller.Transform(new(1,1)));controller.Load(UiEdits.Apply(d,[new(UiEditKind.Replace,a.Id,a with{Locked=true})]),boxes);Check(controller.Hit(new(25,35))==d.Root);
        var hug=UiEdits.Apply(d,[new(UiEditKind.Replace,a.Id,a with{Layout=a.Layout with{WidthMode=UiSizing.Hug}})]);controller.Load(hug,new UiLayoutEngine(new(hug)).Layout(viewport));controller.Select(a.Id);controller.ResizeHandle=UiResizeHandle.East;Reject(()=>controller.Transform(new(1,1)));
        var flow=UiEdits.Apply(d,[new(UiEditKind.Replace,d.Root,d.Elements.Single(e=>e.Id==d.Root) with{Layout=d.Elements.Single(e=>e.Id==d.Root).Layout with{Flow=UiFlow.Horizontal}})]);controller.Load(flow,new UiLayoutEngine(new(flow)).Layout(viewport));controller.Select(a.Id);Reject(()=>controller.Transform(new(1,1)));Check(controller.Order(a.Id,1).Length==2);
        controller.Load(d,boxes);controller.Tool=UiCanvasTool.Move;controller.Select(a.Id);var peer=controller.Transform(new(77,0)).Single().Element!;Check(peer.Layout.X==100&&controller.Guides.Any(g=>g.Start.X==200));
        var parentSnap=controller.Transform(new(-17,0)).Single().Element!;Check(parentSnap.Layout.X==0&&controller.Guides.Any(g=>g.Start.X==0));
        controller.Select(b.Id,append:true);var batch=controller.Transform(new(-17,0));Check(batch.Single(i=>i.Element!.Id==b.Id).Element!.Layout.X-batch.Single(i=>i.Element!.Id==a.Id).Element!.Layout.X==180);
    }
}
