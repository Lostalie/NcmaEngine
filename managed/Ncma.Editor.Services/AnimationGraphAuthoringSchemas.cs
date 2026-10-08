using System.Text.Json;
using System.Text.Json.Nodes;
using Ncma.Editor.Core;

namespace Ncma.Editor.Services;

public static class AnimationGraphAuthoringSchemas
{
    public const string Propose = "ncma.animgraph.propose";
    public static CapabilityDescriptor Descriptor {
        get {
            var original = AnimationGraphInspectionSchemas.Descriptors()[0];
            var inspection = JsonNode.Parse(original.OutputSchema.GetRawText())!;
            var shapes = inspection["oneOf"]![0]!["properties"]!["data"]!["properties"]!["items"]!["items"]!["oneOf"]!;
            var cases = new JsonArray();
            string[] fields = ["node", "parameter", "link", "state", "transition"];
            for (int i = 0; i < fields.Length; i++) {
                string field = fields[i]; cases.Add(Closed(new() { ["op"] = Constant(field + ".upsert"), [field] = shapes[i]!.DeepClone() }));
                cases.Add(Closed(new() { ["op"] = Constant(field + ".delete"), ["id"] = Uuid() }));
            }
            cases.Add(Closed(new() { ["op"] = Constant("graph.rename"), ["name"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 256 } }));
            cases.Add(Closed(new() { ["op"] = Constant("graph.entry"), ["stateId"] = Uuid() }));
            var input = Closed(new() { ["graphId"] = Uuid(), ["proposalId"] = Uuid(), ["operations"] = new JsonObject { ["type"] = "array", ["minItems"] = 1, ["maxItems"] = 64, ["items"] = new JsonObject { ["oneOf"] = cases } } });
            var frame = inspection["oneOf"]![0]!;
            frame["properties"]!["data"] = Closed(new() { ["proposalId"] = Uuid(), ["graphId"] = Uuid(), ["beforeHash"] = new JsonObject { ["oneOf"] = new JsonArray(Hash(), new JsonObject { ["type"] = "null" }) }, ["afterHash"] = Hash(),
                ["added"] = Count(), ["removed"] = Count(), ["updated"] = Count(), ["resourcesPrepared"] = new JsonObject { ["const"] = false } });
            return new(Propose, "Propose a bounded graph semantic edit from the exact approved authoring snapshot. No files, history, resource preparation or live Play changes.", MutationRisk.ReadOnly,
                JsonSerializer.SerializeToElement(input), JsonSerializer.SerializeToElement(inspection));
        }
    }
    private static JsonObject Constant(string text) => new() { ["const"] = text };
    private static JsonObject Uuid() => new() { ["type"] = "string", ["pattern"] = "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$" };
    private static JsonObject Hash() => new() { ["type"] = "string", ["pattern"] = "^[0-9A-F]{64}$" };
    private static JsonObject Count() => new() { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 4096 };
    private static JsonObject Closed(JsonObject properties) => new() { ["type"] = "object", ["additionalProperties"] = false, ["properties"] = properties, ["required"] = new JsonArray(properties.Select(p => (JsonNode?)JsonValue.Create(p.Key)).ToArray()) };
}
