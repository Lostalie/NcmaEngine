using System.Numerics;

namespace Ncma.Assets.Runtime;

public abstract class RuntimeAsset
{
    public Guid Id { get; }
    public AssetKind Kind { get; }
    public ulong Generation { get; }
    public string ContentHash { get; }
    internal RuntimeAsset(Guid id, AssetKind kind, ulong generation, string hash)
    { Id = id; Kind = kind; Generation = generation; ContentHash = hash; }
}
public sealed class RuntimeMeshAsset : RuntimeAsset
{
    private readonly byte[] _encoded;
    public Guid ModelId { get; }
    public Guid SkeletonId { get; }
    public int MaterialSlots { get; }
    public Vector3 BoundsMin { get; }
    public Vector3 BoundsMax { get; }
    internal RuntimeMeshAsset(Guid id, AssetKind kind, ulong generation, string hash, byte[] encoded, Guid model, Guid rig)
        : base(id, kind, generation, hash)
    {
        _encoded = encoded; ModelId = model; SkeletonId = rig;
        var payload = ModelPayloadCodec.DecodeMesh(encoded); MaterialSlots = Math.Max(1, payload.MaterialSlots);
        BoundsMin = payload.Vertices.Select(v => v.Position).Aggregate(Vector3.Min);
        BoundsMax = payload.Vertices.Select(v => v.Position).Aggregate(Vector3.Max);
    }
    // Owned copy, for OFF-frame mesh upload preparation. No mutable arrays retained by callers.
    public MeshPayload CopyPayload() => ModelPayloadCodec.DecodeMesh(_encoded);
}
public sealed class RuntimeMaterialAsset : RuntimeAsset
{
    public MaterialDefinition Definition { get; }
    internal RuntimeMaterialAsset(MaterialDefinition definition, ulong generation, string hash) : base(definition.AssetId, AssetKind.Material, generation, hash) => Definition = definition;
}
public sealed class RuntimeMaterialSetAsset : RuntimeAsset
{
    private readonly Guid[] _materials;
    private readonly string[] _names;
    public bool ImportedSlotsOnly { get; }
    public int SlotCount { get; }
    internal RuntimeMaterialSetAsset(Guid id, ulong generation, string hash, Guid[]? materials, string[]? names) : base(id, AssetKind.MaterialSet, generation, hash)
    { ImportedSlotsOnly = names is not null; _materials = materials is null ? [] : (Guid[])materials.Clone(); _names = names is null ? [] : (string[])names.Clone(); SlotCount = Math.Max(1, ImportedSlotsOnly ? _names.Length : _materials.Length); }
    public Guid MaterialAt(int slot)
    { if (slot < 0 || slot >= SlotCount) throw new ArgumentException("Material slot range."); return ImportedSlotsOnly ? Guid.Empty : _materials[slot]; }
    public string[] CopyImportedNames() => (string[])_names.Clone();
    public MaterialSetDefinition CopyDefinition()
    { if (ImportedSlotsOnly) throw new InvalidOperationException("Imported MAT1 names are not authoring PBR material UUIDs."); return new(1, Id, (Guid[])_materials.Clone()); }
}
public sealed class RuntimeTextureAsset : RuntimeAsset
{
    public TextureData Data { get; }
    internal RuntimeTextureAsset(Guid id, ulong generation, TextureData data) : base(id, AssetKind.Texture, generation, data.ContentHash) => Data = data;
}
public sealed class RuntimeEnvironmentAsset : RuntimeAsset
{
    public EnvironmentPackage Package { get; }
    internal RuntimeEnvironmentAsset(EnvironmentPackage package) : base(package.AssetId, AssetKind.Environment, package.Generation, package.ContentHash) => Package = package;
}
public sealed class RuntimeDataAsset : RuntimeAsset
{
    private readonly byte[] _data;
    public Guid ModelId { get; }
    public Guid SkeletonId { get; }
    internal RuntimeDataAsset(Guid id, AssetKind kind, ulong generation, string hash, byte[] data, Guid model = default, Guid rig = default) : base(id, kind, generation, hash)
    { _data = data; ModelId = model; SkeletonId = rig; }
    public byte[] CopyData() => (byte[])_data.Clone();
}
public sealed record RuntimeAssetDiagnostic(string Code, Guid AssetId);
internal sealed class RuntimeAssetState(Dictionary<Guid, RuntimeAsset> assets, List<RuntimeReadPin> pins, List<RuntimeAssetDiagnostic> diagnostics, Guid project, RuntimeAssetLease? previewParent = null)
{
    internal readonly Dictionary<Guid, RuntimeAsset> Assets = assets;
    internal readonly List<RuntimeReadPin> Pins = pins;
    internal readonly IReadOnlyList<RuntimeAssetDiagnostic> Diagnostics = diagnostics.AsReadOnly();
    internal readonly Guid Project = project, Identity = Guid.NewGuid();
    internal readonly int Thread = Environment.CurrentManagedThreadId;
    internal readonly RuntimeAssetLease? PreviewParent = previewParent;
    internal int References = 1;
    internal void Verify() { if (Environment.CurrentManagedThreadId != Thread) throw new InvalidOperationException("Runtime asset leases require the preparation owner thread."); }
    internal void Release() { Verify(); if (--References == 0) { foreach (var pin in Pins) pin.Dispose(); PreviewParent?.Dispose(); Pins.Clear(); Assets.Clear(); } }
}
// The owner may close while Play leases survive. Generation file pins end only after the final lease.
public sealed class RuntimeAssetSnapshot : IDisposable
{
    private RuntimeAssetState? _state;
    internal RuntimeAssetSnapshot(RuntimeAssetState state) => _state = state;
    public RuntimeAssetLease AcquireLease()
    { var state = _state ?? throw new ObjectDisposedException(nameof(RuntimeAssetSnapshot)); state.Verify(); var lease = new RuntimeAssetLease(state); state.References = checked(state.References + 1); return lease; }
    public void Dispose() { if (_state is null) return; _state.Verify(); _state.Release(); _state = null; }
}
public sealed class RuntimeAssetLease : IDisposable
{
    private RuntimeAssetState? _state;
    internal RuntimeAssetLease(RuntimeAssetState state) => _state = state;
    private RuntimeAssetState State { get { var state = _state ?? throw new ObjectDisposedException(nameof(RuntimeAssetLease)); state.Verify(); return state; } }
    public Guid ProjectId => State.Project;
    public Guid Identity => State.Identity;
    public int PinnedGenerations => State.Pins.Count + (State.PreviewParent?.PinnedGenerations ?? 0);
    public bool IsAuthoringPreview => State.PreviewParent is not null;
    // Explicit trusted off-frame fork. Pins and numerical payloads remain immutable; the source
    // publication/Play never changes. Preview forks cannot be nested or cooked into runtime packages.
    public RuntimeAssetSnapshot CreateGraphPreview(Ncma.Animation.AnimationGraphDefinition definition)
    {
        var source = State; if (IsAuthoringPreview) throw new ArgumentException("Nested authoring previews are forbidden.");
        var d = Ncma.Animation.AnimationGraphCodec.Decode(Ncma.Animation.AnimationGraphCodec.Encode(definition));
        if (source.Assets.TryGetValue(d.AssetId, out var old) && old.Kind != AssetKind.AnimationGraph) throw new ArgumentException("Preview graph identity collision.");
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Ncma.Animation.AnimationGraphCodec.Encode(d)));
        ulong generation = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(Convert.FromHexString(hash)); if (generation == 0) generation = 1;
        var graph = new RuntimeAnimationGraphAsset(d, generation, hash);
        var parent = AcquireLease();
        RuntimeAssetSnapshot? snapshot = null;
        try { var assets = new Dictionary<Guid,RuntimeAsset>(source.Assets) { [d.AssetId] = graph }; snapshot = new(new(assets, [], source.Diagnostics.ToList(), source.Project, parent)); using var view = snapshot.AcquireLease(); _ = graph.PrepareProgram(view); return snapshot; }
        catch { if(snapshot is not null)snapshot.Dispose();else parent.Dispose(); throw; }
    }
    public IReadOnlyList<RuntimeAssetDiagnostic> Diagnostics => State.Diagnostics;
    public RuntimeAssetLease AcquireLease()
    { var state = State; var lease = new RuntimeAssetLease(state); state.References = checked(state.References + 1); return lease; }
    public IReadOnlyList<RuntimeAsset> List() => Array.AsReadOnly(State.Assets.Values.OrderBy(a => a.Id).ToArray());
    public bool TryResolve(Guid id, AssetKind kind, out RuntimeAsset? asset)
    { if (State.Assets.TryGetValue(id, out asset)) { if (asset.Kind != kind) throw new ArgumentException("Runtime asset kind mismatch."); return true; } return false; }
    public RuntimeAsset Require(Guid id, AssetKind kind) => TryResolve(id, kind, out var asset) ? asset! : throw new ArgumentException("Runtime asset missing: " + id);
    public RuntimeMeshAsset RequireMesh(Guid id, AssetKind kind) => Require(id, kind) as RuntimeMeshAsset ?? throw new ArgumentException("Mesh components require a typed mesh subasset, not a model root.");
    public void Dispose() { if (_state is null) return; _state.Verify(); _state.Release(); _state = null; }
}
