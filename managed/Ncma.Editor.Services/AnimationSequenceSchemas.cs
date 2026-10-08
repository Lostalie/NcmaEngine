using System.Text.Json;
using System.Text.Json.Nodes;
using Ncma.Editor.Core;
namespace Ncma.Editor.Services;

public static class AnimationSequenceSchemas
{
    public const string Propose="ncma.animgraph.sequence.propose",Run="ncma.animgraph.sequence.run";
    private static JsonObject Id()=>new(){["type"]="string",["pattern"]="^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"};
    private static JsonObject Text(int n=128)=>new(){["type"]="string",["maxLength"]=n};
    private static JsonObject Number(double min=0,double max=9007199254740991,bool integer=true)=>new(){["type"]=integer?"integer":"number",["minimum"]=min,["maximum"]=max};
    private static JsonObject Bool()=>new(){["type"]="boolean"};
    private static JsonObject Const(object value)=>new(){["const"]=JsonSerializer.SerializeToNode(value)};
    private static JsonObject Values(params string[] values)=>new(){["type"]="string",["enum"]=new JsonArray(values.Select(v=>(JsonNode?)JsonValue.Create(v)).ToArray())};
    private static JsonObject Closed(JsonObject p)=>new(){["type"]="object",["additionalProperties"]=false,["properties"]=p,["required"]=new JsonArray(p.Select(f=>(JsonNode?)JsonValue.Create(f.Key)).ToArray())};
    private static JsonObject Array(JsonNode row,int max)=>new(){["type"]="array",["maxItems"]=max,["items"]=row};
    private static JsonObject Nullable(JsonNode row)=>new(){["oneOf"]=new JsonArray(row,new JsonObject{["type"]="null"})};
    private static JsonObject Vector()=>Closed(new(){["x"]=Number(-1000,1000,false),["y"]=Number(-1000,1000,false),["z"]=Number(-1000,1000,false)});
    private static JsonNode Envelope(JsonObject data){var outer=JsonNode.Parse(AnimationGraphInspectionSchemas.Descriptors()[0].OutputSchema.GetRawText())!;outer["oneOf"]![0]!["properties"]!["data"]=data;return outer;}
    public static CapabilityDescriptor[] Descriptors()
    {
        var test=Closed(new(){["fixedDelta"]=Number(.001,1,false),["steps"]=Number(1,256),
            ["writes"]=Array(Closed(new(){["step"]=Number(1,256),["parameterId"]=Id(),["kind"]=Values("float","int","bool","trigger"),["value"]=Number(int.MinValue,int.MaxValue,false)}),512),
            ["assertions"]=Array(Closed(new(){["step"]=Number(1,256),["kind"]=Values("state","transition","eventCount","parameter","rootX","rootYaw"),["subjectId"]=Id(),["value"]=Number(int.MinValue,int.MaxValue,false)}),64)});
        var propose=Closed(new(){["graphId"]=Id(),["caseId"]=Id(),["test"]=test});
        var proposal=Closed(new(){["caseId"]=Id(),["graphId"]=Id(),["caseHash"]=Text(64),["graphHash"]=Text(64),["eventHash"]=Text(64),["steps"]=Number(1,256),["executionApproved"]=Const(false)});
        var context=Closed(new(){["sessionId"]=Id(),["worldId"]=Id(),["tick"]=Number(0,256)});
        var frame=Closed(new(){["instanceId"]=Id(),["graphId"]=Id(),["context"]=context,["stateId"]=Id(),["fromStateId"]=Id(),["transitionId"]=Id(),["transitionWeight"]=Number(0,1,false),["output"]=Number(0,768),["instructionCount"]=Number(1,769),["frozenPoseGeneration"]=Number(0,256)});
        var timeline=Closed(new(){["frame"]=frame,["sequence"]=Number(1,256),["eventCount"]=Number(0,256),["root"]=Nullable(Closed(new(){["translation"]=Vector(),["yaw"]=Number(-Math.PI,Math.PI,false)}))});
        var receipt=Closed(new(){["instanceId"]=Id(),["graphId"]=Id(),["context"]=context.DeepClone(),["sequence"]=Number(1,256),["stateId"]=Id(),["nodeId"]=Id(),["markerId"]=Id(),["clipId"]=Id(),["unwrappedTime"]=Number(0,2048,false),["name"]=Text()});
        var summary=Closed(new(){["passed"]=Bool(),["steps"]=Number(1,256),["events"]=Number(0,8192),["checks"]=Number(0,64)});
        var check=Closed(new(){["index"]=Number(0,63),["step"]=Number(1,256),["passed"]=Bool(),["code"]=Values("assertion_passed","assertion_failed")});
        var input=Closed(new(){["graphId"]=Id(),["caseId"]=Id(),["section"]=Values("summary","timeline","events","checks"),["offset"]=Number(0,8192),["limit"]=Number(1,8)});
        var output=Closed(new(){["caseId"]=Id(),["graphId"]=Id(),["caseHash"]=Text(64),["graphHash"]=Text(64),["eventHash"]=Text(64),["publication"]=Id(),["resourcesPrepared"]=Const(true),["rootMotionSupported"]=Bool(),["collisionExecuted"]=Const(false),["livePlay"]=Const(false),["section"]=Values("summary","timeline","events","checks"),["total"]=Number(0,8192),["offset"]=Number(0,8192),["nextOffset"]=Nullable(Number(0,8200)),["items"]=Array(new JsonObject{["oneOf"]=new JsonArray(summary,timeline,receipt,check)},8)});
        return [new(Propose,"Propose a bounded closed independent sequence against host-prepared immutable NCA graph resources. No execution, live Play, file or history mutation.",MutationRisk.ReadOnly,JsonSerializer.SerializeToElement(propose),JsonSerializer.SerializeToElement(Envelope(proposal))),
            new(Run,"Read/run the exact human-approved independent numerical sequence, paged state/events/root intentions/checks. Never live Play, physics collision, callbacks or fault recovery.",MutationRisk.ReadOnly,JsonSerializer.SerializeToElement(input),JsonSerializer.SerializeToElement(Envelope(output)))];
    }
}
