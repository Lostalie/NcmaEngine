using Ncma.Application;
using Ncma.Editor.App;
using Ncma.Editor.Services;
using Ncma.Rendering;

internal static class SceneUiPublicationEditorProbe
{
    internal static void Run(string requestPath){using var e=new UiPublicationProductEvidence(requestPath);string[] native;
        using(var editor=new CandidatePresentation(e.Plugins,ProjectContext.Load(e.Project),true,false,false)){
            editor.Start();ulong frame=0;uint width=0;byte[] Draw(){byte[]? pixels=null;editor.CaptureNextCompositeViewportForVerification((p,w,h)=>{pixels=p;width=w;});editor.PumpPlatform();var f=new FrameContext(++frame,0);editor.ApplyEditorIntents(f);editor.PumpAgent(f);editor.BeginPresentation(f);editor.SubmitInput(f);editor.AdvancePlay(f);editor.Present(f);return pixels!;}
            Draw();byte[] red=Draw();float scale=width/editor.VerificationPresenter.Viewport.Width;int at=((int)(20*scale)*(int)width+(int)(20*scale))*4;e.Pass(red[at]>253&&red[at+2]<2,"saved scene generation reaches final Editor viewport");
            e.Pass(EnvironmentProductEvidence.Field<EditorUiPublication>(editor,"_uiPublication") is not null,"formal publication service attached");
            var owner=editor.VerificationEditor;owner.RefreshAssets(true);owner.Assets?.PrepareStoredGenerationsAsync().GetAwaiter().GetResult();
            for(int cycle=0;cycle<3;cycle++){var play=owner.StartPlay();e.Pass(Draw().SequenceEqual(red),"Play generation2");play.Pause();owner.ReloadGameplay(ProjectContext.Load(e.Project).GameplayAssemblyPath);e.Pass(Draw().SequenceEqual(red),"Reload generation2");owner.StopPlay();e.Pass(Draw().SequenceEqual(red),"Stop generation2");}
            var renderer=EnvironmentProductEvidence.Field<RendererSession>(editor,"_renderer");ulong upload=renderer.UiStats.UploadedBytes,production=renderer.UiTargetStats.Productions;
            for(int i=0;i<16;i++)Draw();e.Pass(renderer.UiStats.UploadedBytes==upload&&renderer.UiTargetStats.Productions==production&&renderer.UiTargetStats.Leases==0,"warm16 cached HUD");
            e.Pass(renderer.Stats.ValidationWarnings==0&&renderer.Stats.ValidationErrors==0,"API0/0");native=EnvironmentProductEvidence.NativePaths(e.Root);File.WriteAllBytes(Path.Combine(e.Output,"editor.rgba"),red);
        }
        e.Finish(native);
    }
}
