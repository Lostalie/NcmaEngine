using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ncma.Animation;

// Same semantic batch for local gestures and approved Agent proposals. No IO/history/runtime.
public static class AnimationGraphEdits
{
    public const int MaxOperations = 64;
    private static readonly JsonSerializerOptions Json = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16, Converters = { new ExactEnum<AnimationNodeKind>(), new ExactEnum<AnimationParameterKind>(), new ExactEnum<AnimationComparison>() }
    };
    public static AnimationGraphDefinition CopyDraft(AnimationGraphDefinition d)
    {
        ArgumentNullException.ThrowIfNull(d);
        if (d.Version != AnimationGraphCodec.CurrentVersion || d.AssetId == Guid.Empty || d.SkeletonId == Guid.Empty || d.AssetId == d.SkeletonId ||
            d.Events is null||d.Events.Length>AnimationGraphCodec.MaxEvents||d.Events.Any(e=>e is null)||
            d.Nodes is null || d.Nodes.Length > AnimationGraphCodec.MaxNodes || d.Nodes.Any(n => n is null) ||
            d.Parameters is null || d.Parameters.Length > AnimationGraphCodec.MaxParameters || d.Parameters.Any(p => p is null) ||
            d.Links is null || d.Links.Length > AnimationGraphCodec.MaxLinks || d.Links.Any(l => l is null) ||
            d.States is null || d.States.Length > AnimationGraphCodec.MaxStates || d.States.Any(s => s is null) ||
            d.Transitions is null || d.Transitions.Length > AnimationGraphCodec.MaxTransitions || d.Transitions.Any(t => t is null || t.Conditions is null || t.Conditions.Length > AnimationGraphCodec.MaxConditions || t.Conditions.Any(c => c is null)))
            throw new ArgumentException("Bounded graph draft required.");
        AnimationGraphCodec.Text(d.Name, 256);
        var ids = new HashSet<Guid> { d.AssetId, d.SkeletonId };
        void Id(Guid id) { if (id == Guid.Empty || !ids.Add(id)) throw new ArgumentException("Distinct draft UUIDs required."); }
        AnimationMontageDefinition? montage=null;
        if(d.Montage is{} authored){montage=AnimationMontageCodec.CopyDraft(authored);if(montage.SkeletonId!=d.SkeletonId)throw new ArgumentException("Same draft Montage skeleton required.");Id(montage.AssetId);foreach(var slot in montage.Slots)Id(slot.Id);foreach(var section in montage.Sections)Id(section.Id);}
        foreach (var n in d.Nodes) { Id(n.Id); AnimationGraphCodec.Text(n.Name); if (!Enum.IsDefined(n.Kind)) throw new ArgumentException("Draft node kind.");
            AnimationGraphCodec.Scalar(n.X, -65536, 65536); AnimationGraphCodec.Scalar(n.Y, -65536, 65536); AnimationGraphCodec.Scalar(n.Speed, 0, 8); AnimationGraphCodec.Scalar(n.Weight, 0, 1);
            if(n.BlendSpace is{} space){Id(space.Id);if(space.AxisX is null||space.Dimensions is not (1 or 2)||space.Samples is null||space.Samples.Length>BlendSpaceProgram.MaximumSamples||space.Samples.Any(s=>s is null))throw new ArgumentException("Bounded space draft.");AnimationGraphCodec.Scalar(space.CycleSeconds,.001,600);foreach(var axis in new[]{space.AxisX,space.AxisY}.Where(a=>a is not null)){AnimationGraphCodec.Text(axis!.Name);AnimationGraphCodec.Text(axis.Unit);AnimationGraphCodec.Scalar(axis.Minimum,-1000000,1000000);AnimationGraphCodec.Scalar(axis.Maximum,-1000000,1000000);}foreach(var s in space.Samples){Id(s.Id);AnimationGraphCodec.Scalar(s.X,-1000000,1000000);AnimationGraphCodec.Scalar(s.Y,-1000000,1000000);}}
            if(n.Layer is{} layer){AnimationBoneMaskProgram.Validate(layer.Mask,allowEmpty:true);Id(layer.Mask.Id);AnimationGraphCodec.Scalar(layer.ReferenceTime,0,600);}
        }
        foreach (var p in d.Parameters) { Id(p.Id); AnimationGraphCodec.Text(p.Name); if (!Enum.IsDefined(p.Kind)) throw new ArgumentException("Draft parameter kind."); AnimationGraphCodec.Scalar(p.FloatDefault, -1000000, 1000000); }
        foreach (var l in d.Links) { Id(l.Id); AnimationGraphCodec.Text(l.FromPin, 16); AnimationGraphCodec.Text(l.ToPin, 16); }
        foreach (var s in d.States) { Id(s.Id); AnimationGraphCodec.Text(s.Name); }
        foreach (var t in d.Transitions) { Id(t.Id); AnimationGraphCodec.Scalar(t.Duration, 0, 10); if (t.ExitTime is { } exit) AnimationGraphCodec.Scalar(exit, 0, 1);
            if (t.Priority is < 0 or > 255) throw new ArgumentException("Draft priority."); foreach (var c in t.Conditions) { if (!Enum.IsDefined(c.Comparison)) throw new ArgumentException("Draft comparison."); AnimationGraphCodec.Scalar(c.FloatValue, -1000000, 1000000); } }
        foreach(var e in d.Events){Id(e.Id);AnimationGraphCodec.Text(e.Name);AnimationGraphCodec.Scalar(e.Time,double.Epsilon,600);if(e.ClipId==Guid.Empty)throw new ArgumentException("Event clip UUID.");}
        if (JsonSerializer.SerializeToUtf8Bytes(d, Json).Length > AnimationGraphCodec.MaxBytes) throw new ArgumentException("Draft bytes exceeded.");
        return d with { Nodes = d.Nodes.Select(n=>n with{BlendSpace=n.BlendSpace is{} s?s with{Samples=s.Samples.Select(p=>p with{}).ToArray()}:null,Layer=n.Layer is{} l?l with{Mask=l.Mask with{Bones=l.Mask.Bones.ToArray()}}:null}).ToArray(), Parameters = d.Parameters.ToArray(), Links = d.Links.ToArray(), States = d.States.ToArray(),
            Montage=montage,Events=d.Events.Select(e=>e with{}).ToArray(),Transitions = d.Transitions.Select(t => t with { Conditions = t.Conditions.ToArray() }).ToArray() };
    }
    public static AnimationGraphDefinition Apply(AnimationGraphDefinition source, JsonElement operations, bool requireComplete = true)
    {
        var d = CopyDraft(source);
        if (operations.ValueKind != JsonValueKind.Array || operations.GetArrayLength() is < 1 or > MaxOperations ||
            System.Text.Encoding.UTF8.GetByteCount(operations.GetRawText()) > 48 * 1024) throw new ArgumentException("Bounded graph operations required.");
        RejectDuplicates(operations);
        foreach (var op in operations.EnumerateArray()) {
            if (op.ValueKind != JsonValueKind.Object || !op.TryGetProperty("op", out var kind) || kind.ValueKind != JsonValueKind.String) throw new ArgumentException("Graph operation required.");
            string name = kind.GetString()!;
            switch (name) {
                case "montage.upsert":Closed(op,"op","montage");d=d with{Montage=Decode<AnimationMontageDefinition>(op,"montage")};break;
                case "montage.delete":Closed(op,"op","id");if(Montage(d).AssetId!=Uuid(op,"id"))throw new ArgumentException("Exact Montage required.");d=d with{Montage=null};break;
                case "montage.slot.upsert":Closed(op,"op","slot");var slot=Decode<AnimationMontageSlot>(op,"slot");d=d with{Montage=Montage(d) with{Slots=Upsert(Montage(d).Slots,slot,s=>s.Id)}};break;
                case "montage.slot.delete":Closed(op,"op","id");d=d with{Montage=Montage(d) with{Slots=Delete(Montage(d).Slots,Uuid(op,"id"),s=>s.Id)}};break;
                case "montage.section.upsert":Closed(op,"op","section");var section=Decode<AnimationMontageSection>(op,"section");d=d with{Montage=Montage(d) with{Sections=Upsert(Montage(d).Sections,section,s=>s.Id)}};break;
                case "montage.section.delete":Closed(op,"op","id");d=d with{Montage=Montage(d) with{Sections=Delete(Montage(d).Sections,Uuid(op,"id"),s=>s.Id)}};break;
                case "node.upsert": Closed(op, "op", "node"); var node = Decode<AnimationGraphNode>(op, "node"); d = d with { Nodes = Upsert(d.Nodes, node, n => n.Id) }; break;
                case "link.upsert": Closed(op, "op", "link"); var link = Decode<AnimationGraphLink>(op, "link"); d = d with { Links = Upsert(d.Links, link, l => l.Id) }; break;
                case "parameter.upsert": Closed(op, "op", "parameter"); var parameter = Decode<AnimationParameter>(op, "parameter"); d = d with { Parameters = Upsert(d.Parameters, parameter, p => p.Id) }; break;
                case "state.upsert": Closed(op, "op", "state"); var state = Decode<AnimationGraphState>(op, "state"); d = d with { States = Upsert(d.States, state, s => s.Id) }; break;
                case "transition.upsert": Closed(op, "op", "transition"); var transition = Decode<AnimationTransition>(op, "transition"); d = d with { Transitions = Upsert(d.Transitions, transition, t => t.Id) }; break;
                case "event.upsert":Closed(op,"op","marker");var marker=Decode<AnimationEventMarker>(op,"marker");d=d with{Events=Upsert(d.Events,marker,e=>e.Id)};break;
                case "event.delete":Closed(op,"op","id");d=d with{Events=Delete(d.Events,Uuid(op,"id"),e=>e.Id)};break;
                case "layer.bone.upsert":
                    Closed(op,"op","nodeId","bone");var layerNode=LayerNode(d,Uuid(op,"nodeId"));var bone=Decode<AnimationBoneWeight>(op,"bone");var entries=layerNode.Layer!.Mask.Bones;
                    d=d with{Nodes=d.Nodes.Select(n=>n.Id==layerNode.Id?n with{Layer=n.Layer! with{Mask=n.Layer.Mask with{Bones=entries.Where(b=>b.BonePath!=bone.BonePath).Append(bone).ToArray()}}}:n).ToArray()};break;
                case "layer.bone.delete":
                    Closed(op,"op","nodeId","bonePath");var deleteNode=LayerNode(d,Uuid(op,"nodeId"));string bonePath=op.GetProperty("bonePath").GetString()??throw new ArgumentException("Bone path required.");if(!deleteNode.Layer!.Mask.Bones.Any(b=>b.BonePath==bonePath))throw new ArgumentException("Existing exact bone required.");
                    d=d with{Nodes=d.Nodes.Select(n=>n.Id==deleteNode.Id?n with{Layer=n.Layer! with{Mask=n.Layer.Mask with{Bones=n.Layer.Mask.Bones.Where(b=>b.BonePath!=bonePath).ToArray()}}}:n).ToArray()};break;
                case "blendspace.sample.upsert":
                    Closed(op,"op","nodeId","sample");var spaceNode=SpaceNode(d,Uuid(op,"nodeId"));var point=Decode<BlendSpaceSample>(op,"sample");
                    d=d with{Nodes=Upsert(d.Nodes,spaceNode with{BlendSpace=spaceNode.BlendSpace! with{Samples=Upsert(spaceNode.BlendSpace.Samples,point,s=>s.Id)}},n=>n.Id)};break;
                case "blendspace.sample.delete":
                    Closed(op,"op","nodeId","sampleId");var deleteSpace=SpaceNode(d,Uuid(op,"nodeId"));
                    d=d with{Nodes=Upsert(d.Nodes,deleteSpace with{BlendSpace=deleteSpace.BlendSpace! with{Samples=Delete(deleteSpace.BlendSpace.Samples,Uuid(op,"sampleId"),s=>s.Id)}},n=>n.Id)};break;
                case "graph.interruptions":Closed(op,"op","enabled");var enabled=op.GetProperty("enabled");if(enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new ArgumentException("Boolean interruption policy.");d=d with{InterruptTransitions=enabled.GetBoolean()};break;
                case "graph.rename": Closed(op, "op", "name"); d = d with { Name = op.GetProperty("name").GetString() ?? throw new ArgumentException("Graph name.") }; break;
                case "graph.entry": Closed(op, "op", "stateId"); d = d with { EntryState = Uuid(op, "stateId", empty: true) }; break;
                case "node.delete": case "link.delete": case "parameter.delete": case "state.delete": case "transition.delete":
                    Closed(op, "op", "id"); Guid id = Uuid(op, "id");
                    d = name switch {
                        "node.delete" => DeleteNode(d, id),
                        "link.delete" => d with { Links = Delete(d.Links, id, l => l.Id) },
                        "parameter.delete" => d with { Parameters = Delete(d.Parameters, id, p => p.Id) },
                        "state.delete" => DeleteState(d, id),
                        _ => d with { Transitions = Delete(d.Transitions, id, t => t.Id) }
                    }; break;
                default: throw new ArgumentException("Unsupported graph operation.");
            }
            d = CopyDraft(d);
        }
        return requireComplete ? AnimationGraphCodec.Decode(AnimationGraphCodec.Encode(d)) : CopyDraft(d);
    }
    private static AnimationGraphNode LayerNode(AnimationGraphDefinition d,Guid id)=>d.Nodes.SingleOrDefault(n=>n.Id==id&&n.Kind is AnimationNodeKind.LayerOverride or AnimationNodeKind.LayerAdditive&&n.Layer is not null)??throw new ArgumentException("Exact layer node required.");
    private static AnimationMontageDefinition Montage(AnimationGraphDefinition d)=>d.Montage??throw new ArgumentException("Explicit Montage required.");
    private static AnimationGraphDefinition DeleteNode(AnimationGraphDefinition d, Guid id)
    {
        var states = d.States.Where(s => s.PoseNode == id).Select(s => s.Id).ToHashSet();
        var nodes=Delete(d.Nodes,id,n=>n.Id);var clips=AnimationGraphValidation.ClipIds(d with{Nodes=nodes}).ToHashSet();
        return d with { Nodes = nodes, Events=d.Events.Where(e=>clips.Contains(e.ClipId)).ToArray(),Links = d.Links.Where(l => l.From != id && l.To != id).ToArray(),
            States = d.States.Where(s => !states.Contains(s.Id)).ToArray(), Transitions = d.Transitions.Where(t => !states.Contains(t.From) && !states.Contains(t.To)).ToArray(),
            EntryState = states.Contains(d.EntryState) ? Guid.Empty : d.EntryState };
    }
    private static AnimationGraphDefinition DeleteState(AnimationGraphDefinition d, Guid id) => d with {
        States = Delete(d.States, id, s => s.Id), Transitions = d.Transitions.Where(t => t.From != id && t.To != id).ToArray(), EntryState = d.EntryState == id ? Guid.Empty : d.EntryState };
    private static AnimationGraphNode SpaceNode(AnimationGraphDefinition d,Guid id)=>d.Nodes.SingleOrDefault(n=>n.Id==id&&n.Kind==AnimationNodeKind.BlendSpace&&n.BlendSpace is not null)??throw new ArgumentException("Exact BlendSpace node required.");
    private static T[] Upsert<T>(T[] values, T value, Func<T, Guid> id) => values.Any(v => id(v) == id(value)) ? values.Select(v => id(v) == id(value) ? value : v).ToArray() : [.. values, value];
    private static T[] Delete<T>(T[] values, Guid target, Func<T, Guid> id) => values.Any(v => id(v) == target) ? values.Where(v => id(v) != target).ToArray() : throw new ArgumentException("Unknown graph element.");
    private static T Decode<T>(JsonElement op, string field) where T : class => JsonSerializer.Deserialize<T>(op.GetProperty(field).GetRawText(), Json) ?? throw new ArgumentException("Graph value required.");
    public static void Closed(JsonElement value, params string[] fields) {
        if (value.ValueKind != JsonValueKind.Object) throw new ArgumentException("Closed graph object required."); var names = value.EnumerateObject().Select(p => p.Name).ToArray();
        if (names.Length != fields.Length || names.Distinct(StringComparer.Ordinal).Count() != names.Length || names.Any(n => !fields.Contains(n))) throw new ArgumentException("Closed graph fields required.");
    }
    public static Guid Uuid(JsonElement value, string field, bool empty = false) {
        var text = value.GetProperty(field); if (text.ValueKind != JsonValueKind.String || !Guid.TryParseExact(text.GetString(), "D", out Guid id) || id.ToString("D") != text.GetString() || !empty && id == Guid.Empty) throw new ArgumentException("Canonical graph UUID required."); return id;
    }
    public static JsonElement Operations(params object[] operations) => JsonSerializer.SerializeToElement(operations, Json);
    private static void RejectDuplicates(JsonElement value) {
        if (value.ValueKind == JsonValueKind.Object) { var fields = new HashSet<string>(StringComparer.Ordinal); foreach (var p in value.EnumerateObject()) { if (!fields.Add(p.Name)) throw new ArgumentException("Duplicate graph property."); RejectDuplicates(p.Value); } }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var child in value.EnumerateArray()) RejectDuplicates(child);
    }
    private sealed class ExactEnum<T> : JsonConverter<T> where T : struct, Enum {
        public override T Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) {
            if (reader.TokenType == JsonTokenType.String) foreach (T v in Enum.GetValues<T>()) if (JsonNamingPolicy.CamelCase.ConvertName(v.ToString()) == reader.GetString()) return v;
            throw new JsonException("Exact graph enum required.");
        }
        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) { if (!Enum.IsDefined(value)) throw new JsonException("Graph enum."); writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.ToString())); }
    }
}
