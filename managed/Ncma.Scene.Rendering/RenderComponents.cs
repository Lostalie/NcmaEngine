using System.Text.Json;
using Ncma.Runtime;

namespace Ncma.Scene.Rendering;

// Persistent authoring values only. No GPU handles, object references, arrays or native addresses.
public readonly record struct StaticMeshData(Guid MeshId, Guid MaterialSetId, bool Visible, bool CastShadow, uint LayerMask) : IComponent
{
    public const string TypeId = "ncma.render.static_mesh";
    [ComponentRegistration]
    internal static void Register(ComponentRegistry registry) => registry.Register<StaticMeshData>(TypeId, 1,
        RenderComponentRules.Schema(("meshId", "string"), ("materialSetId", "string"), ("visible", "boolean"), ("castShadow", "boolean"), ("layerMask", "integer")), Validate);
    public static StaticMeshData Validate(StaticMeshData v)
    { RenderComponentRules.RequireIds(v.MeshId, v.MaterialSetId); return v; }
}
// Persistent references only; clocks, numerical leases and GPU instances remain derived session state.
public readonly record struct SkinnedMeshData(Guid CharacterId, Guid MeshId, Guid SkeletonId, Guid MaterialSetId,
    bool Visible, bool CastShadow, uint LayerMask) : IComponent
{
    public const string TypeId = "ncma.render.skinned_mesh";
    [ComponentRegistration]
    internal static void Register(ComponentRegistry registry) => registry.Register<SkinnedMeshData>(TypeId, 1,
        RenderComponentRules.Schema(("characterId", "string"), ("meshId", "string"), ("skeletonId", "string"), ("materialSetId", "string"),
            ("visible", "boolean"), ("castShadow", "boolean"), ("layerMask", "integer")), Validate);
    public static SkinnedMeshData Validate(SkinnedMeshData v)
    { RenderComponentRules.RequireIds(v.CharacterId, v.MeshId, v.SkeletonId, v.MaterialSetId); return v; }
}
public enum CameraProjection { Perspective = 1, Orthographic = 2 }
[GenerateComponent(CameraData.TypeId, 1, CameraData.DefinitionSchema, nameof(CameraData.Validate), nameof(CameraData.Default))]
[ComponentField("projection", "Projection", "projection", Flags = CameraData.FieldUsage)]
[ComponentField("verticalFovRadians", "VerticalFovRadians", "verticalFovRadians", Flags = CameraData.FieldUsage)]
[ComponentField("orthographicHeight", "OrthographicHeight", "orthographicHeight", Flags = CameraData.FieldUsage)]
[ComponentField("near", "Near", "near", Flags = CameraData.FieldUsage)]
[ComponentField("far", "Far", "far", Flags = CameraData.FieldUsage)]
[ComponentField("viewportX", "ViewportX", "viewportX", Flags = CameraData.FieldUsage)]
[ComponentField("viewportY", "ViewportY", "viewportY", Flags = CameraData.FieldUsage)]
[ComponentField("viewportWidth", "ViewportWidth", "viewportWidth", Flags = CameraData.FieldUsage)]
[ComponentField("viewportHeight", "ViewportHeight", "viewportHeight", Flags = CameraData.FieldUsage)]
[ComponentField("layerMask", "LayerMask", "layerMask", Flags = CameraData.FieldUsage)]
public readonly partial record struct CameraData(CameraProjection Projection, float VerticalFovRadians, float OrthographicHeight,
    float Near, float Far, float ViewportX, float ViewportY, float ViewportWidth, float ViewportHeight, uint LayerMask) : IComponent
{
    internal const string DefinitionSchema = """{"type":"object","additionalProperties":false,"required":["projection","verticalFovRadians","orthographicHeight","near","far","viewportX","viewportY","viewportWidth","viewportHeight","layerMask"],"properties":{"projection":{"type":"integer"},"verticalFovRadians":{"type":"number"},"orthographicHeight":{"type":"number"},"near":{"type":"number"},"far":{"type":"number"},"viewportX":{"type":"number"},"viewportY":{"type":"number"},"viewportWidth":{"type":"number"},"viewportHeight":{"type":"number"},"layerMask":{"type":"integer"}}}""";
    internal const ComponentFieldFlags FieldUsage = ComponentFieldFlags.Persistent | ComponentFieldFlags.Editable | ComponentFieldFlags.RuntimeRead | ComponentFieldFlags.RuntimeWrite;
    public const string TypeId = "ncma.render.camera";
    public static CameraData Default => new(CameraProjection.Perspective, MathF.PI / 3, 10, .1f, 1000, 0, 0, 1, 1, uint.MaxValue);
    public static CameraData Validate(CameraData v)
    {
        if (!Enum.IsDefined(v.Projection) || !RenderComponentRules.Range(v.VerticalFovRadians, .01f, 3.13f) ||
            !RenderComponentRules.Range(v.OrthographicHeight, .001f, 1e6f) ||
            !RenderComponentRules.Range(v.Near, .001f, 1e6f) || !RenderComponentRules.Range(v.Far, .001f, 1e7f) || v.Far <= v.Near ||
            !RenderComponentRules.Range(v.ViewportX, 0, 1) || !RenderComponentRules.Range(v.ViewportY, 0, 1) ||
            !RenderComponentRules.Range(v.ViewportWidth, .001f, 1) || !RenderComponentRules.Range(v.ViewportHeight, .001f, 1) ||
            v.ViewportX + v.ViewportWidth > 1 || v.ViewportY + v.ViewportHeight > 1)
            throw new ArgumentException("Invalid camera projection, clipping range or normalized viewport.");
        return v;
    }
}
[GenerateComponent(DirectionalLightData.TypeId, 1, DirectionalLightData.DefinitionSchema, nameof(DirectionalLightData.Validate), nameof(DirectionalLightData.Default))]
[ComponentField("red", "Red", "red", Flags = DirectionalLightData.FieldUsage)]
[ComponentField("green", "Green", "green", Flags = DirectionalLightData.FieldUsage)]
[ComponentField("blue", "Blue", "blue", Flags = DirectionalLightData.FieldUsage)]
[ComponentField("intensity", "Intensity", "intensity", Flags = DirectionalLightData.FieldUsage)]
[ComponentField("isPrimary", "IsPrimary", "isPrimary", Flags = DirectionalLightData.FieldUsage)]
[ComponentField("castShadow", "CastShadow", "castShadow", Flags = DirectionalLightData.FieldUsage)]
[ComponentField("layerMask", "LayerMask", "layerMask", Flags = DirectionalLightData.FieldUsage)]
public readonly partial record struct DirectionalLightData(float Red, float Green, float Blue, float Intensity,
    bool IsPrimary, bool CastShadow, uint LayerMask) : IComponent
{
    internal const string DefinitionSchema = """{"type":"object","additionalProperties":false,"required":["red","green","blue","intensity","isPrimary","castShadow","layerMask"],"properties":{"red":{"type":"number"},"green":{"type":"number"},"blue":{"type":"number"},"intensity":{"type":"number"},"isPrimary":{"type":"boolean"},"castShadow":{"type":"boolean"},"layerMask":{"type":"integer"}}}""";
    internal const ComponentFieldFlags FieldUsage = ComponentFieldFlags.Persistent | ComponentFieldFlags.Editable | ComponentFieldFlags.RuntimeRead | ComponentFieldFlags.RuntimeWrite;
    public const string TypeId = "ncma.render.directional_light";
    public static DirectionalLightData Default => new(1, 1, 1, 4, true, true, uint.MaxValue);
    public static DirectionalLightData Validate(DirectionalLightData v)
    {
        if (!RenderComponentRules.Range(v.Red, 0, 16) || !RenderComponentRules.Range(v.Green, 0, 16) ||
            !RenderComponentRules.Range(v.Blue, 0, 16) || !RenderComponentRules.Range(v.Intensity, 0, 100000) || (v.CastShadow && !v.IsPrimary))
            throw new ArgumentException("Only the primary directional light may cast shadows; light values must be finite linear values.");
        return v;
    }
}
// Nonempty MaterialId replaces every slot; scalar override then applies to every resolved material.
public readonly record struct MaterialOverrideData(Guid MaterialId, bool OverrideScalars, float Metallic, float Roughness) : IComponent
{
    public const string TypeId = "ncma.render.material_override";
    [ComponentRegistration]
    internal static void Register(ComponentRegistry registry) => registry.Register<MaterialOverrideData>(TypeId, 1,
        RenderComponentRules.Schema(("materialId", "string"), ("overrideScalars", "boolean"), ("metallic", "number"), ("roughness", "number")), Validate);
    public static MaterialOverrideData Validate(MaterialOverrideData v)
    {
        if (v.MaterialId == Guid.Empty && !v.OverrideScalars || !RenderComponentRules.Range(v.Metallic, 0, 1) ||
            !RenderComponentRules.Range(v.Roughness, .045f, 1)) throw new ArgumentException("Empty or invalid material override.");
        return v;
    }
}
internal static class RenderComponentRules
{
    internal static void RequireIds(params Guid[] ids)
    { if (ids.Any(id => id == Guid.Empty)) throw new ArgumentException("Render asset references require persistent UUIDs."); }
    internal static bool Range(float v, float min, float max) => float.IsFinite(v) && v >= min && v <= max;
    internal static string Schema(params (string Name, string Type)[] fields) => JsonSerializer.Serialize(new {
        type = "object", additionalProperties = false, required = fields.Select(f => f.Name),
        properties = fields.ToDictionary(f => f.Name, f => new { type = f.Type }) });
}
