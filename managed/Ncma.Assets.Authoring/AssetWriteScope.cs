using Ncma.Editor.Core;

namespace Ncma.Assets.Authoring;

// Host-owned exact grants. Never deserialize these from Agent/request input.
public sealed class AssetWriteScope
{
    private readonly HashSet<string> _metadata, _sources;
    private readonly HashSet<Guid> _ids;
    private readonly Func<bool> _current;
    private readonly Func<string,AssetRecord,bool>? _hostApproval;
    public AssetWriteScope(IEnumerable<string> metadataPaths, IEnumerable<string> sourcePaths, IEnumerable<Guid> assetIds, Func<bool> isCurrent)
    {
        ArgumentNullException.ThrowIfNull(metadataPaths); ArgumentNullException.ThrowIfNull(sourcePaths);
        ArgumentNullException.ThrowIfNull(assetIds); ArgumentNullException.ThrowIfNull(isCurrent);
        _metadata = new(metadataPaths.Select(p => AssetPaths.Validate(p)), StringComparer.Ordinal);
        _sources = new(sourcePaths.Select(p => AssetPaths.Validate(p)), StringComparer.Ordinal);
        _ids = new(assetIds);
        if (_metadata.Count > 128 || _sources.Count > 128 || _ids.Count > 128 || _ids.Contains(Guid.Empty) ||
            _metadata.Any(p => !p.EndsWith(".ncmeta", StringComparison.Ordinal))) throw new ArgumentException("Invalid asset grant.");
        _current = isCurrent;
    }
    private AssetWriteScope(Func<string,AssetRecord,bool> approved,Func<bool> current):this([],[],[],current)=>_hostApproval=approved;
    // Trusted bootstrap only. The predicate is host-owned and never supplied by serialized input.
    public static AssetWriteScope ForHost(Func<string,AssetRecord,bool> approved,Func<bool> current)
    {ArgumentNullException.ThrowIfNull(approved);ArgumentNullException.ThrowIfNull(current);return new(approved,current);}
    internal void Require(string path, AssetRecord record)
    {
        AssetPaths.Validate(path);AssetRecordCodec.Validate(record);
        if (!_current() || (_hostApproval is not null ? !_hostApproval(path,record) : !_metadata.Contains(path) || !_sources.Contains(record.SourcePath) || !_ids.Contains(record.AssetId)))
            throw new EditCommandRejectedException("asset_scope_denied", denied: true);
    }
    internal AssetWriteScope BindCurrent(Func<bool> current) => _hostApproval is null?new(_metadata, _sources, _ids, () => _current() && current()):new(_hostApproval,()=>_current()&&current());
}
