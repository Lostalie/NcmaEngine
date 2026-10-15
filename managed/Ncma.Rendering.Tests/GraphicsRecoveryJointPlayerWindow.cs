using Ncma.Gameplay;
using Ncma.Platform;
using Ncma.Player.App;
using Ncma.Rendering;
using Ncma.Ui.Rendering;

// Runs at the trusted PlayerRunner input boundary after its first automatic recovery.
// Synthetic observations exercise the ordinary presentation API, not manual OS events.
internal static class GraphicsRecoveryJointPlayerWindow
{
    internal static void Check(UiPublicationProductEvidence evidence, PlayerPresentation host,
        PlaySession play, Func<string> snapshot, byte[] original, RelocatedPresentationCosts costs, int mode)
    {
        string state = snapshot();
        var window = EnvironmentProductEvidence.Field<PlatformWindow>(host, "_window");
        var visible = window.State;
        visible.Focused = 1;
        var renderer = EnvironmentProductEvidence.Field<RendererSession>(host, "_renderer");
        var diagnostic = host.FirstNativeFailure;
        var hud = EnvironmentProductEvidence.Field<SceneUiPresentation>(host, "_ui").Runtime;
        var hidden = visible;
        hidden.Minimized = 1;
        hidden.FramebufferWidth = hidden.FramebufferHeight = 0;
        var before = renderer.Stats;
        var previousCosts = host.LastPresentationCosts;
        host.ProcessInput(play, hidden, []);
        host.Present(play, (_, _, _) => throw new Exception("Minimized Player captured a frame"));
        hidden.Minimized = 0;
        hidden.FramebufferHeight = visible.FramebufferHeight;
        host.ProcessInput(play, hidden, []);
        host.Present(play, (_, _, _) => throw new Exception("Zero-width Player captured a frame"));
        evidence.Pass(renderer.Stats.Presents == before.Presents && renderer.Stats.SubmittedFrames == before.SubmittedFrames && snapshot() == state,
            "full Player joint suspended without GPU submission or paused graph/solver mutation");
        evidence.Pass(host.LastPresentationCosts == previousCosts, "suspended Player keeps last cost identity without fabricating a sample");

        byte[] Draw(WindowState input)
        {
            byte[]? pixels = null;
            host.ProcessInput(play, input, []);
            host.Present(play, (value, width, height) => {
                evidence.Pass(value.Length == checked((int)(width * height * 4)), "Player resized capture bounds");
                pixels = value;
            });
            costs.Diagnostic(host.LastPresentationCosts);
            return pixels ?? throw new Exception("Missing Player window capture");
        }
        evidence.Pass(Draw(visible).SequenceEqual(original) && snapshot() == state,
            "full Player resume restores exact paused pixels and coupled state");
        var resized = visible;
        resized.FramebufferWidth = 311;
        resized.FramebufferHeight = 193;
        evidence.Pass(Draw(resized).Length == 311 * 193 * 4 && snapshot() == state,
            "odd-size full Player rendering retains graph/root/physics");
        evidence.Pass(Draw(visible).SequenceEqual(original), "restored Player dimensions give exact original final image");
        costs.Window($"mode-{mode}-recovered-play", renderer, () => host.Present(play), () => host.LastPresentationCosts);
        evidence.Pass(snapshot() == state, "non-capture Player cost window never advances paused simulation");

        ulong generation = renderer.ReadFrameState().Generation;
        bool failed = false;
        try { host.Present(play, (_, _, _) => throw new InvalidOperationException("second submitted Player fault")); }
        catch (InvalidOperationException) { failed = true; }
        costs.Diagnostic(host.LastPresentationCosts);
        evidence.Pass(failed && renderer.ReadFrameState().Active, "second Player fault retains actual active GPU frame");
        var approval = host.ReviewGraphicsRecovery(true);
        host.RebuildGraphics(approval);
        evidence.Pass(renderer.IsClosed && EnvironmentProductEvidence.Field<RendererSession>(host, "_renderer").ReadFrameState().Generation != generation,
            "second trusted SDK recovery retires device and creates a distinct generation");
        evidence.Pass(Draw(visible).SequenceEqual(original) && snapshot() == state && host.FirstNativeFailure == diagnostic &&
            ReferenceEquals(hud, EnvironmentProductEvidence.Field<SceneUiPresentation>(host, "_ui").Runtime),
            "second Player recovery keeps exact image, CPU state, widgets and historical diagnostic");
        costs.Window($"mode-{mode}-second-generation", EnvironmentProductEvidence.Field<RendererSession>(host, "_renderer"), () => host.Present(play), () => host.LastPresentationCosts);
        evidence.Pass(snapshot() == state, "new generation cost window retains exact graph/solver state");
    }
}
