using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Gameplay;
using Ncma.Runtime;
using Ncma.Scene;

// Prepared for consolidated E6 validation only; never executed by build checks.
internal static partial class RuntimeChangeReviewTests
{
    private static void Check(bool value, string message = "Runtime change review") { if (!value) throw new Exception(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or EditRejectedException) { return; }
        throw new Exception("Expected runtime review rejection.");
    }
    private readonly record struct Payload(string Text) : IComponent;
    private sealed class Fixture : IDisposable
    {
        internal readonly EditorSessionOwner Owner;
        internal readonly Guid Changed = Guid.Parse("10000000-0000-0000-0000-000000000001");
        internal readonly Guid Deleted = Guid.Parse("20000000-0000-0000-0000-000000000002");
        internal readonly Guid Created = Guid.Parse("30000000-0000-0000-0000-000000000003");
        internal readonly Guid Plain = Guid.Parse("40000000-0000-0000-0000-000000000004");
        internal readonly Guid Binding = Guid.Parse("50000000-0000-0000-0000-000000000005");
        internal EditorRuntimeChanges Changes => Owner.RuntimeChanges!;
        internal PlaySession Play => Owner.Play!;
        internal Fixture(string repository, bool large = false, Action? beforeStop = null)
        {
            var registry = global::Ncma.Runtime.CompositionRegistry.Build();
            if (large) registry.Register<Payload>("review.payload", 1,
                """{"type":"object","required":["text"],"properties":{"text":{"type":"string"}}}""", v => v);
            Owner = new("Review", components: registry, beforePlayStop: beforeStop);
            Owner.LoadGameplay(Path.Combine(repository, "out/managed/Ncma.Gameplay.Sample.dll"));
            var d = Owner.Document;
            d.SetComponent(d.CreateObject("Changed", Changed), TransformData.Identity);
            d.SetComponent(d.CreateObject("Deleted", Deleted), TransformData.Identity);
            d.CreateObject("Plain", Plain);
            d.SetBindings(Changed, [new(Binding, Owner.Catalog.Snapshot.Types.Single().TypeName, false,
                [new("Multiplier", ExportKind.Integer, 1), new("DegreesPerSecond", ExportKind.Float, 90), new("Clockwise", ExportKind.Boolean, 1)])]);
            Owner.Edit!.Resynchronize(); Owner.StartPlay(); Play.Pause();
        }
        public void Dispose() => Owner.Dispose();
    }
    internal static IEnumerable<(string, Action)> Cases(string repository)
    {
        yield return ("E6 runtime change review deterministic unchanged data and export-order normalization", () => {
            using var f = new Fixture(repository); var d = f.Owner.Document; var scene = f.Play.Scene;
            byte[] author = d.CaptureBytes(), runtime = scene.CaptureCheckpoint().CopyBytes(); var history = f.Owner.Edit!.State;
            var a = f.Changes.Capture([f.Changed, f.Plain]); var b = f.Changes.Capture([f.Plain, f.Changed]);
            Check(a.CopyData().Changes.Length == 0 && a.CopyBytes().SequenceEqual(b.CopyBytes()));
            Check(!f.Changes.IsCurrent(a) && f.Changes.IsCurrent(b), "Only the latest exact issued review is current");
            Check(b.CopyData().Authoring == f.Owner.PlayAuthoringSource && b.CopyData().Runtime == scene.GetInspectionStamp());
            Check(author.SequenceEqual(d.CaptureBytes()) && runtime.SequenceEqual(scene.CaptureCheckpoint().CopyBytes()) && history == f.Owner.Edit.State);
        });
        yield return ("E6 runtime change review covers creation deletion names component add remove and values", () => {
            using var f = new Fixture(repository); var world = f.Play.Scene.World; var d = f.Owner.Document;
            byte[] author = d.CaptureBytes(); var history = f.Owner.Edit!.State;
            world.Rename(world.FindEntity(f.Changed), "Runtime name");
            world.Set(world.FindEntity(f.Changed), TransformData.Identity with { Position = new(1, 2, 3) });
            world.Destroy(world.FindEntity(f.Deleted));
            world.Set(world.CreateEntity("Runtime-only", f.Created), TransformData.Identity);
            world.Set(world.FindEntity(f.Plain), TransformData.Identity);
            var review = f.Changes.Capture([f.Created, f.Changed, f.Deleted, f.Plain]); var changes = review.CopyData().Changes;
            Check(changes.Length == 5 && changes.Count(c => c.Kind == "set_component") == 2);
            Check(changes.Single(c => c.Kind == "delete_object").ObjectId == f.Deleted &&
                changes.Single(c => c.Kind == "delete_object").Before!.Value.GetProperty("components").GetArrayLength() == 1);
            Check(changes.Single(c => c.Kind == "create_object").ObjectId == f.Created &&
                changes.Single(c => c.Kind == "create_object").After!.Value.GetProperty("name").GetString() == "Runtime-only");
            Check(changes.Single(c => c.Kind == "rename_object").Before!.Value.GetString() == "Changed");
            world.Remove<TransformData>(world.FindEntity(f.Changed));
            Check(!f.Changes.IsCurrent(review));
            Check(f.Changes.Capture([f.Changed]).CopyData().Changes.Any(c => c.Kind == "remove_component"));
            Check(author.SequenceEqual(d.CaptureBytes()) && history == f.Owner.Edit.State, "Review never applies changes");
        });
        yield return ("E6 runtime change review owns copied arrays bytes and complete binding deletion", () => {
            using var f = new Fixture(repository); var world = f.Play.Scene.World;
            world.Destroy(world.FindEntity(f.Changed)); world.Set(world.CreateEntity("Changed", f.Changed), TransformData.Identity);
            var review = f.Changes.Capture([f.Changed]); var data = review.CopyData();
            Check(data.Changes.Length == 1 && data.Changes[0].Kind == "set_bindings");
            Check(data.Changes[0].Before!.Value.GetArrayLength() == 1 && data.Changes[0].After!.Value.GetArrayLength() == 0);
            data.ObjectIds[0] = Guid.NewGuid(); data.Changes[0] = data.Changes[0] with { Kind = "fake" };
            var bytes = review.CopyBytes(); bytes[0] = 0;
            Check(review.CopyData().ObjectIds[0] == f.Changed && review.CopyData().Changes[0].Kind == "set_bindings" && f.Changes.IsCurrent(review));
        });
        yield return ("E6 runtime change review requires paused exact source and invalidates on lifecycle", () => {
            using var f = new Fixture(repository); var review = f.Changes.Capture([f.Changed]);
            f.Play.Resume(); Check(!f.Changes.IsCurrent(review)); Reject(() => f.Changes.Capture([f.Changed]));
            f.Play.AdvanceFrame(1d / 60); f.Play.Pause(); Check(!f.Changes.IsCurrent(review));
            review = f.Changes.Capture([f.Changed]); Task.Run(() => Reject(() => f.Changes.Capture([f.Changed]))).GetAwaiter().GetResult();
            f.Play.Reload(f.Owner.Catalog.Instantiate); Check(!f.Changes.IsCurrent(review));
            review = f.Changes.Capture([f.Changed]); f.Owner.StopPlay();
            Check(f.Owner.PlayAuthoringSource is null && !f.Changes.IsCurrent(review)); Reject(() => f.Changes.Capture([f.Changed]));
            f.Owner.StartPlay(); f.Play.Pause(); Check(!f.Changes.IsCurrent(review));
            var current = f.Changes.Capture([f.Changed]); f.Changes.Discard(); Check(!f.Changes.IsCurrent(current));
        });
        yield return ("E6 runtime change review enforces exact scope and whole-response budget without truncation", () => {
            using var f = new Fixture(repository, large: true); var original = f.Changes.Capture([f.Changed]);
            Reject(() => f.Changes.Capture([])); Reject(() => f.Changes.Capture([f.Changed, f.Changed]));
            Reject(() => f.Changes.Capture([Guid.Empty])); Reject(() => f.Changes.Capture([Guid.NewGuid()]));
            Reject(() => f.Changes.Capture(Enumerable.Range(0, 33).Select(_ => Guid.NewGuid()).ToArray()));
            Check(f.Changes.IsCurrent(original), "Failed scope reviews do not replace current review");
            f.Play.Scene.World.Set(f.Play.Scene.World.FindEntity(f.Changed), new Payload(new string('x', 64000)));
            // Two complete large before/after values must not be silently cropped to fit 64 KiB.
            f.Owner.StopPlay(); f.Owner.Document.SetComponent(f.Changed, new Payload(new string('a', 64000)));
            f.Owner.Edit!.Resynchronize(); f.Owner.StartPlay(); f.Play.Pause();
            original = f.Changes.Capture([f.Plain]);
            f.Play.Scene.World.Set(f.Play.Scene.World.FindEntity(f.Changed), new Payload(new string('b', 64000)));
            var stable = f.Changes.Capture([f.Plain]); Reject(() => f.Changes.Capture([f.Changed]));
            Check(stable.ByteCount <= EditorRuntimeChanges.MaxReviewBytes && f.Changes.IsCurrent(stable));
        });
        yield return ("E6 runtime change review rejects author changes outside the frozen original transaction", () => {
            using var f = new Fixture(repository); var review = f.Changes.Capture([f.Changed]);
            f.Owner.Document.RenameObject(f.Changed, "Untracked author change");
            Check(!f.Changes.IsCurrent(review)); Reject(() => f.Changes.Capture([f.Changed]));
        });
    }
}
