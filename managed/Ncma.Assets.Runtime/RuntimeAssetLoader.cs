using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Ncma.Assets.Runtime;

// Trusted startup/explicit refresh API. No frame callback, World access, import worker, source FBX or writes.
public static class RuntimeAssetLoader
{
    public const int MaxEntries = 16384, MaxRequired = 4096, MaxGenerations = 128;
    public const long MaxRetainedBytes = 512L * 1024 * 1024;
    public const int MaxGenerationBytes = 64 * 1024 * 1024;
    public static RuntimeAssetSnapshot Prepare(string projectRoot, Guid project, IEnumerable<AssetRef> required,
        bool strictMissing = true, CancellationToken cancellation = default)
    {
        if (project == Guid.Empty) throw new ArgumentException("Runtime project UUID required.");
        ArgumentNullException.ThrowIfNull(required); ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        var supplied = required.Take(MaxRequired + 1).ToArray();
        if (supplied.Length > MaxRequired) throw new ArgumentException("Runtime reference budget.");
        var requests = supplied.Distinct().ToArray();
        var assets = new Dictionary<Guid, RuntimeAsset>(); var pins = new List<RuntimeReadPin>(); var diagnostics = new List<RuntimeAssetDiagnostic>();
        try
        {
            // Empty/Null scenes do not even scan asset directories or initialize a 3D dependency.
            if (requests.Length == 0) return new(new(assets, pins, diagnostics, project));
            string root = RuntimeReadPin.Root(projectRoot);
            var records = new List<AssetRecord>(); var authored = new Dictionary<Guid, RuntimeAsset>(); long retained = 0;
            foreach (string path in Scan(root, cancellation))
            {
                if (path.EndsWith(".journal", StringComparison.Ordinal)) throw new ArgumentException("Runtime assets require completed authoring recovery.");
                if (!path.EndsWith(".ncmeta", StringComparison.Ordinal) && !path.EndsWith(".ncmaterial", StringComparison.Ordinal) && !path.EndsWith(".ncmatset", StringComparison.Ordinal) && !path.EndsWith(".ncmaanim", StringComparison.Ordinal)) continue;
                using var file = new RuntimeReadPin(root, path); byte[] bytes = file.Read(AssetRecordCodec.MaxBytes, cancellation);
                Charge(bytes.Length);
                if (path.EndsWith(".ncmeta", StringComparison.Ordinal)) { records.Add(AssetRecordCodec.Decode(bytes)); continue; }
                RuntimeAsset asset;
                if (path.EndsWith(".ncmaterial", StringComparison.Ordinal)) { var d = MaterialCodec.Decode(bytes); string hash = Hash(MaterialCodec.Encode(d)); asset = new RuntimeMaterialAsset(d, Token(hash), hash); }
                else if (path.EndsWith(".ncmaanim", StringComparison.Ordinal)) { var d = Ncma.Animation.AnimationGraphCodec.Decode(bytes); string hash = Hash(Ncma.Animation.AnimationGraphCodec.Encode(d)); asset = new RuntimeAnimationGraphAsset(d, Token(hash), hash); }
                else { var d = MaterialCodec.DecodeSet(bytes); string hash = Hash(MaterialCodec.Encode(d)); asset = new RuntimeMaterialSetAsset(d.AssetId, Token(hash), hash, d.Materials, null); }
                if (!authored.TryAdd(asset.Id, asset)) throw new ArgumentException("Duplicate authoring asset UUID.");
            }
            var catalog = new AssetCatalog(records); var loaded = new HashSet<Guid>(); var visited = new HashSet<AssetRef>();
            foreach (var asset in authored.Values)
                if (catalog.TryResolve(new(new(asset.Id), asset.Kind), out _, out var collision) || collision != "asset_missing")
                    throw new ArgumentException("Authoring/derived UUID collision.");
            foreach (var request in requests) Resolve(request);
            foreach (var material in assets.Values.OfType<RuntimeMaterialAsset>())
            {
                _ = MaterialSurfaceContract.Inspect(material.Definition,id=>assets.TryGetValue(id,out var asset)?
                    asset is RuntimeTextureAsset texture?texture.Data.Semantic:throw new ArgumentException("Material dependency is not a texture."):null,strictMissing);
            }
            var state = new RuntimeAssetState(assets, pins, diagnostics, project);
            using (var validation = new RuntimeAssetLease(state)) {
                state.References++;
                foreach (var graph in assets.Values.OfType<RuntimeAnimationGraphAsset>()) graph.ValidateProgram(validation);
            }
            return new(state);

            void Charge(int bytes) { if (bytes > MaxRetainedBytes - retained) throw new ArgumentException("Runtime CPU preparation byte budget."); retained += bytes; cancellation.ThrowIfCancellationRequested(); }
            void Resolve(AssetRef request)
            {
                cancellation.ThrowIfCancellationRequested();
                if (request.Id.Value == Guid.Empty || !Enum.IsDefined(request.ExpectedKind)) throw new ArgumentException("Invalid runtime typed reference.");
                if (!visited.Add(request)) return;
                if (visited.Count > MaxRequired) throw new ArgumentException("Runtime dependency closure budget.");
                if (assets.TryGetValue(request.Id.Value, out var old)) { RequireKind(old); return; }
                if (authored.TryGetValue(request.Id.Value, out var author))
                {
                    RequireKind(author); assets.Add(author.Id, author);
                    if (author is RuntimeMaterialSetAsset set) for (int i = 0; i < set.SlotCount; i++) Resolve(new(new(set.MaterialAt(i)), AssetKind.Material));
                    if (author is RuntimeMaterialAsset material)
                        foreach (Guid texture in material.Definition.TextureIds.Where(id => id != Guid.Empty)) Resolve(new(new(texture), AssetKind.Texture));
                    if (author is RuntimeAnimationGraphAsset graph)
                        foreach (var dependency in Ncma.Animation.AnimationGraphValidation.Dependencies(graph.CopyDefinition())) Resolve(new(new(dependency.Id), dependency.IsSkeleton ? AssetKind.Skeleton : AssetKind.Clip));
                    return;
                }
                if (!catalog.TryResolve(request, out var descriptor, out var code))
                { if (code == "asset_kind_mismatch" || strictMissing) throw new ArgumentException(code + ": " + request.Id); diagnostics.Add(new(code, request.Id.Value)); return; }
                var record = descriptor!;
                if (record.Generation is not { } generation) { Missing("asset_generation_missing"); return; }
                string expected = $"out/assets/{project:N}/{record.AssetId:N}/{generation.Number}-{generation.ContentHash}.nca";
                if (generation.RelativePath != expected) throw new ArgumentException("Generation path/project/identity mismatch.");
                if (loaded.Add(record.AssetId))
                {
                    if (pins.Count >= MaxGenerations) throw new ArgumentException("Runtime generation pin budget.");
                    RuntimeReadPin file;
                    try { file = new(root, expected, derived: true); }
                    catch (Exception e) when (!strictMissing && e is FileNotFoundException or DirectoryNotFoundException) { Missing("asset_generation_missing"); loaded.Remove(record.AssetId); return; }
                    pins.Add(file); byte[] bytes = file.Read(MaxGenerationBytes, cancellation); Charge(bytes.Length);
                    if (Hash(bytes) != generation.ContentHash) throw new ArgumentException("Runtime generation checksum mismatch.");
                    var blocks = DerivedAssetCodec.Decode(bytes);
                    if (record.Kind is AssetKind.Character or AssetKind.StaticMesh)
                    {
                        var manifest = ModelAssetManifestCodec.ValidateBundle(bytes, record);
                        foreach (var block in blocks)
                        {
                            string hash = Hash(block.Data); RuntimeAsset value;
                            if (block.AssetId == record.AssetId) value = new RuntimeDataAsset(block.AssetId, block.Kind, generation.Number, hash, block.Data, record.AssetId, manifest.Skeleton ?? Guid.Empty);
                            else value = block.Kind switch {
                                AssetKind.StaticMesh or AssetKind.SkinnedMesh => new RuntimeMeshAsset(block.AssetId, block.Kind, generation.Number, hash, block.Data, record.AssetId, manifest.Skeleton ?? Guid.Empty),
                                AssetKind.MaterialSet => new RuntimeMaterialSetAsset(block.AssetId, generation.Number, hash, null, ModelPayloadCodec.DecodeMaterials(block.Data).Names),
                                _ => new RuntimeDataAsset(block.AssetId, block.Kind, generation.Number, hash, block.Data, record.AssetId, manifest.Skeleton ?? Guid.Empty) };
                            if (!assets.TryAdd(value.Id, value)) throw new ArgumentException("Duplicate runtime block identity.");
                            if (value is RuntimeMaterialSetAsset { ImportedSlotsOnly: true }) diagnostics.Add(new("imported_material_slots_only", value.Id));
                        }
                    }
                    else if (record.Kind == AssetKind.Texture && blocks.Length == 1 && blocks[0].AssetId == record.AssetId && blocks[0].Kind == AssetKind.Texture && record.Subassets.Length == 0)
                        assets.Add(record.AssetId, new RuntimeTextureAsset(record.AssetId, generation.Number, TextureData.Decode(blocks[0].Data)));
                    else throw new ArgumentException("Unsupported runtime derived asset contract.");
                    foreach (var dependency in record.Dependencies) Resolve(new(new(dependency.AssetId), dependency.ExpectedKind));
                }
                if (!assets.TryGetValue(request.Id.Value, out var result)) throw new ArgumentException("Typed runtime block missing.");
                RequireKind(result);
                void Missing(string missing) { if (strictMissing) throw new ArgumentException(missing + ": " + request.Id); diagnostics.Add(new(missing, request.Id.Value)); }
                void RequireKind(RuntimeAsset value) { if (value.Kind != request.ExpectedKind) throw new ArgumentException("Runtime kind mismatch."); }
            }
        }
        catch { foreach (var pin in pins) pin.Dispose(); throw; }
    }
    private static IEnumerable<string> Scan(string root, CancellationToken cancellation)
    {
        if (!Directory.Exists(Path.Combine(root, "assets"))) yield break;
        var directories = new Queue<string>(); directories.Enqueue("assets"); int entries = 0;
        var cases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (directories.TryDequeue(out string? relative))
        {
            cancellation.ThrowIfCancellationRequested();
            using var lease = new RuntimeReadPin(root, relative, directory: true);
            var children = Directory.EnumerateFileSystemEntries(Path.Combine(root, relative)).Take(MaxEntries - entries + 1).ToArray();
            if (children.Length > MaxEntries - entries) throw new ArgumentException("Runtime asset scan entry budget.");
            Array.Sort(children, StringComparer.Ordinal);
            foreach (string path in children)
            {
                if (++entries > MaxEntries) throw new ArgumentException("Runtime asset scan entry budget.");
                string child = Path.GetRelativePath(root, path).Replace('\\', '/'); AssetPaths.Validate(child);
                if (!cases.Add(child) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Runtime assets reject casing collisions and reparse points.");
                if (Directory.Exists(path)) directories.Enqueue(child); else yield return child;
            }
        }
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static ulong Token(string hash) { ulong token = BinaryPrimitives.ReadUInt64LittleEndian(Convert.FromHexString(hash)); return token == 0 ? 1 : token; }
}
