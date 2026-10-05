using Ncma.Editor.Core;

namespace Ncma.Assets.Authoring;

// Host-owned exact grants. Never deserialize these from Agent/request input.
public sealed class AssetWriteScope
{
    private readonly HashSet<string> _metadata, _sources;
    private readonly HashSet<Guid> _ids;
    private readonly Func<bool> _current;
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
    internal void Require(string path, AssetRecord record)
    {
        if (!_current() || !_metadata.Contains(path) || !_sources.Contains(record.SourcePath) || !_ids.Contains(record.AssetId))
            throw new EditCommandRejectedException("asset_scope_denied", denied: true);
    }
    internal AssetWriteScope BindCurrent(Func<bool> current) => new(_metadata, _sources, _ids, () => _current() && current());
}
