using Ncma.Runtime;
namespace Ncma.Scene.Rendering;

public static class ScenePostProcessState
{
    private static readonly ComponentRegistry Registry = CreateRegistry();
    private static ComponentRegistry CreateRegistry() { var registry = ComponentRegistry.CreateDefault(); PostProcessData.Register(registry); return registry; }
    public static PostProcessData Read(World world, Guid expectedWorld)
    {
        SceneEnvironmentState.VerifyBoundary(world, expectedWorld);
        var value = PostProcessData.Off; int count = 0;
        foreach (var obj in world.GetObjects()) if (obj.Has<PostProcessData>()) {
            if (++count > 1) throw new ArgumentException("One scene post-process configuration required, including Off.");
            value = PostProcessData.Validate(obj.Get<PostProcessData>());
        }
        return value;
    }
    public static PostProcessData Read(SceneDocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var value = PostProcessData.Off; int count = 0;
        foreach (var obj in snapshot.Objects) foreach (var component in obj.Components.Where(c => c.TypeId == PostProcessData.TypeId)) {
            if (++count > 1) throw new ArgumentException("One scene post-process configuration required, including Off.");
            value = Registry.Decode<PostProcessData>(component);
        }
        return value;
    }
}
