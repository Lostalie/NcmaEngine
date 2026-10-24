using System.Text.Json;
using Ncma.Runtime;

namespace Ncma.Scene;

public sealed partial class RuntimeSceneInstance
{
    private sealed record EncodedBindings(string Name, BehaviourBindingData[] Values, int Bytes, int EnvelopeBytes);
    private Dictionary<Guid, EncodedBindings> _runtimeEncoding = [];
    private string? _runtimeHeaderName;
    private int _runtimeHeaderBytes;
    private long _runtimeBindingEncodes;
    internal (int Bytes, long BindingEncodes) RuntimeValidationWork { get; private set; }

    // Metadata-only cold copy at a command lineage boundary, never a per-tick document clone.
    internal Dictionary<Guid, BehaviourBindingData[]> CopyRuntimeBindings()
    {
        VerifyReadBoundary(); ObserveWorld();
        var ids = new HashSet<Guid>();
        return _bindings.ToDictionary(p => p.Key, p => NormalizeBindings(p.Value, ids));
    }

    internal static bool NeedsRuntimeBindingNormalization(IEnumerable<BehaviourBindingData> bindings) =>
        bindings.Any(b => !WellFormed(b.TypeName) || b.Exports.Any(e => !WellFormed(e.Name)));
    private static bool WellFormed(string value)
    {
        for (int i = 0; i < value.Length; ++i)
            if (char.IsHighSurrogate(value[i]))
            { if (++i == value.Length || !char.IsLowSurrogate(value[i])) return false; }
            else if (char.IsLowSurrogate(value[i])) return false;
        return true;
    }
    private static BehaviourBindingData[] NormalizeBindings(BehaviourBindingData[] values, HashSet<Guid> ids)
    {
        // Validate before examining possibly-null fields; also validate AFTER UTF-16 replacement
        // because two distinct malformed Export names can become the same serialized name.
        BehaviourBindingRules.Validate(values, new HashSet<Guid>(), "runtime input bindings");
        var owned = NeedsRuntimeBindingNormalization(values)
            ? JsonSerializer.Deserialize<BehaviourBindingData[]>(SceneJson.EncodeBounded(values, World.MaxSnapshotBytes), SceneJson.Options)!
            : RuntimeSceneImage.CopyBindings(values);
        BehaviourBindingRules.Validate(owned, ids, "runtime bindings");
        return owned;
    }

    internal Action PrepareRuntimeStep(World.PreparedStepView candidate, Dictionary<Guid, BehaviourBindingData[]> bindings)
    {
        VerifyAccess();
        try
        {
            ArgumentNullException.ThrowIfNull(bindings);
            if (!ReferenceEquals(candidate.Owner, World) || !World.IsUpdating || _preparing)
                throw new InvalidOperationException("No matching runtime metadata candidate.");
            _preparing = true;
            try
            {
                string name = candidate.Name;
                int header = name == _runtimeHeaderName ? _runtimeHeaderBytes : SceneJson.MeasureBounded(
                    new RuntimeSceneSnapshot(RuntimeSceneImage.Format, RuntimeSceneImage.FormatVersion, name, []), World.MaxSnapshotBytes);
                int bytes = header, count = 0;
                var identities = new HashSet<Guid>();
                Dictionary<Guid, BehaviourBindingData[]>? changedBindings = null;
                Dictionary<Guid, Entity>? changedEntities = null;
                Dictionary<Guid, EncodedBindings>? changedEncoding = null;
                foreach (var item in candidate.Objects)
                {
                    var values = bindings.GetValueOrDefault(item.Id, []);
                    BehaviourBindingRules.Validate(values, new HashSet<Guid>(), "runtime binding candidate");
                    _runtimeEncoding.TryGetValue(item.Id, out var prior);
                    int envelope = prior is not null && prior.Name == item.Name ? prior.EnvelopeBytes :
                        SceneJson.MeasureBounded(new RuntimeObjectSnapshot(item.Id, item.Name, [], []), World.MaxSnapshotBytes);
                    EncodedBindings current;
                    if (prior is not null && EqualBindings(prior.Values, values))
                    {
                        // Cross-object binding UUID uniqueness is checked even on a cache hit.
                        BehaviourBindingRules.Validate(prior.Values, identities, "runtime bindings");
                        current = prior.Name == item.Name ? prior : prior with { Name = item.Name, EnvelopeBytes = envelope };
                    }
                    else
                    {
                        var owned = NormalizeBindings(values, identities);
                        current = new(item.Name, owned, SceneJson.MeasureBounded(owned, World.MaxSnapshotBytes), envelope);
                        _runtimeBindingEncodes++;
                    }
                    bytes = checked(bytes + envelope + item.ComponentArrayBytes - 2 + current.Bytes - 2 + (count++ == 0 ? 0 : 1));
                    if (bytes > World.MaxSnapshotBytes) throw new ArgumentException("Runtime scene byte budget exceeded.");
                    if (!ReferenceEquals(current.Values, _bindings.GetValueOrDefault(item.Id)))
                        (changedBindings ??= new(_bindings))[item.Id] = current.Values;
                    if (_bindingEntities.GetValueOrDefault(item.Id) != item.Entity)
                        (changedEntities ??= new(_bindingEntities))[item.Id] = item.Entity;
                    if (!ReferenceEquals(current, prior)) (changedEncoding ??= new(_runtimeEncoding))[item.Id] = current;
                }
                foreach (var id in _bindings.Keys)
                    if (!candidate.TryResolve(id, out _))
                    {
                        (changedBindings ??= new(_bindings)).Remove(id);
                        (changedEntities ??= new(_bindingEntities)).Remove(id);
                        (changedEncoding ??= new(_runtimeEncoding)).Remove(id);
                    }
                // Deletion may leave an old metadata key; absent targets are discarded, not loaded.
                var prepared = changedBindings ?? _bindings;
                var entities = changedEntities ?? _bindingEntities;
                var encoding = changedEncoding ?? _runtimeEncoding;
                candidate.VerifyCurrent();
                var verifyPublication = World.PreparePublicationGuard(candidate);
                ulong baseline = _revision, revision = checked(baseline + 1);
                bool published = false;
                RuntimeValidationWork = (bytes, _runtimeBindingEncodes);
                return () =>
                {
                    VerifyAccess(); verifyPublication();
                    if (_preparing || published || _revision != baseline)
                        throw new InvalidOperationException("Runtime metadata publisher is stale or already consumed.");
                    _bindings = prepared; _bindingEntities = entities; _runtimeEncoding = encoding;
                    _revision = revision; _knownWorldRevision = World.Revision;
                    _runtimeHeaderName = name; _runtimeHeaderBytes = header; published = true;
                };
            }
            finally { _preparing = false; }
        }
        catch (Exception error) { World.RejectStep(error); throw; }
    }
    private static bool EqualBindings(BehaviourBindingData[] left, BehaviourBindingData[] right)
    {
        if (left.Length != right.Length) return false;
        for (int i = 0; i < left.Length; ++i)
        {
            var a = left[i]; var b = right[i];
            if (a.Id != b.Id || a.TypeName != b.TypeName || a.Enabled != b.Enabled || a.Exports.Length != b.Exports.Length) return false;
            for (int j = 0; j < a.Exports.Length; ++j)
            {
                var x = a.Exports[j]; var y = b.Exports[j];
                if (x.Name != y.Name || x.Kind != y.Kind || BitConverter.DoubleToInt64Bits(x.Value) != BitConverter.DoubleToInt64Bits(y.Value)) return false;
            }
        }
        return true;
    }
}
