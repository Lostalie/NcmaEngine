using Ncma.Scene;

// Test-only trusted producer. Exercises the actual World candidate/publication protocol;
// not an authoring adapter, public setter, permission grant, or alternate runtime store.
internal static class RuntimeSceneTestTransactions
{
    internal static void CommitMetadata(RuntimeSceneInstance scene, Action<Dictionary<Guid, BehaviourBindingData[]>> change)
    {
        ulong revision = scene.Revision;
        var bindings = scene.CopyRuntimeBindings();
        using (scene.World.ReadOnly()) change(bindings);
        if (scene.Revision != revision) throw new InvalidOperationException("Fixture changed outside its candidate.");
        scene.World.BeginStep();
        try
        {
            Action? metadata = null;
            var commit = scene.World.PrepareStepView(view => metadata = scene.PrepareRuntimeStep(view, bindings));
            commit(); metadata!();
        }
        finally { scene.World.AbortStep(); }
    }
}
