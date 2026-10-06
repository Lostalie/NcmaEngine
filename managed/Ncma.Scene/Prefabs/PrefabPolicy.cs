using System.Text.Json;
using System.Text.Json.Nodes;
using Ncma.Runtime;

namespace Ncma.Scene.Prefabs;

public enum PrefabReferenceRole { Object,Asset }
// Trusted registration only: paths name object properties, never list indices/reflection setters.
public sealed record PrefabReferenceField(string FieldPath,PrefabReferenceRole Role,string? AssetKind=null,bool Optional=false);
public sealed record PrefabComponentRule(string TypeId,int Version,PrefabReferenceField[] References);

public sealed class PrefabPolicy
{
    private readonly Dictionary<string,PrefabComponentRule> _rules=new(StringComparer.Ordinal);
    private readonly Func<BehaviourBindingData,bool> _trustedBinding;
    internal Action<SceneDocumentSnapshot>? Composition {get;}
    private bool _frozen;
    public ComponentRegistry Components {get;}
    public PrefabPolicy(ComponentRegistry components,Func<BehaviourBindingData,bool>? trustedBinding=null,Action<SceneDocumentSnapshot>? validateComposition=null)
    {
        Components=components??throw new ArgumentNullException(nameof(components));
        _trustedBinding=trustedBinding??(_=>false);
        Composition=validateComposition;
        if(components.Describe().Any(d=>d.TypeId=="ncma.transform"&&d.Version==1))AllowComponent("ncma.transform",1,[]);
    }
    // An allow rule asserts the registered payload contains authoring values, not runtime handles,
    // tick/private state or native resources. Do not infer this from arbitrary UUID-looking strings.
    public PrefabPolicy AllowComponent(string typeId,int version,PrefabReferenceField[] fields)
    {
        ArgumentNullException.ThrowIfNull(typeId);
        if(_frozen)throw new InvalidOperationException("Prefab policy is frozen.");
        if(typeId is "ncma.prefab_membership"||typeId.Contains("prefab",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Nested prefab components are unsupported.");
        if(fields is null||fields.Length>64||_rules.Count>=64||_rules.ContainsKey(typeId)||!Components.Describe().Any(d=>d.TypeId==typeId&&d.Version==version))throw new ArgumentException("Invalid/unknown prefab component rule.");
        foreach(var field in fields) {
            if(field is null||!Enum.IsDefined(field.Role))throw new ArgumentException("Invalid reference field.");
            PrefabFields.Segments(field.FieldPath);
            if(field.Role==PrefabReferenceRole.Object?field.AssetKind is not null:string.IsNullOrWhiteSpace(field.AssetKind)||field.AssetKind.Length>128||field.AssetKind.Any(char.IsControl)||field.AssetKind.Contains("prefab",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Invalid asset reference kind.");
        }
        if(fields.Select(f=>f.FieldPath).Distinct(StringComparer.Ordinal).Count()!=fields.Length)throw new ArgumentException("Duplicate reference path.");
        _rules.Add(typeId,new(typeId,version,(PrefabReferenceField[])fields.Clone()));return this;
    }
    internal void Freeze()=>_frozen=true;
    internal PrefabComponentRule Rule(ComponentSnapshot value)=>_rules.TryGetValue(value.TypeId,out var rule)&&rule.Version==value.Version?rule:throw new ArgumentException("prefab_component_not_approved: "+value.TypeId);
    internal void RequireBinding(BehaviourBindingData binding)
    {
        var copy=binding with{Exports=(ExportData[])binding.Exports.Clone()};
        if(!_trustedBinding(copy))throw new ArgumentException("prefab_binding_not_trusted: "+binding.TypeName);
    }
    internal Guid Reference(ComponentSnapshot component,PrefabReferenceField field)
    {
        var value=PrefabFields.Read(component.Data,field.FieldPath);
        if(value.ValueKind!=JsonValueKind.String||!Guid.TryParseExact(value.GetString(),"D",out Guid id)||id==Guid.Empty&&!field.Optional)throw new ArgumentException("Invalid persistent prefab reference.");return id;
    }
}

// Scope belongs to the trusted edit host, never reconstructed from prefab/MCP JSON.
public sealed class PrefabExtractionScope
{
    private readonly SceneDocument _edit;
    private readonly HashSet<Guid> _approved;
    private readonly Func<bool> _current;
    public PrefabExtractionScope(SceneDocument edit,IEnumerable<Guid> approvedObjects,Func<bool> current)
    {
        _edit=edit??throw new ArgumentNullException(nameof(edit));_current=current??throw new ArgumentNullException(nameof(current));
        ArgumentNullException.ThrowIfNull(approvedObjects);
        var ids=approvedObjects.Take(PrefabDocumentCodec.MaxObjects+1).ToArray();_approved=new(ids);
        if(ids.Length is <1 or >PrefabDocumentCodec.MaxObjects||ids.Length!=_approved.Count||ids.Any(id=>id==Guid.Empty))throw new ArgumentException("Invalid exact extraction scope.");
    }
    internal void Require(SceneDocument source,Guid[] selected)
    {
        if(!ReferenceEquals(source,_edit)||!_current()||selected.Any(id=>!_approved.Contains(id)))throw new InvalidOperationException("prefab_extract_scope_denied");
    }
}

internal static class PrefabFields
{
    internal static string[] Segments(string path)
    {
        if(path is null||path.Length is <2 or >256||path[0]!='/')throw new ArgumentException("Invalid registered field path.");
        string[] segments=path[1..].Split('/');
        if(segments.Length>16||segments.Any(s=>s.Length==0||!(char.IsAsciiLetter(s[0])||s[0]=='_')||s.Any(c=>!(char.IsAsciiLetterOrDigit(c)||c=='_'))))throw new ArgumentException("Property paths only; no array index, escape or reflection.");return segments;
    }
    internal static JsonElement Read(JsonElement value,string path)
    {foreach(string part in Segments(path)){if(value.ValueKind!=JsonValueKind.Object||!value.TryGetProperty(part,out value))throw new ArgumentException("Missing registered reference field: "+path);}return value;}
    internal static ComponentSnapshot Replace(ComponentSnapshot component,string path,Guid id)
    {
        var parts=Segments(path);JsonNode node=JsonNode.Parse(component.Data.GetRawText())??throw new ArgumentException("Missing component payload.");
        var cursor=node;foreach(string part in parts[..^1])cursor=cursor[part]??throw new ArgumentException("Missing reference parent.");
        cursor[parts[^1]]=id.ToString("D");return component with{Data=JsonSerializer.SerializeToElement(node)};
    }
}
