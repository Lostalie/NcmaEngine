using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ncma.Assets;
using Ncma.Editor.App;
using Ncma.Editor.Services;
using Ncma.Gui;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Scene.Rendering;

internal static unsafe partial class Program
{
    private static void LowQualityHostChecks(CandidatePresentation editor, Guid settings,
        Func<byte[]> draw, Action fault, string output, Action<bool,string> pass)
    {
        var owner=editor.VerificationEditor;var workspace=new EditorWorkspace(owner);var edit=owner.Edit!;
        var view=editor.VerificationPresenter;
        T Field<T>(string name) where T:class => (T)(typeof(CandidatePresentation).GetField(name,
            System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(editor)
            ?? throw new Exception("Missing graphics owner "+name));
        SceneRenderSession Scene()=>Field<SceneRenderSession>("_editScene");
        byte[] initial=owner.Document.CaptureBytes(),initialImage=draw();
        var document=owner.Document;
        var light=document.GetObjectIds().Single(e=>document.HasComponent<DirectionalLightData>(e));
        var lightValue=document.GetComponent<DirectionalLightData>(light);
        workspace.Transaction(workspace.Stamp,"Low verification baseline",[
            new{op="set_component",objectId=light,typeId=DirectionalLightData.TypeId,version=1,
                data=document.Components.Encode(lightValue with{CastShadow=true})},
            new{op="set_component",objectId=settings,typeId=SceneShadowData.TypeId,version=1,
                data=document.Components.Encode(SceneShadowData.Default)}
        ],settings);
        byte[] before=owner.Document.CaptureBytes(),beforeImage=draw();
        var originalPost=ScenePostProcessState.Read(owner.Document.CaptureSnapshot());
        pass(Scene().Plan!.Resources.Single(r=>r.Role==RenderRole.SceneShadow).Width==1024&&
            Field<RendererSession>("_renderer").PostStats.Bindings==1,"Low baseline real Medium shadow and Bloom live");
        var item=view.Items.ToArray().Single(i=>Encoding.UTF8.GetString(view.Text.Slice((int)i.LabelOffset,(int)i.LabelLength))=="应用场景画质：Low v1");
        var stamp=view.Frame;int history=edit.State.UndoCount;
        view.Apply([new GuiEvent{Kind=item.Kind,WidgetHigh=item.WidgetHigh,WidgetLow=item.WidgetLow,Phase=3,
            Frame=stamp.Frame,ViewGeneration=stamp.ViewGeneration,DocumentGeneration=stamp.DocumentGeneration,Revision=stamp.Revision}],[]);
        byte[] lowImage=draw(),low=owner.Document.CaptureBytes();
        void CheckLow(){
            var scene=Scene();var renderer=Field<RendererSession>("_renderer");
            var shadow=scene.Plan!.Resources.Single(r=>r.Role==RenderRole.SceneShadow);
            pass(scene.PreparedShadowSelection?.Quality==SceneShadowQuality.Low&&shadow.Width==512&&shadow.Height==512&&
                scene.PreparedPostProcess==originalPost with{StackJson="[]"}&&renderer.PostStats.Bindings==0&&renderer.PostStats.ResidentBytes==0,
                "Low exact installed 512 map and zero optional-post GPU resources, Tone/EV retained");
        }
        CheckLow();
        pass(!lowImage.SequenceEqual(beforeImage)&&edit.State.UndoCount==history+1,"Low actual pixels change through one UI transaction");
        var size=editor.VerificationViewport;
        File.WriteAllBytes(Path.Combine(output,"low-medium-bloom.png"),Png(size.Width,size.Height,beforeImage));
        File.WriteAllBytes(Path.Combine(output,"low-512-no-features.png"),Png(size.Width,size.Height,lowImage));
        workspace.History(workspace.Stamp,false);
        pass(draw().SequenceEqual(beforeImage)&&before.SequenceEqual(owner.Document.CaptureBytes()),"Low Undo exact document and GPU image");
        workspace.History(workspace.Stamp,true);
        pass(draw().SequenceEqual(lowImage)&&low.SequenceEqual(owner.Document.CaptureBytes()),"Low Redo exact document and GPU image");
        var oldRenderer=Field<RendererSession>("_renderer");var beforeRecovery=edit.State;
        Reject(fault);
        pass(oldRenderer.ReadFrameState().Active,"Low capture fault retains actual active GPU frame");
        editor.RebuildGraphics(editor.ReviewGraphicsRecovery(true));
        byte[] recovered=draw();CheckLow();
        pass(oldRenderer.IsClosed&&recovered.SequenceEqual(lowImage)&&low.SequenceEqual(owner.Document.CaptureBytes())&&edit.State==beforeRecovery,
            "Low rebuilt graphics retains exact pixels, document and history");
        File.WriteAllBytes(Path.Combine(output,"low-recovered.png"),Png(size.Width,size.Height,recovered));
        // Leave the enclosing original host tests on exactly their original scene and image.
        workspace.History(workspace.Stamp,false);workspace.History(workspace.Stamp,false);
        pass(draw().SequenceEqual(initialImage)&&initial.SequenceEqual(owner.Document.CaptureBytes()),"Low fixture restored original scene without reset shortcut");
        File.WriteAllText(Path.Combine(output,"low-quality-ui.json"),JsonSerializer.Serialize(new{
            sourceEditor=true,managedUiEvents=true,physicalMouseTest=false,manualAccepted=false,performanceAccepted=false,
            width=size.Width,height=size.Height,shadowResolution=512,postBindings=0,
            baselineHash=Convert.ToHexString(SHA256.HashData(beforeImage)),lowHash=Convert.ToHexString(SHA256.HashData(lowImage)),
            recoveryHash=Convert.ToHexString(SHA256.HashData(recovered)),exactUndoRedo=true,exactRecovery=true
        }));
    }
}
