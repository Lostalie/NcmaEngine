using Ncma;
using Ncma.Application.Runtime;
using Ncma.Gameplay;
using Ncma.Scene;
using Ncma.Ui;

internal static class SceneUiRoutingTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Scene UI routing assertion failed."); }
    private static void Reject(Action action) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return; } throw new Exception("Invalid scene UI routing accepted."); }
    private sealed class Probe : Behaviour
    {
        public int Count; public string Text = ""; public Guid Target;
        protected override void OnFixedUpdate(double delta) {
            Count += Context.UiInput.Count;
            for (int i = 0; i < Context.UiInput.Count; i++) Text = Context.UiInput[i].Text;
            if (Context.UiInput.Count != 0) Context.Commands.Rename(Target, "HUD committed");
        }
    }
    private sealed class Fixture : IDisposable
    {
        public readonly SceneUiActions Routes = new(new("fire", GameplayUiActionKind.Activate), new("text", GameplayUiActionKind.TextChanged), new("value", GameplayUiActionKind.ValueChanged));
        public readonly UiDefinition Definition;
        public readonly UiRuntime Ui;
        public readonly SceneDocument Document = new("UI route");
        public readonly PlaySession Play;
        public readonly Probe Probe = new();
        public readonly SceneUiInput Bridge;
        public readonly UiAction[] Buffer = new UiAction[128];
        public Fixture()
        {
            var d = UiDefinition.Create(Guid.NewGuid(), "HUD");
            Definition = d with { Elements = [d.Elements[0],
                UiElement.Create(Guid.NewGuid(), "Fire", UiKind.Button, d.Root) with { Action = "fire", Layout = UiLayout.Fixed(0, 0, 80, 30) },
                UiElement.Create(Guid.NewGuid(), "Text", UiKind.TextInput, d.Root) with { Order = 1, Action = "text", Font = Guid.NewGuid(), Layout = UiLayout.Fixed(0, 40, 100, 30) },
                UiElement.Create(Guid.NewGuid(), "Slider", UiKind.Slider, d.Root) with { Order = 2, Action = "value", Layout = UiLayout.Fixed(0, 80, 100, 30) }
            ] };
            Ui = new(new(Definition)); Ui.Layout(new(200, 150, 1, default));
            Guid obj = Document.CreateObject("Before"); Document.SetBindings(obj, [new(Guid.NewGuid(), "Ui.Probe", true, [])]);
            Probe.Target = obj;
            Play = new(Document.BakeRuntime(Document.Revision).Image.Instantiate(Document.Components), FrameTimePolicy.Strict); Routes.Install(Play); Play.Start(_ => Probe);
            Bridge = new(Ui, Play, Routes);
        }
        public void Click(ulong seq = 1) => Ui.Input(Ui.Stamp, [new(UiInputKind.PointerDown, seq, new(10, 10)), new(UiInputKind.PointerUp, seq + 1, new(10, 10))]);
        public void Submit() => Bridge.Submit(Buffer.AsSpan(0, Ui.Drain(Ui.Stamp, Buffer)));
        public void Step() => Play.AdvanceFrame(1d / 60);
        public void Dispose() { Bridge.Dispose(); Play.Dispose(); }
    }
    public static void Run()
    {
        var cases = new (string Name, Action Run)[] {
            ("Exact startup routes reject unknown, wrong-kind, inert dispatch and empty input font", () => {
                using var f = new Fixture(); var original = f.Definition.Elements;
                void Bad(UiElement e) => Reject(() => f.Routes.Validate(f.Definition with { Elements = [original[0], e] }));
                Bad(original[1] with { Action = "unknown" }); Bad(original[1] with { Action = "text" }); Bad(original[1] with { Action = "" });
                Bad(original[1] with { Kind = UiKind.Rectangle }); Bad(original[1] with { Kind = UiKind.ScrollView }); Bad(original[2] with { Font = Guid.Empty });
                Reject(() => new SceneUiActions(new SceneUiAction("x", (GameplayUiActionKind)999))); Reject(() => new SceneUiActions(new("x", GameplayUiActionKind.Activate), new("x", GameplayUiActionKind.Activate)));
                f.Routes.Validate(f.Definition);
            }),
            ("Route installation is atomic when it overlaps another startup producer", () => {
                var source = new SceneDocument(); using var play = new PlaySession(source.BakeRuntime(source.Revision).Image.Instantiate(source.Components)); play.RegisterUiAction("existing", GameplayUiActionKind.Activate);
                var routes = new SceneUiActions(new("new", GameplayUiActionKind.Activate), new("existing", GameplayUiActionKind.Activate));
                Reject(() => routes.Install(play)); Check(!play.AcceptsUiAction("new", GameplayUiActionKind.Activate));
            }),
            ("Real UiRuntime click is copied through application route into one committed fixed step", () => {
                using var f = new Fixture(); Check(f.Bridge.Observe()); f.Click(); f.Submit(); Check(f.Probe.Count == 0 && f.Play.Scene.World.GetName(f.Play.Scene.World.FindEntity(f.Probe.Target)) == "Before");
                byte[] authored = f.Document.CaptureBytes();
                f.Step(); Check(f.Probe.Count == 1 && f.Play.Scene.World.GetName(f.Play.Scene.World.FindEntity(f.Probe.Target)) == "HUD committed");
                Check(authored.SequenceEqual(f.Document.CaptureBytes()) && f.Document.GetObjectName(f.Probe.Target) == "Before");
                Check(f.Bridge.Observe()); f.Submit(); f.Step(); Check(f.Probe.Count == 1);
            }),
            ("Text, slider and button semantic kinds originate only from their declared widgets", () => {
                using var f = new Fixture(); f.Bridge.Observe();
                f.Ui.Input(f.Ui.Stamp, [new(UiInputKind.PointerDown, 1, new(20, 50)), new(UiInputKind.Text, 2, default, "中文😀")]);
                f.Submit(); f.Step(); Check(f.Probe.Count == 1 && f.Probe.Text == "中文😀");
                f.Bridge.Observe(); f.Ui.Input(f.Ui.Stamp, [new(UiInputKind.PointerDown, 3, new(50, 90))]);
                f.Submit(); f.Step(); Check(f.Probe.Count == 2);
            }),
            ("Complete forged batch fails without consuming sequence or accepting a prefix", () => {
                using var f = new Fixture(); f.Bridge.Observe(); f.Click(); int n = f.Ui.Drain(f.Ui.Stamp, f.Buffer); Check(n == 1);
                var action = f.Buffer[0]; var invalid = new[] {
                    action with { Stamp = action.Stamp with { Generation = action.Stamp.Generation + 1 } },
                    action with { Element = Guid.NewGuid() }, action with { Action = "text" }, action with { Kind = UiInputKind.Text },
                    action with { Value = .5f }, action with { Text = "forged" }
                };
                foreach (var bad in invalid) Reject(() => f.Bridge.Submit([action, bad with { Sequence = 3 }]));
                f.Bridge.Submit([action]); f.Step(); Check(f.Probe.Count == 1);
            }),
            ("Pause/resume and explicit cancellation revoke both queues and stale copied actions", () => {
                using var f = new Fixture(); f.Bridge.Observe(); f.Click(); f.Ui.Drain(f.Ui.Stamp, f.Buffer); var old = f.Buffer[0];
                f.Bridge.Submit([old]); f.Play.Pause(); Check(!f.Bridge.Observe()); f.Play.Resume(); Check(f.Bridge.Observe());
                Reject(() => f.Bridge.Submit([old])); f.Step(); Check(f.Probe.Count == 0);
                f.Bridge.Observe(); f.Click(); f.Submit(); f.Bridge.Cancel(); f.Step(); Check(f.Probe.Count == 0);
            }),
            ("Normal ticks preserve focus, external revision and UI topology cancellation revoke input", () => {
                using var f = new Fixture(); f.Bridge.Observe(); f.Click(); f.Submit(); var focus = f.Ui.Focus; f.Step();
                f.Bridge.Observe(); Check(f.Ui.Focus == focus); var stamp = f.Ui.Stamp;
                f.Play.Scene.World.Rename(f.Play.Scene.World.FindEntity(f.Probe.Target), "External"); f.Bridge.Observe(); Check(f.Ui.Stamp.Generation > stamp.Generation && f.Ui.Focus == Guid.Empty);
                f.Click(); f.Submit(); f.Ui.CancelInput(); f.Bridge.Observe(); f.Step(); Check(f.Probe.Count == 1);
            }),
            ("Another producer cannot be silently stolen or cancelled", () => {
                using var f = new Fixture(); f.Bridge.Observe(); var other = f.Play.BindUiInput(Guid.NewGuid(), 1);
                Reject(() => f.Bridge.Observe()); f.Bridge.Cancel(); Check(f.Play.CurrentUiInputStamp == other);
            }),
            ("Shared route does not introduce native/render/editor dependencies", () => {
                var names = typeof(SceneUiInput).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToArray();
                Check(!names.Any(n => n.Contains("Editor") || n.Contains("Rendering") || n.Contains("Interop") || n.Contains("Platform") || n.Contains("Gui")));
                using var f = new Fixture(); Task.Run(() => Reject(() => f.Bridge.Observe())).GetAwaiter().GetResult();
                f.Bridge.Dispose(); Reject(() => f.Bridge.Observe());
            })
        };
        CaseHarness.RunOrThrow("Scene UI routing", cases);
        Console.WriteLine($"Scene UI routing: {cases.Length}/{cases.Length} groups passed.");
    }
}
