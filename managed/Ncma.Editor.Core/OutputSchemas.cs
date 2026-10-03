using System.Text.Json;
using System.Text.Json.Nodes;
namespace Ncma.Editor.Core;

public sealed partial class EditSession
{
    private static JsonElement OutputSchema(string envelope, string capability)
    {
        const string history = """
        {"type":"object","required":["history"],"properties":{"history":{"type":"object","required":["sessionId","revision","undoCount","redoCount","undoLabel","redoLabel","dirty","historyInvalidated","editBusy","frozen","selection","filePath"],
         "properties":{"sessionId":{"type":"string","format":"uuid"},"revision":{"type":"integer","minimum":0},"undoCount":{"type":"integer","minimum":0},"redoCount":{"type":"integer","minimum":0},"undoLabel":{"type":"string"},"redoLabel":{"type":"string"},"dirty":{"type":"boolean"},"historyInvalidated":{"type":"boolean"},"editBusy":{"type":"boolean"},"frozen":{"type":"boolean"},"selection":{"type":["string","null"]},"filePath":{"type":["string","null"]}}},
         "execution":{"type":"object"}}}
        """;
        string data = capability switch
        {
            "ncma.scene.inspect" => """
            {"type":"object","required":["worldId","tick","totalCount","offset","limit","scene"],"properties":{
              "worldId":{"type":"string","format":"uuid"},"tick":{"type":"integer","minimum":0},"totalCount":{"type":"integer","minimum":0},"offset":{"type":"integer","minimum":0},"limit":{"type":"integer","minimum":1},
              "scene":{"type":"object","required":["version","name","objects"],"properties":{"version":{"const":1},"name":{"type":"string"},"objects":{"type":"array","items":{"type":"object","required":["id","name","components","behaviours"],
                "properties":{"id":{"type":"string","format":"uuid"},"name":{"type":"string"},"components":{"type":"array","items":{"type":"object","required":["typeId","version"],"properties":{"typeId":{"type":"string"},"version":{"type":"integer"}}}},
                "behaviours":{"type":"array","items":{"type":"object","required":["id","typeName","enabled","exportCount"],"properties":{"id":{"type":"string"},"typeName":{"type":"string"},"enabled":{"type":"boolean"},"exportCount":{"type":"integer"}}}}}}}}}}}
            """,
            "ncma.scene.validate" => """{"type":"object","required":["valid","objectCount"],"properties":{"valid":{"type":"boolean"},"objectCount":{"type":"integer","minimum":0}}}""",
            "ncma.engine.component_types" => """{"type":"object","required":["components"],"properties":{"components":{"type":"array","items":{"type":"object","required":["typeId","version","schema"],"properties":{"typeId":{"type":"string"},"version":{"type":"integer"},"schema":{"type":"object"}}}}}}""",
            "ncma.capabilities.list" => """{"type":"object","required":["capabilities"],"properties":{"capabilities":{"type":"array","items":{"type":"object","required":["name","description","risk","inputSchema","outputSchema"],"properties":{"name":{"type":"string"},"description":{"type":"string"},"risk":{"enum":["read_only","reversible","destructive"]},"inputSchema":{"type":"object"},"outputSchema":{"type":"object"}}}}}}""",
            "ncma.scene.object.inspect" => """
            {"type":"object","required":["objectId","section","totalCount","offset","returnedCount","nextOffset","complete","items"],"properties":{"objectId":{"type":"string","format":"uuid"},"section":{"enum":["summary","components","bindings","exports"]},
            "totalCount":{"type":"integer","minimum":0},"offset":{"type":"integer","minimum":0},"returnedCount":{"type":"integer","minimum":0},"nextOffset":{"type":["integer","null"]},"complete":{"type":"boolean"},
            "items":{"type":"array","items":{"anyOf":[
            {"type":"object","required":["id","name","componentCount","bindingCount"],"properties":{"id":{"type":"string"},"name":{"type":"string"},"componentCount":{"type":"integer"},"bindingCount":{"type":"integer"}}},
            {"type":"object","required":["typeId","version","data"],"properties":{"typeId":{"type":"string"},"version":{"type":"integer"},"data":{"type":"object"}}},
            {"type":"object","required":["id","typeName","enabled","exportCount"],"properties":{"id":{"type":"string"},"typeName":{"type":"string"},"enabled":{"type":"boolean"},"exportCount":{"type":"integer"}}},
            {"type":"object","required":["name","kind","value"],"properties":{"name":{"type":"string"},"kind":{"enum":[1,2,3,4]},"value":{"type":"number"}}}]}}}}
            """,
            "ncma.engine.behaviour_types" => """
            {"type":"object","required":["catalogGeneration","totalCount","offset","returnedCount","nextOffset","complete","items"],"properties":{"catalogGeneration":{"type":"string","format":"uuid"},"totalCount":{"type":"integer","minimum":0},"offset":{"type":"integer","minimum":0},"returnedCount":{"type":"integer","minimum":0},"nextOffset":{"type":["integer","null"]},"complete":{"type":"boolean"},
            "items":{"type":"array","items":{"type":"object","required":["typeName","exports"],"properties":{"typeName":{"type":"string"},"exports":{"type":"array","items":{"type":"object","required":["name","kind","displayName","category","defaultValue"],
            "properties":{"name":{"type":"string"},"kind":{"enum":[1,2,3,4]},"displayName":{"type":"string"},"category":{"type":"string"},"defaultValue":{"type":"number"}}}}}}}}}
            """,
            _ => history
        };
        var root = JsonNode.Parse(envelope)!;
        var failure = JsonNode.Parse("""{"type":"object","additionalProperties":false,"properties":{"message":{"type":"string"},"objectId":{"type":"string"}}}""")!;
        root["properties"]!["data"] = new JsonObject { ["anyOf"] = new JsonArray(JsonNode.Parse(data), failure) };
        return JsonSerializer.SerializeToElement(root);
    }
}
