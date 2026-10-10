using System.Text.Json;
using System.Text.Json.Nodes;
using Ncma.Assets;
using Ncma.Editor.Core;

namespace Ncma.Editor.Services;

public static class AssetInspectionSchemas
{
    private static JsonObject Text(int max) => new() { ["type"] = "string", ["maxLength"] = max };
    private static JsonObject Number(long max) => new() { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = max };
    private static JsonObject Values(params string[] values) => new() { ["type"] = "string", ["enum"] = new JsonArray(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray()) };
    private static JsonObject Uuid() => new() { ["type"] = "string", ["pattern"] = "^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$" };
    private static JsonObject Object(JsonObject properties, params string[] required) => new() {
        ["type"] = "object", ["additionalProperties"] = false, ["properties"] = properties,
        ["required"] = new JsonArray(required.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray())
    };
    private static JsonObject Page() => new() { ["offset"] = Number(65536), ["limit"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 64 } };
    private static JsonObject Kind() => Values(Enum.GetValues<AssetKind>().Where(AssetRecordCodec.SupportsKind).Select(k => JsonNamingPolicy.CamelCase.ConvertName(k.ToString())).ToArray());
    private static JsonObject Rows() => new() { ["type"] = "array", ["maxItems"] = 64, ["items"] = Object(new() {
        ["assetId"] = Uuid(), ["kind"] = Kind(), ["name"] = Text(64), ["state"] = Values("registered", "derived", "tombstone")
    }, "assetId", "kind", "name", "state") };
    private static JsonObject Next() => new() { ["anyOf"] = new JsonArray(Number(65600), new JsonObject { ["type"] = "null" }) };
    private static JsonObject Data() => new() { ["assetRevision"] = Number((long)AssetInspectionService.MaxRevision), ["total"] = Number(65536), ["nextOffset"] = Next() };
    private static JsonElement Json(JsonNode node) => JsonSerializer.SerializeToElement(node);
    private static JsonObject Envelope(JsonNode data) => new() { ["type"] = "object", ["oneOf"] = new JsonArray(
        Frame(data, true), Frame(new JsonObject { ["anyOf"] = new JsonArray(Object(new()), Object(new() { ["message"] = Text(EditSession.MaxInputBytes) }, "message")) }, false)) };
    private static JsonObject Frame(JsonNode data, bool success) => Object(new() {
        ["contractVersion"] = new JsonObject { ["const"] = 2 }, ["requestId"] = Uuid(), ["sessionId"] = Uuid(),
        ["revision"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0 }, ["executionRevision"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0 },
        ["status"] = success ? Values("ok") : Values("error", "denied", "conflict"), ["code"] = success ? new JsonObject { ["const"] = "ok" } : Text(128), ["changed"] = new JsonObject { ["const"] = false },
        ["replayed"] = new JsonObject { ["const"] = false }, ["data"] = data
    }, "contractVersion", "requestId", "sessionId", "revision", "executionRevision", "status", "code", "changed", "replayed", "data");
    public static CapabilityDescriptor[] Descriptors()
    {
        var listInput = Page(); listInput["kind"] = Kind(); var listData = Data(); listData["items"] = Rows();
        var inspectInput = Page(); inspectInput["assetId"] = Uuid(); inspectInput["section"] = Values("summary", "subassets", "dependencies");
        var inspectData = Data(); inspectData["assetId"] = Uuid(); inspectData["generation"] = Number((long)AssetInspectionService.MaxRevision);
        inspectData["section"] = Values("summary", "subassets", "dependencies"); inspectData["items"] = Rows();
        var validateInput = Page(); validateInput["assetId"] = Uuid(); var validateData = Data(); validateData["valid"] = new JsonObject { ["type"] = "boolean" };
        validateData["diagnostics"] = new JsonObject { ["type"] = "array", ["maxItems"] = 64, ["items"] = Object(new() {
            ["code"] = Values("source_missing", "asset_diagnostic", "dependency_not_visible", "asset_missing", "asset_tombstone", "asset_kind_mismatch"),
            ["assetId"] = Uuid(), ["field"] = Values("asset", "dependencies"), ["severity"] = Values("error", "warning")
        }, "code", "assetId", "field", "severity") };
        return [
            new("ncma.assets.list", "List bounded sanitized metadata for asset UUIDs explicitly approved by the local host; no filesystem access.", MutationRisk.ReadOnly,
                Json(Object(listInput)), Json(Envelope(Object(listData, "assetRevision", "total", "items", "nextOffset")))),
            new("ncma.assets.inspect", "Inspect approved asset summary, visible subassets or visible dependencies from the committed metadata cache.", MutationRisk.ReadOnly,
                Json(Object(inspectInput, "assetId", "section")), Json(Envelope(Object(inspectData, "assetRevision", "assetId", "generation", "section", "total", "items", "nextOffset")))),
            new("ncma.assets.validate", "Inspect bounded committed metadata diagnostics and typed dependencies; does not validate payload bytes or reimport.", MutationRisk.ReadOnly,
                Json(Object(validateInput, "assetId")), Json(Envelope(Object(validateData, "assetRevision", "valid", "total", "diagnostics", "nextOffset"))))
        ];
    }
}
