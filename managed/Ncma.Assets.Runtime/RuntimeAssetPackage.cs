using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ncma.Assets.Runtime;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RuntimePackageEntry([property: JsonRequired] Guid AssetId, [property: JsonRequired] AssetKind Kind,
    [property: JsonRequired] ulong Generation, [property: JsonRequired] string Hash, [property: JsonRequired] string Encoding,
    [property: JsonRequired] Guid ModelId, [property: JsonRequired] Guid SkeletonId, [property: JsonRequired] int Offset, [property: JsonRequired] int Length);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RuntimePackageIndex([property: JsonRequired] int Version, [property: JsonRequired] Guid ProjectId,
    [property: JsonRequired] RuntimePackageEntry[] Assets);

// NCP1: immutable typed runtime data only, no source paths/catalog/host handles/importer execution.
// This packages an already validated generation closure. It is NOT cold-source cooking or signing.
public static class RuntimeAssetPackage
{
    public const int MaxBytes = 64 * 1024 * 1024, MaxIndexBytes = 2 * 1024 * 1024, MaxAssets = 4096;
    private const uint Magic = 0x3150434E;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, MaxDepth = 12,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) } };
    public static byte[] Encode(RuntimeAssetLease source)
    {
        if(source.IsAuthoringPreview)throw new ArgumentException("Unsaved authoring previews cannot be packaged.");
        ArgumentNullException.ThrowIfNull(source);
        var entries = new List<RuntimePackageEntry>(); var payloads = new List<byte[]>(); int offset = 0;
        foreach (var asset in source.List()) {
            byte[] bytes; string encoding; Guid model = Guid.Empty, rig = Guid.Empty;
            switch (asset) {
                case RuntimeMeshAsset mesh: bytes = ModelPayloadCodec.Encode(mesh.CopyPayload()); encoding = "mesh"; model = mesh.ModelId; rig = mesh.SkeletonId; break;
                case RuntimeAnimationGraphAsset graph:
                    // Author layout is not runtime state. Normalize only presentation coordinates;
                    // preserve every semantic UUID/node/link/policy and derive a runtime content identity.
                    if (Hash(graph.CopyData()) != graph.ContentHash) throw new ArgumentException("Runtime package noncanonical graph publication.");
                    var definition = graph.CopyDefinition();
                    bytes = Ncma.Animation.AnimationGraphCodec.Encode(definition with { Nodes = definition.Nodes.Select(n => n with { X = 0, Y = 0 }).ToArray() });
                    encoding = "animgraph"; break;
                case RuntimeDataAsset data: bytes = data.CopyData(); encoding = "data"; model = data.ModelId; rig = data.SkeletonId; break;
                case RuntimeMaterialAsset material: bytes = MaterialCodec.Encode(material.Definition); encoding = "material"; break;
                case RuntimeMaterialSetAsset { ImportedSlotsOnly: true } slots: bytes = ModelPayloadCodec.Encode(new MaterialSlotsPayload(slots.CopyImportedNames())); encoding = "slots"; break;
                case RuntimeMaterialSetAsset set: bytes = MaterialCodec.Encode(set.CopyDefinition()); encoding = "materialSet"; break;
                case RuntimeTextureAsset texture: bytes = texture.Data.Encode(); encoding = "texture"; break;
                default: throw new ArgumentException("Unsupported runtime package asset.");
            }
            if (entries.Count >= MaxAssets || bytes.Length > MaxBytes - 16 - offset) throw new ArgumentException("Runtime package budget.");
            string hash = Hash(bytes);
            if (asset is not RuntimeAnimationGraphAsset && hash != asset.ContentHash) throw new ArgumentException("Runtime package noncanonical payload.");
            ulong generation = asset.Generation;
            if (asset is RuntimeAnimationGraphAsset) { generation = BinaryPrimitives.ReadUInt64LittleEndian(Convert.FromHexString(hash)); if (generation == 0) generation = 1; }
            entries.Add(new(asset.Id, asset.Kind, generation, hash, encoding, model, rig, offset, bytes.Length)); payloads.Add(bytes); offset += bytes.Length;
        }
        var index = new RuntimePackageIndex(1, source.ProjectId, entries.ToArray());
        byte[] table = JsonSerializer.SerializeToUtf8Bytes(index, Json);
        if (table.Length > MaxIndexBytes || table.Length > MaxBytes - 16 - offset) throw new ArgumentException("Runtime package index budget.");
        byte[] result = new byte[16 + table.Length + offset];
        BinaryPrimitives.WriteUInt32LittleEndian(result, Magic); BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(8), table.Length); BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(12), offset);
        table.CopyTo(result, 16); int at = 16 + table.Length;
        foreach (byte[] payload in payloads) { payload.CopyTo(result, at); at += payload.Length; }
        _ = Decode(result, source.ProjectId); return result;
    }
    // Strict pure validation before any GPU or gameplay callback. Returned index is an owned DTO.
    public static RuntimePackageIndex Inspect(byte[] bytes, Guid project) => Decode(bytes, project).Index;
    private static (RuntimePackageIndex Index, Dictionary<Guid, RuntimeAsset> Assets, List<RuntimeAssetDiagnostic> Diagnostics) Decode(byte[] bytes, Guid project)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (project == Guid.Empty || bytes.Length is < 18 or > MaxBytes || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Magic ||
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)) != 1) throw new ArgumentException("Runtime package header/version.");
        int indexBytes = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8)), payloadBytes = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12));
        if (indexBytes is < 2 or > MaxIndexBytes || payloadBytes < 0 || 16L + indexBytes + payloadBytes != bytes.Length) throw new ArgumentException("Runtime package range.");
        using var document = JsonDocument.Parse(bytes.AsMemory(16, indexBytes), new JsonDocumentOptions { MaxDepth = 12 });
        Visit(document.RootElement);
        var index = JsonSerializer.Deserialize<RuntimePackageIndex>(bytes.AsSpan(16, indexBytes), Json) ?? throw new ArgumentException("Runtime package index.");
        if (index.Version != 1 || index.ProjectId != project || index.Assets is null || index.Assets.Length > MaxAssets) throw new ArgumentException("Runtime package identity/budget.");
        var assets = new Dictionary<Guid, RuntimeAsset>(); var diagnostics = new List<RuntimeAssetDiagnostic>(); int offset = 0; Guid previous = Guid.Empty;
        foreach (var entry in index.Assets) {
            if (entry is null || entry.AssetId == Guid.Empty || entry.AssetId.CompareTo(previous) <= 0 || entry.Generation == 0 ||
                entry.Offset != offset || entry.Length <= 0 || entry.Length > payloadBytes - offset) throw new ArgumentException("Runtime package entry/order/range.");
            byte[] data = bytes.AsSpan(16 + indexBytes + offset, entry.Length).ToArray();
            if (entry.Hash != Hash(data)) throw new ArgumentException("Runtime package checksum.");
            RuntimeAsset asset = entry.Encoding switch {
                "animgraph" when entry.Kind == AssetKind.AnimationGraph && entry.ModelId == Guid.Empty && entry.SkeletonId == Guid.Empty =>
                    new RuntimeAnimationGraphAsset(data, entry.Generation, entry.Hash),
                "mesh" when entry.Kind is AssetKind.StaticMesh or AssetKind.SkinnedMesh && entry.ModelId != Guid.Empty =>
                    new RuntimeMeshAsset(entry.AssetId, entry.Kind, entry.Generation, entry.Hash, data, entry.ModelId, entry.SkeletonId),
                "data" when entry.Kind is AssetKind.Character or AssetKind.StaticMesh or AssetKind.Skeleton or AssetKind.Clip && entry.ModelId != Guid.Empty =>
                    new RuntimeDataAsset(entry.AssetId, entry.Kind, entry.Generation, entry.Hash, data, entry.ModelId, entry.SkeletonId),
                "material" when entry.Kind == AssetKind.Material && entry.ModelId == Guid.Empty && entry.SkeletonId == Guid.Empty =>
                    new RuntimeMaterialAsset(MaterialCodec.Decode(data), entry.Generation, entry.Hash),
                "materialSet" when entry.Kind == AssetKind.MaterialSet && entry.ModelId == Guid.Empty && entry.SkeletonId == Guid.Empty =>
                    new RuntimeMaterialSetAsset(entry.AssetId, entry.Generation, entry.Hash, MaterialCodec.DecodeSet(data).Materials, null),
                "slots" when entry.Kind == AssetKind.MaterialSet && entry.ModelId == Guid.Empty && entry.SkeletonId == Guid.Empty =>
                    new RuntimeMaterialSetAsset(entry.AssetId, entry.Generation, entry.Hash, null, ModelPayloadCodec.DecodeMaterials(data).Names),
                "texture" when entry.Kind == AssetKind.Texture && entry.ModelId == Guid.Empty && entry.SkeletonId == Guid.Empty =>
                    new RuntimeTextureAsset(entry.AssetId, entry.Generation, TextureData.Decode(data)),
                _ => throw new ArgumentException("Runtime package type/encoding.") };
            if (asset.Id != entry.AssetId || asset.ContentHash != entry.Hash || entry.Encoding == "materialSet" && MaterialCodec.DecodeSet(data).AssetId != entry.AssetId)
                throw new ArgumentException("Runtime package payload identity.");
            if (asset is RuntimeMeshAsset mesh && (mesh.CopyPayload().Skinned != (mesh.Kind == AssetKind.SkinnedMesh))) throw new ArgumentException("Runtime package mesh type.");
            assets.Add(asset.Id, asset); previous = entry.AssetId; offset += entry.Length;
            if (asset is RuntimeMaterialSetAsset { ImportedSlotsOnly: true }) diagnostics.Add(new("imported_material_slots_only", asset.Id));
        }
        if (offset != payloadBytes) throw new ArgumentException("Runtime package trailing payload.");
        ValidateClosure(assets);
        var validationState = new RuntimeAssetState(assets, [], diagnostics, project);
        using (var validation = new RuntimeAssetLease(validationState)) {
            validationState.References++;
            foreach (var graph in assets.Values.OfType<RuntimeAnimationGraphAsset>()) {
                if (Hash(graph.CopyData()) != graph.ContentHash) throw new ArgumentException("Noncanonical graph package data.");
                if (graph.CopyDefinition().Nodes.Any(n => n.X != 0 || n.Y != 0)) throw new ArgumentException("Runtime package cannot contain editor layout coordinates.");
                graph.ValidateProgram(validation);
            }
        }
        return (index, assets, diagnostics);
    }
    private static void ValidateClosure(Dictionary<Guid, RuntimeAsset> assets)
    {
        RuntimeAsset Require(Guid id, AssetKind kind) => assets.TryGetValue(id, out var asset) && asset.Kind == kind ? asset : throw new ArgumentException("Runtime package dependency/type.");
        var modelMembers = new HashSet<Guid>();
        foreach (var root in assets.Values.OfType<RuntimeDataAsset>().Where(d => d.Kind is AssetKind.Character or AssetKind.StaticMesh)) {
            var manifest = ModelAssetManifestCodec.Decode(root.CopyData());
            if (manifest.Root != root.Id || root.ModelId != root.Id || manifest.StaticOnly != (root.Kind == AssetKind.StaticMesh) || root.SkeletonId != (manifest.Skeleton ?? Guid.Empty))
                throw new ArgumentException("Runtime package model identity.");
            int bones = 0; modelMembers.Add(root.Id);
            if (manifest.Skeleton is Guid rig) {
                var skeleton = (RuntimeDataAsset)Require(rig, AssetKind.Skeleton); Same(skeleton); bones = ModelPayloadCodec.DecodeSkeleton(skeleton.CopyData()).Bones.Length;
            }
            foreach (var part in manifest.Meshes) {
                var mesh = Require(part.Mesh, manifest.StaticOnly ? AssetKind.StaticMesh : AssetKind.SkinnedMesh) as RuntimeMeshAsset ?? throw new ArgumentException("Runtime package mesh role.");
                var slots = (RuntimeMaterialSetAsset)Require(part.Materials, AssetKind.MaterialSet);
                var payload = mesh.CopyPayload();
                if (mesh.ModelId != root.Id || mesh.SkeletonId != root.SkeletonId || mesh.Generation != root.Generation || payload.BoneCount != bones ||
                    !slots.ImportedSlotsOnly || slots.Generation != root.Generation || payload.MaterialSlots != slots.CopyImportedNames().Length || !modelMembers.Add(mesh.Id) || !modelMembers.Add(slots.Id))
                    throw new ArgumentException("Runtime package model roles.");
            }
            foreach (Guid id in manifest.Clips) {
                var clip = (RuntimeDataAsset)Require(id, AssetKind.Clip); Same(clip);
                if (ModelPayloadCodec.DecodeClip(clip.CopyData()).BoneCount != bones) throw new ArgumentException("Runtime package clip rig.");
            }
            void Same(RuntimeDataAsset member) { if (member.ModelId != root.Id || member.SkeletonId != root.SkeletonId || member.Generation != root.Generation || !modelMembers.Add(member.Id)) throw new ArgumentException("Runtime package rig/clip identity."); }
        }
        foreach (var asset in assets.Values) {
            if ((asset is RuntimeMeshAsset or RuntimeDataAsset || asset is RuntimeMaterialSetAsset { ImportedSlotsOnly: true }) && !modelMembers.Contains(asset.Id))
                throw new ArgumentException("Runtime package orphan model role.");
            if (asset is RuntimeMaterialSetAsset { ImportedSlotsOnly: false } set) for (int i = 0; i < set.SlotCount; i++) _ = Require(set.MaterialAt(i), AssetKind.Material);
            if (asset is RuntimeMaterialAsset material) {
                var d = material.Definition;
                foreach (Guid id in d.TextureIds.Where(id => id != Guid.Empty)) _ = Require(id, AssetKind.Texture);
                Check(d.BaseTexture, TextureSemantic.Color); Check(d.EmissiveTexture, TextureSemantic.Color); Check(d.NormalTexture, TextureSemantic.Normal);
                Check(d.MetallicTexture, TextureSemantic.Data); Check(d.RoughnessTexture, TextureSemantic.Data); Check(d.AOTexture, TextureSemantic.Data);
                void Check(Guid id, TextureSemantic semantic) { if (id != Guid.Empty && ((RuntimeTextureAsset)Require(id, AssetKind.Texture)).Data.Semantic != semantic) throw new ArgumentException("Runtime package texture semantic."); }
            }
        }
    }
    public static RuntimeAssetSnapshot Prepare(string root, string relative, Guid project, IEnumerable<AssetRef> required, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(required);
        ArgumentException.ThrowIfNullOrEmpty(relative);
        if (!relative.EndsWith(".ncpak", StringComparison.Ordinal)) throw new ArgumentException("Runtime package extension.");
        var requests = required.Take(RuntimeAssetLoader.MaxRequired + 1).ToArray();
        if (requests.Length > RuntimeAssetLoader.MaxRequired) throw new ArgumentException("Runtime package request budget.");
        var pin = new RuntimeReadPin(RuntimeReadPin.Root(root), relative);
        try {
            var decoded = Decode(pin.Read(MaxBytes, cancellation), project);
            foreach (var request in requests) if (request.Id.Value == Guid.Empty || !decoded.Assets.TryGetValue(request.Id.Value, out var asset) || asset.Kind != request.ExpectedKind)
                throw new ArgumentException("Runtime package required reference missing/type.");
            cancellation.ThrowIfCancellationRequested(); return new(new(decoded.Assets, [pin], decoded.Diagnostics, project));
        }
        catch { pin.Dispose(); throw; }
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static void Visit(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object) { var names = new HashSet<string>(StringComparer.Ordinal); foreach (var property in element.EnumerateObject()) { if (!names.Add(property.Name)) throw new ArgumentException("Duplicate runtime package index key."); Visit(property.Value); } }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) Visit(item);
    }
}
