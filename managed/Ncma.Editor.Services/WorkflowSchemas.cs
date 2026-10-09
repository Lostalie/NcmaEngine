using System.Text.Json;
using System.Text.Json.Nodes;
using Ncma.Editor.Core;
namespace Ncma.Editor.Services;

public static class WorkflowSchemas
{
    private static JsonObject Closed(JsonObject p) => new() { ["type"] = "object", ["additionalProperties"] = false,
        ["properties"] = p, ["required"] = new JsonArray(p.Select(x => (JsonNode?)JsonValue.Create(x.Key)).ToArray()) };
    private static JsonObject Text(int max = 128) => new() { ["type"] = "string", ["maxLength"] = max };
    private static JsonObject Id() => new() { ["type"] = "string", ["pattern"] = "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$" };
    private static JsonObject Int(int min, double max) => new() { ["type"] = "integer", ["minimum"] = min, ["maximum"] = max };
    private static JsonObject Values(params string[] values) => new() { ["type"] = "string", ["enum"] = new JsonArray(values.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()) };
    private static JsonObject Const(object value) => new() { ["const"] = JsonSerializer.SerializeToNode(value) };
    private static JsonObject Array(JsonNode row, int min, int max) => new() { ["type"] = "array", ["minItems"] = min, ["maxItems"] = max, ["items"] = row };
    public static CapabilityDescriptor Descriptor()
    {
        var step = Closed(new() { ["id"] = Id(), ["capability"] = Text(), ["input"] = new JsonObject { ["type"] = "object" }, ["expectation"] = Values("success", "valid", "passed") });
        var plan = Closed(new() { ["version"] = Const(1), ["workflowId"] = Id(), ["deadlineSeconds"] = Int(1, 120),
            ["repairBudget"] = Int(0, 2), ["steps"] = Array(step, 1, EditorWorkflows.MaxSteps) });
        var propose = Closed(new() { ["op"] = Const("propose"), ["plan"] = plan });
        var operation = Closed(new() { ["op"] = Values("inspect", "next", "cancel"), ["workflowId"] = Id() });
        var repair = Closed(new() { ["op"] = Const("repair"), ["workflowId"] = Id(), ["input"] = new JsonObject { ["type"] = "object" } });
        var receipt = Closed(new() { ["stepId"] = Id(), ["requestId"] = Id(), ["capability"] = Text(), ["status"] = Text(), ["code"] = Text(),
            ["changed"] = new JsonObject { ["type"] = "boolean" }, ["executionRevision"] = Int(0, ulong.MaxValue),
            ["resultHash"] = Text(64), ["assertionPassed"] = new JsonObject { ["type"] = "boolean" } });
        var data = Closed(new() { ["workflowId"] = Id(), ["status"] = Values("proposed", "approved", "completed", "failed", "cancelled", "revoked", "expired", "conflict"),
            ["index"] = Int(0, EditorWorkflows.MaxSteps), ["stepCount"] = Int(1, EditorWorkflows.MaxSteps), ["repairCount"] = Int(0, 2), ["hash"] = Text(64),
            ["ticket"] = new JsonObject { ["oneOf"] = new JsonArray(Text(EditSession.MaxInputBytes), new JsonObject { ["type"] = "null" }) },
            ["receipts"] = Array(receipt, 0, EditorWorkflows.MaxSteps + 2) });
        var envelope = JsonNode.Parse(AnimationGraphInspectionSchemas.Descriptors()[0].OutputSchema.GetRawText())!;
        envelope["oneOf"]![0]!["properties"]!["data"] = data;
        return new(EditorWorkflows.Capability, "Bounded approved workflow ledger. Emits exact tickets for original independently approved tools; records receipts. No nested execution, self approval, live Play or automatic Undo.",
            MutationRisk.ReadOnly, JsonSerializer.SerializeToElement(new JsonObject { ["oneOf"] = new JsonArray(propose, operation, repair) }), JsonSerializer.SerializeToElement(envelope));
    }
}
