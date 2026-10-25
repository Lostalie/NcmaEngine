using System.Text.Json;
using Ncma.Editor.Core;
using Ncma.Scene;

// Compile now; execute only in the consolidated E6 validation phase.
internal static class OriginalCommandBatchTests
{
    private readonly record struct Normalized(int Value) : Ncma.Runtime.IComponent;
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
    private static void Check(bool ok, string message = "Original command batch") { if (!ok) throw new Exception(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return; }
        throw new Exception("Expected rejection");
    }
    private sealed class Fixture
    {
        internal readonly SceneDocument Document = new("Batch");
        internal readonly Guid A, B;
        internal readonly Guid Created = Guid.NewGuid();
        internal readonly EditSession Edit;
        internal Fixture() { A = Document.CreateObject("A"); B = Document.CreateObject("B"); Edit = new(Document); }
        internal CapabilityPermissions Permissions(bool deletion = true, Guid[]? targets = null, Func<bool>? current = null) => new(
            deletion ? ["ncma.scene.transaction", "ncma.scene.delete_object", "ncma.history.undo", "ncma.history.redo"] :
                ["ncma.scene.transaction", "ncma.history.undo", "ncma.history.redo"],
            targets ?? [A, B], objectScope: [A, B, Created], createScope: [Created], componentScope: [], bindingScope: [], isCurrent: current);
        internal OriginalEditCommand[] Commands() => [
            new("ncma.scene.transaction", Json(new { operations = new object[] {
                new { op = "rename", objectId = A, name = "Changed" }, new { op = "create", objectId = Created, name = "New" } } })),
            new("ncma.scene.delete_object", Json(new { objectId = B })) ];
        internal CapabilityRequest History(bool redo = false, Guid? id = null) =>
            new(2, id ?? Guid.NewGuid(), Edit.SessionId, Edit.Revision, redo ? "ncma.history.redo" : "ncma.history.undo", Json(new { }));
    }
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("E6 original batch retains every destructive UUID through multi-delete history", () => {
            var f = new Fixture(); var review = f.Edit.ReviewOriginalBatch("Two removals", f.Edit.Revision, [
                new("ncma.scene.delete_object", Json(new { objectId = f.A })),
                new("ncma.scene.delete_object", Json(new { objectId = f.B })) ]);
            byte[] before = f.Document.CaptureBytes();
            Check(f.Edit.ApplyOriginalBatch(review, Guid.NewGuid(), f.Permissions(targets: [f.A])).Status == "denied");
            Check(before.SequenceEqual(f.Document.CaptureBytes()) && f.Edit.State.UndoCount == 0);
            Check(f.Edit.ApplyOriginalBatch(review, Guid.NewGuid(), f.Permissions()).Changed && f.Document.ObjectCount == 0);
            Check(f.Edit.HistoryDestructiveTargets(false).Order().SequenceEqual(new[] { f.A, f.B }.Order()));
            Check(f.Edit.Invoke(f.History(), f.Permissions(targets: [f.B])).Status == "denied" && f.Document.ObjectCount == 0);
            Check(f.Edit.Invoke(f.History(), f.Permissions()).Changed && before.SequenceEqual(f.Document.CaptureBytes()));
            Check(f.Edit.Invoke(f.History(true), f.Permissions(targets: [f.A])).Status == "denied");
        });
        yield return ("E6 original batch normalization drift refuses the whole candidate before installation", () => {
            int drift = 0; var registry = global::Ncma.Runtime.CompositionRegistry.Build();
            registry.Register<Normalized>("test.normalized", 1,
                """{"type":"object","required":["value"],"properties":{"value":{"type":"integer"}}}""", v => new(v.Value + drift));
            var d = new SceneDocument("Drift", registry); Guid id = d.CreateObject("Original"); d.SetComponent(id, new Normalized(1));
            Guid removed = d.CreateObject("Remove"); var edit = new EditSession(d);
            var review = edit.ReviewOriginalBatch("Drift batch", edit.Revision, [
                new("ncma.scene.transaction", Json(new { operations = new[] { new { op = "rename", objectId = id, name = "Changed" } } })),
                new("ncma.scene.delete_object", Json(new { objectId = removed })) ]);
            byte[] before = d.CaptureBytes(); var state = edit.State; drift = 1;
            var result = edit.ApplyOriginalBatch(review, Guid.NewGuid(), new(["ncma.scene.transaction", "ncma.scene.delete_object"], [removed]));
            Check(result.Code == "batch_normalization_changed" && before.SequenceEqual(d.CaptureBytes()) && edit.State == state);
        });
        yield return ("E6 original batch is atomic with sole history and immutable review inputs", () => {
            var f = new Fixture(); byte[] before = f.Document.CaptureBytes(); var state = f.Edit.State;
            var review = f.Edit.ReviewOriginalBatch("Keep runtime changes", f.Edit.Revision, f.Commands());
            Check(before.SequenceEqual(f.Document.CaptureBytes()) && f.Edit.State == state);
            byte[] copy = review.CopyCommandBytes(); copy[0] = 0;
            var result = f.Edit.ApplyOriginalBatch(review, Guid.NewGuid(), f.Permissions());
            Check(result.Changed && result.Status == "ok" && f.Edit.State.UndoCount == 1 && f.Edit.Revision == state.Revision + 1);
            Check(f.Document.GetObjectName(f.A) == "Changed" && !f.Document.ContainsObject(f.B) && f.Document.ContainsObject(f.Created));
            byte[] after = f.Document.CaptureBytes();
            var targets = f.Edit.HistoryDestructiveTargets(false); Check(targets.SequenceEqual(new[] { f.B })); targets[0] = Guid.Empty;
            Check(f.Edit.Invoke(f.History(), f.Permissions()).Changed && before.SequenceEqual(f.Document.CaptureBytes()));
            Check(f.Edit.Invoke(f.History(true), f.Permissions()).Changed && after.SequenceEqual(f.Document.CaptureBytes()));
        });
        yield return ("E6 original batch requires delete capability exact UUID and rechecks cached authorization", () => {
            var f = new Fixture(); var review = f.Edit.ReviewOriginalBatch("Mixed", f.Edit.Revision, f.Commands());
            byte[] before = f.Document.CaptureBytes(); Guid request = Guid.NewGuid();
            Check(f.Edit.ApplyOriginalBatch(review, request).Status == "denied");
            Check(f.Edit.ApplyOriginalBatch(review, request, f.Permissions(false)).Status == "denied");
            Check(f.Edit.ApplyOriginalBatch(review, request, f.Permissions(targets: [f.A])).Status == "denied");
            Check(before.SequenceEqual(f.Document.CaptureBytes()) && f.Edit.State.UndoCount == 0);
            Check(f.Edit.ApplyOriginalBatch(review, request, f.Permissions()).Changed);
            ulong revision = f.Edit.Revision;
            Check(f.Edit.ApplyOriginalBatch(review, request, f.Permissions(false)).Status == "denied");
            Check(f.Edit.ApplyOriginalBatch(review, request, f.Permissions(targets: [f.A])).Status == "denied");
            Check(f.Edit.ApplyOriginalBatch(review, request, f.Permissions()).Replayed && f.Edit.Revision == revision);
            Check(f.Edit.Invoke(f.History(), f.Permissions(false)).Status == "denied");
            Check(f.Edit.Invoke(f.History(), f.Permissions(targets: [f.A])).Status == "denied");
            var undo = f.History(); Check(f.Edit.Invoke(undo, f.Permissions()).Changed);
            Check(f.Edit.Invoke(undo, f.Permissions(false)).Status == "denied", "Cached Undo retains original delete grants");
            Check(f.Edit.Invoke(f.History(true), f.Permissions(false)).Status == "denied");
        });
        yield return ("E6 original batch validates complete candidate and cannot publish a partial prefix", () => {
            var f = new Fixture(); byte[] before = f.Document.CaptureBytes();
            Reject(() => f.Edit.ReviewOriginalBatch("Invalid", f.Edit.Revision, [
                new("ncma.scene.delete_object", Json(new { objectId = f.B })),
                new("ncma.scene.transaction", Json(new { operations = new[] { new { op = "rename", objectId = Guid.NewGuid(), name = "Missing" } } })) ]));
            Reject(() => f.Edit.ReviewOriginalBatch("Unknown", f.Edit.Revision, [new("ncma.scene.restore", Json(new { }))]));
            Reject(() => f.Edit.ReviewOriginalBatch("No hidden delete", f.Edit.Revision,
                [new("ncma.scene.transaction", Json(new { operations = new[] { new { op = "delete", objectId = f.B } } }))]));
            Check(before.SequenceEqual(f.Document.CaptureBytes()) && f.Edit.State.UndoCount == 0);
        });
        yield return ("E6 original batch frozen review stale source foreign owner and no remote entry", () => {
            var f = new Fixture(); f.Edit.SetFrozen(true);
            var review = f.Edit.ReviewOriginalBatch("Frozen review", f.Edit.Revision, f.Commands());
            Check(f.Edit.ApplyOriginalBatch(review, Guid.NewGuid(), f.Permissions()).Code == "play_frozen");
            f.Edit.SetFrozen(false); var foreign = new Fixture();
            Check(foreign.Edit.ApplyOriginalBatch(review, Guid.NewGuid(), foreign.Permissions()).Code == "batch_source_stale");
            Check(!f.Edit.Describe().Any(d => d.Name.Contains("batch", StringComparison.Ordinal)));
            f.Edit.SetBehaviourCatalog(new(Guid.NewGuid(), []));
            Check(f.Edit.ApplyOriginalBatch(review, Guid.NewGuid(), f.Permissions()).Code == "batch_source_stale");
            review = f.Edit.ReviewOriginalBatch("Fresh catalog", f.Edit.Revision, f.Commands());
            Check(f.Edit.ApplyOriginalBatch(review, Guid.NewGuid(), f.Permissions()).Changed);
            Reject(() => f.Edit.DescribeRequestImpact(f.History()));
        });
        yield return ("E6 original batch stores destructive targets even when final content is unchanged", () => {
            var f = new Fixture(); Guid request = Guid.NewGuid();
            var review = f.Edit.ReviewOriginalBatch("Transient creation", f.Edit.Revision, [
                new("ncma.scene.transaction", Json(new { operations = new[] { new { op = "create", objectId = f.Created, name = "Temporary" } } })),
                new("ncma.scene.delete_object", Json(new { objectId = f.Created })) ]);
            Check(f.Edit.ApplyOriginalBatch(review, request, f.Permissions()).Status == "denied");
            var scope = f.Permissions(targets: [f.Created]); var result = f.Edit.ApplyOriginalBatch(review, request, scope);
            Check(result.Code == "no_change" && !result.Changed && f.Edit.State.UndoCount == 0 && !f.Document.ContainsObject(f.Created));
            Check(f.Edit.ApplyOriginalBatch(review, request, f.Permissions(false)).Status == "denied");
            Check(f.Edit.ApplyOriginalBatch(review, request, scope).Replayed);
        });
        yield return ("E6 original batch operation and byte budgets remain bounded", () => {
            var f = new Fixture();
            Reject(() => f.Edit.ReviewOriginalBatch("Empty", f.Edit.Revision, []));
            var operations = Enumerable.Range(0, 128).Select(i => new { op = "rename", objectId = f.A, name = "Name" + i }).ToArray();
            Reject(() => f.Edit.ReviewOriginalBatch("129 operations", f.Edit.Revision, [
                new("ncma.scene.transaction", Json(new { operations })), new("ncma.scene.delete_object", Json(new { objectId = f.B })) ]));
            var huge = Enumerable.Range(0, 128).Select(_ => new { op = "rename", objectId = f.A, name = new string('界', 256) }).ToArray();
            Reject(() => f.Edit.ReviewOriginalBatch("Oversized", f.Edit.Revision, [new("ncma.scene.transaction", Json(new { operations = huge }))]));
            var review = f.Edit.ReviewOriginalBatch("Valid", f.Edit.Revision, f.Commands());
            Check(f.Edit.ApplyOriginalBatch(review, Guid.NewGuid(), f.Permissions(current: () => false)).Status == "denied");
            Check(f.Document.ObjectCount == 2 && f.Edit.State.UndoCount == 0);
        });
    }
}
