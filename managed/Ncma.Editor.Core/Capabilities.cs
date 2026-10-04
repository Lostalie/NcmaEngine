using System.Text.Json;
using Ncma.Scene;
namespace Ncma.Editor.Core;
public sealed partial class EditSession
{
    private CapabilityResult Inspect(CapabilityRequest request)
    {
        if (_inspections.ContainsKey(request.Capability)) return InspectExtension(request);
        object data;
        switch (request.Capability)
        {
            case "ncma.scene.object.inspect": return ObjectDetails(request);
            case "ncma.engine.behaviour_types": return BehaviourTypes(request);
            case "ncma.scene.inspect":
                Closed(request.Input, ["offset", "limit"], []);
                int offset = request.Input.TryGetProperty("offset", out var o) ? o.GetInt32() : 0;
                int limit = request.Input.TryGetProperty("limit", out var l) ? l.GetInt32() : 32;
                if (offset < 0 || limit is < 1 or > 128) throw new ArgumentException("Invalid pagination.");
                var snapshot = _document.CaptureSnapshot();
                data = new { worldId = _document.World.Identity, tick = _document.World.Tick,
                    totalCount = snapshot.Objects.Length, offset, limit,
                    scene = new { snapshot.Version, snapshot.Name, objects = snapshot.Objects.Skip(offset).Take(limit).Select(item => new {
                        item.Id, item.Name, components = item.Components.Select(c => new { c.TypeId, c.Version }),
                        behaviours = item.Behaviours.Select(b => new { b.Id, b.TypeName, b.Enabled, exportCount = b.Exports.Length }) }) } };
                break;
            case "ncma.scene.validate":
                Closed(request.Input, [], []);
                var current = _document.CaptureSnapshot(); _ = _document.PrepareRestore(current, Revision);
                data = new { valid = true, objectCount = current.Objects.Length }; break;
            case "ncma.capabilities.list":
                Closed(request.Input, [], []);
                data = new { capabilities = Describe().Select(c => new { c.Name, c.Description, risk = RiskName(c.Risk), c.InputSchema, c.OutputSchema }) }; break;
            case "ncma.engine.component_types":
                Closed(request.Input, [], []);
                data = new { components = _document.World.Components.Describe() }; break;
            default: throw new ArgumentException("Unknown inspection.");
        }
        if (JsonSerializer.SerializeToUtf8Bytes(data, OutputJson).Length > 256 * 1024 - 4096)
            return Result(request, "error", "item_too_large", false, new { });
        return Result(request, "ok", "ok", false, data);
    }
    private static string RiskName(MutationRisk risk) => risk switch
    { MutationRisk.ReadOnly => "read_only", MutationRisk.Reversible => "reversible", _ => "destructive" };
    private static JsonElement Json(string json) { using var document = JsonDocument.Parse(json); return document.RootElement.Clone(); }

