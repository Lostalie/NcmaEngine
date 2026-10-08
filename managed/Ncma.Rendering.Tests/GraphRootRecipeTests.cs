using Ncma.Animation;
using Ncma.Assets;
using Ncma.Characters;
using System.Numerics;
using System.Text.Json;

internal static unsafe partial class Program
{
    private static void TestGraphRootRecipes(string output)
    {
        var source = SyntheticModel(); var skeleton = new SkeletonPayload(source.Bones);
        Guid right = Guid.NewGuid(), left = Guid.NewGuid();
        var tracks = new Dictionary<Guid, RootMotionTrack> {
            [right] = new(skeleton, new(source.Bones.Length, source.Clips[0]), 0),
            [left] = new(skeleton, new(source.Bones.Length, source.Clips[1]), 0)
        };
        var recipe = new GraphRootMotionRecipe(tracks, 8);
        AnimationPoseInstruction Clip(Guid id, double start, double end, bool loop = true) =>
            new(AnimationPoseOperation.Clip, Guid.NewGuid(), id, start, end, 1, loop, -1, -1, 0);
        AnimationPoseInstruction Blend(int a, int b, float weight) =>
            new(AnimationPoseOperation.Blend, Guid.NewGuid(), Guid.Empty, 0, 0, 0, false, a, b, weight);
        var plan = new[] { Clip(right, .9, 1.1), Clip(left, .9, 1.1), Blend(0, 1, .25f), Blend(2, 1, .5f) };
        Check(Vector3.Distance(recipe.Evaluate(plan, 2).Translation, new(.1f, 0, 0)) < 1e-6f, "Graph root crossing/blend oracle");
        Check(Vector3.Distance(recipe.Evaluate(plan, 3).Translation, new(-.05f, 0, 0)) < 1e-6f, "Graph nested root oracle");
        Check(Vector3.Distance(recipe.Evaluate(plan, 0).Translation, new(.2f, 0, 0)) < 1e-6f, "Graph root output is not implicitly last");
        tracks.Clear(); // Caller dictionary is not live recipe storage.
        Check(recipe.Evaluate(plan, 2).Translation.X > 0, "Graph root tracks were not copied");
        var bad = (AnimationPoseInstruction[])plan.Clone(); bad[3] = bad[3] with { SourceB = 3 };
        Reject(() => recipe.Evaluate(bad, 0)); // Unused invalid branch still rejects the whole candidate.
        foreach (var row in new[] {
            plan[0] with { ClipId = Guid.NewGuid() }, plan[0] with { Duration = 2 }, plan[0] with { Previous = .2, Current = .1 },
            plan[0] with { Current = double.NaN }, plan[0] with { Previous = 0, Current = 33 }, plan[0] with { Loop = false },
            plan[0] with { SourceA = 0 }, plan[0] with { Operation = (AnimationPoseOperation)99 }, plan[0] with { NodeId = Guid.Empty }
        }) Reject(() => recipe.Evaluate([row], 0));
        foreach (float weight in new[] { float.NaN, float.PositiveInfinity, -.1f, 1.1f }) {
            bad = (AnimationPoseInstruction[])plan.Clone(); bad[2] = bad[2] with { Weight = weight }; var candidate = bad;
            Reject(() => recipe.Evaluate(candidate, 2));
        }
        Reject(() => recipe.Evaluate([], 0)); Reject(() => recipe.Evaluate(plan, plan.Length));
        Reject(() => new GraphRootMotionRecipe(new Dictionary<Guid, RootMotionTrack>(), 1));
        Reject(() => new GraphRootMotionRecipe(new Dictionary<Guid, RootMotionTrack> { [right] = new(skeleton, new(2, source.Clips[0]), 0) }, 770));
        Task.Run(() => Reject(() => recipe.Evaluate(plan, 2))).GetAwaiter().GetResult();
        Check(Math.Abs(recipe.Evaluate(plan, 3).Translation.X + .05f) < 1e-6f, "Rejected root candidate contaminated the next evaluation");

        // Independent quaternion oracle across the +/-pi boundary.
        RootMotionTrack Turn(float yaw) => new(skeleton, new(2, new("Turn", 1, [new(0, [
            new(0, new(Vector3.Zero, Quaternion.Identity, Vector3.One)),
            new(1, new(Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw), Vector3.One))
        ])])), 0);
        float angle = 170 * MathF.PI / 180;
        var turn = new GraphRootMotionRecipe(new Dictionary<Guid, RootMotionTrack> { [right] = Turn(angle), [left] = Turn(-angle) }, 3);
        var turnPlan = new[] { Clip(right, 0, 1, false), Clip(left, 0, 1, false), Blend(0, 1, .4f) };
        var actual = Quaternion.CreateFromAxisAngle(Vector3.UnitY, turn.Evaluate(turnPlan, 2).Yaw);
        var expected = Quaternion.Slerp(Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle), Quaternion.CreateFromAxisAngle(Vector3.UnitY, -angle), .4f);
        Check(MathF.Abs(Quaternion.Dot(actual, expected)) > .999999f, "Graph root yaw differs from shortest quaternion blend");
        for (int i = 0; i < 32; i++) recipe.Evaluate(plan, 3);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1024; i++) recipe.Evaluate(plan, 3);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0, "Warm graph root recipe allocated");
        File.WriteAllBytes(Path.Combine(output, "graph-root-recipe-results.json"), JsonSerializer.SerializeToUtf8Bytes(new {
            schema = 1, nestedBlend = true, crossing = true, explicitOutput = true, shortestYawOracle = true,
            invalidUnusedBranchRejected = true, ownerThread = true, warmEvaluations = 1024, allocatedBytes = allocated,
            characterIntegration = false, manualAcceptance = false
        }));
    }
}
