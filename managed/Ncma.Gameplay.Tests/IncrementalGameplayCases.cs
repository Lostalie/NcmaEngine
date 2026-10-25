using System.Reflection;
using Ncma.Gameplay;
using Ncma.Runtime;
using Ncma.Scene;

internal static class IncrementalGameplayCases
{
    private static void Check(bool value) { if (!value) throw new Exception("Incremental gameplay assertion failed."); }
    private static long Count(object owner, string name) => (long)owner.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static RuntimeSceneInstance RuntimeScene()
    {
        var document = new SceneDocument("Incremental fixture");
        for (int i = 0; i < 64; ++i) document.SetComponent(document.CreateObject("Object " + i), TransformData.Identity);
        document.SetBindings(document.GetObjectIds()[0], [new(Guid.NewGuid(), "Test.Probe", true, [])]);
        return document.BakeRuntime(document.Revision).Image.Instantiate(document.Components);
    }
    internal static void Run()
    {
        var cases = new (string Name, Action Run)[]
        {
            ("real Play reuses committed encodings on no-write fixed steps", () => {
                var document = RuntimeScene(); using var play = new PlaySession(document); play.Start(_ => new Probe());
                play.AdvanceFrame(1d / 60); long components = Count(document.World, "_encodedComponentCount");
                long bindings = Count(document, "_runtimeBindingEncodes"), snapshots = Count(document.World, "_copiedValidationSnapshotCount");
                long instances = Count(play, "_instancePreparations");
                byte[] before = document.CaptureCheckpoint().CopyBytes();
                for (int i = 0; i < 10; ++i) play.AdvanceFrame(1d / 60);
                Check(play.State == PlayState.Running && play.Tick == 11 && document.CaptureCheckpoint().CopyBytes().SequenceEqual(before));
                Check(Count(document.World, "_encodedComponentCount") == components && Count(document, "_runtimeBindingEncodes") == bindings);
                Check(Count(document.World, "_copiedValidationSnapshotCount") == snapshots);
                Check(Count(play, "_instancePreparations") == instances);
            }),
            ("real Play sparse write encodes only its changed component", () => {
                var document = RuntimeScene(); var probe = new Probe(); using var play = new PlaySession(document); play.Start(_ => probe);
                play.AdvanceFrame(1d / 60); long components = Count(document.World, "_encodedComponentCount");
                probe.Fixed = _ => { var value = probe.GameObject.LocalTransform; value.Position.X += 1; probe.GameObject.LocalTransform = value; };
                for (int i = 0; i < 6; ++i) play.AdvanceFrame(1d / 60);
                Check(play.State == PlayState.Running && Count(document.World, "_encodedComponentCount") == components + 6);
                Check(document.World.Get<TransformData>(document.World.GetEntities()[0]).Position.X == 6);
            }),
            ("failed real Play callback leaves committed encodings and runtime state intact", () => {
                var document = RuntimeScene(); var probe = new Probe(); using var play = new PlaySession(document); play.Start(_ => probe);
                play.AdvanceFrame(1d / 60); byte[] before = document.CaptureCheckpoint().CopyBytes();
                probe.Fixed = _ => { var value = probe.GameObject.LocalTransform; value.Position.X = 99; probe.GameObject.LocalTransform = value; throw new InvalidOperationException("late failure"); };
                play.AdvanceFrame(1d / 60);
                Check(play.State == PlayState.Faulted && play.Tick == 1 && document.CaptureCheckpoint().CopyBytes().SequenceEqual(before));
            }),
        };
        CaseHarness.RunOrThrow("Incremental gameplay", cases);
    }
}
