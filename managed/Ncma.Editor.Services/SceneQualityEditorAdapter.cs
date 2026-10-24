using System.Text.Json;
using Ncma.Editor.Core;
using Ncma.Rendering.Scene;
using Ncma.Scene.Rendering;

namespace Ncma.Editor.Services;

public static partial class RenderingEditorAdapter
{
    // Builds the existing scoped transaction, not a new mutation capability or approval.
    // Existing singleton owners are retained; only absent settings use the explicit fallback object.
    public static CapabilityRequest QualityRequest(EditSession edit, Guid fallbackObjectId,
        string preset, PostProcessModules modules, ulong expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var document = edit.Document;
        using var read = document.ReadOnly();
        if (edit.Revision != expectedRevision) throw new InvalidOperationException("Stale quality request revision.");
        var snapshot = edit.Document.CaptureSnapshot();
        if (fallbackObjectId == Guid.Empty || !snapshot.Objects.Any(o => o.Id == fallbackObjectId))
            throw new ArgumentException("Existing fallback settings object required.");
        // Reject ambiguity, including disabled settings, before building any operations.
        _ = SceneShadowState.Read(snapshot);
        var current = ScenePostProcessState.Read(snapshot);
        var selection = SceneQualityPresets.Create(preset, modules, current);
        Guid Owner(string type) => snapshot.Objects.SingleOrDefault(o => o.Components.Any(c => c.TypeId == type))?.Id ?? fallbackObjectId;
        object Operation(Guid owner, string type, JsonElement data) => new {
            op = "set_component", objectId = owner, typeId = type, version = 1, data
        };
        var operations = new[] {
            Operation(Owner(SceneShadowData.TypeId), SceneShadowData.TypeId, document.Components.Encode(selection.Shadows)),
            Operation(Owner(PostProcessData.TypeId), PostProcessData.TypeId, document.Components.Encode(selection.PostProcess))
        };
        return new(EditSession.ContractVersion, Guid.NewGuid(), edit.SessionId, expectedRevision,
            "ncma.scene.transaction", JsonSerializer.SerializeToElement(new { label = "Apply scene quality: Low v1", operations }));
    }
}
