using Ncma.Runtime;
namespace Ncma.Scene.Rendering;

public static class ScenePostProcessState
{
    private static readonly ComponentRegistry Registry = CompositionRegistry.Build(Generated.ComponentModule.Module);
    public static PostProcessData Read(World world, Guid expectedWorld)
        => Read(new SceneComponentReader<PostProcessData>(world), expectedWorld);
    public static PostProcessData Read(SceneComponentReader<PostProcessData> reader, Guid expectedWorld)
    {
        ArgumentNullException.ThrowIfNull(reader);
        int count = reader.ReadSingle(expectedWorld, out var value);
        if (count > 1) throw new ArgumentException("One scene post-process configuration required, including Off.");
        return count == 0 ? PostProcessData.Off : PostProcessData.Validate(value);
    }
    public static PostProcessData Read(ISceneContent snapshot)
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
