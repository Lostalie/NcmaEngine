using System.Text.Json;
using Ncma.Runtime;

namespace Ncma.Editor.Core;

public sealed partial class EditSession
{
    private object ComponentTypes(JsonElement input)
    {
        Closed(input, ["typeId", "offset", "limit"], []);
        if (!input.TryGetProperty("typeId", out var selected)) {
            if (input.TryGetProperty("offset", out _) || input.TryGetProperty("limit", out _)) throw new ArgumentException("Field pagination requires typeId.");
            return new { components = _document.Components.Describe() };
        }
        string typeId = selected.GetString() ?? "";
        if (string.IsNullOrWhiteSpace(typeId) || typeId.Length > 128) throw new ArgumentException("Invalid component TypeId.");
        int offset = input.TryGetProperty("offset", out var o) ? o.GetInt32() : 0;
        int limit = input.TryGetProperty("limit", out var l) ? l.GetInt32() : 16;
        if (offset < 0 || limit is < 1 or > 32) throw new ArgumentException("Invalid field pagination.");
        var descriptor = _document.Components.Describe(typeId);
        var metadata = descriptor.Metadata;
        int total = metadata?.Fields.Count ?? 0;
        int returned = Math.Min(limit, Math.Max(0, total - offset));
        var items = new object[returned];
        for (int i = 0; i < returned; i++) {
            var field = metadata!.Fields[offset + i];
            // Explicit projection. Do NOT serialize the descriptor: it contains default values.
            items[i] = new { field.FieldId, field.JsonPath, field.ValueKind,
                persistent = (field.Flags & ComponentFieldFlags.Persistent) != 0,
                editable = (field.Flags & ComponentFieldFlags.Editable) != 0,
                runtimeRead = (field.Flags & ComponentFieldFlags.RuntimeRead) != 0,
                runtimeWrite = (field.Flags & ComponentFieldFlags.RuntimeWrite) != 0 };
        }
        return new { components = new[] { descriptor }, fieldPage = new {
            metadataVersion = ComponentMetadata.ContractVersion, available = metadata is not null,
            totalCount = total, offset, returnedCount = returned,
            nextOffset = returned == 0 || offset + returned >= total ? (int?)null : offset + returned,
            complete = returned == 0 || offset + returned >= total,
            defaultsIncluded = false, usageIsAuthority = false, items
        } };
    }
}
