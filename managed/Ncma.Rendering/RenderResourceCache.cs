using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using Ncma.Assets;
namespace Ncma.Rendering;

public readonly record struct RenderAssetVersion(Guid AssetId, ulong Generation, string ContentHash)
{
    public void Validate()
    { if (AssetId == Guid.Empty || Generation == 0 || ContentHash is null || ContentHash.Length != 64 || ContentHash.Any(c => !char.IsAsciiHexDigit(c) || char.IsLower(c))) throw new ArgumentException("Persistent UUID, immutable generation and uppercase SHA256 required."); }
}
public sealed record ResolvedTexture(RenderAssetVersion Version, TextureData Data);
public sealed record ResolvedMaterial(RenderAssetVersion Version, MaterialDefinition Definition);
public sealed record MaterialFallback(string Code, int Slot);
public sealed class RenderMaterialSetLease : IDisposable
{
    private RenderAssetLease<GpuMaterial>[]? _materials;
    public IReadOnlyList<MaterialFallback> Diagnostics { get; }
    internal RenderMaterialSetLease(RenderAssetLease<GpuMaterial>[] materials, MaterialFallback[] diagnostics) { _materials = materials; Diagnostics = Array.AsReadOnly(diagnostics); }
    public GpuMaterial this[uint slot] { get { ObjectDisposedException.ThrowIf(_materials is null, this); if (slot >= _materials.Length) throw new ArgumentException("Material-set slot."); return _materials[slot].Resource; } }
    public void Dispose() { if (_materials is null) return; foreach (var material in _materials) material.Dispose(); _materials = null; }
}
public sealed class RenderAssetLease<T> : IDisposable where T : class, IDisposable
{
    private RenderResourceCache? _owner;
    internal RenderResourceCache.Entry Entry { get; }
    internal RenderAssetLease(RenderResourceCache owner, RenderResourceCache.Entry entry) { _owner = owner; Entry = entry; }
    public T Resource { get { ObjectDisposedException.ThrowIf(_owner is null, this); _owner.Verify(); return (T)(Entry.Resource ?? throw new InvalidOperationException("Unpublished resource candidate.")); } }
    public void Dispose() { if (_owner is null) return; _owner.Verify(); Entry.References--; _owner = null; }
}
// Policy/storage is managed; immutable generations may coexist while Play/history hold leases.
// No parsing, hashing, resolver call, allocation or cache mutation is required by frame submission.
public sealed class RenderResourceCache : IDisposable
{
    internal sealed class Entry(string fingerprint, ulong bytes, bool geometry)
    { internal IDisposable? Resource; internal readonly string Fingerprint = fingerprint; internal readonly ulong Bytes = bytes; internal readonly bool Geometry = geometry; internal IDisposable[] Dependencies = []; internal int References; }
    private readonly RendererSession _renderer;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly Dictionary<(int Kind, RenderAssetVersion Version, string Variant), Entry> _entries = [];
    private bool _disposed;
    private readonly ulong _geometryBudget, _textureBudget;
    public ulong GeometryBytes { get; private set; }
    public ulong TextureBytes { get; private set; }
    public ulong Reuses { get; private set; }
    public int Count => _entries.Count;
    public RenderResourceCache(RendererSession renderer, ulong geometryBudget = 256 * 1024 * 1024, ulong textureBudget = 256 * 1024 * 1024)
    { _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer)); _ = renderer.Handle; if (geometryBudget > 256 * 1024 * 1024 || textureBudget > 256 * 1024 * 1024) throw new ArgumentException("Native resident budget exceeded."); _geometryBudget = geometryBudget; _textureBudget = textureBudget; }
    internal void Verify() { ObjectDisposedException.ThrowIf(_disposed, this); if (_thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Cache requires owner thread."); _ = _renderer.Handle; }
    private static string MeshHash(MeshUploadData data)
    { using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); hash.AppendData(data.Vertices); hash.AppendData(MemoryMarshal.AsBytes(data.Indices)); return Convert.ToHexString(hash.GetHashAndReset()); }
    public RenderAssetLease<GpuMesh> AcquireMesh(RenderAssetVersion version, MeshUploadData data)
    {
        ArgumentNullException.ThrowIfNull(data); Verify(); version.Validate(); string hash = MeshHash(data);
        return Acquire(1, version, "", hash, (ulong)data.UploadBytes, true, () => (_renderer.CreateStaticMesh(data), Array.Empty<IDisposable>()));
    }
    public RenderAssetLease<GpuTexture> AcquireTexture(RenderAssetVersion version, TextureData data)
    {
        ArgumentNullException.ThrowIfNull(data); Verify(); version.Validate();
        if (version.ContentHash != data.ContentHash) throw new ArgumentException("Texture generation/hash mismatch.");
        return Acquire(2, version, "", data.ContentHash, (ulong)data.Pixels.Length, false, () => (_renderer.CreateTexture(data), Array.Empty<IDisposable>()));
    }
    public RenderAssetLease<GpuMesh> AcquireBindPoseMesh(RenderAssetVersion version, BindPoseMeshUploadData data)
    {
        ArgumentNullException.ThrowIfNull(data); Verify(); version.Validate();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); hash.AppendData(data.Vertices); hash.AppendData(MemoryMarshal.AsBytes(data.Indices)); hash.AppendData(data.Palette); hash.AppendData(data.SourceMesh);
        return Acquire(4, version, "bind-pose-v2", Convert.ToHexString(hash.GetHashAndReset()), (ulong)data.UploadBytes, true, () => (_renderer.CreateBindPoseMesh(data), Array.Empty<IDisposable>()));
    }
    public RenderAssetLease<GpuMaterial> AcquireMaterial(RenderAssetVersion version, MaterialDefinition definition,
        Func<Guid, ResolvedTexture?> resolve, bool safeUV, bool safeTangents, out IReadOnlyList<MaterialFallback> diagnostics)
    {
        Verify(); version.Validate(); MaterialCodec.Validate(definition); ArgumentNullException.ThrowIfNull(resolve);
        byte[] encoded = MaterialCodec.Encode(definition);
        if (version.AssetId != definition.AssetId || version.ContentHash != Convert.ToHexString(SHA256.HashData(encoded))) throw new ArgumentException("Material identity/hash mismatch.");
        var fallbacks = new List<MaterialFallback>(); var resolved = new ResolvedTexture?[6]; var ids = definition.TextureIds;
        for (int i = 0; i < 6; i++) if (ids[i] != Guid.Empty) {
            if (!safeUV || (i == 1 && !safeTangents)) { fallbacks.Add(new(i == 1 ? "unsafe_normal_basis" : "missing_uv", i)); continue; }
            resolved[i] = resolve(ids[i]);
            if (resolved[i] is not { } t) { fallbacks.Add(new("missing_texture", i)); continue; }
            t.Version.Validate();
            if (t.Version.AssetId != ids[i] || t.Version.ContentHash != t.Data.ContentHash || t.Data.Semantic != ((i == 0 || i == 5) ? TextureSemantic.Color : i == 1 ? TextureSemantic.Normal : TextureSemantic.Data)) throw new ArgumentException("Texture reference identity/role mismatch.");
        }
        string variant = string.Join('|', resolved.Select(t => t is null ? "fallback" : $"{t.Version.AssetId:N}:{t.Version.Generation}:{t.Version.ContentHash}"));
        diagnostics = fallbacks.AsReadOnly();
        return Acquire(3, version, variant, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(variant))), 0, false, () => {
            var dependencies = new List<IDisposable>(); var textures = new GpuTexture?[6];
            try { for (int i = 0; i < 6; i++) if (resolved[i] is { } t) { var lease = AcquireTexture(t.Version, t.Data); dependencies.Add(lease); textures[i] = lease.Resource; }
                IDisposable[] retained = dependencies.ToArray(); // Allocate before the native material is published.
                return (_renderer.CreateMaterial(definition, textures, safeUV && safeTangents), retained); }
            catch { foreach (var lease in dependencies) lease.Dispose(); throw; }
        });
    }
    public RenderMaterialSetLease AcquireMaterialSet(RenderAssetVersion version, MaterialSetDefinition definition,
        Func<Guid, ResolvedMaterial?> resolveMaterial, Func<Guid, ResolvedTexture?> resolveTexture, bool safeUV, bool safeTangents)
    {
        Verify(); version.Validate(); MaterialCodec.Validate(definition); ArgumentNullException.ThrowIfNull(resolveMaterial); ArgumentNullException.ThrowIfNull(resolveTexture);
        if (version.AssetId != definition.AssetId || version.ContentHash != Convert.ToHexString(SHA256.HashData(MaterialCodec.Encode(definition)))) throw new ArgumentException("Material-set identity/hash.");
        // Own the slot array before invoking trusted resolvers; do not retain mutable authoring DTOs.
        Guid[] ids = (Guid[])definition.Materials.Clone(); var leases = new List<RenderAssetLease<GpuMaterial>>(); var diagnostics = new List<MaterialFallback>();
        try {
            for (int i = 0; i < ids.Length; i++) {
                var resolved = resolveMaterial(ids[i]);
                if (resolved is null) { var fallback = MaterialDefinition.Default(ids[i]); resolved = new(new(ids[i], 1, Convert.ToHexString(SHA256.HashData(MaterialCodec.Encode(fallback)))), fallback); diagnostics.Add(new("missing_material", i)); }
                if (resolved.Version.AssetId != ids[i]) throw new ArgumentException("Material-set UUID reference mismatch.");
                leases.Add(AcquireMaterial(resolved.Version, resolved.Definition, resolveTexture, safeUV, safeTangents, out var losses));
                foreach (var loss in losses) diagnostics.Add(new($"{loss.Code}:texture_{loss.Slot}", i));
            }
            return new(leases.ToArray(), diagnostics.ToArray());
        } catch { foreach (var lease in leases) lease.Dispose(); throw; }
    }
    private RenderAssetLease<T> Acquire<T>(int kind, RenderAssetVersion version, string variant, string fingerprint, ulong bytes, bool geometry, Func<(T Resource, IDisposable[] Dependencies)> create) where T : class, IDisposable
    {
        var key = (kind, version, variant);
        foreach (var existing in _entries.Keys) if (existing.Kind == kind && existing.Version.AssetId == version.AssetId && existing.Version.Generation == version.Generation && existing.Version.ContentHash != version.ContentHash) throw new ArgumentException("Immutable generation hash changed.");
        if (_entries.TryGetValue(key, out var old)) { if (old.Resource is null) throw new InvalidOperationException("Reentrant candidate acquisition."); if (old.Fingerprint != fingerprint) throw new ArgumentException("Immutable generation content changed."); var reused = new RenderAssetLease<T>(this, old); Reuses++; old.References++; return reused; }
        if (_entries.Count >= 512 || bytes > (geometry ? _geometryBudget - GeometryBytes : _textureBudget - TextureBytes)) throw new InvalidOperationException("Managed resource cache budget.");
        // Entry, external lease and dictionary storage all allocated BEFORE native publication.
        Entry candidate = new(fingerprint, bytes, geometry) { References = 1 }; var lease = new RenderAssetLease<T>(this, candidate);
        _entries.Add(key, candidate); if (geometry) GeometryBytes += bytes; else TextureBytes += bytes;
        try { var created = create(); candidate.Resource = created.Resource; candidate.Dependencies = created.Dependencies; return lease; }
        catch { _entries.Remove(key); if (geometry) GeometryBytes -= bytes; else TextureBytes -= bytes; throw; }
    }
    public void Trim()
    {
        Verify();
        // Native material dependencies must be released BEFORE textures; a failed release stays resident.
        foreach (int kind in new[] { 3, 1, 4, 2 }) foreach (var pair in _entries.Where(p => p.Key.Kind == kind && p.Value.References == 0).ToArray()) {
            var entry = pair.Value; if (entry.Resource is null) throw new InvalidOperationException("Unpublished resource candidate."); entry.Resource.Dispose(); foreach (var dependency in entry.Dependencies) dependency.Dispose();
            _entries.Remove(pair.Key); if (entry.Geometry) GeometryBytes -= entry.Bytes; else TextureBytes -= entry.Bytes;
        }
    }
    public void Dispose() { if (_disposed) return; Verify(); Trim(); if (_entries.Count != 0) throw new InvalidOperationException("Outstanding resource leases."); _disposed = true; }
}
