using Ncma.Runtime;

namespace Ncma.Scene;

public sealed partial class SceneDocument
{
    // Cold, explicit authoring -> runtime conversion. Does not install an ECS candidate,
    // increment Edit revision, retain a document or serialize runtime entity handles.
    public SceneBakeResult BakeRuntime(ulong expectedRevision,
        Action<SceneDocumentSnapshot>? validateRuntimeComposition = null)
    {
        VerifyMutation();
        if (_revision != expectedRevision) throw new InvalidOperationException("Bake source revision conflict.");
        var source = CaptureSnapshot();
        _preparing = true;
        try
        {
            using var read = ReadOnly();
            var objects = source.Objects.Select(item => item with
            {
                Components = NormalizeComponents(item.Components),
                Behaviours = item.Behaviours.OrderBy(b => b.Id).Select(b => b with
                {
                    Exports = b.Exports.OrderBy(e => e.Name, StringComparer.Ordinal).ToArray()
                }).ToArray()
            }).ToArray();
            var normalized = source with { Objects = objects };
            _ = SceneDocumentCodec.Encode(normalized); // Normalization may increase size.
            ValidateComposition(normalized);
            // Host checks pinned assets/catalog here; it receives an isolated copy, not output.
            validateRuntimeComposition?.Invoke(SceneDocumentCodec.Copy(normalized));
            var image = new RuntimeSceneImage(normalized);
            if (_revision != expectedRevision)
                throw new InvalidOperationException("Bake source changed during conversion.");
            return new(new(SessionId, expectedRevision), image);
        }
        finally { _preparing = false; }
    }

    private ComponentSnapshot[] NormalizeComponents(ComponentSnapshot[] values)
    {
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var result = new ComponentSnapshot[values.Length];
        for (int i = 0; i < values.Length; ++i)
        {
            var component = values[i];
            if (component is null || !identities.Add(component.TypeId))
                throw new ArgumentException("Missing or duplicate bake component.");
            var value = Components.Decode(component);
            var normalized = component with { Data = Components.EncodeObject(value) };
            Components.ValidateEncoding(normalized);
            result[i] = normalized;
        }
        return result.OrderBy(c => c.TypeId, StringComparer.Ordinal).ToArray();
    }
}