    private static IEnumerable<CapabilityDescriptor> BuildCapabilities()
    {
        const string empty = """{"type":"object","additionalProperties":false,"properties":{}}""";
        const string result = """
            {"type":"object","additionalProperties":false,
             "required":["contractVersion","requestId","sessionId","revision","status","code","changed","data","executionRevision","replayed"],
             "properties":{"contractVersion":{"const":2},"requestId":{"type":"string","format":"uuid"},
             "sessionId":{"type":"string","format":"uuid"},"revision":{"type":"integer","minimum":0},
             "status":{"enum":["ok","error","conflict","denied"]},"code":{"type":"string"},
             "executionRevision":{"type":"integer","minimum":0},"replayed":{"type":"boolean"},"changed":{"type":"boolean"},"data":{"type":"object"}}}
            """;
        CapabilityDescriptor Descriptor(string name, string description, MutationRisk risk, string input) =>
            new(name, description, risk, name == "ncma.scene.transaction" ? TransactionSchema(input) : Json(input), OutputSchema(result, name));
        yield return Descriptor("ncma.capabilities.list", "Inspect implemented document capabilities and schemas.", MutationRisk.ReadOnly, empty);
        yield return Descriptor("ncma.engine.component_types", "Inspect trusted registered component identities, versions and schemas.", MutationRisk.ReadOnly, empty);
        yield return Descriptor("ncma.scene.inspect", "Read bounded paged object summaries from the committed complete document.", MutationRisk.ReadOnly, """{"type":"object","additionalProperties":false,"properties":{"offset":{"type":"integer","minimum":0},"limit":{"type":"integer","minimum":1,"maximum":128}}}""");
        yield return Descriptor("ncma.scene.object.inspect", "Read complete committed component or binding/export data with bounded pagination.", MutationRisk.ReadOnly,
            """{"type":"object","additionalProperties":false,"required":["objectId","section"],"properties":{"objectId":{"type":"string","format":"uuid"},"section":{"enum":["summary","components","bindings","exports"]},"offset":{"type":"integer","minimum":0},"limit":{"type":"integer","minimum":1,"maximum":64},"bindingId":{"type":"string","format":"uuid"}}}""");
        yield return Descriptor("ncma.engine.behaviour_types", "Read the trusted loaded Behaviour/Export catalog; never load or execute code.", MutationRisk.ReadOnly,
            """{"type":"object","additionalProperties":false,"properties":{"offset":{"type":"integer","minimum":0},"limit":{"type":"integer","minimum":1,"maximum":64}}}""");
        yield return Descriptor("ncma.scene.validate", "Validate the managed scene without changing it.", MutationRisk.ReadOnly, empty);
        yield return Descriptor("ncma.scene.transaction", "Atomically edit objects, registered components and complete C# Behaviour/Export configurations.", MutationRisk.Reversible, """
            {"type":"object","additionalProperties":false,"required":["operations"],"properties":{"label":{"type":"string","minLength":1,"maxLength":256},"selection":{"type":["string","null"],"format":"uuid"},
            "operations":{"type":"array","minItems":1,"maxItems":128,"items":{"oneOf":[
              {"type":"object","additionalProperties":false,"required":["op","objectId","name"],"properties":{
                "op":{"enum":["create","rename"]},"objectId":{"type":"string","format":"uuid"},"name":{"type":"string","minLength":1,"maxLength":256}}},
              {"type":"object","additionalProperties":false,"required":["op","objectId","typeId","version","data"],"properties":{
                "op":{"const":"set_component"},"objectId":{"type":"string","format":"uuid"},"typeId":{"type":"string","maxLength":128},
                "version":{"type":"integer","minimum":1},"data":{"type":"object"}}},
              {"type":"object","additionalProperties":false,"required":["op","objectId","typeId"],"properties":{
                "op":{"const":"remove_component"},"objectId":{"type":"string","format":"uuid"},"typeId":{"type":"string","maxLength":128}}},
              {"type":"object","additionalProperties":false,"required":["op","objectId","bindings"],"properties":{
                "op":{"const":"set_bindings"},"objectId":{"type":"string","format":"uuid"},
                "bindings":{"type":"array","maxItems":1024,"items":{"type":"object","additionalProperties":false,"required":["id","typeName","enabled","exports"],"properties":{
                  "id":{"type":"string","format":"uuid"},"typeName":{"type":"string","minLength":1,"maxLength":512},"enabled":{"type":"boolean"},
                  "exports":{"type":"array","maxItems":1024,"items":{"type":"object","additionalProperties":false,"required":["name","kind","value"],"properties":{
                    "name":{"type":"string","minLength":1,"maxLength":128},"kind":{"enum":[1,2,3,4]},"value":{"type":"number"}}}}}}}}}]}}}}
            """);
        yield return Descriptor("ncma.scene.delete_object", "Delete one explicitly approved object UUID; deletion is undoable.", MutationRisk.Destructive,
            """{"type":"object","additionalProperties":false,"required":["objectId"],"properties":{"objectId":{"type":"string","format":"uuid"}}}""");
        yield return Descriptor("ncma.history.undo", "Undo one command; original capability permissions still apply.", MutationRisk.Reversible, empty);
        yield return Descriptor("ncma.history.redo", "Redo one command; original capability and destructive target permissions still apply.", MutationRisk.Reversible, empty);
    }
}
