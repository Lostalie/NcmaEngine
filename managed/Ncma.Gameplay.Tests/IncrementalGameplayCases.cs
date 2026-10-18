using System.Reflection;
using Ncma.Gameplay;
using Ncma.Runtime;
using Ncma.Scene;

internal static class IncrementalGameplayCases
{
    private static void Check(bool value) { if (!value) throw new Exception("Incremental gameplay assertion failed."); }
    private static long Count(object owner, string name) => (long)owner.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static SceneDocument Document()
    {
        var document = new SceneDocument("Incremental fixture");
        for (int i = 0; i < 64; ++i) document.World.Set(document.World.CreateEntity("Object " + i), TransformData.Identity);
        document.SetBindings(document.World.GetUuid(document.World.GetEntities()[0]), [new(Guid.NewGuid(), "Test.Probe", true, [])]);
        return document;
    }
    internal static void Run()
    {
        var cases = new (string Name, Action Run)[]
        {
            ("real Play reuses committed encodings on no-write fixed steps", () => {
                var document = Document(); using var play = new PlaySession(document); play.Start(_ => new Probe());
                play.AdvanceFrame(1d / 60); long components = Count(document.World, "_encodedComponentCount");
                long bindings = Count(document, "_runtimeBindingEncodes"), snapshots = Count(document.World, "_copiedValidationSnapshotCount");
                byte[] before = document.CaptureBytes();
                for (int i = 0; i < 10; ++i) play.AdvanceFrame(1d / 60);
                Check(play.State == PlayState.Running && play.Tick == 11 && document.CaptureBytes().SequenceEqual(before));
                Check(Count(document.World, "_encodedComponentCount") == components && Count(document, "_runtimeBindingEncodes") == bindings);
                // Explicit remaining E3-C consumer: one final DTO for instances/receipts/interpolation per step.
                Check(Count(document.World, "_copiedValidationSnapshotCount") == snapshots + 10);
            }),
            ("real Play sparse write encodes only its changed component", () => {
                var document = Document(); var probe = new Probe(); using var play = new PlaySession(document); play.Start(_ => probe);
                play.AdvanceFrame(1d / 60); long components = Count(document.World, "_encodedComponentCount");
                probe.Fixed = _ => { var value = probe.GameObject.LocalTransform; value.Position.X += 1; probe.GameObject.LocalTransform = value; };
                for (int i = 0; i < 6; ++i) play.AdvanceFrame(1d / 60);
                Check(play.State == PlayState.Running && Count(document.World, "_encodedComponentCount") == components + 6);
                Check(document.World.Get<TransformData>(document.World.GetEntities()[0]).Position.X == 6);
            }),
            ("failed real Play callback leaves committed encodings and document intact", () => {
                var document = Document(); var probe = new Probe(); using var play = new PlaySession(document); play.Start(_ => probe);
                play.AdvanceFrame(1d / 60); byte[] before = document.CaptureBytes();
                probe.Fixed = _ => { var value = probe.GameObject.LocalTransform; value.Position.X = 99; probe.GameObject.LocalTransform = value; throw new InvalidOperationException("late failure"); };
                play.AdvanceFrame(1d / 60);
                Check(play.State == PlayState.Faulted && play.Tick == 1 && document.CaptureBytes().SequenceEqual(before));
            }),
        };
        foreach (var test in cases) { test.Run(); Console.WriteLine("PASS incremental gameplay " + test.Name); }
        Console.WriteLine($"Incremental gameplay tests: {cases.Length}/{cases.Length} passed.");
    }
}
