using System.Text.Json;
using System.Text.Json.Nodes;
using Ncma.Editor.Core;
using Ncma.Scene;

namespace Ncma.Editor.App;

internal static class RuntimeInspectionSchemas
{
    private static JsonObject Id() => new() { ["type"] = "string", ["pattern"] = "^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$" };
    private static JsonObject Text(int max = 256 * 1024) => new() { ["type"] = "string", ["maxLength"] = max };
    private static JsonObject Number(double min = 0, double max = 9007199254740991, bool integer = true) => new()
    { ["type"] = integer ? "integer" : "number", ["minimum"] = min, ["maximum"] = max };
    private static JsonObject Object(JsonObject properties, string[]? required = null) => new()
    { ["type"] = "object", ["additionalProperties"] = false, ["properties"] = properties,
        ["required"] = new JsonArray((required ?? properties.Select(p => p.Key).ToArray()).Select(k => (JsonNode?)JsonValue.Create(k)).ToArray()) };
    private static JsonObject Array(JsonNode item, int max) => new() { ["type"] = "array", ["minItems"] = 0, ["maxItems"] = max, ["items"] = item };
    private static JsonObject NullableId() => new() { ["anyOf"] = new JsonArray(Id(), new JsonObject { ["type"] = "null" }) };
    private static JsonObject Stamp() => Object(new() { ["version"] = new JsonObject { ["const"] = RuntimeSceneInstance.InspectionVersion },
        ["sceneSessionId"] = Id(), ["worldId"] = Id(), ["revision"] = Number(), ["tick"] = Number() });
    private static JsonObject Page(JsonNode row) => Object(new() { ["stamp"] = Stamp(), ["objectId"] = NullableId(), ["bindingId"] = NullableId(),
        ["offset"] = Number(0, 65536), ["totalCount"] = Number(0, 65536), ["items"] = Array(row, 128) });
    private static JsonObject Envelope(JsonNode data)
    {
        JsonObject Branch(bool ok) => Object(new() { ["contractVersion"] = new JsonObject { ["const"] = 2 },
            ["requestId"] = Id(), ["sessionId"] = Id(), ["revision"] = Number(0, ulong.MaxValue), ["executionRevision"] = Number(0, ulong.MaxValue),
            ["status"] = new JsonObject { ["enum"] = new JsonArray((ok ? new[] { "ok" } : new[] { "error", "denied", "conflict" }).Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()) },
            ["code"] = Text(128), ["changed"] = new JsonObject { ["const"] = false }, ["replayed"] = new JsonObject { ["const"] = false },
            ["data"] = ok ? data : new JsonObject { ["anyOf"] = new JsonArray(Object(new()), Object(new() { ["message"] = Text(EditSession.MaxInputBytes) })) } });
        return new() { ["oneOf"] = new JsonArray(Branch(true), Branch(false)) };
    }
    internal static CapabilityDescriptor[] Descriptors()
    {
        JsonNode Row(string section) => section switch
        {
            "objects" => Object(new() { ["id"] = Id(), ["name"] = Text() }),
            // Registered component JSON is schema-owned by its TypeId/version, not executable input.
            // The enclosing detached response is strictly byte bounded by both service and EditSession.
            "components" => Object(new() { ["typeId"] = Text(), ["version"] = Number(0, int.MaxValue), ["data"] = new JsonObject() }),
            "bindings" => Object(new() { ["id"] = Id(), ["typeName"] = Text(512), ["enabled"] = new JsonObject { ["type"] = "boolean" }, ["exportCount"] = Number(0, 1024) }),
            _ => Object(new() { ["name"] = Text(128), ["kind"] = Number(1, 4), ["value"] = Number(-double.MaxValue, double.MaxValue, false) })
        };
        var alternatives = new JsonArray();
        foreach (string section in new[] { "objects", "components", "bindings", "exports" })
            alternatives.Add(Object(new() { ["playSessionId"] = Id(), ["section"] = new JsonObject { ["const"] = section }, ["page"] = Page(Row(section)) }));
        JsonElement Json(JsonNode node) => JsonSerializer.SerializeToElement(node);
        return [
            new("ncma.runtime.state", "Read the current committed runtime stamp and human-approved exact object IDs. Default denied; no approval, writes, stepping or inference. Approval lasts 60 seconds for the displayed paired audience.",
                MutationRisk.ReadOnly, Json(Object(new())), Json(Envelope(Object(new() { ["playSessionId"] = Id(), ["stamp"] = Stamp(), ["objectIds"] = Array(Id(), RuntimeInspectionService.MaxObjects) })))),
            new("ncma.runtime.inspect", "Read bounded committed objects/components/bindings/Exports for the human-approved generations. Match the state stamp exactly; obtain a new state on stale reads. Object values may contain business data/references and are not secret-redacted. No writes or approval.",
                MutationRisk.ReadOnly, Json(Object(new() { ["playSessionId"] = Id(), ["stamp"] = Stamp(), ["section"] = new JsonObject { ["enum"] = new JsonArray("objects", "components", "bindings", "exports") },
                    ["objectId"] = Id(), ["bindingId"] = Id(), ["offset"] = Number(0, 65536), ["limit"] = Number(1, 128) }, ["playSessionId", "stamp", "section", "offset", "limit"])),
                Json(Envelope(new JsonObject { ["oneOf"] = alternatives })))
        ];
    }
}
