using Ncma.Application.Runtime;
using Ncma.Gameplay;
using Ncma.Runtime;
using Ncma.Ui;
using Ncma.Application;
using Ncma.Assets;
using Ncma.Rendering;
using Ncma.Rendering.Scene;
using Ncma.Scene;
using Ncma.Scene.Rendering;

internal static class GraphicsRecoveryJointState
{
    internal static SceneUiActions Routes()=>new(new SceneUiAction("value",Ncma.GameplayUiActionKind.ValueChanged));
    internal static string Interactive(string project)=>Path.Combine(Path.GetDirectoryName(project)!,"interactive.ncmaproject");
    internal static SceneShadowQuality ShadowQuality(string project){var context=ProjectContext.Load(project);return SceneShadowState.Read(SceneDocumentCodec.Decode(File.ReadAllBytes(context.StartupScenePath)))?.Quality??throw new Exception("Explicit recovery shadow selection required");}
    internal static void CheckShadow(UiPublicationProductEvidence evidence,SceneRenderSession scene,RendererSession renderer,SceneShadowQuality quality)
    {
        var plan=scene.Plan??throw new Exception("Rendered recovery plan required");
        var shadows=plan.Resources.Where(r=>r.Role==RenderRole.SceneShadow).ToArray();uint resolution=SceneShadowPresets.Get(quality).Budget.Resolution;
        evidence.Pass(scene.PreparedShadowSelection?.Quality==quality,"exact selected recovery shadow quality "+quality);
        evidence.Pass(quality==SceneShadowQuality.Off?shadows.Length==0&&scene.Costs.ShadowDraws==0&&renderer.PipelineStats.ShadowDraws==0:
            shadows.Length==1&&shadows[0].Width==resolution&&shadows[0].Height==resolution&&scene.Costs.ShadowDraws>0&&renderer.PipelineStats.ShadowDraws>0,"actual submitted shadow graph resolution/presence "+quality);
    }
    internal static Guid Slider(UiRuntime runtime)=>runtime.CaptureDefinition().Elements.Single(e=>e.Kind==UiKind.Slider).Id;
    internal static bool PrivateProgress(PlaySession play){var value=play.Document.World.Get<TransformData>(play.Document.World.GetEntities().Single(o=>play.Document.World.GetName(o)=="Private recovery state"));return value.Position.X==play.Tick&&value.Position.Z==1&&value.Scale.Y==2;}
    internal static float GameplayValue(PlaySession play)=>play.Document.World.Get<TransformData>(play.Document.World.GetEntities().Single(o=>play.Document.World.GetName(o)=="HUD recovery state")).Position.Y;
}
