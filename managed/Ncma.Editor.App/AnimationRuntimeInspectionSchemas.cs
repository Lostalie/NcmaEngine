using System.Text.Json;
using System.Text.Json.Nodes;
using Ncma.Editor.Core;
using Ncma.Animation;
namespace Ncma.Editor.App;

internal static class AnimationRuntimeInspectionSchemas
{
    private static JsonObject Text(int max=128)=>new(){["type"]="string",["maxLength"]=max};
    private static JsonObject Id()=>new(){["type"]="string",["pattern"]="^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"};
    private static JsonObject Number(double min=0,double max=9007199254740991,bool integer=true)=>new(){["type"]=integer?"integer":"number",["minimum"]=min,["maximum"]=max};
    private static JsonObject Bool()=>new(){["type"]="boolean"};
    private static JsonObject Object(JsonObject properties,params string[] required)=>new(){["type"]="object",["additionalProperties"]=false,["properties"]=properties,["required"]=new JsonArray((required.Length==0?properties.Select(p=>p.Key):required).Select(k=>(JsonNode?)JsonValue.Create(k)).ToArray())};
    private static JsonObject Array(JsonNode shape,int maximum)=>new(){["type"]="array",["minItems"]=0,["maxItems"]=maximum,["items"]=shape};
    private static JsonObject Nullable(JsonNode shape)=>new(){["anyOf"]=new JsonArray(shape,new JsonObject{["type"]="null"})};
    private static JsonObject Vector()=>Object(new(){["x"]=Number(-1000000,1000000,false),["y"]=Number(-1000000,1000000,false),["z"]=Number(-1000000,1000000,false)});
    internal static CapabilityDescriptor Descriptor {
        get {
            var input=Object(new(){["playSessionId"]=Id(),["worldId"]=Id(),["objectId"]=Id(),["expectedTick"]=Number(),["offset"]=Number(0,AnimationGraphCodec.MaxPlanInstructions-1),["limit"]=Number(1,32)},"playSessionId","worldId","objectId");
            var binding=Object(new(){["objectId"]=Id(),["graphId"]=Id(),["skeletonId"]=Id(),["publicationId"]=Id(),["graphHash"]=Text(64)});
            var instruction=Object(new(){["operation"]=Text(16),["nodeId"]=Id(),["clipId"]=Id(),["previous"]=Number(0,double.MaxValue,false),["current"]=Number(0,double.MaxValue,false),["duration"]=Number(0,600,false),["loop"]=Bool(),["sourceA"]=Number(-1,AnimationGraphCodec.MaxPlanInstructions-1),["sourceB"]=Number(-1,AnimationGraphCodec.MaxPlanInstructions-1),["sourceC"]=Number(-1,AnimationGraphCodec.MaxPlanInstructions-1),["weight"]=Number(0,1,false),["cacheGeneration"]=Number()});
            var observation=Object(new(){["binding"]=binding,["instanceId"]=Id(),["stateId"]=Id(),["fromStateId"]=Id(),["transitionId"]=Id(),["transitionWeight"]=Number(0,1,false),["sequence"]=Number(),["output"]=Number(0,AnimationGraphCodec.MaxPlanInstructions-1),["instructionTotal"]=Number(1,AnimationGraphCodec.MaxPlanInstructions),["offset"]=Number(0,AnimationGraphCodec.MaxPlanInstructions-1),["nextOffset"]=Nullable(Number(0,AnimationGraphCodec.MaxPlanInstructions+32)),["instructions"]=Array(instruction,32),
                ["frozenPoseGeneration"]=Number(),["parameters"]=Array(Object(new(){["id"]=Id(),["kind"]=Text(16),["value"]=Number(int.MinValue,int.MaxValue,false)}),64),
                ["root"]=Nullable(Object(new(){["desired"]=Vector(),["accepted"]=Vector(),["yaw"]=Number(-Math.PI,Math.PI,false),["numericalEpoch"]=Id(),["numericalSequence"]=Number()})),
                ["pose"]=Nullable(Object(new(){["rendererFrame"]=Number(),["poseGeneration"]=Number(),["geometryDraws"]=Number(0,4096),["shadowDraws"]=Number(0,4096)}))});
            var data=Object(new(){["playSessionId"]=Id(),["worldId"]=Id(),["tick"]=Number(),["snapshotValid"]=Bool(),["faultCode"]=Text(64),["observation"]=Nullable(observation)});
            JsonObject Envelope(bool ok)=>Object(new(){["contractVersion"]=new JsonObject{["const"]=2},["requestId"]=Id(),["sessionId"]=Id(),["revision"]=Number(0,ulong.MaxValue),["executionRevision"]=Number(0,ulong.MaxValue),["status"]=new JsonObject{["enum"]=new JsonArray((ok?new[]{"ok"}:new[]{"error","denied","conflict"}).Select(v=>(JsonNode?)JsonValue.Create(v)).ToArray())},["code"]=Text(),["changed"]=new JsonObject{["const"]=false},["replayed"]=new JsonObject{["const"]=false},["data"]=ok?data:new JsonObject{["anyOf"]=new JsonArray(Object(new()),Object(new(){["message"]=Text(EditSession.MaxInputBytes)}))}});
            return new("ncma.animgraph.runtime","Read the exact human-approved graph instance, pinned resource identities, committed parameters/recipe and optional root/pose submission. No sampling, files, live controls or inference.",MutationRisk.ReadOnly,
                JsonSerializer.SerializeToElement(input),JsonSerializer.SerializeToElement(new JsonObject{["oneOf"]=new JsonArray(Envelope(true),Envelope(false))}));
        }
    }
}
