using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Ncma.Editor.Services;

public sealed record EditorPreferences(
    [property:JsonRequired] int Version,
    [property:JsonRequired] string Theme,
    [property:JsonRequired] float SideWidth,
    [property:JsonRequired] float ToolbarHeight,
    [property:JsonRequired] bool ShowConsole,
    [property:JsonRequired] bool ShowPreviews,
    [property:JsonRequired] string FontPath,
    [property:JsonRequired] float FontSize,
    [property:JsonRequired] string LastScenePath,
    [property:JsonRequired] string LastFbxPath)
{
    public static EditorPreferences Default(string font="") => new(1,"Dark",300,185,true,true,font,18,"","");
    public static EditorPreferences Validate(EditorPreferences value) {
        if (value.Version!=1 || value.Theme is not ("Dark" or "Light" or "Classic") ||
            !float.IsFinite(value.SideWidth) || value.SideWidth is <180 or >480 ||
            !float.IsFinite(value.ToolbarHeight) || value.ToolbarHeight is <160 or >300 ||
            !float.IsFinite(value.FontSize) || value.FontSize is <8 or >64) throw new ArgumentException("Invalid editor preferences.");
        foreach(string path in new[]{value.FontPath,value.LastScenePath,value.LastFbxPath})
            if(path is null || path.Contains('\0') || new UTF8Encoding(false,true).GetByteCount(path)>1023 ||
                path.Length!=0 && !Path.IsPathFullyQualified(path)) throw new ArgumentException("Absolute bounded preference paths required.");
        if(value.FontPath.Length!=0 && (!File.Exists(value.FontPath) || !new[]{".ttf",".ttc",".otf"}.Contains(Path.GetExtension(value.FontPath).ToLowerInvariant())))
            throw new ArgumentException("Trusted font file is missing or invalid.");
        if(value.LastScenePath.Length!=0 && !value.LastScenePath.EndsWith(".ncmascene",StringComparison.OrdinalIgnoreCase) ||
            value.LastFbxPath.Length!=0 && !value.LastFbxPath.EndsWith(".fbx",StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Invalid remembered path format.");
        return value;
    }
}

// Separate user-settings command domain, never serialized in scene/project or exposed over MCP.
public sealed class EditorPreferencesStore
{
    private readonly int _owner=Environment.CurrentManagedThreadId;
    private readonly string _path;
    private readonly List<EditorPreferences> _undo=[],_redo=[];
    private static readonly JsonSerializerOptions Json=new() {PropertyNamingPolicy=JsonNamingPolicy.CamelCase,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};
    public EditorPreferences Current { get; private set; }
    public ulong Revision { get; private set; }
    public bool CanUndo => _undo.Count>0;
    public bool CanRedo => _redo.Count>0;
    public EditorPreferencesStore(string path,string font="") {
        if(!Path.IsPathFullyQualified(path))throw new ArgumentException("Absolute preference store required.");
        _path=Path.GetFullPath(path);Current=EditorPreferences.Validate(EditorPreferences.Default(font));
        if(File.Exists(_path)) {
            if((File.GetAttributes(_path)&FileAttributes.ReparsePoint)!=0 || new FileInfo(_path).Length>16384)throw new ArgumentException("Invalid preference file.");
            string text=File.ReadAllText(_path,new UTF8Encoding(false,true));
            using var json=JsonDocument.Parse(text);var keys=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var property in json.RootElement.EnumerateObject())if(!keys.Add(property.Name))throw new JsonException("Duplicate preference field.");
            Current=EditorPreferences.Validate(JsonSerializer.Deserialize<EditorPreferences>(text,Json)!);
        }
    }
    private void Check(ulong revision) {if(Environment.CurrentManagedThreadId!=_owner)throw new InvalidOperationException("Preferences owner thread required.");if(revision!=Revision)throw new InvalidOperationException("stale_preferences");}
    private void Write(EditorPreferences value) {
        string directory=Path.GetDirectoryName(_path)!;Directory.CreateDirectory(directory);
        // Reject links in every existing ancestor; never replace an unrelated linked file.
        for(var info=new DirectoryInfo(directory);info is not null;info=info.Parent)
            if((info.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked preferences directory rejected.");
        if(File.Exists(_path)&&(File.GetAttributes(_path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked preference file rejected.");
        string temporary=_path+".tmp-"+Guid.NewGuid().ToString("N");
        try {
            byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(value,Json);
            using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {stream.Write(bytes);stream.Flush(true);}
            File.Move(temporary,_path,true);
        } finally {if(File.Exists(temporary))File.Delete(temporary);}
    }
    public void Save(ulong revision,EditorPreferences value) {
        Check(revision);value=EditorPreferences.Validate(value);ulong next=checked(Revision+1);Write(value);
        _undo.Add(Current);if(_undo.Count>16)_undo.RemoveAt(0);_redo.Clear();Current=value;Revision=next;
    }
    public void History(ulong revision,bool redo) {
        Check(revision);var source=redo?_redo:_undo;var target=redo?_undo:_redo;
        if(source.Count==0)throw new InvalidOperationException("Preferences history empty.");
        ulong next=checked(Revision+1);var candidate=source[^1];Write(candidate);
        target.Add(Current);source.RemoveAt(source.Count-1);Current=candidate;Revision=next;
    }
}
