using System.Numerics;
using Ncma.Application;
using Ncma.Editor.App;
using Ncma.Gameplay;
using Ncma.Platform;
using Ncma.Rendering;
using Ncma.Runtime;
using Ncma.Ui;
using Ncma.Ui.Rendering;

internal static unsafe class SceneUiInteractionEditorProbe
{
    internal static void Run(string requestPath)
    {
        var e=new SceneUiInteractionEvidence(requestPath);var context=ProjectContext.Load(e.Project);var routes=SceneUiInteractionEvidence.Routes();string[] native;
        using(var editor=new CandidatePresentation(e.Plugins,context,true,false,false,uiActions:routes)) {
            editor.Start();var owner=editor.VerificationEditor;Guid target=owner.Document.World.GetObjects().Single(o=>o.Name=="HUD target").PersistentId;
            var window=EnvironmentProductEvidence.Field<PlatformWindow>(editor,"_window");var state=window.State;state.Focused=1;state.InputReset=state.Overflow=0;state.Sequence=0;
            ulong frame=0,sequence=0;
            byte[] Draw(InputEvent[]? input=null,double dt=0,bool capture=false){state.Sequence=sequence;editor.SetWindowInputForVerification(state,input??[]);var f=new FrameContext(++frame,dt);byte[]? pixels=null;
                if(capture)editor.CaptureNextCompositeViewportForVerification((p,w,h)=>{e.Pass(p.Length==w*h*4,"final GUI image dimensions");pixels=p;});
                editor.ApplyEditorIntents(f);editor.PumpAgent(f);editor.BeginPresentation(f);editor.SubmitInput(f);editor.AdvancePlay(f);editor.Present(f);return pixels??[];}
            InputEvent Pointer(uint action,float x,float y){var area=editor.VerificationPresenter.Viewport;state.PointerX=area.X+x;state.PointerY=area.Y+y;return new(){Kind=2,Action=action,Sequence=++sequence,X=state.PointerX,Y=state.PointerY};}
            Draw();byte[] baseline=Draw(capture:true),edit=owner.Document.CaptureBytes();owner.RefreshAssets(true);owner.Assets?.PrepareStoredGenerationsAsync().GetAwaiter().GetResult();
            var renderer=EnvironmentProductEvidence.Field<RendererSession>(editor,"_renderer");
            for(int cycle=0;cycle<3;cycle++){
                var play=owner.StartPlay();Draw();Draw(dt:1d/60);var editHud=EnvironmentProductEvidence.Field<SceneUiPresentation>(editor,"_sceneHud");var hud=EnvironmentProductEvidence.Field<SceneUiPresentation>(editor,"_playHud");
                Guid text=hud.Runtime.CaptureDefinition().Elements.Single(i=>i.Kind==UiKind.TextInput).Id;
                e.Pass(hud.Runtime.Stamp.Instance!=editHud.Runtime.Stamp.Instance&&hud.Runtime.Widget(text).Text=="","fresh independent Play HUD");
                Vector3 Position()=>play.Document.World.FindObject(target).Get<TransformData>().Position;
                Draw([Pointer(1,20,20),Pointer(0,20,20)],1d/60);e.Pass(Position()==new Vector3(1,0,0),"Editor click consumes physical edge");
                Draw([new(){Kind=1,Key=32,Action=1,Sequence=++sequence}],1d/60);e.Pass(Position()==new Vector3(2,0,0),"Editor Space consumes physical edge");
                var changed=Draw([Pointer(1,20,60),Pointer(0,20,60),new(){Kind=5,Codepoint='A',Sequence=++sequence}],1d/60,true);
                e.Pass(Position()==new Vector3(2,0,1)&&hud.Runtime.Widget(text).Text=="A","Editor text consumed in fixed step");
                e.Pass(editHud.Runtime.Widget(text).Text==""&&owner.Document.CaptureBytes().SequenceEqual(edit),"Edit document and UI isolated");
                e.Pass(!changed.SequenceEqual(baseline),"changed text reaches final GUI image");File.WriteAllBytes(Path.Combine(e.Output,$"editor-{cycle}.rgba"),changed);
                Draw([Pointer(1,20,20),Pointer(0,20,20)]);play.Pause();Draw();play.Step();play.Resume();Draw(dt:1d/60);e.Pass(Position()==new Vector3(2,0,1),"pause and step cancel pending input");
                Draw([Pointer(1,20,20),Pointer(0,20,20)]);owner.ReloadGameplay(context.GameplayAssemblyPath);Draw();play.Resume();Draw(dt:1d/60);e.Pass(Position()==new Vector3(2,0,1),"reload cancels pending input");
                owner.StopPlay();e.Pass(Draw(capture:true).SequenceEqual(baseline),"Stop restores exact Edit image");e.Pass(renderer.UiTargetStats.Targets==1&&renderer.UiTargetStats.Leases==0,"Play target and leases released");
                e.Pass(renderer.Stats.ValidationErrors==0&&renderer.Stats.ValidationWarnings==0,"Editor API0/0");
            }
            var textResources=EnvironmentProductEvidence.Field<UiTextResources>(editor,"_uiTextResources");e.Pass(textResources.Modules.Count==1,"single native Text owner");
            native=EnvironmentProductEvidence.NativePaths(e.Root);e.Pass(native.Length>=4,"Editor live native paths recorded");
        }
        e.Finish(native);
    }
}
