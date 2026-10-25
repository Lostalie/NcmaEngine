using System.Text.Json;
using Ncma.Runtime;
using Ncma.Scene;
using Ncma.Scene.Rendering;
namespace Ncma.Characters;

[GenerateComponent(CharacterData.TypeId, 1, CharacterData.DefinitionSchema, nameof(CharacterData.Validate), nameof(CharacterData.Default), RuntimeAttachable = false)]
[ComponentField("radius", "Radius", "radius", Flags = CharacterData.FieldUsage)]
[ComponentField("halfHeight", "HalfHeight", "halfHeight", Flags = CharacterData.FieldUsage)]
[ComponentField("footOffsetY", "FootOffsetY", "footOffsetY", Flags = CharacterData.FieldUsage)]
[ComponentField("maximumSlope", "MaximumSlope", "maximumSlope", Flags = CharacterData.FieldUsage)]
[ComponentField("stepHeight", "StepHeight", "stepHeight", Flags = CharacterData.FieldUsage)]
[ComponentField("floorDistance", "FloorDistance", "floorDistance", Flags = CharacterData.FieldUsage)]
[ComponentField("speed", "Speed", "speed", Flags = CharacterData.FieldUsage)]
[ComponentField("gravity", "Gravity", "gravity", Flags = CharacterData.FieldUsage)]
[ComponentField("jumpSpeed", "JumpSpeed", "jumpSpeed", Flags = CharacterData.FieldUsage)]
[ComponentField("turnSpeed", "TurnSpeed", "turnSpeed", Flags = CharacterData.FieldUsage)]
[ComponentField("category", "Category", "category", Flags = CharacterData.FieldUsage)]
[ComponentField("mask", "Mask", "mask", Flags = CharacterData.FieldUsage)]
[ComponentField("controlled", "Controlled", "controlled", Flags = CharacterData.FieldUsage)]
public readonly partial record struct CharacterData(float Radius, float HalfHeight, float FootOffsetY, float MaximumSlope,
    float StepHeight, float FloorDistance, float Speed, float Gravity, float JumpSpeed, float TurnSpeed,
    uint Category, uint Mask, bool Controlled) : IComponent
{
    internal const string DefinitionSchema = """{"type":"object","additionalProperties":false,"required":["radius","halfHeight","footOffsetY","maximumSlope","stepHeight","floorDistance","speed","gravity","jumpSpeed","turnSpeed","category","mask","controlled"],"properties":{"radius":{"type":"number"},"halfHeight":{"type":"number"},"footOffsetY":{"type":"number"},"maximumSlope":{"type":"number"},"stepHeight":{"type":"number"},"floorDistance":{"type":"number"},"speed":{"type":"number"},"gravity":{"type":"number"},"jumpSpeed":{"type":"number"},"turnSpeed":{"type":"number"},"category":{"type":"integer"},"mask":{"type":"integer"},"controlled":{"type":"boolean"}}}""";
    internal const ComponentFieldFlags FieldUsage = ComponentFieldFlags.Persistent | ComponentFieldFlags.Editable | ComponentFieldFlags.RuntimeRead;
    public const string TypeId = "ncma.character.capsule";
    public static CharacterData Default => new(.3f, .6f, 0, .7f, .4f, .5f, 4, -9.81f, 5, 10, 2, uint.MaxValue, true);
    public static CharacterData Validate(CharacterData v)
    {
        if (!CharacterComponents.Range(v.Radius, .01f, 10) || !CharacterComponents.Range(v.HalfHeight, .001f, 10) ||
            !CharacterComponents.Range(v.FootOffsetY, -100, 100) || !CharacterComponents.Range(v.MaximumSlope, .01f, 1.55f) ||
            !CharacterComponents.Range(v.StepHeight, 0, 1) || !CharacterComponents.Range(v.FloorDistance, 0, 1) ||
            !CharacterComponents.Range(v.Speed, 0, 100) || !CharacterComponents.Range(v.Gravity, -100, 0) ||
            !CharacterComponents.Range(v.JumpSpeed, 0, 100) || !CharacterComponents.Range(v.TurnSpeed, 0, 100) || !CharacterComponents.Category(v.Category))
            throw new ArgumentException("Invalid capsule movement configuration.");
        return v;
    }
}
public readonly record struct BoxColliderData(float HalfX, float HalfY, float HalfZ, float Density, uint Category, bool Dynamic) : IComponent
{
    public const string TypeId = "ncma.physics.box";
    [ComponentRegistration]
    internal static void Register(ComponentRegistry registry) => registry.Register<BoxColliderData>(TypeId, 1,
        CharacterComponents.Schema(("halfX", "number"), ("halfY", "number"), ("halfZ", "number"), ("density", "number"), ("category", "integer"), ("dynamic", "boolean")), Validate, runtimeAttachable: false);
    public static BoxColliderData Validate(BoxColliderData v)
    {
        if (!CharacterComponents.Range(v.HalfX, .001f, 1000) || !CharacterComponents.Range(v.HalfY, .001f, 1000) ||
            !CharacterComponents.Range(v.HalfZ, .001f, 1000) || !CharacterComponents.Range(v.Density, .001f, 10000) || !CharacterComponents.Category(v.Category))
            throw new ArgumentException("Invalid box collision configuration.");
        return v;
    }
}
public readonly record struct FollowCameraData(Guid Target, float OffsetX, float OffsetY, float OffsetZ, float LookHeight) : IComponent
{
    public const string TypeId = "ncma.camera.follow";
    [ComponentRegistration]
    internal static void Register(ComponentRegistry registry) => registry.Register<FollowCameraData>(TypeId, 1,
        CharacterComponents.Schema(("target", "string"), ("offsetX", "number"), ("offsetY", "number"), ("offsetZ", "number"), ("lookHeight", "number")), Validate, runtimeAttachable: false);
    public static FollowCameraData Validate(FollowCameraData v)
    {
        if (v.Target == Guid.Empty || !CharacterComponents.Range(v.OffsetX, -1000, 1000) || !CharacterComponents.Range(v.OffsetY, -1000, 1000) ||
            !CharacterComponents.Range(v.OffsetZ, -1000, 1000) || !CharacterComponents.Range(v.LookHeight, -1000, 1000) ||
            v.OffsetX * v.OffsetX + v.OffsetZ * v.OffsetZ < .01f) throw new ArgumentException("Follow camera requires an offset away from its vertical look axis.");
        return v;
    }
}
public static class CharacterComponents
{
    internal static bool Range(float v, float min, float max) => float.IsFinite(v) && v >= min && v <= max;
    internal static bool Category(uint v) => v != 0 && (v & (v - 1)) == 0;
    internal static string Schema(params (string Name, string Type)[] f) => JsonSerializer.Serialize(new
    {
        type = "object",
        additionalProperties = false,
        required = f.Select(x => x.Name),
        properties = f.ToDictionary(x => x.Name, x => new { type = x.Type })
    });
    private static readonly ComponentRegistry Decoder = CompositionRegistry.Build(Generated.ComponentModule.Module);
    public static bool HasPhysics(ISceneContent s) => s.Objects.Any(o => o.Components.Any(c => c.TypeId is CharacterData.TypeId or BoxColliderData.TypeId));
    public static void RequireComposition(ISceneContent snapshot)
    {
        SceneRenderValidation.RequireComposition(snapshot); var objects = snapshot.Objects.ToDictionary(o => o.Id); int characters = 0, controlled = 0, boxes = 0;
        foreach (var obj in snapshot.Objects)
        {
            var parts = obj.Components.ToDictionary(c => c.TypeId);
            bool character = parts.ContainsKey(CharacterData.TypeId), box = parts.ContainsKey(BoxColliderData.TypeId);
            if(parts.ContainsKey(Ncma.Animation.RootMotionData.TypeId) && (!character || !parts.ContainsKey(SkinnedMeshData.TypeId) ||
                parts.ContainsKey(Ncma.Animation.ClipPlaybackData.TypeId) == parts.ContainsKey(Ncma.Animation.AnimatorData.TypeId)))
                throw new ArgumentException("Root motion requires capsule/skin and exactly one clip or Animator source.");
            if (character && box) throw new ArgumentException("Character capsule and box collider are exclusive.");
            if(parts.ContainsKey(HealthData.TypeId) && !character && !box)throw new ArgumentException("Combat health requires a numerical collision binding.");
            if (character || box)
            {
                if (!parts.TryGetValue("ncma.transform", out var t)) throw new ArgumentException("Physics binding requires Transform.");
                var transform = Decoder.Decode<TransformData>(t);
                if (transform.Scale != System.Numerics.Vector3.One || MathF.Abs(transform.Position.X) > 90000 || MathF.Abs(transform.Position.Y) > 90000 || MathF.Abs(transform.Position.Z) > 90000 ||
                    character && (MathF.Abs(transform.Rotation.X) > 1e-6f || MathF.Abs(transform.Rotation.Z) > 1e-6f))
                    throw new ArgumentException("Physics bindings require unit scale, bounded position and yaw-only character rotation.");
            }
            if (character) { var data = Decoder.Decode<CharacterData>(parts[CharacterData.TypeId]); characters++; if (data.Controlled) controlled++; }
            if (box) { _ = Decoder.Decode<BoxColliderData>(parts[BoxColliderData.TypeId]); boxes++; }
            if (parts.TryGetValue(FollowCameraData.TypeId, out var follow))
            {
                var data = Decoder.Decode<FollowCameraData>(follow);
                if (!parts.ContainsKey(CameraData.TypeId) || !objects.TryGetValue(data.Target, out var target) || !target.Components.Any(c => c.TypeId == CharacterData.TypeId))
                    throw new ArgumentException("Follow camera requires a scene camera and exact character target.");
            }
        }
        if (characters > 32 || boxes > 4096 || controlled > 1) throw new ArgumentException("Character/collider/control budget exceeded.");
    }
}
