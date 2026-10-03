using System.Text;
using System.Text.Json;
namespace Ncma.Editor.Core;

public sealed partial class EditSession
{
    public const int MaxReadOutputBytes = 256 * 1024;
    private CapabilityResult ObjectDetails(CapabilityRequest request)
    {
        Closed(request.Input, ["objectId", "section", "offset", "limit", "bindingId"], ["objectId", "section"]);
        Guid id = Uuid(request.Input, "objectId"); string section = Text(request.Input, "section");
        int offset = request.Input.TryGetProperty("offset", out var o) ? o.GetInt32() : 0;
        int limit = request.Input.TryGetProperty("limit", out var l) ? l.GetInt32() : 16;
        if (offset < 0 || limit is < 1 or > 64) throw new ArgumentException("Invalid detail pagination.");
        var obj = _document.CaptureSnapshot().Objects.FirstOrDefault(o => o.Id == id);
        if (obj is null) return Result(request, "error", "object_not_found", false, new { objectId = id });
        if (section != "exports" && request.Input.TryGetProperty("bindingId", out _)) throw new ArgumentException("bindingId is only valid for exports.");
        object[] values = section switch
        {
            "summary" => [new { obj.Id, obj.Name, componentCount = obj.Components.Length, bindingCount = obj.Behaviours.Length }],
            "components" => obj.Components.OrderBy(c => c.TypeId, StringComparer.Ordinal).Cast<object>().ToArray(),
            "bindings" => obj.Behaviours.OrderBy(b => b.Id).Select(b => (object)new { b.Id, b.TypeName, b.Enabled, exportCount = b.Exports.Length }).ToArray(),
            "exports" => (obj.Behaviours.FirstOrDefault(b => b.Id == Uuid(request.Input, "bindingId")) ?? throw new ArgumentException("Unknown binding."))
                .Exports.OrderBy(e => e.Name, StringComparer.Ordinal).Cast<object>().ToArray(),
            _ => throw new ArgumentException("Unknown detail section.")
        };
        return Paged(request, values, offset, limit, (items, next) => new { objectId = id, section, totalCount = values.Length,
            offset, returnedCount = items.Length, nextOffset = next, complete = next is null, items });
    }
    private CapabilityResult BehaviourTypes(CapabilityRequest request)
    {
        Closed(request.Input, ["offset", "limit"], []);
        if (_catalog is null) return Result(request, "error", "catalog_unavailable", false, new { });
        int offset = request.Input.TryGetProperty("offset", out var o) ? o.GetInt32() : 0;
        int limit = request.Input.TryGetProperty("limit", out var l) ? l.GetInt32() : 16;
        if (offset < 0 || limit is < 1 or > 64) throw new ArgumentException("Invalid catalog pagination.");
        var values = _catalog.Types.OrderBy(t => t.TypeName, StringComparer.Ordinal).Cast<object>().ToArray();
        return Paged(request, values, offset, limit, (items, next) => new { catalogGeneration = _catalog.Generation,
            totalCount = values.Length, offset, returnedCount = items.Length, nextOffset = next, complete = next is null, items });
    }
    private CapabilityResult Paged(CapabilityRequest request, object[] values, int offset, int limit, Func<object[], int?, object> data)
    {
        int count = Math.Min(limit, Math.Max(0, values.Length - offset));
        for (;;)
        {
            var items = values.Skip(offset).Take(count).ToArray();
            int? next = offset + count < values.Length ? offset + count : null;
            var result = Result(request, "ok", "ok", false, data(items, next));
            if (Encoding.UTF8.GetByteCount(EncodeResult(result)) <= MaxReadOutputBytes - 4096) return result;
            if (count <= 1) return Result(request, "error", "item_too_large", false, new { });
            count--;
        }
    }
}
