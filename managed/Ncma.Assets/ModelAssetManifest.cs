using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ncma.Assets;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ModelMeshReference([property: JsonRequired] Guid Mesh, [property: JsonRequired] Guid Materials);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ModelAssetManifest([property: JsonRequired] int Version, [property: JsonRequired] Guid Root,
    [property: JsonRequired] bool StaticOnly, [property: JsonRequired] string SourceHash, [property: JsonRequired] ImportSettings Settings,
    [property: JsonRequired] Guid? Skeleton, [property: JsonRequired] ModelMeshReference[] Meshes, [property: JsonRequired] Guid[] Clips);

public static class ModelAssetManifestCodec
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, MaxDepth = 8 };
    public static byte[] Encode(ModelAssetManifest m) { Validate(m); byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(m, Json); if (bytes.Length > 65536) throw new ArgumentException("Manifest too large."); return bytes; }
    public static ModelAssetManifest Decode(byte[] bytes)
    {
        if (bytes.Length > 65536) throw new ArgumentException("Manifest too large.");
        using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
        void Visit(JsonElement e) { if (e.ValueKind == JsonValueKind.Object) { var names = new HashSet<string>(); foreach (var p in e.EnumerateObject()) { if (!names.Add(p.Name)) throw new ArgumentException("Duplicate manifest key."); Visit(p.Value); } } else if (e.ValueKind == JsonValueKind.Array) foreach (var item in e.EnumerateArray()) Visit(item); }
        Visit(doc.RootElement); var m = JsonSerializer.Deserialize<ModelAssetManifest>(bytes, Json) ?? throw new ArgumentException("Null manifest."); Validate(m); return m;
    }
    private static void Validate(ModelAssetManifest m)
    {
        if (m.Version != 1 || m.Root == Guid.Empty || m.Settings is null || m.Settings.Version != 1 || m.Settings.SampleRate is < 1 or > 120 ||
            m.Meshes is null || m.Meshes.Length is < 1 or > 64 || m.Clips is null || m.Clips.Length > 64 ||
            (m.StaticOnly ? m.Skeleton is not null || m.Clips.Length != 0 : m.Skeleton is null || m.Skeleton == Guid.Empty)) throw new ArgumentException("Invalid model manifest.");
        AssetRecordCodec.ValidateHash(m.SourceHash); var ids = new HashSet<Guid> { m.Root };
        if (m.Skeleton is Guid rig && !ids.Add(rig)) throw new ArgumentException("Duplicate rig.");
        foreach (var mesh in m.Meshes) if (mesh is null || mesh.Mesh == Guid.Empty || mesh.Materials == Guid.Empty || !ids.Add(mesh.Mesh) || !ids.Add(mesh.Materials)) throw new ArgumentException("Invalid/duplicate mesh reference.");
        foreach (Guid clip in m.Clips) if (clip == Guid.Empty || !ids.Add(clip)) throw new ArgumentException("Invalid/duplicate clip reference.");
    }
    // Load persistent data WITHOUT reparsing FBX. Validates typed blocks and all internal references.
    public static ModelAssetManifest ValidateBundle(byte[] bytes, AssetRecord descriptor)
    {
        var blocks = DerivedAssetCodec.Decode(bytes); var byId = blocks.ToDictionary(b => b.AssetId);
        if (!byId.TryGetValue(descriptor.AssetId, out var root) || root.Kind != descriptor.Kind) throw new ArgumentException("Missing model root.");
        var m = Decode(root.Data);
        if (m.Root != descriptor.AssetId || m.StaticOnly != (descriptor.Kind == AssetKind.StaticMesh) || m.SourceHash != descriptor.SourceHash || m.Settings != descriptor.Settings)
            throw new ArgumentException("Model root/descriptor mismatch.");
        var used = new HashSet<Guid> { descriptor.AssetId };
        int bones = m.Skeleton is Guid skeleton ? ModelPayloadCodec.DecodeSkeleton(Get(skeleton, AssetKind.Skeleton)).Bones.Length : 0;
        foreach (var entry in m.Meshes) { var mesh = ModelPayloadCodec.DecodeMesh(Get(entry.Mesh, m.StaticOnly ? AssetKind.StaticMesh : AssetKind.SkinnedMesh)); var material = ModelPayloadCodec.DecodeMaterials(Get(entry.Materials, AssetKind.MaterialSet));
            if (mesh.Skinned == m.StaticOnly || mesh.BoneCount != bones || mesh.MaterialSlots != material.Names.Length) throw new ArgumentException("Mesh dependency mismatch."); }
        foreach (Guid clip in m.Clips) if (ModelPayloadCodec.DecodeClip(Get(clip, AssetKind.Clip)).BoneCount != bones) throw new ArgumentException("Clip rig mismatch.");
        var active = descriptor.Subassets.Where(s => !s.Tombstone).ToDictionary(s => s.AssetId, s => s.Kind);
        if (byId.Count != active.Count + 1 || active.Any(p => !byId.TryGetValue(p.Key, out var b) || b.Kind != p.Value)) throw new ArgumentException("Descriptor block set mismatch.");
        if (used.Count != byId.Count) throw new ArgumentException("Unreferenced model block."); return m;
        byte[] Get(Guid id, AssetKind kind) { if (!used.Add(id) || !byId.TryGetValue(id, out var b) || b.Kind != kind) throw new ArgumentException("Missing/duplicate/wrong-kind model dependency."); return b.Data; }
    }
}
