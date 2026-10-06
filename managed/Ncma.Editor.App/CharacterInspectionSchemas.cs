using System.Text.Json;
using System.Text.Json.Nodes;
using Ncma.Editor.Core;
namespace Ncma.Editor.App;

internal static class CharacterInspectionSchemas
{
    private static JsonObject Text(int max)=>new(){["type"]="string",["maxLength"]=max};
    private static JsonObject Number(double min,double max,bool integer=false)=>new(){["type"]=integer?"integer":"number",["minimum"]=min,["maximum"]=max};
    private static JsonObject Bool()=>new(){["type"]="boolean"};
    private static JsonObject Id()=>new(){["type"]="string",["pattern"]="^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"};
    private static JsonObject Nullable(JsonNode value)=>new(){["anyOf"]=new JsonArray(value,new JsonObject{["type"]="null"})};
    private static JsonObject Object(JsonObject properties)=>new(){["type"]="object",["additionalProperties"]=false,["required"]=new JsonArray(properties.Select(p=>(JsonNode?)JsonValue.Create(p.Key)).ToArray()),["properties"]=properties};
    private static JsonObject Vector()=>Object(new(){["x"]=Number(-1000000,1000000),["y"]=Number(-1000000,1000000),["z"]=Number(-1000000,1000000)});
    private static JsonObject Array(JsonNode value,int max,int min=0)=>new(){["type"]="array",["items"]=value,["maxItems"]=max,["minItems"]=min};
    private static JsonObject Common()=>new(){["playSessionId"]=Id(),["worldId"]=Id(),["tick"]=Number(0,9007199254740991,true),["state"]=Text(32),["snapshotValid"]=Bool(),["faultCode"]=Text(64)};
    private static JsonObject Envelope(JsonNode data)
    {
        JsonObject Frame(bool success)=>Object(new(){["contractVersion"]=new JsonObject{["const"]=2},["requestId"]=Id(),["sessionId"]=Id(),
            ["revision"]=Number(0,ulong.MaxValue,true),["executionRevision"]=Number(0,ulong.MaxValue,true),["status"]=new JsonObject{["enum"]=new JsonArray((success?new[]{"ok"}:new[]{"error","denied","conflict"}).Select(v=>(JsonNode?)JsonValue.Create(v)).ToArray())},
            ["code"]=Text(128),["changed"]=new JsonObject{["const"]=false},["replayed"]=new JsonObject{["const"]=false},["data"]=success?data:new JsonObject{["anyOf"]=new JsonArray(Object(new()),Object(new(){["message"]=Text(EditSession.MaxInputBytes)}))}});
        return new(){["oneOf"]=new JsonArray(Frame(true),Frame(false))};
    }
    internal static CapabilityDescriptor[] Descriptors()
    {
        var rows=Common();rows["characters"]=Array(Object(new(){["objectId"]=Id(),["position"]=Vector(),["ground"]=Text(32),["contacts"]=Number(0,64,true),
            ["rootEnabled"]=Bool(),["time"]=Number(0,double.MaxValue),["desired"]=Vector(),["accepted"]=Vector(),["action"]=Text(16),["instance"]=Number(0,9007199254740991,true),
            ["health"]=Nullable(Number(0,1000000)),["maximumHealth"]=Nullable(Number(0,1000000))}),32);
        var events=Common();events["events"]=Array(Object(new(){["tick"]=Number(0,9007199254740991,true),["actor"]=Id(),["target"]=Nullable(Id()),["notifyId"]=Id(),
            ["instance"]=Number(0,9007199254740991,true),["kind"]=Text(32),["damage"]=Nullable(Number(0,100000))}),256);
        JsonElement Json(JsonNode value)=>JsonSerializer.SerializeToElement(value);
        JsonObject Input()=>Object(new(){["playSessionId"]=Id(),["worldId"]=Id(),["objectIds"]=Array(Id(),32,1)});
        return [new("ncma.character.inspect","Read copied committed character/action/root/ground/health diagnostics for the human-approved Play and exact object UUIDs; no simulation writes.",MutationRisk.ReadOnly,Json(Input()),Json(Envelope(Object(rows)))),
            new("ncma.combat.events","Read only the last committed quantum's bounded events for approved actors; hidden target UUIDs/damage are redacted. Not an event stream or action control.",MutationRisk.ReadOnly,Json(Input()),Json(Envelope(Object(events))))];
    }
}
