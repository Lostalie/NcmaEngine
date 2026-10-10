using Ncma.Runtime;
namespace Ncma.Scene.Rendering;

// Trusted application preparation only. No permission grant, World write or Agent endpoint.
public static class SceneEnvironmentState
{
    public static void VerifyBoundary(World world, Guid expectedWorld)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (world.Identity != expectedWorld || !world.IsCommittedBoundary)
            throw new InvalidOperationException("Environment preparation requires the exact committed World.");
    }
    public static IDisposable BeginRead(World world, Guid expectedWorld)
    { VerifyBoundary(world, expectedWorld); return world.ReadOnly(); }
    public static EnvironmentLightingData Read(World world, Guid expectedWorld)
    {
        VerifyBoundary(world, expectedWorld);
        var value=EnvironmentLightingData.Off; int count=0;
        foreach(var obj in world.GetObjects()) if(obj.Has<EnvironmentLightingData>()) {
            if(++count>1)throw new ArgumentException("One scene environment configuration required.");
            value=EnvironmentLightingData.Validate(obj.Get<EnvironmentLightingData>());
        }
        return value;
    }
    public static EnvironmentLightingData Read(SceneDocumentSnapshot snapshot)
    {
        var registry=RenderComponentRegistry.CreateRegistry();var value=EnvironmentLightingData.Off;int count=0;
        foreach(var obj in snapshot.Objects)foreach(var component in obj.Components.Where(c=>c.TypeId==EnvironmentLightingData.TypeId)) {
            if(++count>1)throw new ArgumentException("One scene environment configuration required.");
            value=registry.Decode<EnvironmentLightingData>(component);
        }
        return value;
    }
}
