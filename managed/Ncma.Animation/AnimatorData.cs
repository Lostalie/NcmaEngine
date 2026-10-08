using System.Text.Json;
using Ncma.Runtime;
namespace Ncma.Animation;

public readonly record struct AnimatorData(Guid GraphId, Guid SkeletonId) : IComponent
{
    public const string TypeId = "ncma.animation.animator";
    public static AnimatorData Validate(AnimatorData value)
    { if (value.GraphId == Guid.Empty || value.SkeletonId == Guid.Empty || value.GraphId == value.SkeletonId) throw new ArgumentException("Animator persistent graph/skeleton UUIDs required."); return value; }
    public static void Register(ComponentRegistry registry) => registry.Register<AnimatorData>(TypeId, 1,
        JsonSerializer.Serialize(new { type = "object", additionalProperties = false, required = new[] { "graphId", "skeletonId" },
            properties = new { graphId = new { type = "string" }, skeletonId = new { type = "string" } } }), Validate, runtimeAttachable: false);
}
