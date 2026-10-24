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
    private Guid _rowDocument;
    private ulong _rowRevision;
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
    internal bool SceneHudInputAllowed => !UiWorkspaceActive && !GraphWorkspaceActive && _activeMenu < 0 && !_showPreferences && !_showSceneFiles && !_showKeepPlay && _layoutBefore is null;
    public SceneObjectData? Selected => _page?.Selected;
    public Guid SceneCamera { get; private set; }
    private bool _refreshRenderAssets;
    public bool ConsumeRenderAssetRefresh() { bool pending=_refreshRenderAssets; _refreshRenderAssets=false; return pending; }
    public void SelectStartupCamera(Guid? camera) => SceneCamera=camera??Guid.Empty;
    private string[] _renderDiagnostics=[];
    public void ShowRenderDiagnostics(IReadOnlyList<Ncma.Scene.Rendering.SceneRenderDiagnostic>? diagnostics) {
        _renderDiagnostics=diagnostics?.Take(4).Select(d=>$"Render: {d.Code}; object={d.ObjectId}; asset={d.AssetId}").ToArray()??[];
    }
    public void CancelInteraction() { if(_layoutBefore is not null){_layout=_layoutBefore;_layoutBefore=null;}CancelGraph();CancelUi();CancelEnvironmentReview();CancelPostReview();ClearKeepPlayReview();workspace.CancelDraft(); _jsonEdit = null; _activeMenu = -1; _generation = checked(_generation + 1); }
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
        int labelLength = Encoding.UTF8.GetByteCount(label), valueLength = Encoding.UTF8.GetByteCount(value);
        if (labelLength > 4096 || valueLength > 1023 || _items.Count >= 8192 || _text.Count + labelLength + valueLength + (_items.Count+1)*96 > 2*1024*1024)
            throw new ArgumentException("Editor view budget exceeded.");
        int labelOffset = _text.Count, valueOffset = labelOffset + labelLength;
        var item = new GuiItem { Kind = (uint)kind, Enabled = enabled ? 1u : 0u, WidgetHigh = high, WidgetLow = low,
            LabelOffset = (uint)labelOffset, LabelLength = (uint)labelLength, TextOffset = (uint)valueOffset,
            TextLength = (uint)valueLength, Value = number, Minimum = min, Maximum = max };
        // Preflight byte limits before growing. Fill every new byte before publishing the item;
        // the default UTF-8 replacement fallback is identical to the former GetBytes(string) path.
        CollectionsMarshal.SetCount(_text, valueOffset + valueLength);
        Span<byte> text = CollectionsMarshal.AsSpan(_text);
        Encoding.UTF8.GetBytes(label.AsSpan(), text.Slice(labelOffset, labelLength));
        Encoding.UTF8.GetBytes(value.AsSpan(), text.Slice(valueOffset, valueLength));
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
            var document = workspace.Owner.Document;
            if (_rowDocument != document.SessionId || _rowRevision != document.Revision)
            {
                foreach (var id in _rowIds.Keys.Where(id => !document.ContainsObject(id)).ToArray()) _rowIds.Remove(id);
                _rowDocument = document.SessionId; _rowRevision = document.Revision;
            }
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
        if(workspaceStyle&&_graphMode&&_graphAuthor is not null)return BuildGraphEditor(frameId);
        // User-approved slice changes only the header. Preserve side/status/viewport geometry,
        // existing preference field meanings and the remaining scene controls below the header.
        ulong labelId = 10000;
        if(!workspaceStyle||_showSceneFiles){
            BuildSceneFileControls(_page, w, toolbar, header, writable, ref labelId);
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
            BuildSelectedInspector(selected, catalog, writable, ref labelId);
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
            BuildLocalPreferences(preferences, prefs, ref labelId);
        }
        if (fbxPreview is not null && preferences?.Current.ShowPreviews!=false && (!workspaceStyle||_activeMenu==2))
        {
            BuildFbxPreview(fbxPreview, center, ref labelId);
        }
        if (animationPreview is not null && preferences?.Current.ShowPreviews!=false) {
            Add(GuiItemKind.Button,9,1,_showAnimation?"Hide Action Animation Lab":"Open Action Animation Lab",new("animation_toggle"));
            if (_showAnimation && animationPreview.Current is { } animation) {
                BuildActionPreview(animation, center, ref labelId);
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
        BuildAnimationRuntime(w,h,ref labelId);
        BuildRuntimeInspection(w,h,ref labelId);
        if (_showKeepPlay) BuildKeepPlayChanges(w,h,ref labelId);
        BuildWorkflows(w,h,ref labelId);
        if(workspaceStyle)WorkspaceSplitters();
        _frame = new() { StructSize = (uint)Marshal.SizeOf<GuiFrame>(), Frame = frameId, ViewGeneration = _generation,
            DocumentGeneration = _page.Stamp.Generation, Revision = _page.Stamp.Revision, ItemCount = (uint)_items.Count, TextBytes = (uint)_text.Count };
        return _frame;
    }
    // Adds the renderer's opaque target identity to a copied view, never a native pointer.
    public GuiFrame AttachViewport(GuiImageToken token,GuiCachedImageToken? hud = null)
    {
        Panel(5,workspaceStyle?"场景视口":"Scene Viewport",Viewport.X-8,Viewport.Y-(workspaceStyle?42:28),Viewport.Width+16,Viewport.Height+(workspaceStyle?50:36));
        _items.Add(GuiItem.Image(15,1,token,Viewport.X,Viewport.Y,Viewport.Width,Viewport.Height,enabled:_page is {State.Frozen:false,State.HistoryInvalidated:false}));
        _actions.Add((15,1),new("viewport"));
        if (hud is {} image) {
            Add(GuiItemKind.OverlayBegin,15,2,"");
            var clip=_items[^1];clip.Rect[0]=Viewport.X;clip.Rect[1]=Viewport.Y;clip.Rect[2]=Viewport.Width;clip.Rect[3]=Viewport.Height;_items[^1]=clip;
            _items.Add(GuiItem.CachedImage(15,3,image,Viewport.X,Viewport.Y,Viewport.Width,Viewport.Height));
            Add(GuiItemKind.CanvasEnd,15,4,"");
        }
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
        if (_showKeepPlay) return false; // Never operate a hidden scene behind the confirmation panel.
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
                if(ApplyGraphEditor(action,e,text))continue;
                if (action.Kind is "name" or "transform" or "component" or "component_page" or "render_field" or "environment_field" or "post_field" or "export")
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
                if(ApplyEnvironmentAction(action,e.Value))continue;
                if(ApplyPostAction(action,e.Value))continue;
                if(ApplyAssetAction(action,e.Value,text,stamp))continue;
                if(ApplyViewportAction(action,e.Value,text,stamp))continue;
                if(ApplyCharacterAction(action,e.Value))continue;
                if(ApplyGraphRead(action,e.Value))continue;
                if(ApplyAnimationRuntime(action,e.Value))continue;
                if(ApplyRuntimeInspection(action,e.Value))continue;
                if(ApplyKeepPlayChanges(action,e.Value))continue;
                if(ApplyWorkflow(action,e.Value))continue;
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
        if(action.Kind=="post_field") {
            var modules=workspace.Owner.PostProcessing!.Modules;
            return RenderingEditorAdapter.PostOperation(modules,action.Object,RenderingEditorAdapter.PostField(modules,PostValue(),action.Field,number));
        }
        if (action.Kind == "environment_field")
            return RenderingEditorAdapter.EnvironmentFieldOperation(action.Object,
                _page!.Selected!.Components.Single(c => c.TypeId == Ncma.Scene.Rendering.EnvironmentLightingData.TypeId).Data.Deserialize<Ncma.Scene.Rendering.EnvironmentLightingData>(DataJson), action.Field, number);
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
