using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Gameplay;
using Ncma.Gui;
using Ncma.Runtime;
using Ncma.Rendering;
using Ncma.Scene;
using Ncma.Platform;
using Ncma.Application;

namespace Ncma.Editor.App;

// Only this presentation layer knows GuiItem. Business routing and the document stay managed.
internal sealed unsafe partial class EditorPresenter(EditorWorkspace workspace, string? gameplayAssembly, string? projectRoot = null, FbxPreviewSession? fbxPreview = null, ActionPreviewSession? animationPreview = null,
    EditorPreferencesStore? preferences = null, Func<LocalFileKind,string?>? filePicker = null, ApplicationLog? log = null, EditorAssetWorkflow? assetWorkflow=null, bool toolbarStyle=false, bool workspaceStyle=false, string? projectName=null)
{
    private static readonly JsonSerializerOptions DataJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, IncludeFields = true, IgnoreReadOnlyProperties = true };
    private readonly Dictionary<Guid, ulong> _rowIds = [];
    private ulong _nextRowId;
    private sealed record ActionView(string Kind, Guid Object = default, string Field = "", int Index = 0, object? Operation = null);
    private readonly List<GuiItem> _items = [];
    private readonly List<byte> _text = [];
    private readonly Dictionary<(ulong, ulong), ActionView> _actions = [];
    private readonly Queue<string> _messages = [];
    private EditorPage? _page;
    private GuiFrame _frame;
    private ulong _generation = 1, _lastRevision = ulong.MaxValue, _lastDocument;
    private Guid? _lastSelection;
    private Guid _lastCatalog;
    private int _offset;
    private int _componentOffset, _bindingOffset, _catalogOffset;
    private readonly Dictionary<Guid, int> _exportOffsets = [];
    private readonly Dictionary<(Guid Object, string Type), int> _jsonPageOffsets = [];
    private readonly Dictionary<(Guid Object, string Type), JsonTextPages> _jsonPageCache = [];
    private sealed record JsonPageEdit(Guid Object, string Type, int Page, JsonTextPages Pages);
    private JsonPageEdit? _jsonEdit;
    private string _path = "", _deleteConfirmation = "";
    private string _fbxPath = "";
    private FbxPreviewStatus? _fbxStatus;
    private int _fbxClipOffset;
    private float[] _fbxSample = [];
    private ulong _sampleRevision;
    private double _sampleTime;
    private readonly FbxWireframe _wireframe = new();
    private float _fbxYaw;
    private Guid? _reportResource;
    private string[] _fbxReport = [];
    private int _reportOffset;
    private bool _showAnimation;
    private EditorPreferences? _pendingPreferences;
    private bool _preferencesLoaded, _showPreferences;
    private ulong _logSequence;
    private LogEvent[] _logEvents = [];
    private int _logOffset;
    private bool _discardConfirmed;
    private readonly EditorAuthorizationController _authorizationController = new(workspace);
    private AuthorizationPage? _authorization;
    private int _proposalOffset;
    private bool _allowAgentHistory, _reviewedProposal;
    private string _agentDeleteConfirmation = "";
    private string _proposalFingerprint = "";
    public ReadOnlySpan<GuiItem> Items => CollectionsMarshal.AsSpan(_items);
    public ReadOnlySpan<byte> Text => CollectionsMarshal.AsSpan(_text);
    public GuiFrame Frame => _frame;
    public string LastMessage => _messages.LastOrDefault() ?? "";
    public (float X, float Y, float Width, float Height) Viewport { get; private set; }
    public SceneObjectData? Selected => _page?.Selected;
    public Guid SceneCamera { get; private set; }
    private bool _refreshRenderAssets;
    public bool ConsumeRenderAssetRefresh() { bool pending=_refreshRenderAssets; _refreshRenderAssets=false; return pending; }
    public void SelectStartupCamera(Guid? camera) => SceneCamera=camera??Guid.Empty;
    private string[] _renderDiagnostics=[];
    public void ShowRenderDiagnostics(IReadOnlyList<Ncma.Scene.Rendering.SceneRenderDiagnostic>? diagnostics) {
        _renderDiagnostics=diagnostics?.Take(4).Select(d=>$"Render: {d.Code}; object={d.ObjectId}; asset={d.AssetId}").ToArray()??[];
    }
    public void CancelInteraction() { if(_layoutBefore is not null){_layout=_layoutBefore;_layoutBefore=null;}CancelUi();workspace.CancelDraft(); _jsonEdit = null; _activeMenu = -1; _generation = checked(_generation + 1); }
    private static string BoundMessage(string message, int limit) {
        if (message.Length <= limit) return message;
        if (char.IsHighSurrogate(message[limit - 1])) limit--;
        return message[..limit];
    }
    private void Record(string message)
    {
        _messages.Enqueue(BoundMessage(message, 512));
        while (_messages.Count > 16) _messages.Dequeue();
        if(log is not null) {
            try {log.Write("error","editor.command",BoundMessage(message, 2000),_page?.Stamp.SessionId??Guid.Empty);}
            catch(Exception e) when(e is IOException or UnauthorizedAccessException) { /* Keep the bounded UI error when log storage is unavailable. */ }
        }
    }
    private void Add(GuiItemKind kind, ulong high, ulong low, string label, ActionView? action = null,
        string value = "", double number = 0, double min = 0, double max = 0, bool enabled = true)
    {
        if(workspaceStyle)label=WorkspaceLabel(label);
        byte[] labelBytes = Encoding.UTF8.GetBytes(label), valueBytes = Encoding.UTF8.GetBytes(value);
        if (labelBytes.Length > 4096 || valueBytes.Length > 1023 || _items.Count >= 8192 || _text.Count + labelBytes.Length + valueBytes.Length + (_items.Count+1)*96 > 2*1024*1024)
            throw new ArgumentException("Editor view budget exceeded.");
        var item = new GuiItem { Kind = (uint)kind, Enabled = enabled ? 1u : 0u, WidgetHigh = high, WidgetLow = low,
            LabelOffset = (uint)_text.Count, LabelLength = (uint)labelBytes.Length, Value = number, Minimum = min, Maximum = max };
        _text.AddRange(labelBytes); item.TextOffset = (uint)_text.Count; item.TextLength = (uint)valueBytes.Length; _text.AddRange(valueBytes);
        _items.Add(item); if (action is not null) _actions.Add((high, low), action);
    }
    private void Panel(ulong id, string label, float x, float y, float width, float height, string headerStatus = "")
    {
        Add(GuiItemKind.PanelBegin, 1, id, label, value:headerStatus);
        var item = _items[^1]; item.Rect[0] = x; item.Rect[1] = y; item.Rect[2] = Math.Max(1, width); item.Rect[3] = Math.Max(1, height); _items[^1] = item;
    }
    private void End() => Add(GuiItemKind.PanelEnd, 0, 0, "");
    private void Line() => Add(GuiItemKind.SameLine,6,(ulong)_items.Count+1,"");
    private void Canvas(ulong domain, ReadOnlySpan<PreviewLine> lines)
    {
        ulong id = 1;
        Add(GuiItemKind.CanvasBegin,domain,id++,"");
        var item = _items[^1]; item.Rect[3] = 340; _items[^1] = item;
        // At most 16 segments per copied UTF-8 batch; native parses/prefights all batches before drawing.
        int offset = 0;
        while (offset < lines.Length) {
            PreviewLine first = lines[offset]; var payload = new StringBuilder(); int count=0;
            while (offset < lines.Length && count < 16 && lines[offset].Color == first.Color && lines[offset].Width == first.Width) {
                var line = lines[offset++]; count++;
                payload.Append(System.FormattableString.Invariant($"{line.A.X:F4} {line.A.Y:F4} {line.B.X:F4} {line.B.Y:F4} "));
            }
            Add(GuiItemKind.CanvasLines,domain,id++,"",value:payload.ToString(),number:first.Color,min:first.Width,max:first.Width);
        }
        Add(GuiItemKind.CanvasEnd,domain,id,"");
    }
    private void Button(ulong id, string label, string action, bool enabled = true, Guid target = default, object? operation = null) =>
        Add(GuiItemKind.Button, 2, id, label, new(action, target, Operation: operation), enabled: enabled);
    public GuiFrame Build(ulong frameId, uint width, uint height)
    {
        if (!_preferencesLoaded) {
            _pendingPreferences=preferences?.Current;
            if(_pendingPreferences is not null) {_path=_pendingPreferences.LastScenePath;_fbxPath=_pendingPreferences.LastFbxPath;}
            _preferencesLoaded=true;
        }
        _page = workspace.Capture(_offset);
        if (_offset > 0 && _offset >= _page.Total) { _offset = Math.Max(0, (_page.Total - 1) / 32 * 32); _page = workspace.Capture(_offset); }
        var catalog = workspace.Owner.Catalog.Snapshot;
        if (_lastRevision != _page.Stamp.Revision || _lastDocument != _page.Stamp.Generation || _lastSelection != _page.State.Selection || _lastCatalog != catalog.Generation)
        {
            if (_lastSelection != _page.State.Selection || _lastDocument != _page.Stamp.Generation)
            { _componentOffset = 0; _bindingOffset = 0; _exportOffsets.Clear(); _jsonPageOffsets.Clear(); _jsonEdit = null; }
            var alive = workspace.Owner.Document.World.GetObjects().Select(o => o.PersistentId).ToHashSet();
            foreach (var id in _rowIds.Keys.Where(id => !alive.Contains(id)).ToArray()) _rowIds.Remove(id);
            _generation = checked(_generation + 1); _deleteConfirmation = ""; _jsonPageCache.Clear();
            _lastRevision = _page.Stamp.Revision; _lastDocument = _page.Stamp.Generation; _lastSelection = _page.State.Selection; _lastCatalog = catalog.Generation;
        }
        _items.Clear(); _text.Clear(); _actions.Clear();
        bool writable = !_page.State.Frozen && !_page.State.HistoryInvalidated;
        float w = Math.Max(1, width), h = Math.Max(1, height), side = Math.Min(preferences?.Current.SideWidth??300, w * .35f), toolbar = Math.Min(preferences?.Current.ToolbarHeight??185, h * .3f);
        bool header=toolbarStyle&&w>=520&&toolbar>=104;
        if(workspaceStyle){ArrangeWorkspace(w,h);WorkspaceToolbar(w);}
        else if(header)Toolbar(w);
        if(workspaceStyle&&_uiMode&&_ui is not null)return BuildUi(frameId);
        // User-approved slice changes only the header. Preserve side/status/viewport geometry,
        // existing preference field meanings and the remaining scene controls below the header.
        ulong labelId = 10000;
        if(!workspaceStyle||_showSceneFiles){
        if(workspaceStyle){Panel(1,"场景文件",8,54,Math.Min(600,w-16),240);var overlay=_items[^1];overlay.Value=2;_items[^1]=overlay;}
        else Panel(1, "Scene / 场景", 0, header?64:0, w, toolbar-(header?64:0));
        if(!workspaceStyle)Add(GuiItemKind.Theme,12,1,"",number:preferences?.Current.Theme switch {"Light"=>1,"Classic"=>2,_=>0});
        Add(GuiItemKind.Label, 3, labelId++, $"{workspace.Owner.Document.World.Name} | Revision {_page.State.Revision} | {(_page.State.Dirty ? "Modified" : "Saved")} | {_page.State.FilePath ?? "Unsaved"}");
        Add(GuiItemKind.Text, 2, 1, "Scene path (.ncmascene)", new("path"), _path);
        Add(GuiItemKind.Checkbox, 2, 2, "Confirm replacing unsaved document", new("discard"), number: _discardConfirmed ? 1 : 0, max: 1, enabled: writable);
        Button(3, "New", "new", writable); Line(); Button(4, "Open", "open", writable && _path.Length > 0); Line(); Button(5, "Save", "save", writable);
        Button(6, "Undo", "undo", writable && _page.State.UndoCount > 0); Line(); Button(7, "Redo", "redo", writable && _page.State.RedoCount > 0);
        if(filePicker is not null) {
            Line(); Button(40,"Open Scene...","browse_open",writable);Line(); Button(41,"Save Scene As...","browse_save",writable);
        }
        if(preferences is not null) { Line(); Button(42,_showPreferences?"Hide Preferences":"Preferences","preferences_toggle"); }
        if(_characters is not null){Line();Add(GuiItemKind.Button,22,1,_showCharacters?"Hide Character Debug":"Character Debug",new("character_toggle"));}
        End();
        }
        if(workspaceStyle)Region(2,"场景对象",_geometry.Objects);
        else Panel(2, "GameObjects (flat list)", 0, toolbar, side, h - toolbar);
        if(workspaceStyle)Add(GuiItemKind.Text,24,50,"搜索对象",new("workspace_search"),value:_objectSearch);
        Button(8, "+ GameObject", "create", writable);
        Button(9, "Previous page", "previous", _offset > 0); Line(); Button(10, "Next page", "next", _offset + 32 < _page.Total);
        Add(GuiItemKind.Label, 3, labelId++, $"Objects: {_page.Total}; page {_offset / 32 + 1}");
        foreach (var row in _page.Rows)
        {
            if(workspaceStyle&&_objectSearch.Length>0&&!row.Name.Contains(_objectSearch,StringComparison.OrdinalIgnoreCase))continue;
            if (!_rowIds.TryGetValue(row.Id, out ulong low)) _rowIds.Add(row.Id, low = checked(++_nextRowId));
            Add(GuiItemKind.Button, 4, low, (_page.State.Selection == row.Id ? "> " : "") + row.Name,
                new("select", row.Id));
        }
        if(!workspaceStyle)BuildAssets(ref labelId,writable);
        End();
        if(workspaceStyle)Region(3,"属性检查器",_geometry.Inspector);
        else Panel(3, "Inspector / 检查器", w - side, toolbar, side, h - toolbar);
        if (_page.Selected is { } selected)
        {
            Add(GuiItemKind.Label, 3, labelId++, selected.Id.ToString());
            if (Encoding.UTF8.GetByteCount(selected.Name) <= 1023)
                Add(GuiItemKind.Text, 2, 11, "Name", new("name", selected.Id), selected.Name, enabled: writable);
            else Add(GuiItemKind.Label, 3, labelId++, selected.Name + " (exceeds single-widget text budget)");
            ulong control = 100;
            _componentOffset = Math.Clamp(_componentOffset,0,Math.Max(0,(selected.Components.Length-1)/8*8));
            Button(30,"Previous components","components_previous",_componentOffset>0);
            Line();
            Button(31,"Next components","components_next",_componentOffset+8<selected.Components.Length);
            Add(GuiItemKind.Label,3,labelId++,$"Components {selected.Components.Length}; page {_componentOffset/8+1}");
            foreach (var component in selected.Components.Skip(_componentOffset).Take(8))
            {
                Add(GuiItemKind.Label, 3, labelId++, component.TypeId);
                if (component.TypeId == "ncma.transform")
                {
                    var transform = component.Data.Deserialize<TransformData>(DataJson);
                    double[] values = [transform.Position.X, transform.Position.Y, transform.Position.Z, transform.Rotation.X, transform.Rotation.Y, transform.Rotation.Z, transform.Rotation.W, transform.Scale.X, transform.Scale.Y, transform.Scale.Z];
                    string[] names = ["Position X", "Position Y", "Position Z", "Rotation X", "Rotation Y", "Rotation Z", "Rotation W", "Scale X", "Scale Y", "Scale Z"];
                    for (int i = 0; i < values.Length; i++) Add(GuiItemKind.Number, 2, control++, names[i], new("transform", selected.Id, Index: i), number: values[i], min: i is >= 3 and <= 6 ? -1 : -100, max: i is >= 3 and <= 6 ? 1 : 100, enabled: writable);
                }
                else if (Encoding.UTF8.GetByteCount(component.Data.GetRawText()) <= 1023 && !(_jsonEdit is { } jsonActive && jsonActive.Object == selected.Id && jsonActive.Type == component.TypeId))
                    Add(GuiItemKind.Text, 2, control++, "Component JSON", new("component", selected.Id, component.TypeId, component.Version), component.Data.GetRawText(), enabled: writable);
                else
                {
                    var key = (selected.Id, component.TypeId);
                    // During activation use the immutable baseline: changing page length must not
                    // shift page boundaries while the native text cursor/IME draft is active.
                    JsonTextPages pages;
                    if (_jsonEdit is { } active && active.Object == selected.Id && active.Type == component.TypeId) pages = active.Pages;
                    else if (!_jsonPageCache.TryGetValue(key, out pages!)) {
                        pages = new JsonTextPages(component.Data.GetRawText());
                        if (_jsonPageCache.Count >= 32) _jsonPageCache.Clear();
                        _jsonPageCache.Add(key, pages);
                    }
                    int page = Math.Clamp(_jsonPageOffsets.GetValueOrDefault(key), 0, pages.Count - 1);
                    _jsonPageOffsets[key] = page;
                    Add(GuiItemKind.Button, 2, control++, "Previous JSON page", new("json_previous", selected.Id, component.TypeId), enabled: page > 0);
                    Line();
                    Add(GuiItemKind.Button, 2, control++, "Next JSON page", new("json_next", selected.Id, component.TypeId), enabled: page + 1 < pages.Count);
                    Add(GuiItemKind.Label, 3, labelId++, $"Component JSON page {page + 1}/{pages.Count}; full JSON validates on commit");
                    Add(GuiItemKind.Text, 2, control++, "Component JSON page",
                        new("component_page", selected.Id, component.TypeId, component.Version, new JsonPageEdit(selected.Id, component.TypeId, page, pages)),
                        pages.Text(page), enabled: writable);
                }
                if (component.TypeId == RenderConfiguration.ComponentType)
                {
                    var config = component.Data.Deserialize<RenderConfiguration>(DataJson);
                    (string Field, string Label, double Value, double Min, double Max)[] fields = [
                        ("exposure", "Reference exposure", config.Exposure, .01, 16),
                        ("metallic", "Reference metallic", config.Metallic, 0, 1),
                        ("roughness", "Reference roughness", config.Roughness, .04, 1),
                        ("toneExposureOverride", "Tone exposure override (0 = default)", config.ToneExposureOverride, 0, 16),
                        ("featureExposure", "Feature exposure (0 = off)", config.FeatureExposure, 0, 16),
                        ("clearRed", "Clear red", config.ClearRed, 0, 1),
                        ("clearGreen", "Clear green", config.ClearGreen, 0, 1),
                        ("clearBlue", "Clear blue", config.ClearBlue, 0, 1),
                        ("baseRed","Base color red",config.BaseRed,0,1),("baseGreen","Base color green",config.BaseGreen,0,1),("baseBlue","Base color blue",config.BaseBlue,0,1),
                        ("lightIntensity","Light intensity",config.LightIntensity,0,20),("ambient","Ambient",config.Ambient,0,.25),
                        ("constantBias","Shadow constant bias",config.ConstantBias,0,.01),("slopeBias","Shadow slope bias",config.SlopeBias,0,8),
                        ("shadowDistance","Shadow distance",config.ShadowDistance,5,100),("cascadeLambda","Cascade lambda",config.CascadeLambda,0,1),
                        ("shadowFilter","Shadow filter: 0 PCF3 / 1 PCF5 / 2 PCSS",config.ShadowFilter,0,2),("lightRadius","Light angular radius",config.LightRadius,.001,.2),
                        ("contactSteps","Contact steps",config.ContactSteps,4,32),("contactDistance","Contact distance",config.ContactDistance,.05,3),
                        ("contactThickness","Contact thickness",config.ContactThickness,.005,.3),("contactStrength","Contact strength",config.ContactStrength,0,1)
                    ];
                    foreach (var field in fields)
                        Add(GuiItemKind.Number, 2, control++, field.Label, new("render_field", selected.Id, field.Field),
                            number: field.Value, min: field.Min, max: field.Max, enabled: writable);
                    Add(GuiItemKind.Checkbox, 2, control++, "Replace tone stage", new("render_field", selected.Id, "replaceToneStage"),
                        number: config.ReplaceToneStage ? 1 : 0, max: 1, enabled: writable);
                    Add(GuiItemKind.Checkbox,2,control++,"Directional shadows",new("render_field",selected.Id,"shadowEnabled"),number:config.ShadowEnabled?1:0,max:1,enabled:writable);
                    Add(GuiItemKind.Checkbox,2,control++,"Contact shadows",new("render_field",selected.Id,"contactEnabled"),number:config.ContactEnabled?1:0,max:1,enabled:writable);
                    Add(GuiItemKind.Label, 3, labelId++, "Reference PBR/soft shadow controls; 4 cascades × 2048. Not a material asset authoring system.");
                }
                Button(control++, "Remove " + component.TypeId, "transaction", writable, selected.Id, new { op = "remove_component", objectId = selected.Id, typeId = component.TypeId });
            }
            if (!selected.Components.Any(c => c.TypeId == "ncma.transform"))
                Button(control++, "Add Transform", "transaction", writable, selected.Id, new { op = "set_component", objectId = selected.Id, typeId = "ncma.transform", version = 1, data = workspace.Owner.Document.World.Components.Encode(TransformData.Identity) });
            _bindingOffset = Math.Clamp(_bindingOffset,0,Math.Max(0,(selected.Behaviours.Length-1)/8*8));
            Button(32,"Previous bindings","bindings_previous",_bindingOffset>0);
            Line();
            Button(33,"Next bindings","bindings_next",_bindingOffset+8<selected.Behaviours.Length);
            Add(GuiItemKind.Label,3,labelId++,$"Bindings {selected.Behaviours.Length}; page {_bindingOffset/8+1}");
            foreach (var binding in selected.Behaviours.Skip(_bindingOffset).Take(8))
            {
                Add(GuiItemKind.Label, 3, labelId++, binding.TypeName);
                Add(GuiItemKind.Checkbox, 2, control++, "Enabled", new("binding", selected.Id, binding.Id.ToString()), number: binding.Enabled ? 1 : 0, max: 1, enabled: writable);
                int exportOffset = Math.Clamp(_exportOffsets.GetValueOrDefault(binding.Id),0,Math.Max(0,(binding.Exports.Length-1)/16*16));
                _exportOffsets[binding.Id] = exportOffset;
                Add(GuiItemKind.Button,2,control++,"Previous Exports",new("exports_page",binding.Id,Index:-16),enabled:exportOffset>0);
                Line();
                Add(GuiItemKind.Button,2,control++,"Next Exports",new("exports_page",binding.Id,Index:16),enabled:exportOffset+16<binding.Exports.Length);
                Add(GuiItemKind.Label,3,labelId++,$"Exports {binding.Exports.Length}; page {exportOffset/16+1}");
                foreach (var export in binding.Exports.Skip(exportOffset).Take(16))
                    Add(export.Kind == ExportKind.Boolean ? GuiItemKind.Checkbox : GuiItemKind.Number, 2, control++, export.Name,
                        new("export", selected.Id, binding.Id + "/" + export.Name, (int)export.Kind), number: export.Value,
                        min: export.Kind == ExportKind.Boolean ? 0 : -1000, max: export.Kind == ExportKind.Boolean ? 1 : 1000, enabled: writable);
                Button(control++, "Remove binding", "transaction", writable, selected.Id, new { op = "remove_binding", objectId = selected.Id, bindingId = binding.Id });
            }
            _catalogOffset = Math.Clamp(_catalogOffset,0,Math.Max(0,(catalog.Types.Length-1)/8*8));
            Button(34,"Previous script types","catalog_previous",_catalogOffset>0);
            Line();
            Button(35,"Next script types","catalog_next",_catalogOffset+8<catalog.Types.Length);
            Add(GuiItemKind.Label,3,labelId++,$"Trusted types {catalog.Types.Length}; page {_catalogOffset/8+1}");
            foreach (var type in catalog.Types.Skip(_catalogOffset).Take(8))
                Button(control++, "Attach " + type.TypeName, "attach", writable, selected.Id, type);
            Add(GuiItemKind.Text, 2, 12, "Type UUID to confirm deletion", new("delete_text"), _deleteConfirmation, enabled: writable);
            Button(13, "Delete confirmed object", "delete", writable && Guid.TryParse(_deleteConfirmation, out Guid confirmed) && confirmed == selected.Id, selected.Id);
        }
        End();
        float center = Math.Max(1, w - 2 * side);
        if(!workspaceStyle)Viewport = (side+8, toolbar + Math.Min(220, h - toolbar)+28, Math.Max(1,center-16), Math.Max(1, h - toolbar - 256));
        if(workspaceStyle){Region(92,"资源浏览器",_geometry.Assets);BuildAssets(ref labelId,writable);End();Region(4,"控制台",_geometry.Console);}
        else Panel(4, "Play / Status / Console", side, toolbar, center, Math.Min(220, h - toolbar));
        if(!workspaceStyle){
        Button(14, "Play", "start", _page.Play is null && !_page.State.HistoryInvalidated);
        Line();
        Button(15, "Stop", "stop", _page.Play is not null);
        Line();
        Button(16, "Pause", "pause", _page.Play?.State == PlayState.Running);
        Line();
        Button(17, "Resume", "resume", _page.Play?.State == PlayState.Paused);
        Line();
        Button(18, "Step", "step", _page.Play?.State == PlayState.Paused);
        Line();
        Button(19, "Restart", "restart", _page.Play is not null);
        Button(20, "Reload configured gameplay", "reload", gameplayAssembly is not null);
        Add(GuiItemKind.Button,14,1,"Browser camera",new("browser_camera")); Line();
        Add(GuiItemKind.Button,14,2,"Use selected scene camera",new("scene_camera",_page.Selected?.Id??Guid.Empty),enabled:_page.Selected?.Components.Any(c=>c.TypeId==Ncma.Scene.Rendering.CameraData.TypeId)==true);
        if(projectRoot is not null) { Line(); Add(GuiItemKind.Button,14,3,"Refresh render assets",new("render_refresh")); }
        BuildBrowserControls();
        }
        Add(GuiItemKind.Label,3,labelId++,SceneCamera==Guid.Empty?"Independent browser camera / scene static PBR":"Scene camera: "+SceneCamera.ToString("D"));
        foreach(string diagnostic in _renderDiagnostics) Add(GuiItemKind.Label,3,labelId++,diagnostic);
        if (_page.Play is { } play)
        {
            Add(GuiItemKind.Label, 3, labelId++, $"{play.State} tick={play.Tick} alpha={play.InterpolationAlpha:F3} dropped={play.TotalDroppedSeconds:F4}");
            Add(GuiItemKind.Label, 3, labelId++, $"Session {play.SessionId}; World {play.WorldId}");
            if (play.Fault is { } fault) Add(GuiItemKind.Label, 3, labelId++, $"FAULT {fault.Phase}: {fault.Message}; attempted tick={fault.AttemptTick}; object={fault.ObjectId}; binding={fault.BindingId}; type={fault.TypeName}");
        }
        if (_page.State.HistoryInvalidated) Add(GuiItemKind.Label, 3, labelId++, "History invalidated by external mutation; restart/reopen required.");
        if(preferences?.Current.ShowConsole!=false) {
            foreach (string message in _messages) Add(GuiItemKind.Label, 3, labelId++, message);
            if(log is not null) {
                if(_logSequence!=log.Sequence) {_logEvents=log.Snapshot;_logSequence=log.Sequence;}
                _logOffset=Math.Clamp(_logOffset,0,Math.Max(0,(_logEvents.Length-1)/8*8));
                Add(GuiItemKind.Button,13,1,"Older Console page",new("log_previous"),enabled:_logOffset>0);
                Line();Add(GuiItemKind.Button,13,2,"Newer Console page",new("log_next"),enabled:_logOffset+8<_logEvents.Length);
                Add(GuiItemKind.Button,13,3,"Latest Console page",new("log_latest"));
                foreach(var entry in _logEvents.Skip(_logOffset).Take(8)) {
                    Add(GuiItemKind.Label,3,labelId++,$"#{entry.Sequence} {entry.Level} {entry.Code} [{entry.Correlation}]");
                    foreach(string chunk in InspectionText.Split(entry.Message,8192))Add(GuiItemKind.Label,3,labelId++,chunk);
                }
            }
        }
        if (_showPreferences && preferences is not null && _pendingPreferences is { } prefs) {
            Add(GuiItemKind.Label,3,labelId++,"Local preferences only; separate reversible history. Font path/size apply after restart; no docking.");
            Add(GuiItemKind.Number,11,1,"Theme: 0 Dark / 1 Light / 2 Classic",new("preference_theme"),number:prefs.Theme switch {"Light"=>1,"Classic"=>2,_=>0},max:2);
            Add(GuiItemKind.Number,11,2,"Sidebar width",new("preference_side"),number:prefs.SideWidth,min:180,max:480);
            Add(GuiItemKind.Number,11,3,"Toolbar height",new("preference_toolbar"),number:prefs.ToolbarHeight,min:160,max:300);
            Add(GuiItemKind.Checkbox,11,4,"Show Console",new("preference_console"),number:prefs.ShowConsole?1:0,max:1);
            Add(GuiItemKind.Checkbox,11,5,"Show isolated previews",new("preference_previews"),number:prefs.ShowPreviews?1:0,max:1);
            Add(GuiItemKind.Text,11,6,"Font path (restart)",new("preference_font"),value:prefs.FontPath);
            Add(GuiItemKind.Number,11,7,"Font size (restart)",new("preference_font_size"),number:prefs.FontSize,min:8,max:64);
            Add(GuiItemKind.Button,11,8,"Save local preferences",new("preferences_save",Operation:preferences.Revision));
            Add(GuiItemKind.Button,11,9,"Preferences Undo",new("preferences_undo",Operation:preferences.Revision),enabled:preferences.CanUndo);
            Add(GuiItemKind.Button,11,10,"Preferences Redo",new("preferences_redo",Operation:preferences.Revision),enabled:preferences.CanRedo);
        }
        if (fbxPreview is not null && preferences?.Current.ShowPreviews!=false && (!workspaceStyle||_activeMenu==2))
        {
            _fbxStatus = fbxPreview.Capture();
            Add(GuiItemKind.Label, 3, labelId++, "FBX — isolated C# preview/history; not a scene Animator");
            Add(GuiItemKind.Text, 7, 1, "Absolute FBX path", new("fbx_path"), _fbxPath);
            Add(GuiItemKind.Button, 7, 2, "Import FBX", new("fbx_import", Operation: _fbxStatus.Revision), enabled: _fbxPath.Length > 0);
            if(filePicker is not null)Add(GuiItemKind.Button,7,13,"Open FBX...",new("fbx_browse",Operation:_fbxStatus.Revision));
            Line();
            Add(GuiItemKind.Button, 7, 3, "Preview Undo", new("fbx_undo", Operation: _fbxStatus.Revision), enabled: _fbxStatus.UndoCount > 0);
            Line();
            Add(GuiItemKind.Button, 7, 4, "Preview Redo", new("fbx_redo", Operation: _fbxStatus.Revision), enabled: _fbxStatus.RedoCount > 0);
            if (_fbxStatus.AssetId is not null)
            {
                Add(GuiItemKind.Label, 3, labelId++, $"Asset {_fbxStatus.AssetId} | {_fbxStatus.Bones} bones | {_fbxStatus.Meshes} meshes");
                Add(GuiItemKind.Label, 3, labelId++, $"Clip {Short(_fbxStatus.ClipName!)} | {_fbxStatus.Time:F3}/{_fbxStatus.Duration:F3}s");
                Add(GuiItemKind.Button, 7, 5, _fbxStatus.Paused ? "Play FBX" : "Pause FBX",
                    new(_fbxStatus.Paused ? "fbx_resume" : "fbx_pause", Operation: _fbxStatus.Revision));
                Line();
                Add(GuiItemKind.Button, 7, 6, "FBX Step 1/60", new("fbx_step", Operation: _fbxStatus.Revision));
                Line();
                Add(GuiItemKind.Button, 7, 7, "Inspect CPU pose", new("fbx_sample", Operation: _fbxStatus.Revision));
                _fbxClipOffset = Math.Clamp(_fbxClipOffset, 0, Math.Max(0, (fbxPreview.ClipCount - 1) / 8 * 8));
                Add(GuiItemKind.Button, 7, 8, "Previous clips", new("fbx_previous"), enabled: _fbxClipOffset > 0);
                Line();
                Add(GuiItemKind.Button, 7, 9, "Next clips", new("fbx_next"), enabled: _fbxClipOffset + 8 < fbxPreview.ClipCount);
                for (int i = _fbxClipOffset; i < Math.Min(fbxPreview.ClipCount, _fbxClipOffset + 8); i++)
                    Add(GuiItemKind.Button, 7, 100 + (ulong)i, Short(fbxPreview.ClipName(i)),
                        new("fbx_clip", Index: i, Operation: _fbxStatus.Revision));
                if (_fbxSample.Length != 0 && _sampleRevision == _fbxStatus.Revision && _sampleTime == _fbxStatus.Time)
                {
                    Add(GuiItemKind.Label, 3, labelId++, $"CPU sample: {_fbxSample.Length} copied floats (matrices + skinned vertices)");
                    for (int i = 0; i < Math.Min(8, _fbxStatus.Bones); i++)
                        Add(GuiItemKind.Label, 3, labelId++, $"Bone {i}: ({_fbxSample[i*16+12]:F3}, {_fbxSample[i*16+13]:F3}, {_fbxSample[i*16+14]:F3})");
                }
                Add(GuiItemKind.Number,7,10,"FBX Orbit",new("fbx_orbit"),number:_fbxYaw,min:-Math.PI,max:Math.PI);
                Canvas(8,_wireframe.Build(fbxPreview,_fbxYaw,Math.Max(1,center-32),340));
                Add(GuiItemKind.Label,3,labelId++,$"CPU wireframe: {_wireframe.DisplayedTriangles}/{_wireframe.TotalTriangles} triangles; cap=10000; reference cube is separate.");
                if (_reportResource != fbxPreview.ResourceIdentity) {
                    _fbxReport = InspectionText.Split(fbxPreview.Report!.Value.GetRawText());
                    _reportResource = fbxPreview.ResourceIdentity; _reportOffset = 0;
                }
                Add(GuiItemKind.Button,7,11,"Previous report page",new("fbx_report_previous"),enabled:_reportOffset>0);
                Line(); Add(GuiItemKind.Button,7,12,"Next report page",new("fbx_report_next"),enabled:_reportOffset+4<_fbxReport.Length);
                Add(GuiItemKind.Label,3,labelId++,$"Full import report: chunks {_reportOffset+1}..{Math.Min(_reportOffset+4,_fbxReport.Length)} / {_fbxReport.Length} (includes skeleton names/parents and warnings)");
                foreach (string chunk in _fbxReport.Skip(_reportOffset).Take(4)) Add(GuiItemKind.Label,3,labelId++,chunk);
            }
        }
        if (animationPreview is not null && preferences?.Current.ShowPreviews!=false) {
            Add(GuiItemKind.Button,9,1,_showAnimation?"Hide Action Animation Lab":"Open Action Animation Lab",new("animation_toggle"));
            if (_showAnimation && animationPreview.Current is { } animation) {
                using var json = JsonDocument.Parse(animation.InspectJson()); var state = json.RootElement;
                var revision = animation.Revision;
                Add(GuiItemKind.Label,3,labelId++,"Isolated C# action preview/history; native ABI 2 samples only; no scene Animator.");
                Add(GuiItemKind.Number,9,2,"Locomotion speed",new("animation_command",Index:1,Operation:revision),number:state.GetProperty("speed").GetDouble(),max:1);
                foreach (var (id,label,command,value,text,enabled) in new (ulong,string,int,double,string,bool)[] {
                    (3,animation.Paused?"Play Preview":"Pause Preview",3,animation.Paused?0:1,"",true),
                    (4,"Animation Step 1/60",4,1.0/60,"",true),(5,"Attack",2,0,"Attack",true),(6,"Dodge",2,0,"Dodge",true),
                    (7,"Reset Animation",5,0,"",true),(8,"Animation Undo",6,0,"",state.GetProperty("can_undo").GetBoolean()),
                    (9,"Animation Redo",7,0,"",state.GetProperty("can_redo").GetBoolean()) })
                    Add(GuiItemKind.Button,9,id,label,new("animation_button",Field:text,Index:command,Operation:(revision,value)),enabled:enabled);
                Add(GuiItemKind.Label,3,labelId++,$"State {state.GetProperty("state").GetString()} time={animation.Time:F3} blend={state.GetProperty("blend_weight").GetDouble():F3}");
                var lines = new List<PreviewLine>(); var bones=state.GetProperty("bones");
                System.Numerics.Vector2 Point(int i) { var p=bones[i].GetProperty("position");return new(.28f+(p[0].GetSingle()+p[2].GetSingle()*.45f)*120/Math.Max(1,center-32),248f/340-(p[1].GetSingle()-p[2].GetSingle()*.15f)*120/340); }
                for(int i=0;i<bones.GetArrayLength();i++) {
                    int parent=bones[i].GetProperty("parent").GetInt32();var point=Point(i);
                    if(parent>=0)lines.Add(new(Point(parent),point,0xFFFF879Au,3));lines.Add(new(point,point,0xFFF5E1DCu,4));
                }
                Canvas(10,CollectionsMarshal.AsSpan(lines));
                foreach (string chunk in InspectionText.Split(state.GetRawText())) Add(GuiItemKind.Label,3,labelId++,chunk);
            }
        }
        if(workspaceStyle){End();if(_geometry.Ai.Width>0)Region(93,"AI 工具",_geometry.Ai,"未接入推理服务");else if(_activeMenu==4)Panel(93,"AI 工具",w-Math.Min(360,w),48,Math.Min(360,w),h-80,headerStatus:"未接入推理服务");}
        if(!workspaceStyle||_geometry.Ai.Width>0||_activeMenu==4){
        if(workspaceStyle){string[] tools=["场景检查","资源检查","脚本工具","性能检查"];for(int i=0;i<tools.Length;i++)Add(GuiItemKind.Button,24,60+(ulong)i,tools[i],enabled:false);}
        Add(GuiItemKind.Label, 3, labelId++, "MCP is off by default. Pairing and approval are human UI actions only.");
        _authorization = _authorizationController.Capture();
        Add(GuiItemKind.Button, 5, 1, _authorization is null ? "Enable local MCP" : "Disable local MCP", new("mcp_toggle"), enabled: projectRoot is not null);
        ulong authId = 100;
        if (_authorization is not null)
        {
            Add(GuiItemKind.Label, 3, labelId++, _authorization.Endpoint.DescriptorPath);
            foreach (var connection in _authorization.Endpoint.Connections)
            {
                Add(GuiItemKind.Label, 3, labelId++, $"{connection.ClientName} | {connection.ConnectionId} | paired={connection.Paired} connected={connection.Connected}");
                if (!connection.Paired && connection.Connected)
                {
                    Add(GuiItemKind.Button, 5, authId++, "Pair this client", new("mcp_pair", connection.ConnectionId));
                    Add(GuiItemKind.Button, 5, authId++, "Reject this client", new("mcp_reject", connection.ConnectionId));
                }
                if (connection.Paired) Add(GuiItemKind.Button, 5, authId++, "Revoke this client", new("mcp_revoke", connection.ConnectionId));
            }
            foreach (var grant in _authorization.Grants) Add(GuiItemKind.Label, 3, labelId++, $"Grant {grant.ConnectionId}: {grant.RemainingSeconds}s; history={grant.AllowHistory}");
            _proposalOffset = Math.Clamp(_proposalOffset,0,Math.Max(0,_authorization.Proposals.Length-1));
            Add(GuiItemKind.Button, 5, 2, "Previous proposal", new("proposal_previous"), enabled: _proposalOffset > 0);
            Add(GuiItemKind.Button, 5, 3, "Next proposal", new("proposal_next"), enabled: _proposalOffset+1 < _authorization.Proposals.Length);
            if (_authorization.Proposals.ElementAtOrDefault(_proposalOffset) is { } proposal)
            {
                if (proposal.Fingerprint != _proposalFingerprint) { _proposalFingerprint = proposal.Fingerprint; _reviewedProposal = false; _allowAgentHistory = false; _agentDeleteConfirmation = ""; }
                // All scope fields and raw input are available, with Unicode-safe bounded chunks.
                foreach (string chunk in Chunks(JsonSerializer.Serialize(proposal.Scope) + "\nRAW INPUT\n" + proposal.Input)) Add(GuiItemKind.Label, 3, labelId++, chunk);
                Add(GuiItemKind.Checkbox, 5, 4, "I reviewed the full input and target scope", new("proposal_reviewed"), number: _reviewedProposal ? 1 : 0, max: 1);
                Add(GuiItemKind.Checkbox, 5, 5, "Allow scoped Undo/Redo for 60 seconds", new("proposal_history"), number: _allowAgentHistory ? 1 : 0, max: 1);
                if (proposal.Scope.DeleteTarget is not null) Add(GuiItemKind.Text, 5, 6, "Confirm exact deletion UUID", new("proposal_delete"), _agentDeleteConfirmation);
                Add(GuiItemKind.Button, 5, 7, "Approve displayed proposal", new("mcp_approve",proposal.Scope.Id,proposal.Fingerprint),
                    enabled: _reviewedProposal && !_page.State.EditBusy && !_page.State.Frozen && !_page.State.HistoryInvalidated &&
                    (proposal.Scope.DeleteTarget is null || Guid.TryParse(_agentDeleteConfirmation,out Guid target) && target == proposal.Scope.DeleteTarget));
            }
        }
        End();
        }
        if(workspaceStyle){Region(94,"状态",_geometry.Status);Add(GuiItemKind.Label,3,labelId++,$"{(_page.State.Dirty?"场景已修改":"场景已保存")} | 对象 {_page.Total} | {_page.Play?.State.ToString()??"Edit"} | Direct3D11");End();WorkspaceMenu(ref labelId,writable);}
        BuildCharacterDebug(w,h,ref labelId);
        BuildGraphReads(w,h,ref labelId);
        if(workspaceStyle)WorkspaceSplitters();
        _frame = new() { StructSize = (uint)Marshal.SizeOf<GuiFrame>(), Frame = frameId, ViewGeneration = _generation,
            DocumentGeneration = _page.Stamp.Generation, Revision = _page.Stamp.Revision, ItemCount = (uint)_items.Count, TextBytes = (uint)_text.Count };
        return _frame;
    }
    // Adds the renderer's opaque target identity to a copied view, never a native pointer.
    public GuiFrame AttachViewport(GuiImageToken token)
    {
        Panel(5,workspaceStyle?"场景视口":"Scene Viewport",Viewport.X-8,Viewport.Y-(workspaceStyle?42:28),Viewport.Width+16,Viewport.Height+(workspaceStyle?50:36));
        _items.Add(GuiItem.Image(15,1,token,Viewport.X,Viewport.Y,Viewport.Width,Viewport.Height,enabled:_page is {State.Frozen:false,State.HistoryInvalidated:false}));
        _actions.Add((15,1),new("viewport"));
        End();_frame.ItemCount=(uint)_items.Count;_frame.TextBytes=(uint)_text.Count;return _frame;
    }
    private void ChooseFile(string operation)
    {
        CancelInteraction();
        var kind=operation=="fbx_browse"?LocalFileKind.OpenFbx:operation=="browse_save"?LocalFileKind.SaveScene:LocalFileKind.OpenScene;
        string? path=filePicker?.Invoke(kind); if(path is null)return;
        if(!Path.IsPathFullyQualified(path) || Encoding.UTF8.GetByteCount(path)>1023)throw new ArgumentException("Invalid selected absolute path.");
        if(kind==LocalFileKind.OpenFbx) {
            fbxPreview!.Import(fbxPreview.Revision,path);_fbxPath=path;_fbxSample=[];
        } else if(kind==LocalFileKind.OpenScene) {
            workspace.Open(workspace.Stamp,path,_discardConfirmed);_path=path;_discardConfirmed=false;
        } else {workspace.Save(workspace.Stamp,path);_path=path;}
    }
    public bool Shortcut(uint key,bool control,bool shift,bool captured,bool focused)
    {
        if(_uiMode&&_ui is not null&&focused&&!captured){
            if(!control&&key==70){_uiController.Focus(Viewport.Width,Viewport.Height);BoundUiZoom();return true;}
            if(control&&key==83){try{if(_ui.HasDraft){_uiPreview?.Prepare(_ui,UiPreviewViewport);_ui.Confirm();}else _ui.ValidateFile();}catch(Exception e){CancelUi();Record(e.Message);}return true;}
            if(control&&key is 90 or 89&&!_ui.HasDraft&&!_uiTest){try{workspace.History(workspace.Stamp,key==89||shift);SynchronizeUi();}catch(Exception e){Record(e.Message);}return true;}
            // Unhandled shortcuts in UI mode must not create/open a scene behind it.
            if(control&&key is 78 or 79){try{CancelUi();string? path=filePicker?.Invoke(key==78?LocalFileKind.SaveUi:LocalFileKind.OpenUi);if(path is not null){_uiPath=Ncma.Assets.Authoring.UiAuthoringSource.ValidatePath(Path.GetRelativePath(projectRoot!,path).Replace('\\','/'));_ui.ReviewFile(workspace.Stamp,_uiPath,key==78);_uiWrite=false;_uiConfirm="";}}catch(Exception e){Record(e.Message);}return true;}
            return false;
        }
        if(!control || captured || !focused || workspace.HasDraft || workspace.Owner.Play is not null)return false;
        if(key is not (78 or 79 or 83 or 90 or 89 or 82) || key==82 && !shift)return false;
        try {
            switch(key) {
                case 78:workspace.New(workspace.Stamp,_discardConfirmed);_discardConfirmed=false;break;
                case 79:ChooseFile("browse_open");break;
                case 83:
                    if(workspace.Owner.Edit!.State.FilePath is null && _path.Length==0)ChooseFile("browse_save");
                    else workspace.Save(workspace.Stamp,_path.Length==0?null:_path);break;
                case 90:workspace.History(workspace.Stamp,shift);break;
                case 89:workspace.History(workspace.Stamp,true);break;
                case 82:if(gameplayAssembly is not null)workspace.Owner.ReloadGameplay(gameplayAssembly);break;
            }
        }catch(Exception e){Record(e.Message);}
        return true;
    }
    public void Apply(ReadOnlySpan<GuiEvent> events, ReadOnlySpan<byte> output)
    {
        foreach (var e in events)
        {
            try
            {
                if (_page is null || e.Frame != _frame.Frame || e.ViewGeneration != _generation || e.ViewGeneration != _frame.ViewGeneration || e.DocumentGeneration != _frame.DocumentGeneration ||
                    e.Revision != _frame.Revision || workspace.Stamp != _page.Stamp || !_actions.TryGetValue((e.WidgetHigh, e.WidgetLow), out var action)) continue;
                if (!_items.Any(item => item.WidgetHigh == e.WidgetHigh && item.WidgetLow == e.WidgetLow && item.Enabled != 0 && item.Kind == e.Kind)) continue;
                if (e.Phase is < 1 or > 3 || !double.IsFinite(e.Value) || e.TextOffset > output.Length || e.TextLength > output.Length - e.TextOffset)
                    throw new ArgumentException("Invalid GUI event.");
                string text = new UTF8Encoding(false, true).GetString(output.Slice((int)e.TextOffset, (int)e.TextLength));
                var stamp = _page.Stamp;
                if(ApplyLayout(action,e))continue;
                if(ApplyUi(action,e,text))continue;
                if (action.Kind is "name" or "transform" or "component" or "component_page" or "render_field" or "export")
                {
                    if (e.Phase == 1) {
                        workspace.BeginDraft(stamp, action.Object, "Edit " + action.Kind);
                        _jsonEdit = action.Kind == "component_page" ? (JsonPageEdit)action.Operation! : null;
                    }
                    else
                    {
                        try { workspace.UpdateDraft(stamp, action.Object, Operation(action, text, e.Value)); }
                        catch (Exception invalid) when (e.Phase == 2 && invalid is ArgumentException or JsonException) { continue; }
                        if (e.Phase == 3) { workspace.CommitDraft(stamp, action.Object); _jsonEdit = null; }
                    }
                    continue;
                }
                if (e.Phase != 3) continue;
                if(ApplyAssetAction(action,e.Value,text,stamp))continue;
                if(ApplyViewportAction(action,e.Value,text,stamp))continue;
                if(ApplyCharacterAction(action,e.Value))continue;
                if(ApplyGraphRead(action,e.Value))continue;
                switch (action.Kind)
                {
                    case "workspace_menu": int nextMenu=_activeMenu==action.Index?-1:action.Index;CancelInteraction();_activeMenu=nextMenu;break;
                    case "workspace_close_menu":_activeMenu=-1;_generation=checked(_generation+1);break;
                    case "workspace_ai":_showAiTools=!_showAiTools;_layoutStore?.Save(_layoutStore.Revision,_layout with{ShowAi=_showAiTools});_activeMenu=-1;_generation=checked(_generation+1);break;
                    case "workspace_files":_showSceneFiles=!_showSceneFiles;_activeMenu=-1;_generation=checked(_generation+1);break;
                    case "workspace_search":_objectSearch=text;_generation=checked(_generation+1);break;
                    case "browser_camera": SceneCamera=Guid.Empty; break;
                    case "scene_camera": SceneCamera=action.Object; break;
                    case "render_refresh": _refreshRenderAssets=true; break;
                    case "browse_open": case "browse_save": case "fbx_browse": ChooseFile(action.Kind); break;
                    case "log_previous":_logOffset=Math.Max(0,_logOffset-8);break;
                    case "log_next":_logOffset+=8;break;
                    case "log_latest":_logOffset=Math.Max(0,(_logEvents.Length-1)/8*8);break;
                    case "preferences_toggle":_showPreferences=!_showPreferences;break;
                    case "preference_theme":
                        if(e.Value!=Math.Floor(e.Value) || e.Value is <0 or >2)throw new ArgumentException("Invalid theme.");
                        _pendingPreferences=_pendingPreferences! with {Theme=e.Value==0?"Dark":e.Value==1?"Light":"Classic"};break;
                    case "preference_side":_pendingPreferences=_pendingPreferences! with {SideWidth=(float)e.Value};break;
                    case "preference_toolbar":_pendingPreferences=_pendingPreferences! with {ToolbarHeight=(float)e.Value};break;
                    case "preference_console":_pendingPreferences=_pendingPreferences! with {ShowConsole=e.Value!=0};break;
                    case "preference_previews":_pendingPreferences=_pendingPreferences! with {ShowPreviews=e.Value!=0};break;
                    case "preference_font":_pendingPreferences=_pendingPreferences! with {FontPath=text};break;
                    case "preference_font_size":_pendingPreferences=_pendingPreferences! with {FontSize=(float)e.Value};break;
                    case "preferences_save":preferences!.Save((ulong)action.Operation!,_pendingPreferences! with {LastScenePath=_path,LastFbxPath=_fbxPath});_pendingPreferences=preferences.Current;break;
                    case "preferences_undo":case "preferences_redo":preferences!.History((ulong)action.Operation!,action.Kind=="preferences_redo");_pendingPreferences=preferences.Current;break;
                    case "animation_toggle":
                        if (!_showAnimation) animationPreview!.Open(); _showAnimation=!_showAnimation; break;
                    case "animation_command": animationPreview!.Current!.Execute((uint)action.Index,e.Value,expectedRevision:(ulong)action.Operation!); break;
                    case "animation_button":
                        var animationArgs = ((ulong Revision,double Value))action.Operation!;
                        animationPreview!.Current!.Execute((uint)action.Index,animationArgs.Value,action.Field,animationArgs.Revision); break;
                    case "fbx_orbit": _fbxYaw = Math.Clamp((float)e.Value,-MathF.PI,MathF.PI); break;
                    case "fbx_report_previous": _reportOffset = Math.Max(0,_reportOffset-4); break;
                    case "fbx_report_next": _reportOffset = Math.Min(Math.Max(0,(_fbxReport.Length-1)/4*4),_reportOffset+4); break;
                    case "fbx_path": _fbxPath = text; break;
                    case "fbx_previous": _fbxClipOffset = Math.Max(0, _fbxClipOffset - 8); break;
                    case "fbx_next": _fbxClipOffset += 8; break;
                    case "fbx_import":
                        fbxPreview!.Import((ulong)action.Operation!, _fbxPath); _fbxSample = []; break;
                    case "fbx_sample":
                        if (fbxPreview!.Revision != (ulong)action.Operation!) throw new InvalidOperationException("stale_preview");
                        _fbxSample = new float[fbxPreview.SampleFloatCount]; fbxPreview.Sample(_fbxSample);
                        var sample = fbxPreview.Capture(); _sampleRevision = sample.Revision; _sampleTime = sample.Time; break;
                    case "fbx_pause": case "fbx_resume": case "fbx_step": case "fbx_clip": case "fbx_undo": case "fbx_redo":
                        var command = action.Kind switch {
                            "fbx_pause" => FbxPreviewCommand.Pause, "fbx_resume" => FbxPreviewCommand.Resume,
                            "fbx_step" => FbxPreviewCommand.Step, "fbx_clip" => FbxPreviewCommand.SelectClip,
                            "fbx_undo" => FbxPreviewCommand.Undo, _ => FbxPreviewCommand.Redo };
                        fbxPreview!.Execute((ulong)action.Operation!, command, clip: action.Index); _fbxSample = []; break;
                    case "path": _path = text; break;
                    case "discard": _discardConfirmed = e.Value != 0; break;
                    case "delete_text": _deleteConfirmation = text; break;
                    case "select": workspace.Select(stamp, action.Object); CancelInteraction(); break;
                    case "previous": CancelInteraction(); _offset = Math.Max(0, _offset - 32); break;
                    case "next": CancelInteraction(); _offset += 32; break;
                    case "json_previous": case "json_next":
                        CancelInteraction(); var key = (action.Object, action.Field);
                        _jsonPageOffsets[key] = Math.Max(0, _jsonPageOffsets.GetValueOrDefault(key) + (action.Kind == "json_next" ? 1 : -1)); break;
                    case "components_previous": CancelInteraction(); _componentOffset=Math.Max(0,_componentOffset-8); break;
                    case "components_next": CancelInteraction(); _componentOffset+=8; break;
                    case "bindings_previous": CancelInteraction(); _bindingOffset=Math.Max(0,_bindingOffset-8); break;
                    case "bindings_next": CancelInteraction(); _bindingOffset+=8; break;
                    case "catalog_previous": CancelInteraction(); _catalogOffset=Math.Max(0,_catalogOffset-8); break;
                    case "catalog_next": CancelInteraction(); _catalogOffset+=8; break;
                    case "exports_page": CancelInteraction(); _exportOffsets[action.Object]=Math.Max(0,_exportOffsets.GetValueOrDefault(action.Object)+action.Index); break;
                    case "create": workspace.CreateObject(stamp); break;
                    case "new": workspace.New(stamp, _discardConfirmed); _discardConfirmed = false; break;
                    case "open": workspace.Open(stamp, _path, _discardConfirmed); _discardConfirmed = false; break;
                    case "save": workspace.Save(stamp, _path.Length == 0 ? null : _path); break;
                    case "undo": workspace.History(stamp, false); break;
                    case "redo": workspace.History(stamp, true); break;
                    case "delete": workspace.Delete(stamp, action.Object, Guid.Parse(_deleteConfirmation)); break;
                    case "transaction": workspace.Transaction(stamp, "Edit component/binding", [action.Operation!], action.Object); break;
                    case "binding": workspace.Transaction(stamp, "Enable binding", [new { op = "set_binding_enabled", objectId = action.Object, bindingId = Guid.Parse(action.Field), enabled = e.Value != 0 }], action.Object); break;
                    case "attach":
                        var type = (Ncma.Scripting.ScriptDescriptor)action.Operation!;
                        var binding = new BehaviourBindingData(Guid.NewGuid(), type.TypeName, true, type.Exports.Select(v => new ExportData(v.Name, (ExportKind)v.Kind, v.DefaultValue)).ToArray());
                        workspace.Transaction(stamp, "Attach Behaviour", [new { op = "add_binding", objectId = action.Object, binding = JsonSerializer.SerializeToElement(binding, DataJson) }], action.Object); break;
                    case "reload": workspace.Reload(stamp, gameplayAssembly!); break;
                    case "mcp_toggle": _authorizationController.Configure(stamp,_authorization is null,projectRoot!); CancelInteraction(); break;
                    case "mcp_pair": _authorizationController.Pair(_authorization!,action.Object,true); break;
                    case "mcp_reject": _authorizationController.Pair(_authorization!,action.Object,false); break;
                    case "mcp_revoke": _authorizationController.Revoke(_authorization!,action.Object); break;
                    case "proposal_previous": _proposalOffset--; CancelInteraction(); _reviewedProposal = false; break;
                    case "proposal_next": _proposalOffset++; CancelInteraction(); _reviewedProposal = false; break;
                    case "proposal_reviewed": _reviewedProposal = e.Value != 0; break;
                    case "proposal_history": _allowAgentHistory = e.Value != 0; break;
                    case "proposal_delete": _agentDeleteConfirmation = text; break;
                    case "mcp_approve":
                        if (!_reviewedProposal) throw new EditRejectedException("proposal_review_required");
                        _authorizationController.Approve(_authorization!,action.Object,action.Field,_allowAgentHistory,
                            Guid.TryParse(_agentDeleteConfirmation,out Guid deletion) ? deletion : null); _reviewedProposal = false; break;
                    default: workspace.PlayControl(stamp, action.Kind); break;
                }
                if(workspaceStyle&&e.WidgetHigh==25){_activeMenu=-1;_generation=checked(_generation+1);}
            }
            // Trusted user constructors/lifecycle callbacks may throw arbitrary managed errors.
            // Keep their failure visible in the editor; do not recover fatal process corruption.
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
            { CancelInteraction(); Record(error is EditRejectedException rejected ? rejected.Code : error.Message); }
        }
    }
    private static string Short(string value) => string.Concat(value.EnumerateRunes().Take(100).Select(r => r.ToString()));
    private static IEnumerable<string> Chunks(string text)
    {
        var part = new StringBuilder(); int count = 0;
        foreach (var rune in text.EnumerateRunes()) { part.Append(rune); if (++count == 100) { yield return part.ToString(); part.Clear(); count = 0; } }
        if (part.Length != 0) yield return part.ToString();
    }
    private object Operation(ActionView action, string text, double number)
    {
        if (action.Kind == "name") return new { op = "rename", objectId = action.Object, name = text };
        if (action.Kind == "render_field")
        {
            var renderComponent = _page!.Selected!.Components.Single(c => c.TypeId == RenderConfiguration.ComponentType);
            var renderNode = JsonNode.Parse(renderComponent.Data.GetRawText())!;
            renderNode[action.Field] = action.Field is "replaceToneStage" or "shadowEnabled" or "contactEnabled" ? JsonValue.Create(number != 0) :
                action.Field is "shadowFilter" or "contactSteps" ? JsonValue.Create(checked((int)Math.Round(number))) : JsonValue.Create((float)number);
            return new { op = "set_component", objectId = action.Object, typeId = RenderConfiguration.ComponentType,
                version = renderComponent.Version, data = JsonSerializer.SerializeToElement(renderNode) };
        }
        if (action.Kind == "component_page")
        {
            var edit = _jsonEdit ?? throw new EditRejectedException("draft_target_mismatch");
            if (edit.Object != action.Object || edit.Type != action.Field) throw new EditRejectedException("draft_target_mismatch");
            return new { op = "set_component", objectId = action.Object, typeId = action.Field, version = action.Index,
                data = JsonSerializer.Deserialize<JsonElement>(edit.Pages.Replace(edit.Page, text)) };
        }
        if (action.Kind == "component") return new { op = "set_component", objectId = action.Object, typeId = action.Field, version = action.Index, data = JsonSerializer.Deserialize<JsonElement>(text) };
        if (action.Kind == "export")
        {
            int slash = action.Field.IndexOf('/');
            return new { op = "set_export", objectId = action.Object, bindingId = Guid.Parse(action.Field[..slash]), name = action.Field[(slash + 1)..], kind = action.Index,
                value = action.Index == (int)ExportKind.Integer ? Math.Round(number) : number };
        }
        var component = _page!.Selected!.Components.Single(c => c.TypeId == "ncma.transform");
        var node = JsonNode.Parse(component.Data.GetRawText())!;
        string group = action.Index < 3 ? "position" : action.Index < 7 ? "rotation" : "scale";
        string coordinate = "xyzw"[action.Index < 3 ? action.Index : action.Index < 7 ? action.Index - 3 : action.Index - 7].ToString();
        node[group]![coordinate] = number;
        return new { op = "set_component", objectId = action.Object, typeId = "ncma.transform", version = 1, data = JsonSerializer.SerializeToElement(node) };
    }
}
