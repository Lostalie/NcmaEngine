using Ncma.Runtime;

namespace Ncma.Scene;

public sealed partial class SceneDocument
{
    private sealed record EncodedBindings(string Name, BehaviourBindingData[] Values, int Bytes, int EnvelopeBytes);
    private Dictionary<Guid, EncodedBindings> _runtimeBindingEncoding = [];
    private long _runtimeBindingEncodes;
    private string? _runtimeHeaderName;
    private int _runtimeHeaderBytes;
    internal (int Bytes, long BindingEncodes) RuntimeValidationWork { get; private set; }

    internal Action PrepareRuntimeStep(World.PreparedStepView world, Dictionary<Guid, BehaviourBindingData[]> bindings)
    {
        VerifyAccess();
        if (!ReferenceEquals(world.Owner, World) || !World.IsUpdating || _preparing)
            throw new InvalidOperationException("No matching active runtime step.");
        _preparing = true;
        try
        {
            string name = world.Name;
            int headerBytes = name == _runtimeHeaderName ? _runtimeHeaderBytes :
                SceneJson.MeasureBounded(new SceneDocumentSnapshot(1, name, []), SceneDocumentCodec.MaxBytes);
            int bytes = headerBytes;
            var identities = new HashSet<Guid>();
            var prepared = new Dictionary<Guid, BehaviourBindingData[]>();
            var encoding = new Dictionary<Guid, EncodedBindings>();
            foreach (var item in world.Objects)
            {
                var values = bindings.GetValueOrDefault(item.Id, []);
                // Recheck all cross-object identities and Export constraints, even on cache hits.
                SceneDocumentValidator.ValidateBindings(values, identities, "runtime bindings");
                _runtimeBindingEncoding.TryGetValue(item.Id, out var prior);
                int envelope = prior is not null && prior.Name == item.Name ? prior.EnvelopeBytes :
                    SceneJson.MeasureBounded(new SceneObjectData(item.Id, item.Name, [], []), SceneDocumentCodec.MaxBytes);
                EncodedBindings current;
                if (prior is not null && EqualBindings(prior.Values, values)) current = prior.Name == item.Name ? prior : prior with { Name = item.Name, EnvelopeBytes = envelope };
                else
                {
                    var owned = values.Select(b => b with { Exports = (ExportData[])b.Exports.Clone() }).ToArray();
                    current = new(item.Name, owned, SceneJson.MeasureBounded(owned, SceneDocumentCodec.MaxBytes), envelope);
                    _runtimeBindingEncodes++;
                }
                bytes = checked(bytes + envelope + item.ComponentArrayBytes - 2 + current.Bytes - 2 + (prepared.Count == 0 ? 0 : 1));
                if (bytes > SceneDocumentCodec.MaxBytes) throw new ArgumentException("Snapshot size limit exceeded.");
                prepared.Add(item.Id, current.Values); encoding.Add(item.Id, current);
            }
            world.VerifyCurrent();
            ulong baseline = _revision, revision = checked(_revision + 1), worldRevision = checked(World.Revision + 1);
            Guid identity = World.Identity;
            RuntimeValidationWork = (bytes, _runtimeBindingEncodes);
            return () =>
            {
                VerifyAccess(); World.VerifyWriteAccess();
                if (_preparing || World.IsUpdating || World.Identity != identity || World.Revision != worldRevision || _revision != baseline)
                    throw new InvalidOperationException("Prepared document metadata is stale or not committed.");
                _bindings = prepared; _runtimeBindingEncoding = encoding; _revision = revision; _knownWorldRevision = worldRevision;
                _runtimeHeaderName = name; _runtimeHeaderBytes = headerBytes;
            };
        }
        catch (Exception error) { World.RejectStep(error); throw; }
        finally { _preparing = false; }
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
                // Record equality considers +0 and -0 equal, but their JSON lengths differ.
                if (x.Name != y.Name || x.Kind != y.Kind || BitConverter.DoubleToInt64Bits(x.Value) != BitConverter.DoubleToInt64Bits(y.Value)) return false;
            }
        }
        return true;
    }
}
