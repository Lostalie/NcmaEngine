using Ncma.Runtime;
using Ncma.Scene;

internal static class RuntimeSceneStateTests
{
    private static void Check(bool value, string reason)
    { if (!value) throw new Exception(reason); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return; }
        throw new Exception("Invalid runtime state operation accepted.");
    }
    private static RuntimeSceneInstance Make()
    {
        var document = new SceneDocument("Runtime \"预算😀\"");
        var entity = document.CreateObject("Logic");
        document.SetBindings(entity, [new(Guid.NewGuid(), "Game.Logic", true,
            [new("Value", ExportKind.Double, 0)])]);
        return document.BakeRuntime(document.Revision).Image.Instantiate(ComponentRegistry.CreateDefault());
    }
    private static void Commit(RuntimeSceneInstance scene, Dictionary<Guid, BehaviourBindingData[]> bindings, Action? stage = null)
    {
        scene.World.BeginStep();
        try
        {
            stage?.Invoke(); Action? publish = null;
            var commit = scene.World.PrepareStepView(view => publish = scene.PrepareRuntimeStep(view, bindings));
            commit(); publish!();
        }
        finally { scene.World.AbortStep(); }
    }
    public static IEnumerable<(string, Action)> Cases()
    {
        yield return ("E6-A runtime transaction uses exact independent byte envelope and no whole snapshot", () => {
            var scene = Make(); var bindings = scene.CopyRuntimeBindings();
            long snapshots = scene.CapturedSnapshotCount; Commit(scene, bindings);
            long encoded = scene.RuntimeValidationWork.BindingEncodes;
            for (int i = 0; i < 3; ++i) Commit(scene, bindings);
            Check(scene.CapturedSnapshotCount == snapshots && scene.RuntimeValidationWork.BindingEncodes == encoded,
                "Unchanged runtime transaction rebuilt snapshots/binding encodings");
            Check(RuntimeSceneCodec.Encode(scene.CaptureSnapshot()).Length == scene.RuntimeValidationWork.Bytes,
                "Runtime incremental byte envelope differs from complete encoding");
        });
        yield return ("E6-A runtime prepared binding state owns its arrays and preserves negative zero", () => {
            var scene = Make(); var bindings = scene.CopyRuntimeBindings(); var id = bindings.Keys.Single();
            Commit(scene, bindings); int prior = scene.RuntimeValidationWork.Bytes;
            bindings[id][0].Exports[0] = new("Value", ExportKind.Double, -0d);
            scene.World.BeginStep();
            try
            {
                Action? publish = null;
                var commit = scene.World.PrepareStepView(view => publish = scene.PrepareRuntimeStep(view, bindings));
                bindings[id][0].Exports[0] = new("Bad", ExportKind.Double, 99);
                commit(); publish!();
            }
            finally { scene.World.AbortStep(); }
            Check(BitConverter.DoubleToInt64Bits(scene.GetBindings(id)[0].Exports[0].Value) == long.MinValue &&
                scene.RuntimeValidationWork.Bytes == prior + 1, "Pending metadata aliases caller or lost negative zero");
        });
        yield return ("E6-A metadata cannot follow a different same-revision committed attempt", () => {
            var scene = Make(); var bindings = scene.CopyRuntimeBindings(); var id = bindings.Keys.Single();
            bindings[id][0] = bindings[id][0] with { Enabled = false };
            scene.World.BeginStep(); Action? abandoned = null;
            _ = scene.World.PrepareStepView(view => abandoned = scene.PrepareRuntimeStep(view, bindings));
            Reject(abandoned!); scene.World.AbortStep();
            // Another producer commits without publishing this scene's metadata. Revision alone
            // would match the abandoned action; the exact candidate identity must reject it.
            scene.World.BeginStep(); scene.World.CommitStep();
            Reject(abandoned!);
            Check(scene.GetBindings(id)[0].Enabled, "Abandoned metadata attached to an unrelated World commit");
        });
        yield return ("E6-A runtime metadata rejection poisons candidate and preserves committed content", () => {
            var scene = Make(); var bindings = scene.CopyRuntimeBindings(); var id = bindings.Keys.Single();
            byte[] before = scene.CaptureCheckpoint().CopyBytes();
            bindings[id] = [bindings[id][0], bindings[id][0]];
            scene.World.BeginStep();
            Reject(() => scene.World.PrepareStepView(view => scene.PrepareRuntimeStep(view, bindings)));
            Reject(scene.World.CommitStep); scene.World.AbortStep();
            Check(before.SequenceEqual(scene.CaptureCheckpoint().CopyBytes()), "Rejected metadata mutated committed state");
        });
        yield return ("E6-A runtime checkpoint restore is explicit session-bound and invalidates handles", () => {
            var scene = Make(); var checkpoint = scene.CaptureCheckpoint(); var old = scene.World.GetEntities()[0];
            Guid id = scene.World.GetUuid(old); scene.World.Set(old, TransformData.Identity);
            ulong revision = scene.Revision; scene.RestoreCheckpoint(checkpoint, revision);
            Check(scene.Revision == revision + 1 && !scene.World.Has<TransformData>(scene.World.FindEntity(id)), "Checkpoint did not restore state once");
            Reject(() => scene.World.GetName(old));
            Reject(() => scene.RestoreCheckpoint(checkpoint, revision));
            var other = Make(); byte[] otherBefore = other.CaptureCheckpoint().CopyBytes();
            Reject(() => other.RestoreCheckpoint(checkpoint, other.Revision));
            Check(otherBefore.SequenceEqual(other.CaptureCheckpoint().CopyBytes()), "Foreign checkpoint mutated runtime");
        });
        yield return ("E6-A checkpoint byte copies cannot modify stored startup and scopes reject writes", () => {
            var scene = Make(); var checkpoint = scene.CaptureCheckpoint(); byte[] bytes = checkpoint.CopyBytes(); bytes[0] = 0;
            using (scene.World.ReadOnly()) Reject(() => scene.RestoreCheckpoint(checkpoint, scene.Revision));
            scene.RestoreCheckpoint(checkpoint, scene.Revision);
            Check(checkpoint.CopyBytes()[0] != 0 && scene.World.Count == 1, "Checkpoint storage aliased caller");
            Reject(() => Task.Run(scene.CaptureCheckpoint).GetAwaiter().GetResult());
        });
        yield return ("E6-A runtime binding normalization rejects serialized Export collisions", () => {
            var scene = Make(); var bindings = scene.CopyRuntimeBindings(); var id = bindings.Keys.Single();
            bindings[id][0] = bindings[id][0] with { Exports = [new("\ud800", ExportKind.Double, 1), new("\udc00", ExportKind.Double, 2)] };
            scene.World.BeginStep();
            Reject(() => scene.World.PrepareStepView(view => scene.PrepareRuntimeStep(view, bindings)));
            Reject(scene.World.CommitStep); scene.World.AbortStep();
        });
        yield return ("E6-A runtime structural metadata publication follows UUID/entity generation", () => {
            var scene = Make(); var bindings = scene.CopyRuntimeBindings(); var oldId = bindings.Keys.Single();
            var newId = Guid.NewGuid(); var value = bindings[oldId]; bindings.Remove(oldId); bindings[newId] = value;
            Commit(scene, bindings, () => { scene.World.StageDestroy(oldId); scene.World.StageCreate(newId, "New"); });
            Check(scene.GetBindings(newId).Length == 1, "Created runtime entity lost script metadata");
            Reject(() => scene.GetBindings(oldId));
            scene.World.Destroy(scene.World.FindEntity(newId)); scene.World.CreateEntity("Reused", newId);
            Check(scene.CopyRuntimeBindings().Count == 0 && scene.GetBindings(newId).Length == 0, "Stale generation kept script metadata");
        });
    }
}
