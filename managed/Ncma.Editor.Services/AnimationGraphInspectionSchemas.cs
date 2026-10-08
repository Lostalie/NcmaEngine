using System.Text.Json;
using System.Text.Json.Nodes;
using Ncma.Editor.Core;

namespace Ncma.Editor.Services;

public static class AnimationGraphInspectionSchemas
{
    private static JsonElement Json(JsonNode value) => JsonSerializer.SerializeToElement(value);
    private static JsonObject Text(int maximum = 128) => new() { ["type"] = "string", ["maxLength"] = maximum };
    private static JsonObject Uuid() => new() { ["type"] = "string", ["pattern"] = "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$" };
    private static JsonObject Number(double min = -1000000, double max = 1000000, bool integer = false) => new() { ["type"] = integer ? "integer" : "number", ["minimum"] = min, ["maximum"] = max };
    private static JsonObject Bool() => new() { ["type"] = "boolean" };
    private static JsonObject Values(params string[] values) => new() { ["type"] = "string", ["enum"] = new JsonArray(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray()) };
    private static JsonObject Object(JsonObject properties, params string[] required) => new() {
        ["type"] = "object", ["additionalProperties"] = false, ["properties"] = properties,
        ["required"] = new JsonArray((required.Length == 0 ? properties.Select(p => p.Key) : required).Select(v => (JsonNode?)JsonValue.Create(v)).ToArray())
    };
    private static JsonObject Array(JsonNode items, int maximum = 32) => new() { ["type"] = "array", ["maxItems"] = maximum, ["items"] = items };
    private static JsonObject Nullable(JsonNode value) => new() { ["anyOf"] = new JsonArray(value, new JsonObject { ["type"] = "null" }) };
    private static JsonObject Envelope(JsonObject data) => new() { ["oneOf"] = new JsonArray(Frame(data, true), Frame(new() {
        ["oneOf"] = new JsonArray(Object(new()), Object(new() { ["message"] = Text(EditSession.MaxInputBytes) })) }, false)) };
    private static JsonObject Frame(JsonObject data, bool ok) => Object(new() {
        ["contractVersion"] = new JsonObject { ["const"] = 2 }, ["requestId"] = Uuid(), ["sessionId"] = Uuid(), ["revision"] = Number(0, ulong.MaxValue, true),
        ["executionRevision"] = Number(0, ulong.MaxValue, true), ["status"] = ok ? Values("ok") : Values("error", "denied", "conflict"),
        ["code"] = Text(), ["changed"] = new JsonObject { ["const"] = false }, ["replayed"] = new JsonObject { ["const"] = false }, ["data"] = data
    });
    public static CapabilityDescriptor[] Descriptors()
    {
        var axis=Object(new(){["parameterId"]=Uuid(),["name"]=Text(),["unit"]=Text(),["minimum"]=Number(),["maximum"]=Number()});
        var sample=Object(new(){["id"]=Uuid(),["clipId"]=Uuid(),["x"]=Number(),["y"]=Number()});
        var space=Object(new(){["id"]=Uuid(),["dimensions"]=Number(1,2,true),["axisX"]=axis,["axisY"]=Nullable(axis.DeepClone()),["cycleSeconds"]=Number(.001,600),["syncGroup"]=Uuid(),["samples"]=Array(sample,32)});
        var node = Object(new() { ["id"] = Uuid(), ["name"] = Text(), ["kind"] = Values("clip", "blend", "parameter", "stateMachine", "output","blendSpace"),
            ["x"] = Number(-65536, 65536), ["y"] = Number(-65536, 65536), ["clipId"] = Uuid(), ["parameterId"] = Uuid(), ["loop"] = Bool(), ["speed"] = Number(0, 8), ["weight"] = Number(0, 1),["blendSpace"]=Nullable(space) });
        var parameter = Object(new() { ["id"] = Uuid(), ["name"] = Text(), ["kind"] = Values("float", "int", "bool", "trigger"),
            ["floatDefault"] = Number(), ["intDefault"] = Number(int.MinValue, int.MaxValue, true), ["boolDefault"] = Bool() });
        var link = Object(new() { ["id"] = Uuid(), ["from"] = Uuid(), ["fromPin"] = Text(16), ["to"] = Uuid(), ["toPin"] = Text(16) });
        var state = Object(new() { ["id"] = Uuid(), ["name"] = Text(), ["poseNode"] = Uuid() });
        var condition = Object(new() { ["parameterId"] = Uuid(), ["comparison"] = Values("equal", "notEqual", "greater", "greaterOrEqual", "less", "lessOrEqual", "triggered"),
            ["floatValue"] = Number(), ["intValue"] = Number(int.MinValue, int.MaxValue, true), ["boolValue"] = Bool() });
        var transition = Object(new() { ["id"] = Uuid(), ["from"] = Uuid(), ["to"] = Uuid(), ["priority"] = Number(0, 255, true), ["duration"] = Number(0, 10),
            ["exitTime"] = Nullable(Number(0, 1)), ["conditions"] = Array(condition, 8) });
        var summary = Object(new() { ["assetId"] = Uuid(), ["name"] = Text(256), ["skeletonId"] = Uuid(), ["entryState"] = Uuid(), ["version"] = new JsonObject { ["const"] = 3 },["interruptTransitions"]=Bool() });
        var dependency = Object(new() { ["id"] = Uuid(), ["isSkeleton"] = Bool() });
        var marker=Object(new(){["id"]=Uuid(),["clipId"]=Uuid(),["time"]=Number(double.Epsilon,600),["name"]=Text()});
        var input = Object(new() { ["graphId"] = Uuid(), ["section"] = Values("summary", "nodes", "parameters", "links", "states", "transitions", "dependencies","events"),
            ["offset"] = Number(0, 4096, true), ["limit"] = Number(1, 32, true) }, "graphId", "section");
        var data = Object(new() { ["graphId"] = Uuid(), ["hash"] = Text(64), ["section"] = Text(32), ["total"] = Number(0, 4096, true),
            ["nextOffset"] = Nullable(Number(0, 4128, true)), ["items"] = Array(new JsonObject { ["oneOf"] = new JsonArray(node, parameter, link, state, transition, summary, dependency,marker) }) });
        var validation = Object(new() { ["graphId"] = Uuid(), ["hash"] = Text(64), ["valid"] = Bool(), ["validation"] = Values("structural_only"),
            ["resourcesPrepared"] = new JsonObject { ["const"] = false }, ["diagnostics"] = Array(Object(new() { ["code"] = Text(), ["subject"] = Uuid(), ["field"] = Text(), ["expected"] = Text(), ["actual"] = Text() }), 1) });
        return [new("ncma.animgraph.inspect", "Inspect the exact host-reviewed immutable authoring graph and its reviewed UUID dependencies. No files, simulation or pose execution.",
            MutationRisk.ReadOnly, Json(input), Json(Envelope(data))),
            new("ncma.animgraph.validate", "Read cached strict graph v3 structural validation. Does NOT prepare resources, compile, sample or advance Play.",
                MutationRisk.ReadOnly, Json(Object(new() { ["graphId"] = Uuid() })), Json(Envelope(validation)))];
    }
}
