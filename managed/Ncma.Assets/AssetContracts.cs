using System.Text.Json.Serialization;

namespace Ncma.Assets;

// Persistent identity. Default(AssetId) is invalid and rejected at every persistence boundary.
public readonly record struct AssetId
{
    public AssetId(Guid value)
    {
        if (value == Guid.Empty) throw new ArgumentException("Asset UUID must not be empty.", nameof(value));
        Value = value;
    }
    public Guid Value { get; }
    public override string ToString() => Value.ToString("D");
}

public enum AssetKind { Character, StaticMesh, SkinnedMesh, Skeleton, Clip, Texture, Material, MaterialSet, Prefab, OverrideSet, AnimationGraph, Environment }
public readonly record struct AssetRef(AssetId Id, AssetKind ExpectedKind);
public sealed record AssetDiagnostic(string Code, Guid? AssetId, string Path, string Message);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ImportSettings(
    [property: JsonRequired] int Version,
    [property: JsonRequired] int SampleRate,
    [property: JsonRequired] bool GenerateTangents);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AssetDependency(
    [property: JsonRequired] Guid AssetId,
    [property: JsonRequired] AssetKind ExpectedKind);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SubassetRecord(
    [property: JsonRequired] Guid AssetId,
    [property: JsonRequired] AssetKind Kind,
    [property: JsonRequired] string SourceKey,
    [property: JsonRequired] string Name,
    [property: JsonRequired] bool Tombstone);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AssetGeneration(
    [property: JsonRequired] ulong Number,
    [property: JsonRequired] string ContentHash,
    [property: JsonRequired] string RelativePath);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AssetRecord(
    [property: JsonRequired] int Version,
    [property: JsonRequired] Guid AssetId,
    [property: JsonRequired] AssetKind Kind,
    [property: JsonRequired] string SourcePath,
    [property: JsonRequired] string SourceHash,
    [property: JsonRequired] string Importer,
    [property: JsonRequired] int ImporterVersion,
    [property: JsonRequired] ImportSettings Settings,
    [property: JsonRequired] SubassetRecord[] Subassets,
    [property: JsonRequired] AssetDependency[] Dependencies,
    [property: JsonRequired] AssetGeneration? Generation);

// Frozen asset math contract; the old Renderer reference DTO keeps its existing LH meaning.
public static class AssetCoordinates
{
    public const string Handedness = "right";
    public const string UpAxis = "+Y";
    public const string ForwardAxis = "-Z";
    public const string Unit = "metre";
    public const string MatrixEncoding = "column-major-column-vector";
    public const string TextureOrigin = "top-left";
    public const string FrontFace = "counter-clockwise";
}
