using System.Text.Json;
using Ncma.Runtime;

namespace Ncma.Scene;

// This is a runtime-instance observation, not a Play lifecycle ticket or a write capability.
public readonly record struct RuntimeInspectionStamp(int Version, Guid SceneSessionId, Guid WorldId, ulong Revision, ulong Tick);
public sealed record RuntimeObjectInfo(Guid Id, string Name);
public sealed record RuntimeBindingInfo(Guid Id, string TypeName, bool Enabled, int ExportCount);
public sealed record RuntimeInspectionPage<T>(RuntimeInspectionStamp Stamp, Guid? ObjectId, Guid? BindingId,
    int Offset, int TotalCount, T[] Items);

public sealed partial class RuntimeSceneInstance
{
    public const int InspectionVersion = 1, MaxInspectionItems = 128, MaxInspectionBytes = 256 * 1024;
    private bool _inspecting;
    private EntityQuery? _inspectionQuery;
    private Guid _inspectionQueryWorld;

    public RuntimeInspectionStamp GetInspectionStamp()
    {
        VerifyReadBoundary(); ObserveWorld();
        return new(InspectionVersion, SessionId, World.Identity, _revision, World.Tick);
    }

    public RuntimeInspectionPage<RuntimeObjectInfo> InspectObjects(RuntimeInspectionStamp expected, int offset = 0, int limit = 128)
        => Inspect<RuntimeObjectInfo>(expected, offset, limit, () => {
            if (_inspectionQuery is null || _inspectionQueryWorld != World.Identity)
            { _inspectionQuery = World.CreateQuery([], []); _inspectionQueryWorld = World.Identity; }
            int total = _inspectionQuery.Count;
            var items = new RuntimeObjectInfo[Math.Min(limit, Math.Max(0, total - offset))];
            if (items.Length != 0)
            {
                int skip = offset, index = 0;
                foreach (var entity in _inspectionQuery)
                {
                    if (skip > 0) { skip--; continue; }
                    items[index++] = new(World.GetUuid(entity), World.GetName(entity));
                    if (index == items.Length) break;
                }
            }
            return new(expected, null, null, offset, total, items);
        });

    public RuntimeInspectionPage<ComponentSnapshot> InspectComponents(RuntimeInspectionStamp expected, Guid objectId,
        int offset = 0, int limit = 16)
        => Inspect<ComponentSnapshot>(expected, offset, limit, () => {
            // At most one object's 64 bounded components, never a whole-scene capture. Custom
            // encoders run inside the same read-only/reentry guard as all other inspection reads.
            var item = World.CaptureReadObject(World.FindEntity(objectId));
            var components = item.Components.OrderBy(c => c.TypeId, StringComparer.Ordinal).ToArray();
            return new(expected, objectId, null, offset, components.Length, components.Skip(offset).Take(limit).ToArray());
        });

    public RuntimeInspectionPage<RuntimeBindingInfo> InspectBindings(RuntimeInspectionStamp expected, Guid objectId,
        int offset = 0, int limit = 128)
        => Inspect<RuntimeBindingInfo>(expected, offset, limit, () => {
            _ = World.FindEntity(objectId);
            var bindings = _bindings.GetValueOrDefault(objectId, []);
            return new(expected, objectId, null, offset, bindings.Length,
                bindings.Skip(offset).Take(limit).Select(b => new RuntimeBindingInfo(b.Id, b.TypeName, b.Enabled, b.Exports.Length)).ToArray());
        });

    public RuntimeInspectionPage<ExportData> InspectExports(RuntimeInspectionStamp expected, Guid objectId, Guid bindingId,
        int offset = 0, int limit = 128)
        => Inspect<ExportData>(expected, offset, limit, () => {
            _ = World.FindEntity(objectId);
            var binding = _bindings.GetValueOrDefault(objectId, []).FirstOrDefault(b => b.Id == bindingId)
                ?? throw new ArgumentException("Unknown runtime binding.", nameof(bindingId));
            return new(expected, objectId, bindingId, offset, binding.Exports.Length, binding.Exports.Skip(offset).Take(limit).ToArray());
        });

    private RuntimeInspectionPage<T> Inspect<T>(RuntimeInspectionStamp expected, int offset, int limit,
        Func<RuntimeInspectionPage<T>> capture)
    {
        var actual = GetInspectionStamp();
        if (expected != actual) throw new InvalidOperationException("Stale or foreign runtime observation.");
        if (offset < 0 || limit is < 1 or > MaxInspectionItems) throw new ArgumentException("Invalid runtime inspection page.");
        using var read = World.ReadOnly();
        _inspecting = true;
        try
        {
            var page = capture();
            // Bound the entire response, including stamp/identity/pagination, before publication.
            // Decode into detached arrays/JSON and normalize strings like the runtime codec.
            // Over-budget responses reject rather than silently dropping fields or items.
            byte[] bytes = SceneJson.EncodeBounded(page, MaxInspectionBytes);
            return JsonSerializer.Deserialize<RuntimeInspectionPage<T>>(bytes, SceneJson.Options)
                ?? throw new InvalidOperationException("Missing runtime inspection response.");
        }
        finally { _inspecting = false; }
    }
}
