using System.Text.Json;
using Ncma.Editor.Services;
using Ncma.Gameplay;
using Ncma.Runtime;
using Ncma.Scene;

// Prepared now; executed only after all E6 substages are implemented.
internal static partial class RuntimeChangeReviewTests
{
    private sealed class IndexedIds(Guid[] ids, Action? read = null) : IReadOnlyList<Guid>
    {
        public int Count => ids.Length;
        public Guid this[int index] { get { read?.Invoke(); return ids[index]; } }
        public IEnumerator<Guid> GetEnumerator() => throw new Exception("Caller enumeration must not execute");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
    private readonly record struct BudgetA(int Value) : IComponent;
    private readonly record struct BudgetB(int Value) : IComponent;
    private readonly record struct BudgetC(int Value) : IComponent;
    private readonly record struct BudgetD(int Value) : IComponent;
    internal static IEnumerable<(string, Action)> ApplyCases(string repository)
    {
        yield return ("E6 reviewed input uses bounded index copies and rechecks callback invalidation", () => {
            using var f = new Fixture(repository);
            f.Play.Scene.World.Destroy(f.Play.Scene.World.FindEntity(f.Deleted));
            var prepared = f.Changes.PrepareApply(f.Changes.Capture(new IndexedIds([f.Deleted])));
            Reject(() => f.Changes.ConfirmStopAndApply(prepared, prepared.Fingerprint, new IndexedIds([f.Deleted], f.Changes.Discard)));
            Check(f.Owner.Play is not null && f.Owner.Document.ContainsObject(f.Deleted));
            var fresh = f.Changes.PrepareApply(f.Changes.Capture([f.Deleted]));
            Check(f.Changes.ConfirmStopAndApply(fresh, fresh.Fingerprint, new IndexedIds([f.Deleted])).Changed);
        });
        yield return ("E6 runtime object expansion enforces original operation budget before stopping", () => {
            var registry = global::Ncma.Runtime.CompositionRegistry.Build();
            const string schema = """{"type":"object","required":["value"],"properties":{"value":{"type":"integer"}}}""";
            registry.Register<BudgetA>("budget.a", 1, schema, v => v);
            registry.Register<BudgetB>("budget.b", 1, schema, v => v);
            registry.Register<BudgetC>("budget.c", 1, schema, v => v);
            registry.Register<BudgetD>("budget.d", 1, schema, v => v);
            using var owner = new EditorSessionOwner("Expansion", components: registry);
            owner.LoadGameplay(Path.Combine(repository, "out/managed/Ncma.Gameplay.Sample.dll"));
            var play = owner.StartPlay(); play.Pause(); var ids = new Guid[32];
            byte[] before = owner.Document.CaptureBytes(); var state = owner.Edit!.State;
            for (int i = 0; i < ids.Length; i++)
            {
                ids[i] = Guid.NewGuid(); var entity = play.Scene.World.CreateEntity("Created", ids[i]);
                play.Scene.World.Set(entity, new BudgetA(i)); play.Scene.World.Set(entity, new BudgetB(i));
                play.Scene.World.Set(entity, new BudgetC(i)); play.Scene.World.Set(entity, new BudgetD(i));
            }
            var review = owner.RuntimeChanges!.Capture(ids);
            Check(review.CopyData().Changes.Length == 32 && review.ByteCount < EditorRuntimeChanges.MaxReviewBytes);
            Reject(() => owner.RuntimeChanges.PrepareApply(review)); // 32 object diffs expand to 160 original operations.
            Check(owner.RuntimeChanges.IsCurrent(review) && owner.Play == play && before.SequenceEqual(owner.Document.CaptureBytes()) && owner.Edit.State == state);
        });
        yield return ("E6 changed normalization is rejected before confirmation and Play stop", () => {
            int drift = 0; var registry = global::Ncma.Runtime.CompositionRegistry.Build();
            registry.Register<BudgetA>("normalize.a", 1,
                """{"type":"object","required":["value"],"properties":{"value":{"type":"integer"}}}""", v => new(v.Value + drift));
            using var owner = new EditorSessionOwner("Normalization", components: registry);
            owner.LoadGameplay(Path.Combine(repository, "out/managed/Ncma.Gameplay.Sample.dll"));
            Guid id = owner.Document.CreateObject("Original"); owner.Document.SetComponent(id, new BudgetA(1)); owner.Edit!.Resynchronize();
            var play = owner.StartPlay(); play.Pause(); play.Scene.World.Rename(play.Scene.World.FindEntity(id), "Runtime name");
            var review = owner.RuntimeChanges!.Capture([id]); byte[] before = owner.Document.CaptureBytes(); var state = owner.Edit.State;
            drift = 1; Reject(() => owner.RuntimeChanges.PrepareApply(review));
            Check(owner.Play == play && before.SequenceEqual(owner.Document.CaptureBytes()) && owner.Edit.State == state);
            drift = 0; var prepared = owner.RuntimeChanges.PrepareApply(review);
            Check(owner.RuntimeChanges.ConfirmStopAndApply(prepared, prepared.Fingerprint, []).Changed);
        });
        yield return ("E6 reviewed Play changes stop and use one original transaction history", () => {
            using var f = new Fixture(repository); var document = f.Owner.Document; var edit = f.Owner.Edit!;
            byte[] before = document.CaptureBytes(); var history = edit.State; var world = f.Play.Scene.World;
            world.Rename(world.FindEntity(f.Changed), "Kept name");
            var transform = TransformData.Identity with { Position = new(7, 8, 9) };
            world.Set(world.FindEntity(f.Changed), transform);
            world.Destroy(world.FindEntity(f.Deleted));
            world.Set(world.CreateEntity("Kept creation", f.Created), TransformData.Identity);
            var prepared = f.Changes.PrepareApply(f.Changes.Capture([f.Changed, f.Deleted, f.Created]));
            Check(f.Owner.Play is not null && before.SequenceEqual(document.CaptureBytes()) && edit.State == history);
            using (var commands = JsonDocument.Parse(prepared.CopyOriginalCommands()))
                Check(commands.RootElement.GetProperty("commands").EnumerateArray().Any(c =>
                    c.GetProperty("capability").GetString() == "ncma.scene.delete_object"));
            var result = f.Changes.ConfirmStopAndApply(prepared, prepared.Fingerprint, [f.Deleted]);
            Check(result.Status == "ok" && result.Changed && f.Owner.Play is null && !edit.State.Frozen);
            Check(edit.State.UndoCount == history.UndoCount + 1 && edit.Revision == history.Revision + 1);
            var snapshot = document.CaptureSnapshot();
            Check(snapshot.Objects.Single(o => o.Id == f.Changed).Name == "Kept name" &&
                snapshot.Objects.Any(o => o.Id == f.Created && o.Name == "Kept creation") &&
                snapshot.Objects.All(o => o.Id != f.Deleted) && snapshot.Objects.Single(o => o.Id == f.Plain).Name == "Plain");
            Check(document.GetComponent<TransformData>(f.Changed) == transform);
            byte[] after = document.CaptureBytes(); var workspace = new EditorWorkspace(f.Owner);
            workspace.History(workspace.Stamp, false); Check(before.SequenceEqual(document.CaptureBytes()));
            workspace.History(workspace.Stamp, true); Check(after.SequenceEqual(document.CaptureBytes()));
            Reject(() => f.Changes.ConfirmStopAndApply(prepared, prepared.Fingerprint, [f.Deleted]));
        });
        yield return ("E6 Play change confirmation requires exact full review and every deletion UUID", () => {
            using var f = new Fixture(repository); var world = f.Play.Scene.World;
            byte[] before = f.Owner.Document.CaptureBytes();
            world.Destroy(world.FindEntity(f.Deleted)); world.Destroy(world.FindEntity(f.Plain));
            var prepared = f.Changes.PrepareApply(f.Changes.Capture([f.Deleted, f.Plain]));
            Reject(() => f.Changes.ConfirmStopAndApply(prepared, "wrong", [f.Deleted, f.Plain]));
            Reject(() => f.Changes.ConfirmStopAndApply(prepared, prepared.Fingerprint, [f.Deleted]));
            Reject(() => f.Changes.ConfirmStopAndApply(prepared, prepared.Fingerprint, [f.Deleted, f.Deleted]));
            Reject(() => f.Changes.ConfirmStopAndApply(prepared, prepared.Fingerprint, [f.Deleted, f.Plain, f.Changed]));
            var copy = prepared.CopyDeletionTargets(); copy[0] = f.Changed;
            var bytes = prepared.CopyOriginalCommands(); bytes[0] = 0;
            Check(f.Changes.IsPreparedCurrent(prepared) && f.Play.State == PlayState.Paused && before.SequenceEqual(f.Owner.Document.CaptureBytes()));
            Check(f.Changes.ConfirmStopAndApply(prepared, prepared.Fingerprint, [f.Plain, f.Deleted]).Changed);
        });
        yield return ("E6 prepared runtime command rejects stale runtime foreign and replaced tickets", () => {
            using var a = new Fixture(repository); using var b = new Fixture(repository);
            a.Play.Scene.World.Rename(a.Play.Scene.World.FindEntity(a.Plain), "Changed");
            var prepared = a.Changes.PrepareApply(a.Changes.Capture([a.Plain]));
            Reject(() => b.Changes.ConfirmStopAndApply(prepared, prepared.Fingerprint, []));
            Task.Run(() => Reject(() => a.Changes.ConfirmStopAndApply(prepared, prepared.Fingerprint, []))).GetAwaiter().GetResult();
            a.Play.Scene.World.Rename(a.Play.Scene.World.FindEntity(a.Plain), "Changed twice");
            Reject(() => a.Changes.ConfirmStopAndApply(prepared, prepared.Fingerprint, []));
            var current = a.Changes.PrepareApply(a.Changes.Capture([a.Plain]));
            var replacement = a.Changes.PrepareApply(current.Review);
            Reject(() => a.Changes.ConfirmStopAndApply(current, current.Fingerprint, []));
            a.Play.Resume(); Reject(() => a.Changes.ConfirmStopAndApply(replacement, replacement.Fingerprint, []));
            Check(a.Owner.Document.CaptureSnapshot().Objects.Single(o => o.Id == a.Plain).Name == "Plain");
        });
        yield return ("E6 failed Play stop consumes approval and cannot write authoring during callbacks", () => {
            EditorSessionOwner? owner = null; bool interfere = true; Guid id = Guid.Empty;
            using var f = new Fixture(repository, beforeStop: () => { if (interfere) owner!.Document.RenameObject(id, "Illegal stop write"); });
            owner = f.Owner; id = f.Plain;
            byte[] before = owner.Document.CaptureBytes(); var state = owner.Edit!.State;
            f.Play.Scene.World.Rename(f.Play.Scene.World.FindEntity(id), "Kept");
            var prepared = f.Changes.PrepareApply(f.Changes.Capture([id]));
            try
            {
                Reject(() => f.Changes.ConfirmStopAndApply(prepared, prepared.Fingerprint, []));
                Check(owner.Play is not null && before.SequenceEqual(owner.Document.CaptureBytes()) && owner.Edit.State == state);
                Check(!f.Changes.IsPreparedCurrent(prepared));
            }
            finally { interfere = false; }
            Reject(() => f.Changes.ConfirmStopAndApply(prepared, prepared.Fingerprint, []));
            var fresh = f.Changes.PrepareApply(f.Changes.Capture([id]));
            Check(f.Changes.ConfirmStopAndApply(fresh, fresh.Fingerprint, []).Changed);
        });
        yield return ("E6 original change expansion preserves component and complete binding removals", () => {
            using var f = new Fixture(repository); var world = f.Play.Scene.World;
            byte[] before = f.Owner.Document.CaptureBytes();
            world.Destroy(world.FindEntity(f.Changed)); world.CreateEntity("Changed", f.Changed);
            var prepared = f.Changes.PrepareApply(f.Changes.Capture([f.Changed]));
            Check(prepared.Review.CopyData().Changes.Select(c => c.Kind).Order().SequenceEqual(new[] { "remove_component", "set_bindings" }));
            Check(f.Changes.ConfirmStopAndApply(prepared, prepared.Fingerprint, []).Changed);
            var item = f.Owner.Document.CaptureSnapshot().Objects.Single(o => o.Id == f.Changed);
            Check(item.Components.Length == 0 && item.Behaviours.Length == 0);
            var workspace = new EditorWorkspace(f.Owner); workspace.History(workspace.Stamp, false);
            Check(before.SequenceEqual(f.Owner.Document.CaptureBytes()));
        });
        yield return ("E6 empty runtime differences cannot silently stop Play or create history", () => {
            using var f = new Fixture(repository); var state = f.Owner.Edit!.State;
            var review = f.Changes.Capture([f.Plain]); Reject(() => f.Changes.PrepareApply(review));
            Check(f.Changes.IsCurrent(review) && f.Play.State == PlayState.Paused && f.Owner.Edit.State == state);
        });
    }
}
