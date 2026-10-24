using System.Numerics;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;

internal static partial class Program
{
    // Explicit cold bake boundary; authoring documents never expose an ECS World.
    private static RuntimeSceneInstance Bake(SceneDocument source) =>
        source.BakeRuntime(source.Revision).Image.Instantiate(source.Components);
    private static RuntimeSceneInstance Runtime() => Bake(Document());

    // Extractor diagnostics deliberately mutate the actual runtime, including malformed
    // compositions that the authoring bake correctly rejects before publication.
    private static Entity Geometry(RuntimeSceneInstance scene, Vector3? position = null)
    {
        var obj = scene.World.CreateEntity("Geometry");
        scene.World.Set(obj, TransformData.Identity with { Position = position ?? new(0, 0, -3) });
        scene.World.Set(obj, Mesh);
        return obj;
    }
    private static Guid Camera(RuntimeSceneInstance scene)
    {
        var obj = scene.World.CreateEntity("Camera");
        scene.World.Set(obj, TransformData.Identity);
        scene.World.Set(obj, CameraData.Default);
        return scene.World.GetUuid(obj);
    }
    private static void Light(RuntimeSceneInstance scene)
    {
        var obj = scene.World.CreateEntity("Light");
        scene.World.Set(obj, TransformData.Identity);
        scene.World.Set(obj, DirectionalLightData.Default);
    }
}
