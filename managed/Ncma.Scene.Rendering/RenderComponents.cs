using System.Text.Json;
using Ncma.Runtime;

namespace Ncma.Scene.Rendering;

// Persistent authoring values only. No GPU handles, object references, arrays or native addresses.
public readonly record struct StaticMeshData(Guid MeshId, Guid MaterialSetId, bool Visible, bool CastShadow, uint LayerMask) : IComponent
{
    public const string TypeId = "ncma.render.static_mesh";
    public static StaticMeshData Validate(StaticMeshData v)
    { RenderComponentRegistry.RequireIds(v.MeshId, v.MaterialSetId); return v; }
}
// Persistent references only; clocks, numerical leases and GPU instances remain derived session state.
public readonly record struct SkinnedMeshData(Guid CharacterId, Guid MeshId, Guid SkeletonId, Guid MaterialSetId,
    bool Visible, bool CastShadow, uint LayerMask) : IComponent
{
    public const string TypeId = "ncma.render.skinned_mesh";
    public static SkinnedMeshData Validate(SkinnedMeshData v)
    { RenderComponentRegistry.RequireIds(v.CharacterId, v.MeshId, v.SkeletonId, v.MaterialSetId); return v; }
}
public enum CameraProjection { Perspective = 1, Orthographic = 2 }
public readonly record struct CameraData(CameraProjection Projection, float VerticalFovRadians, float OrthographicHeight,
    float Near, float Far, float ViewportX, float ViewportY, float ViewportWidth, float ViewportHeight, uint LayerMask) : IComponent
{
    public const string TypeId = "ncma.render.camera";
    public static CameraData Default => new(CameraProjection.Perspective, MathF.PI / 3, 10, .1f, 1000, 0, 0, 1, 1, uint.MaxValue);
    public static CameraData Validate(CameraData v)
    {
        if (!Enum.IsDefined(v.Projection) || !RenderComponentRegistry.Range(v.VerticalFovRadians, .01f, 3.13f) ||
            !RenderComponentRegistry.Range(v.OrthographicHeight, .001f, 1e6f) ||
            !RenderComponentRegistry.Range(v.Near, .001f, 1e6f) || !RenderComponentRegistry.Range(v.Far, .001f, 1e7f) || v.Far <= v.Near ||
            !RenderComponentRegistry.Range(v.ViewportX, 0, 1) || !RenderComponentRegistry.Range(v.ViewportY, 0, 1) ||
            !RenderComponentRegistry.Range(v.ViewportWidth, .001f, 1) || !RenderComponentRegistry.Range(v.ViewportHeight, .001f, 1) ||
            v.ViewportX + v.ViewportWidth > 1 || v.ViewportY + v.ViewportHeight > 1)
            throw new ArgumentException("Invalid camera projection, clipping range or normalized viewport.");
        return v;
    }
}
public readonly record struct DirectionalLightData(float Red, float Green, float Blue, float Intensity,
    bool IsPrimary, bool CastShadow, uint LayerMask) : IComponent
{
    public const string TypeId = "ncma.render.directional_light";
    public static DirectionalLightData Default => new(1, 1, 1, 4, true, true, uint.MaxValue);
    public static DirectionalLightData Validate(DirectionalLightData v)
    {
        if (!RenderComponentRegistry.Range(v.Red, 0, 16) || !RenderComponentRegistry.Range(v.Green, 0, 16) ||
            !RenderComponentRegistry.Range(v.Blue, 0, 16) || !RenderComponentRegistry.Range(v.Intensity, 0, 100000) || (v.CastShadow && !v.IsPrimary))
            throw new ArgumentException("Only the primary directional light may cast shadows; light values must be finite linear values.");
        return v;
    }
}
// Nonempty MaterialId replaces every slot; scalar override then applies to every resolved material.
public readonly record struct MaterialOverrideData(Guid MaterialId, bool OverrideScalars, float Metallic, float Roughness) : IComponent
{
    public const string TypeId = "ncma.render.material_override";
    public static MaterialOverrideData Validate(MaterialOverrideData v)
    {
        if (v.MaterialId == Guid.Empty && !v.OverrideScalars || !RenderComponentRegistry.Range(v.Metallic, 0, 1) ||
            !RenderComponentRegistry.Range(v.Roughness, .045f, 1)) throw new ArgumentException("Empty or invalid material override.");
        return v;
    }
}
public static class RenderComponentRegistry
{
    public static ComponentRegistry Register(ComponentRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        Ncma.Animation.ClipPlaybackData.Register(registry);
        registry.Register<StaticMeshData>(StaticMeshData.TypeId, 1, Schema(("meshId", "string"), ("materialSetId", "string"),
            ("visible", "boolean"), ("castShadow", "boolean"), ("layerMask", "integer")), StaticMeshData.Validate);
        registry.Register<SkinnedMeshData>(SkinnedMeshData.TypeId, 1, Schema(("characterId", "string"), ("meshId", "string"),
            ("skeletonId", "string"), ("materialSetId", "string"), ("visible", "boolean"), ("castShadow", "boolean"), ("layerMask", "integer")), SkinnedMeshData.Validate);
        registry.Register<CameraData>(CameraData.TypeId, 1, Schema(("projection", "integer"), ("verticalFovRadians", "number"),
            ("orthographicHeight", "number"), ("near", "number"), ("far", "number"), ("viewportX", "number"), ("viewportY", "number"),
            ("viewportWidth", "number"), ("viewportHeight", "number"), ("layerMask", "integer")), CameraData.Validate);
        registry.Register<DirectionalLightData>(DirectionalLightData.TypeId, 1, Schema(("red", "number"), ("green", "number"),
            ("blue", "number"), ("intensity", "number"), ("isPrimary", "boolean"), ("castShadow", "boolean"), ("layerMask", "integer")), DirectionalLightData.Validate);
        registry.Register<MaterialOverrideData>(MaterialOverrideData.TypeId, 1, Schema(("materialId", "string"), ("overrideScalars", "boolean"),
            ("metallic", "number"), ("roughness", "number")), MaterialOverrideData.Validate);
        return registry;
    }
    public static ComponentRegistry CreateRegistry() => Register(ComponentRegistry.CreateDefault());
    internal static void RequireIds(params Guid[] ids)
    { if (ids.Any(id => id == Guid.Empty)) throw new ArgumentException("Render asset references require persistent UUIDs."); }
    internal static bool Range(float v, float min, float max) => float.IsFinite(v) && v >= min && v <= max;
    private static string Schema(params (string Name, string Type)[] fields) => JsonSerializer.Serialize(new {
        type = "object", additionalProperties = false, required = fields.Select(f => f.Name),
        properties = fields.ToDictionary(f => f.Name, f => new { type = f.Type }) });
}
