namespace Ncma.Assets;

public sealed class AssetCatalog
{
    public const int MaxRecords = 4096;
    public const int MaxIdentities = 65536, MaxMetadataBytes = 64 * 1024 * 1024;
    private readonly Dictionary<Guid, AssetRecord> _roots = [];
    private readonly Dictionary<Guid, (Guid Root, AssetKind Kind, bool Tombstone)> _identities = [];
    private readonly AssetDiagnostic[] _diagnostics;

    public AssetCatalog(IEnumerable<AssetRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int metadataBytes = 0;
        foreach (var record in records)
        {
            if (_roots.Count >= MaxRecords) throw new ArgumentException("Asset catalog exceeds record budget.");
            var copy = AssetRecordCodec.Copy(record);
            int bytes = AssetRecordCodec.Encode(copy).Length;
            if (bytes > MaxMetadataBytes - metadataBytes || copy.Subassets.Length + 1 > MaxIdentities - _identities.Count)
                throw new ArgumentException("Asset catalog exceeds identity/byte budget.");
            metadataBytes += bytes;
            if (!sources.Add(copy.SourcePath) || !_roots.TryAdd(copy.AssetId, copy) ||
                !_identities.TryAdd(copy.AssetId, (copy.AssetId, copy.Kind, false)))
                throw new ArgumentException("Duplicate asset UUID or case-insensitive source path.");
            foreach (var sub in copy.Subassets)
                if (!_identities.TryAdd(sub.AssetId, (copy.AssetId, sub.Kind, sub.Tombstone)))
                    throw new ArgumentException("Duplicate root/subasset UUID across catalog.");
        }
        var diagnostics = new List<AssetDiagnostic>();
        foreach (var root in _roots.Values.OrderBy(r => r.AssetId))
            foreach (var dependency in root.Dependencies)
                if (!TryResolve(new(new(dependency.AssetId), dependency.ExpectedKind), out _, out string code))
                    diagnostics.Add(new(code, dependency.AssetId, root.SourcePath, "Unresolved typed asset dependency."));
        _diagnostics = diagnostics.ToArray();
    }

    public int Count => _roots.Count;
    public AssetRecord[] List(int offset = 0, int limit = 64)
    {
        if (offset < 0 || limit is < 1 or > 128) throw new ArgumentException("Invalid asset pagination.");
        return _roots.Values.OrderBy(r => r.AssetId).Skip(offset).Take(limit).Select(AssetRecordCodec.Copy).ToArray();
    }
    public AssetDiagnostic[] Diagnostics => (AssetDiagnostic[])_diagnostics.Clone();
    public bool TryResolve(AssetRef reference, out AssetRecord? root, out string code)
    {
        root = null;
        if (reference.Id.Value == Guid.Empty || !Enum.IsDefined(reference.ExpectedKind)) throw new ArgumentException("Invalid typed asset reference.");
        if (!_identities.TryGetValue(reference.Id.Value, out var identity)) { code = "asset_missing"; return false; }
        if (identity.Tombstone) { code = "asset_tombstone"; return false; }
        if (identity.Kind != reference.ExpectedKind) { code = "asset_kind_mismatch"; return false; }
        root = AssetRecordCodec.Copy(_roots[identity.Root]); code = "ok"; return true;
    }

    public void RequireDependencies()
    {
        if (_diagnostics.Length != 0) throw new InvalidOperationException("Required asset dependency is unresolved: " + _diagnostics[0].Code);
    }
}
