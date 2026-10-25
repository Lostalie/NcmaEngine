using Ncma.Gui;
using Ncma.Runtime;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ncma.Editor.App;

internal sealed unsafe partial class EditorPresenter
{
    // UX ordering only; paths/defaults/values come from the sole registered metadata.
    // The original transaction/gesture path is retained; edit targets use FieldId, not array indices.
    private static readonly string[] TransformFields = ["position.x", "position.y", "position.z",
        "rotation.x", "rotation.y", "rotation.z", "rotation.w", "scale.x", "scale.y", "scale.z"];

    private sealed record DeclaredFieldTarget(string TypeId, int Version, string FieldId);
    // All-or-nothing bounded scalar form. Larger/non-scalar declarations keep the existing
    // complete paginated JSON editor rather than silently dropping fields.
    private bool BuildDeclaredScalars(Guid objectId, ComponentSnapshot component, bool writable, ref ulong control)
    {
        var descriptor = workspace.Owner.Document.Components.Describe(component.TypeId);
        if (descriptor.Version != component.Version || descriptor.Metadata is not { } metadata) return false;
        var fields = metadata.Fields.Where(f => f.ValueKind != "object").ToArray();
        if (fields.Length is 0 or > 16 || fields.Any(f => f.ValueKind is not ("number" or "integer" or "boolean"))) return false;
        foreach (var field in fields) {
            var value = metadata.ReadSerializedField(field.FieldId, component.Data);
            bool boolean = field.ValueKind == "boolean";
            Add(boolean ? GuiItemKind.Checkbox : GuiItemKind.Text, 2, control++, component.TypeId + " / " + field.MemberPath,
                new("declared_field", objectId, Operation: new DeclaredFieldTarget(component.TypeId, component.Version, field.FieldId)),
                boolean ? "" : value.GetRawText(), number: boolean && value.GetBoolean() ? 1 : 0, max: 1,
                enabled: writable && (field.Flags & ComponentFieldFlags.Editable) != 0);
        }
        return true;
    }

    private object DeclaredScalarOperation(ActionView action, string text, double number)
    {
        if (action.Operation is not DeclaredFieldTarget target) throw new ArgumentException("Missing declared field target.");
        var component = _page!.Selected!.Components.Single(c => c.TypeId == target.TypeId && c.Version == target.Version);
        var descriptor = workspace.Owner.Document.Components.Describe(target.TypeId);
        if (descriptor.Version != target.Version || descriptor.Metadata is not { } metadata) throw new ArgumentException("Stale declared component.");
        var field = metadata.Field(target.FieldId);
        if ((field.Flags & ComponentFieldFlags.Editable) == 0) throw new ArgumentException("Field is not editable.");
        _ = metadata.ReadSerializedField(field.FieldId, component.Data);
        JsonNode? value;
        if (field.ValueKind == "boolean") {
            if (number is not (0 or 1)) throw new ArgumentException("Invalid boolean value.");
            value = JsonValue.Create(number == 1);
        } else {
            using var parsed = JsonDocument.Parse(text);
            var root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Number || !root.TryGetDouble(out double finite) || !double.IsFinite(finite) ||
                field.ValueKind == "integer" && !root.TryGetInt64(out _) && !root.TryGetUInt64(out _) || field.ValueKind is not ("number" or "integer"))
                throw new ArgumentException("Expected a finite number or exact integer.");
            value = JsonNode.Parse(text);
        }
        var node = JsonNode.Parse(component.Data.GetRawText())!; JsonNode parent = node;
        var path = field.JsonPath.Split('.');
        for (int i = 0; i < path.Length - 1; i++) parent = parent[path[i]] ?? throw new ArgumentException("Missing field parent.");
        parent[path[^1]] = value;
        return new { op = "set_component", objectId = action.Object, typeId = target.TypeId, version = target.Version, data = JsonSerializer.SerializeToElement(node) };
    }

    private bool BuildDeclaredTransform(Guid objectId, ComponentSnapshot component, bool writable, ref ulong control)
    {
        if (component.TypeId != TransformData.Generated.TypeId) return false;
        var descriptor = workspace.Owner.Document.Components.Describe(component.TypeId);
        if (descriptor.Version != component.Version || descriptor.Metadata is not { } metadata) return false;
        for (int i = 0; i < TransformFields.Length; i++) {
            var field = metadata.Field(TransformFields[i]);
            double value = metadata.ReadSerializedField(field.FieldId, component.Data).GetDouble();
            bool editable = writable && (field.Flags & ComponentFieldFlags.Editable) != 0;
            Add(GuiItemKind.Number, 2, control++, field.MemberPath.Replace('.', ' '), new("transform", objectId, field.FieldId),
                number: value, min: i is >= 3 and <= 6 ? -1 : -100, max: i is >= 3 and <= 6 ? 1 : 100, enabled: editable);
        }
        return true;
    }

    private object DeclaredTransformOperation(ActionView action, double number)
    {
        if (action.Kind != "transform" || !double.IsFinite(number)) throw new ArgumentException("Invalid declared Transform edit.");
        var component = _page!.Selected!.Components.Single(c => c.TypeId == TransformData.Generated.TypeId);
        var descriptor = workspace.Owner.Document.Components.Describe(component.TypeId);
        if (descriptor.Version != component.Version || descriptor.Metadata is not { } metadata) throw new ArgumentException("Transform metadata unavailable.");
        var field = metadata.Field(action.Field);
        if (field.ValueKind != "number" || (field.Flags & ComponentFieldFlags.Editable) == 0) throw new ArgumentException("Field is not an editable numeric control.");
        _ = metadata.ReadSerializedField(field.FieldId, component.Data); // No insertion of missing fields.
        var node = JsonNode.Parse(component.Data.GetRawText())!;
        var path = field.JsonPath.Split('.'); JsonNode parent = node;
        for (int i = 0; i < path.Length - 1; i++) parent = parent[path[i]] ?? throw new ArgumentException("Missing field parent.");
        parent[path[^1]] = number;
        return new { op = "set_component", objectId = action.Object, typeId = component.TypeId,
            version = component.Version, data = JsonSerializer.SerializeToElement(node) };
    }
}
