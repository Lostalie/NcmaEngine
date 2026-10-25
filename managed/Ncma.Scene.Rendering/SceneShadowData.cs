using Ncma.Assets;
using Ncma.Runtime;

namespace Ncma.Scene.Rendering;

// One immutable scene-wide selection, not a spatial component or gameplay attachment.
public readonly record struct SceneShadowData(int Version, SceneShadowQuality Quality) : IComponent
{
    public const string TypeId = "ncma.render.shadow";
    public static SceneShadowData Default => new(1, SceneShadowQuality.Medium);
    public static SceneShadowData Validate(SceneShadowData value)
    {
        if (value.Version != 1 || !Enum.IsDefined(value.Quality)) throw new ArgumentException("Scene shadow version/quality.");
        return value;
    }
    [ComponentRegistration]
    public static void Register(ComponentRegistry registry) => registry.Register<SceneShadowData>(TypeId, 1, """
        {"type":"object","additionalProperties":false,"required":["version","quality"],
         "properties":{"version":{"type":"integer"},"quality":{"type":"integer"}}}
        """, Validate, runtimeAttachable:false);
}

public static class SceneShadowState
{
    private static readonly ComponentRegistry Registry = CompositionRegistry.Build(Generated.ComponentModule.Module);
    // Absence means the official Medium default; custom pipelines retain their explicit policy
    // until a scene selection is authored. Preserve absence rather than synthesizing a component.
    public static SceneShadowData? Read(World world, Guid expectedWorld)
        => Read(new SceneComponentReader<SceneShadowData>(world), expectedWorld);
    public static SceneShadowData? Read(SceneComponentReader<SceneShadowData> reader, Guid expectedWorld)
    {
        ArgumentNullException.ThrowIfNull(reader);
        int count = reader.ReadSingle(expectedWorld, out var value);
        if (count > 1) throw new ArgumentException("At most one scene shadow selection, including Off.");
        return count == 0 ? null : SceneShadowData.Validate(value);
    }
    public static SceneShadowData? Read(ISceneContent snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot); SceneShadowData? value = null;
        foreach (var obj in snapshot.Objects) foreach (var component in obj.Components.Where(c => c.TypeId == SceneShadowData.TypeId)) {
            if (value is not null) throw new ArgumentException("At most one scene shadow selection, including Off.");
            value = Registry.Decode<SceneShadowData>(component);
        }
        return value;
    }
}
