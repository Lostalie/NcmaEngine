using System.Reflection;
using Ncma;
using Ncma.Gameplay;
using Ncma.Runtime;
using Ncma.Scene;

internal static class IncrementalCommitCases
{
    private static void Check(bool value) { if (!value) throw new Exception("Incremental commit assertion failed."); }
    private static object Field(object owner, string name) => owner.GetType().GetField(name,
        BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static void Reject(Action action)
    {
        try { action(); } catch (ArgumentException) { return; }
        throw new Exception("Uncommitted receipt accepted.");
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly SceneDocument Document = new("Incremental commit");
        internal readonly Guid Id, Binding = Guid.NewGuid();
        internal readonly Entity Entity;
        internal readonly Probe Probe = new();
        internal readonly PlaySession Play;
        internal Fixture()
        {
            Entity = Document.World.CreateEntity("Object"); Id = Document.World.GetUuid(Entity);
            Document.World.Set(Entity, TransformData.Identity);
            Document.SetBindings(Id, [new(Binding, "Probe", true, [new("Value", ExportKind.Double, 3)])]);
            Play = new(Document);
        }
        internal void Start(Func<BehaviourBindingData, Behaviour>? factory = null)
        { Play.Start(factory ?? (_ => Probe)); Step(); }
        internal void Step() { Play.AdvanceFrame(1d / 60); Check(Play.State == PlayState.Running); }
        internal void Once(Action action) => Probe.Fixed = _ => { Probe.Fixed = null; action(); };
        public void Dispose() => Play.Dispose();
    }
    private static ComponentSnapshot Transform(Fixture f, float x) => new("ncma.transform", 1,
        f.Document.World.Components.Encode(TransformData.Identity with { Position = new(x, 0, 0) }));
    internal static void Run()
    {
        var cases = new (string Name, Action Run)[]
        {
            ("runtime noncanonical bindings normalize at the next boundary without mutating current factory input", () => {
                using var f = new Fixture(); var seen = new List<string>();
                f.Start(b => { if (b.Id != f.Binding) seen.Add(b.TypeName); return b.Id == f.Binding ? f.Probe : new Probe(); });
                f.Once(() => f.Play.Commands.AttachBehaviour(f.Id, new(Guid.NewGuid(), "Type.\ud800", true, [])));
                f.Step(); Check(seen.SequenceEqual(new[] { "Type.\ud800" }));
                f.Step(); Check(seen.SequenceEqual(new[] { "Type.\ud800", "Type.\ufffd" }));
                f.Step(); Check(seen.Count == 2 && f.Document.CaptureSnapshot().Objects[0].Behaviours.Any(b => b.TypeName == "Type.\ufffd"));
            }),
            ("no-write Play preserves restored noncanonical Export ordering", () => {
                using var f = new Fixture(); var snapshot = f.Document.CaptureSnapshot(); var obj = snapshot.Objects[0];
                snapshot.Objects[0] = obj with { Behaviours = [obj.Behaviours[0] with {
                    Exports = [new("Z", ExportKind.Double, -0d), new("A", ExportKind.Double, 2)] }] };
                f.Document.RestoreSnapshot(snapshot); byte[] before = f.Document.CaptureBytes();
                f.Start(); f.Step(); f.Step(); Check(f.Document.CaptureBytes().SequenceEqual(before));
            }),
            ("stable steps retain metadata and instance roots without validation snapshots", () => {
                using var f = new Fixture(); f.Start();
                var instances = Field(f.Play, "_instances"); var metadata = Field(f.Document, "_bindings");
                var commands = Field(f.Play, "_commands"); var bindings = Field(commands, "_committedBindings");
                long snapshots = (long)Field(f.Document.World, "_copiedValidationSnapshotCount");
                for (int i = 0; i < 20; ++i) f.Step();
                Check(ReferenceEquals(instances, Field(f.Play, "_instances")) && ReferenceEquals(metadata, Field(f.Document, "_bindings")));
                Check(ReferenceEquals(bindings, Field(commands, "_committedBindings")) && (long)Field(f.Document.World, "_copiedValidationSnapshotCount") == snapshots);
                Check(f.Probe.Created == 1 && f.Probe.Enabled == 1 && f.Probe.FixedCount == 21);
            }),
            ("value and rename-only batches do not rebuild instances", () => {
                using var f = new Fixture(); f.Start(); var instances = Field(f.Play, "_instances");
                f.Once(() => {
                    var value = f.Probe.GameObject.LocalTransform; value.Position.X = 4; f.Probe.GameObject.LocalTransform = value;
                    f.Play.Commands.SubmitBatch([new StructuralCommand.Rename(StructuralTarget.Existing(f.Entity), "Renamed")]);
                    Check(f.Document.World.Get<TransformData>(f.Entity).Position.X == 0);
                });
                f.Step(); Check(ReferenceEquals(instances, Field(f.Play, "_instances")) && f.Document.World.GetName(f.Entity) == "Renamed");
                f.Play.Pause(); Check(f.Play.RenderView.Objects.Single().Transform.Position.X == 4);
            }),
            ("enable metadata changes reuse behaviour and consume lifecycle once", () => {
                using var f = new Fixture(); int factories = 0; f.Start(_ => { factories++; return f.Probe; });
                f.Once(() => f.Play.Commands.SetBehaviourEnabled(f.Id, f.Binding, false)); f.Step();
                Check(f.Probe.Disabled == 0 && factories == 1); f.Step(); f.Step(); Check(f.Probe.Disabled == 1);
                f.Document.SetBindings(f.Id, [new(f.Binding, "Probe", true, [])]); f.Step(); f.Step(); f.Step();
                Check(f.Probe.Enabled == 2 && f.Probe.Disabled == 1 && factories == 1);
            }),
            ("same binding UUID with a new type retires the original exactly once", () => {
                using var f = new Fixture(); var replacement = new Probe();
                f.Start(b => b.TypeName == "Probe" ? f.Probe : replacement);
                f.Once(() => { f.Play.Commands.RemoveBehaviour(f.Id, f.Binding);
                    f.Play.Commands.AttachBehaviour(f.Id, new(f.Binding, "Replacement", true, [])); });
                f.Step(); Check(replacement.Created == 0 && f.Probe.Destroyed == 0);
                f.Step(); f.Step(); Check(replacement.Created == 1 && replacement.Enabled == 1 && f.Probe.Disabled == 1 && f.Probe.Destroyed == 1);
            }),
            ("factory Export arrays cannot rewrite committed or retained command metadata", () => {
                using var f = new Fixture(); var extra = Guid.NewGuid(); var child = new Probe();
                f.Start(b => { b.Exports[0] = new("changed", ExportKind.Double, 999); return b.Id == f.Binding ? f.Probe : child; });
                Check(f.Document.GetBindings(f.Id)[0].Exports[0].Value == 3);
                f.Once(() => f.Play.Commands.AttachBehaviour(f.Id, new(extra, "Child", true, [new("Value", (uint)ExportKind.Double, 7)])));
                f.Step(); f.Step(); Check(f.Document.GetBindings(f.Id).Single(b => b.Id == extra).Exports[0].Value == 7);
                Check(f.Document.GetBindings(f.Id).Single(b => b.Id == f.Binding).Exports[0].Value == 3 && child.Created == 1);
            }),
            ("late factory failure installs no metadata instances receipts or interpolation", () => {
                using var f = new Fixture(); f.Start(b => b.TypeName == "Probe" ? f.Probe : throw new Exception("factory failure"));
                var instances = Field(f.Play, "_instances"); byte[] before = f.Document.CaptureBytes(); var tick = f.Play.Tick;
                CommandToken token = default;
                f.Once(() => { var value = f.Probe.GameObject.LocalTransform; value.Position.X = 19; f.Probe.GameObject.LocalTransform = value;
                    token = f.Play.Commands.AttachBehaviour(f.Id, new(Guid.NewGuid(), "Bad", true, [])); });
                f.Play.AdvanceFrame(1d / 60);
                Check(f.Play.State == PlayState.Faulted && f.Play.Tick == tick && f.Document.CaptureBytes().SequenceEqual(before));
                Check(ReferenceEquals(instances, Field(f.Play, "_instances")) && f.Play.RenderView.Objects.Single().Transform.Position.X == 0);
                Reject(() => f.Play.Commands.GetReceipt(token));
            }),
            ("boundary authoring invalidates cached metadata without a full component document", () => {
                using var f = new Fixture(); var replacement = new Probe(); f.Start(b => b.TypeName == "Probe" ? f.Probe : replacement);
                f.Document.SetBindings(f.Id, [new(f.Binding, "New", true, [new("Value", ExportKind.Double, 5)])]);
                f.Step(); f.Step(); Check(replacement.Created == 1 && f.Probe.Destroyed == 1);
                Check(f.Document.GetBindings(f.Id)[0].TypeName == "New");
            }),
            ("restored World identities never reuse the old high-level instance", () => {
                using var f = new Fixture(); var replacement = new Probe(); int factories = 0;
                f.Start(_ => ++factories == 1 ? f.Probe : replacement);
                f.Document.RestoreBytes(f.Document.CaptureBytes()); f.Step(); f.Step();
                Check(factories == 2 && replacement.GameObject.Entity != f.Entity && f.Probe.Destroyed == 1 && replacement.Created == 1);
                Check(replacement.GameObject.Entity == f.Document.World.FindEntity(f.Id));
            }),
            ("typed interpolation handles old values optional Transform deletion and creation", () => {
                using var f = new Fixture(); f.Start(); var id = Guid.NewGuid(); PendingEntity? pending = null;
                f.Once(() => { var value = f.Probe.GameObject.LocalTransform; value.Position.X = 10; f.Probe.GameObject.LocalTransform = value;
                    pending = f.Play.Commands.SpawnEmpty("New", id); f.Play.Commands.AddComponent(id, Transform(f, 7)); });
                f.Play.AdvanceFrame(1.5d / 60); Check(f.Play.State == PlayState.Running);
                Check(Math.Abs(f.Play.RenderView.Objects.Single(o => o.ObjectId == f.Id).Transform.Position.X - 5) < 0.001);
                Check(f.Play.RenderView.Objects.Single(o => o.ObjectId == id).Transform.Position.X == 7);
                Check(f.Play.Commands.Resolve(pending!) == f.Document.World.FindEntity(id));
                f.Once(() => { f.Play.Commands.RemoveComponent(f.Id, "ncma.transform"); f.Play.Commands.Destroy(id); }); f.Step();
                Check(f.Play.RenderView.Objects.Length == 0); Reject(() => f.Play.Commands.Resolve(pending!));
            }),
            ("factories keep UUID ordering independent of submission order", () => {
                using var f = new Fixture(); var built = new List<Guid>();
                f.Start(b => { built.Add(b.Id); return b.Id == f.Binding ? f.Probe : new Probe(); }); built.Clear();
                var ids = new[] { Guid.Parse("00000001-0000-0000-0000-000000000000"), Guid.Parse("00000002-0000-0000-0000-000000000000") };
                var bindings = new[] { Guid.NewGuid(), Guid.NewGuid() };
                f.Once(() => { for (int i = 1; i >= 0; --i) { f.Play.Commands.SpawnEmpty("New", ids[i]);
                    f.Play.Commands.AttachBehaviour(ids[i], new(bindings[i], "New", true, [])); } });
                f.Step(); Check(built.SequenceEqual(bindings)); f.Step(); Check(built.Count == 2);
            }),
        };
        foreach (var test in cases) { test.Run(); Console.WriteLine("PASS incremental commit " + test.Name); }
        Console.WriteLine($"Incremental commit tests: {cases.Length}/{cases.Length} passed.");
    }
}
