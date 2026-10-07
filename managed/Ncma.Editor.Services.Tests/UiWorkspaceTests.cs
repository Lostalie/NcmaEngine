using System.Numerics;
using System.Text;
using System.Security.Cryptography;
using Ncma.Assets.Authoring;
using Ncma.Editor.App;
using Ncma.Editor.Services;
using Ncma.Gui;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Ui;

internal static unsafe class UiWorkspaceTests
{
    private static void Check(bool value,string detail=""){if(!value)throw new Exception("UI workspace assertion failed. "+detail);}
    private static void Reject(Action action){try{action();}catch(Exception e)when(e is ArgumentException or InvalidOperationException or IOException){return;}throw new Exception("UI workspace accepted invalid input.");}
    private static GuiEvent Event(EditorPresenter p,ulong high,ulong low,uint phase=3)=>new(){Kind=p.Items.ToArray().Single(i=>i.WidgetHigh==high&&i.WidgetLow==low).Kind,Phase=phase,WidgetHigh=high,WidgetLow=low,Frame=p.Frame.Frame,ViewGeneration=p.Frame.ViewGeneration,DocumentGeneration=p.Frame.DocumentGeneration,Revision=p.Frame.Revision};
    public static void Run(string root,string plugins,string output)
    {
        string directory=Path.Combine(output,"ui-workspace");Directory.CreateDirectory(Path.Combine(directory,"assets"));
        using var owner=new EditorSessionOwner("UI independent scene");var workspace=new EditorWorkspace(owner);using var ui=new EditorUiWorkspace(workspace,directory);
        owner.ConfigureAssets(directory,Guid.NewGuid(),1,uiScope:ui.Scope);
        byte[] scene=owner.Document.CaptureBytes();var sceneIdentity=owner.Document.World.Identity;
        var review=ui.ReviewFile(workspace.Stamp,"assets/Hud.ncmaui",true);
        Reject(()=>ui.ConfirmFile(review.Review,review.Document,false));Reject(()=>ui.ConfirmFile(review.Review,Guid.NewGuid(),true));
        ui.ConfirmFile(review.Review,review.Document,true);Check(ui.Writable&&owner.Edit!.State.UndoCount==1);
        workspace.History(workspace.Stamp,false);ui.Synchronize();Check(ui.DocumentId==Guid.Empty&&!File.Exists(Path.Combine(directory,"assets/Hud.ncmaui")));
        workspace.History(workspace.Stamp,true);ui.Synchronize();Check(ui.DocumentId==review.Document&&ui.Writable);
        var definition=ui.Capture()!;var a=UiElement.Create(Guid.NewGuid(),"A",UiKind.Rectangle,definition.Root) with{Layout=UiLayout.Fixed(16,16,64,48),Style=UiStyle.Default with{Fill=new(1,0,0,1)}};
        var b=UiElement.Create(Guid.NewGuid(),"B",UiKind.Button,definition.Root) with{Order=1,Layout=UiLayout.Fixed(160,16,64,48),Style=UiStyle.Default with{Fill=new(0,1,0,1)},Action="preview.test"};
        ui.Edit(ui.Stamp,[new(UiEditKind.Add,a.Id,a),new(UiEditKind.Add,b.Id,b)]);int history=owner.Edit!.State.UndoCount;
        using var loader=new PluginLoader();loader.Load(plugins,[new("platform",ModuleKind.Platform,"NcmaPlatform.dll","NcmaPlatform.dll",1,0,[]),new("renderer",ModuleKind.Renderer,"NcmaRenderer.dll","NcmaRenderer.dll",1,2,["platform"]),new("gui",ModuleKind.Gui,"NcmaGui.dll","NcmaGui.dll",1,6,["platform","renderer"])]);
        using var window=new PlatformWindow(loader.Modules.Single(m=>m.Kind==ModuleKind.Platform),"UI authoring verification",1280,720,false);
        using var renderer=new RendererSession(loader.Modules.Single(m=>m.Kind==ModuleKind.Renderer),window,1280,720,pureUi:true);
        using var preview=new EditorUiPreview(renderer,plugins);
        using var gui=new GuiSession(loader.Modules.Single(m=>m.Kind==ModuleKind.Gui),window,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts),"msyh.ttc"));gui.AttachRenderer(renderer);
        var presenter=new EditorPresenter(workspace,null,directory,workspaceStyle:true);presenter.AttachUi(ui,preview);
        presenter.Build(1,1280,720);presenter.Apply([Event(presenter,24,15)],[]);presenter.Build(2,1280,720);presenter.Apply([Event(presenter,24,46)],[]);presenter.SynchronizeUi();Check(presenter.UiWorkspaceActive);
        preview.Prepare(ui,presenter.UiPreviewViewport);var controller=new UiCanvasController();controller.Load(ui.Capture()!,preview.Boxes);Check(controller.Hit(new(20,20))==a.Id&&controller.Hit(new(170,20))==b.Id);
        controller.Select(a.Id);controller.Select(b.Id,append:true);var group=controller.Group();var grouped=UiEdits.Apply(ui.Capture()!,group);var layout=new UiLayoutEngine(new(grouped));var groupedBoxes=layout.Layout(presenter.UiPreviewViewport).ToArray();
        Check(UiCanvasController.Bounds(groupedBoxes.Single(i=>i.Id==a.Id))==UiCanvasController.Bounds(preview.Boxes.Single(i=>i.Id==a.Id)));
        controller.Select(a.Id);controller.Tool=UiCanvasTool.Move;ui.Begin(ui.Stamp);for(int i=0;i<32;i++)ui.Update(controller.Transform(new(i,0),false));
        Check(owner.Edit.State.UndoCount==history&&UiAuthoringSource.Open(directory,"assets/Hud.ncmaui").Elements.Single(i=>i.Id==a.Id).Layout.X==16);
        preview.Prepare(ui,presenter.UiPreviewViewport);ui.Confirm();Check(owner.Edit.State.UndoCount==history+1);
        workspace.History(workspace.Stamp,false);ui.Synchronize();Check(ui.Capture()!.Elements.Single(i=>i.Id==a.Id).Layout.X==16);
        ui.Begin(ui.Stamp);ui.Update(controller.Transform(new(50,0),false));ui.Cancel();Check(ui.Capture()!.Elements.Single(i=>i.Id==a.Id).Layout.X==16&&!owner.Edit.State.EditBusy);
        presenter.SynchronizeUi();sceneIdentity=owner.Document.World.Identity;ulong prepared=preview.Preparations;using var white=renderer.CreateUiImage(1,1,new byte[]{255,255,255,255});var builder=new UiDisplayListBuilder(renderer);builder.Quad(new(Guid.NewGuid(),new(0,0,1,1),Matrix3x2.Identity,new(0,0,1,1),1,true,-1),white,UiColor.Transparent);using var clear=builder.Build();ulong uploads=renderer.UiStats.UploadedBytes;
        for(ulong frame=3;frame<=34;frame++){
            gui.Begin(window.Poll(),1.0/60);presenter.SynchronizeUi();presenter.Build(frame,1280,720);var token=preview.Acquire(frame)!.Value;var description=presenter.AttachUiImage(token);gui.Draw(description,presenter.Items,presenter.Text);renderer.SubmitUi(clear,frame,new(.02f,.03f,.04f,1));gui.RenderGpu();if(frame==34){byte[] screenshot=new byte[1280*720*4];renderer.Capture(screenshot);int red=((int)(presenter.Viewport.Y+20)*1280+(int)(presenter.Viewport.X+20))*4;Check(screenshot[red]>250&&screenshot[red+1]<3&&screenshot[red+2]<3);SaveBmp(Path.Combine(output,"ui-workspace-review.bmp"),screenshot,1280,720);}renderer.Present();preview.ReleasePresentation();
        }
        Check(preview.Preparations==prepared&&renderer.UiTargetStats.Productions==1&&renderer.UiTargetStats.Presentations==32&&renderer.UiTargetStats.Leases==0);
        Check(renderer.UiStats.UploadedBytes==uploads);
        byte[] pixels=preview.Capture();int at=(20*1024+20)*4;Check(pixels[at]>250&&pixels[at+1]<3&&pixels[at+2]<3);
        var runtime=preview.Runtime!;runtime.Input(runtime.Stamp,[new(UiInputKind.PointerDown,1,new(170,20)),new(UiInputKind.PointerUp,2,new(170,20))]);var actions=new UiAction[128];Check(runtime.Drain(runtime.Stamp,actions)==1&&actions[0].Action=="preview.test"&&actions[0].Stamp.PlaySession==Guid.Empty);
        Check(scene.AsSpan().SequenceEqual(owner.Document.CaptureBytes())&&owner.Document.World.Identity==sceneIdentity);
        int beforeGesture=owner.Edit.State.UndoCount;
        void Canvas(uint phase,string value){var input=Event(presenter,31,90,phase);input.Value=4;input.TextLength=(uint)Encoding.UTF8.GetByteCount(value);presenter.Apply([input],Encoding.UTF8.GetBytes(value));}
        Canvas(1,"0.01953125 0.03125 0");Check(ui.HasDraft);Canvas(2,"0.0390625 0.03125 0");Check(ui.HasDraft&&owner.Edit.State.UndoCount==beforeGesture);Canvas(3,"0.0390625 0.03125 0");Check(!ui.HasDraft&&owner.Edit.State.UndoCount==beforeGesture+1&&ui.Capture()!.Elements.Single(e=>e.Id==a.Id).Layout.X==36);
        presenter.Apply([Event(presenter,31,90,3)],Encoding.UTF8.GetBytes("0 0 0"));Check(owner.Edit.State.UndoCount==beforeGesture+1); // Old frame/stamp cannot commit twice.
        // Real trusted font parsing, exact content review and shared runtime glyph preparation.
        string source="assets/test-font.ttc";byte[] font=File.ReadAllBytes(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts),"msyh.ttc"));File.WriteAllBytes(Path.Combine(directory,source),font);Guid fid=Guid.NewGuid();string hash=Convert.ToHexString(SHA256.HashData(font));
        Reject(()=>ui.Resources.AddFont(fid,source,new string('0',64),"test only; no redistribution",false,preview.Text));
        ui.Resources.AddFont(fid,source,hash,"Windows regression only; no redistribution",false,preview.Text);Reject(()=>ui.Resources.AddFont(fid,source,hash,"duplicate",false,preview.Text));
        var label=UiElement.Create(Guid.NewGuid(),"Label",UiKind.Text,ui.Capture()!.Root)with{Order=2,Font=fid,Text="生命值 100",Layout=UiLayout.Fixed(8,100,200,40)};
        ui.Edit(ui.Stamp,[new(UiEditKind.Add,label.Id,label)]);preview.Prepare(ui,presenter.UiPreviewViewport);ulong shapes=preview.Text.PrepareCalls;Check(shapes>0);for(int i=0;i<32;i++)preview.Prepare(ui,presenter.UiPreviewViewport);Check(preview.Text.PrepareCalls==shapes);
        int beforeRejected=owner.Edit.State.UndoCount;ui.Begin(ui.Stamp);Reject(()=>ui.Update([new(UiEditKind.Replace,b.Id,b with{Action="unregistered.code"})]));Reject(ui.Confirm);Check(!ui.HasDraft&&!owner.Edit.State.EditBusy&&owner.Edit.State.UndoCount==beforeRejected&&ui.Capture()!.Elements.Single(e=>e.Id==b.Id).Action=="preview.test");
        // Approved UUIDs must be re-reviewed after restart; source paths never enter .ncmaui.
        string serialized=Encoding.UTF8.GetString(UiCodec.Encode(ui.Capture()!));Check(!serialized.Contains(source,StringComparison.Ordinal)&&!serialized.Contains("msyh.ttc",StringComparison.Ordinal));
        for(ulong frame=35;frame<=66;frame++){
            preview.Prepare(ui,new(frame%2==0?512:640,360,1,default));using(var lease=preview.Acquire(frame) is {}?new LeaseRelease(preview):throw new Exception("Missing preview")){renderer.SubmitUi(clear,frame,new(.02f,.03f,.04f,1));renderer.Present();}Check(renderer.UiTargetStats.Targets==1&&renderer.UiTargetStats.Leases==0);
        }
        // Copied number events cannot install an invalid safe-area configuration.
        ulong viewFrame=67;void Setting(ulong id,float value){presenter.SynchronizeUi();presenter.Build(viewFrame++,1280,720);var input=Event(presenter,31,id);input.Value=value;presenter.Apply([input],[]);}
        Setting(80,64);Setting(82,4);var valid=presenter.UiPreviewViewport;Setting(83,9);Check(presenter.UiPreviewViewport==valid);Setting(80,4096);Setting(82,8);Check(presenter.UiPreviewViewport==valid);
        Setting(80,2048);Setting(81,2048);Setting(82,.25f);presenter.SynchronizeUi();presenter.Build(viewFrame++,1280,720);presenter.AttachUiImage(new(1,1));var wheel=Event(presenter,31,90);wheel.Value=64;wheel.TextLength=6;presenter.Apply([wheel],Encoding.UTF8.GetBytes("0 0 32"));
        presenter.Build(viewFrame++,1280,720);presenter.AttachUiImage(new(1,1));var displayed=presenter.Items.ToArray().Single(i=>i.Kind==(uint)GuiItemKind.CachedImage);Check(displayed.Rect[2]<=16384&&displayed.Rect[3]<=16384);
        Setting(80,1024);Setting(81,640);Setting(82,1);
        presenter.SynchronizeUi();presenter.Build(viewFrame++,1280,720);var select=Event(presenter,31,10002);presenter.Apply([select],[]);presenter.Build(viewFrame++,1280,720);select=Event(presenter,31,10003);select.Value=2;presenter.Apply([select],[]);presenter.Build(viewFrame++,1280,720);Check(presenter.Items.ToArray().Any(i=>i.WidgetHigh==31&&i.WidgetLow==420));
        var unicodePages=new JsonTextPages(new string('字',400)+"😀尾");Check(unicodePages.Replace(0,"修改")=="修改"+new string('字',100)+"😀尾"); // Surrogate-safe pagination does not imply this font covers emoji.
        string longText=new string('字',400)+"尾";ui.Edit(ui.Stamp,[new(UiEditKind.Replace,label.Id,label with{Text=longText})]);presenter.SynchronizeUi();presenter.Build(viewFrame++,1280,720);presenter.Apply([Event(presenter,31,10004)],[]);presenter.Build(viewFrame++,1280,720);int textHistory=owner.Edit.State.UndoCount;
        var inputText=Event(presenter,31,101,1);presenter.Apply([inputText],[]);Check(ui.HasDraft,"text activate: "+presenter.LastMessage);byte[] replacement=Encoding.UTF8.GetBytes("修改");inputText=Event(presenter,31,101,2);inputText.TextLength=(uint)replacement.Length;presenter.Apply([inputText],replacement);Check(ui.HasDraft,"text update: "+presenter.LastMessage);presenter.SynchronizeUi();Check(ui.HasDraft,"text prepare: "+presenter.LastMessage);presenter.Build(viewFrame++,1280,720);inputText=Event(presenter,31,101,3);inputText.TextLength=(uint)replacement.Length;presenter.Apply([inputText],replacement);Check(owner.Edit.State.UndoCount==textHistory+1&&ui.Capture()!.Elements.Single(e=>e.Id==label.Id).Text=="修改"+new string('字',100)+"尾",$"text history {textHistory}/{owner.Edit.State.UndoCount}; last={presenter.LastMessage}; draft={ui.HasDraft}; length={ui.Capture()!.Elements.Single(e=>e.Id==label.Id).Text.Length}");
        presenter.SynchronizeUi();presenter.Build(viewFrame++,1280,720);inputText=Event(presenter,31,101,1);presenter.Apply([inputText],[]);byte[] missingGlyph=Encoding.UTF8.GetBytes(char.ConvertFromUtf32(0x10ffff));inputText=Event(presenter,31,101,2);inputText.TextLength=(uint)missingGlyph.Length;presenter.Apply([inputText],missingGlyph);presenter.SynchronizeUi();Check(!ui.HasDraft&&!preview.Prepared&&owner.Edit.State.UndoCount==textHistory+1&&presenter.LastMessage.Contains("lacks required shaped glyphs",StringComparison.Ordinal));presenter.SynchronizeUi();Check(preview.Prepared);
        presenter.SynchronizeUi();presenter.Build(viewFrame++,520,360);float narrow=presenter.Viewport.Width;var frozen=owner.Document.CaptureBytes();Guid frozenIdentity=owner.Document.World.Identity;presenter.Apply([Event(presenter,31,87)],[]);presenter.Build(viewFrame++,520,360);Check(presenter.Viewport.Width>narrow&&presenter.Items.ToArray().All(i=>i.WidgetHigh!=1||i.WidgetLow!=201));presenter.Apply([Event(presenter,31,87)],[]);presenter.Build(viewFrame++,1280,720);presenter.Apply([Event(presenter,31,121)],[]);Check(presenter.UiPreviewViewport.Width==1920&&presenter.UiPreviewViewport.Height==1080&&frozen.AsSpan().SequenceEqual(owner.Document.CaptureBytes())&&frozenIdentity==owner.Document.World.Identity);
        presenter.SynchronizeUi();presenter.Build(viewFrame++,1280,720);presenter.Apply([Event(presenter,31,75)],[]);gui.Begin(window.Poll(),1.0/60);presenter.Build(viewFrame++,1280,720);var finalFrame=presenter.AttachUiImage(preview.Acquire(presenter.Frame.Frame)!.Value);gui.Draw(finalFrame,presenter.Items,presenter.Text);renderer.SubmitUi(clear,presenter.Frame.Frame,new(.02f,.03f,.04f,1));gui.RenderGpu();byte[] authorImage=new byte[1280*720*4];renderer.Capture(authorImage);SaveBmp(Path.Combine(output,"ui-authoring-review.bmp"),authorImage,1280,720);renderer.Present();preview.ReleasePresentation();Check(frozen.AsSpan().SequenceEqual(owner.Document.CaptureBytes())&&frozenIdentity==owner.Document.World.Identity);
        string documentPath=Path.Combine(directory,"assets/Hud.ncmaui");byte[] original=File.ReadAllBytes(documentPath);byte[] external=UiCodec.Encode(ui.Capture()! with{Name="External file change"});File.WriteAllBytes(documentPath,external);Reject(()=>ui.Begin(ui.Stamp));Check(!ui.HasDraft&&external.AsSpan().SequenceEqual(File.ReadAllBytes(documentPath)));File.WriteAllBytes(documentPath,original); // Restore only this isolated test fixture.
        ui.Revoke();Reject(()=>ui.Begin(ui.Stamp));review=ui.ReviewFile(workspace.Stamp,"assets/Hud.ncmaui",false);ui.ConfirmFile(review.Review,review.Document,false);Check(!ui.Writable&&ui.DocumentId!=Guid.Empty);
        review=ui.ReviewFile(workspace.Stamp,"assets/Hud.ncmaui",false);ui.ConfirmFile(review.Review,review.Document,true);
        owner.RefreshAssets();workspace.PlayControl(workspace.Stamp,"start");workspace.PlayControl(workspace.Stamp,"pause");var play=owner.Play!;ulong tick=play.Tick;Guid session=play.SessionId,world=play.Status.WorldId;
        presenter.SynchronizeUi();presenter.Build(viewFrame++,1280,720);presenter.Apply([Event(presenter,31,4)],[]);Check(!presenter.UiWorkspaceActive&&play.Tick==tick&&play.SessionId==session&&play.Status.WorldId==world);Reject(()=>ui.Begin(ui.Stamp));workspace.PlayControl(workspace.Stamp,"stop");
        Check(renderer.Stats.ValidationWarnings==0&&renderer.Stats.ValidationErrors==0);
        Reject(()=>ui.ReviewFile(workspace.Stamp,"assets/old.ncscene",false));Reject(()=>ui.ReviewFile(workspace.Stamp,"../out.ncmaui",true));
        Console.WriteLine("UI workspace: actual cached preview, 32 frames, shared history, geometry, read-only/exact grants, isolated actions and Play identity passed.");
    }
    private sealed class LeaseRelease(EditorUiPreview owner):IDisposable{public void Dispose()=>owner.ReleasePresentation();}
    private static void SaveBmp(string path,byte[] pixels,int width,int height){using var bmp=new BinaryWriter(File.Create(path));bmp.Write((ushort)0x4d42);bmp.Write(54+pixels.Length);bmp.Write(0);bmp.Write(54);bmp.Write(40);bmp.Write(width);bmp.Write(-height);bmp.Write((ushort)1);bmp.Write((ushort)32);bmp.Write(0);bmp.Write(pixels.Length);bmp.Write(2835);bmp.Write(2835);bmp.Write(0);bmp.Write(0);for(int i=0;i<pixels.Length;i+=4){bmp.Write(pixels[i+2]);bmp.Write(pixels[i+1]);bmp.Write(pixels[i]);bmp.Write(pixels[i+3]);}Console.WriteLine("UI workspace review image: "+path);}
}
