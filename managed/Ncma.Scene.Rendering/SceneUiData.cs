using Ncma.Assets;
using Ncma.Runtime;
using System.Text.Json.Serialization;

namespace Ncma.Scene.Rendering;

// Scene-wide immutable package selection, not a UI object tree or live widget state.
// Absence uses the explicit project default; Off deliberately suppresses that default.
public readonly record struct SceneUiData(int Version,bool Enabled,Guid DocumentId,ulong Generation,string Path,string Hash):IComponent
{
    public const string TypeId="ncma.scene.ui";
    public static SceneUiData Off=>new(1,false,Guid.Empty,0,"","");
    public static SceneUiData From(UiPackageReference reference){reference=reference.Validate();return new(1,true,reference.DocumentId,reference.Generation,reference.Path,reference.Hash);}
    [JsonIgnore] public UiPackageReference? Package=>Enabled?new UiPackageReference(Version,DocumentId,Generation,Path,Hash).Validate():null;
    public static SceneUiData Validate(SceneUiData value)
    {
        if(value.Version!=1)throw new ArgumentException("Scene UI selection version.");
        if(!value.Enabled){if(value!=Off)throw new ArgumentException("Disabled scene UI must be canonical Off.");return value;}
        return From(value.Package!);
    }
    public static void Register(ComponentRegistry registry)=>registry.Register<SceneUiData>(TypeId,1,"""
        {"type":"object","additionalProperties":false,"required":["version","enabled","documentId","generation","path","hash"],"properties":{
        "version":{"type":"integer"},"enabled":{"type":"boolean"},"documentId":{"type":"string"},"generation":{"type":"integer"},"path":{"type":"string"},"hash":{"type":"string"}}}
        """,Validate,runtimeAttachable:false);
}

public static class SceneUiState
{
    private static readonly ComponentRegistry Registry=CreateRegistry();
    private static ComponentRegistry CreateRegistry(){var r=ComponentRegistry.CreateDefault();SceneUiData.Register(r);return r;}
    public static SceneUiData? Read(ISceneContent snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);SceneUiData? found=null;
        foreach(var obj in snapshot.Objects)foreach(var component in obj.Components.Where(c=>c.TypeId==SceneUiData.TypeId)){
            if(found is not null)throw new ArgumentException("Only one scene UI selection is allowed, including Off.");
            found=Registry.Decode<SceneUiData>(component);
        }
        return found;
    }
    public static UiPackageReference? Resolve(ISceneContent snapshot,UiPackageReference? projectDefault)
        =>Read(snapshot) is {} selected?selected.Package:projectDefault?.Validate();
}
