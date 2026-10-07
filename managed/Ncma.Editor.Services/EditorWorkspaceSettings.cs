using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ncma.Editor.Services;

// Separate local view state; legacy SideWidth/ToolbarHeight retain their original meanings.
public sealed record EditorWorkspaceSettings([property:JsonRequired]int Version,[property:JsonRequired]float Left,
    [property:JsonRequired]float Right,[property:JsonRequired]float Bottom,[property:JsonRequired]float Assets,
    [property:JsonRequired]float Ai,[property:JsonRequired]bool ShowAi)
{
    public static EditorWorkspaceSettings Default=>new(1,.17f,.23f,.28f,.62f,.19f,true);
    public static EditorWorkspaceSettings Validate(EditorWorkspaceSettings s){
        if(s.Version!=1||!float.IsFinite(s.Left+s.Right+s.Bottom+s.Assets+s.Ai)||s.Left is <.12f or >.3f||s.Right is <.16f or >.32f||s.Bottom is <.15f or >.45f||s.Assets is <.35f or >.8f||s.Ai is <.14f or >.28f)throw new ArgumentException("Invalid workspace layout.");return s;
    }
}
public sealed class EditorWorkspaceSettingsStore
{
    private readonly string _path;private readonly int _thread=Environment.CurrentManagedThreadId;
    private static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};
    public EditorWorkspaceSettings Current {get;private set;}=EditorWorkspaceSettings.Default;
    public string Diagnostic {get;private set;}="";
    public ulong Revision {get;private set;}
    public EditorWorkspaceSettingsStore(string path){
        if(!Path.IsPathFullyQualified(path))throw new ArgumentException("Absolute workspace settings path required.");_path=Path.GetFullPath(path);
        try{if(File.Exists(_path)){CheckPaths();if(new FileInfo(_path).Length is <=0 or >4096)throw new ArgumentException("Workspace settings byte budget.");
                byte[] data=File.ReadAllBytes(_path);using var doc=JsonDocument.Parse(data,new(){MaxDepth=4});var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var property in doc.RootElement.EnumerateObject())if(!names.Add(property.Name))throw new JsonException("Duplicate workspace field.");
                Current=EditorWorkspaceSettings.Validate(JsonSerializer.Deserialize<EditorWorkspaceSettings>(data,Json)!);}}
        catch(Exception e)when(e is ArgumentException or JsonException or IOException or UnauthorizedAccessException){Diagnostic=e.Message;}
    }
    private void CheckPaths(){for(var parent=new DirectoryInfo(Path.GetDirectoryName(_path)!);parent is not null;parent=parent.Parent)if(parent.Exists&&(parent.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked workspace directory rejected.");if(File.Exists(_path)&&(File.GetAttributes(_path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked workspace file rejected.");}
    public void Save(ulong revision,EditorWorkspaceSettings value){
        if(_thread!=Environment.CurrentManagedThreadId||revision!=Revision)throw new InvalidOperationException("Stale/wrong-thread workspace settings.");if(Diagnostic.Length>0)throw new InvalidOperationException("Invalid workspace file preserved; resolve it explicitly before saving.");
        value=EditorWorkspaceSettings.Validate(value);CheckPaths();Directory.CreateDirectory(Path.GetDirectoryName(_path)!);CheckPaths();string temp=_path+".tmp-"+Guid.NewGuid().ToString("N");
        using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(JsonSerializer.SerializeToUtf8Bytes(value,Json));file.Flush(true);}File.Move(temp,_path,true);Current=value;Revision++;
    }
}
