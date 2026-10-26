using Ncma.Editor.Services;
using Ncma.Gui;
#if NCMA_AI
using System.Text;
using System.Text.Json;
using Ncma.AI;
using Ncma.Editor.Core;
using Ncma.Editor.Modules;
#endif

namespace Ncma.Editor.App;

internal sealed unsafe partial class EditorPresenter
{
    private bool AiAuthoringModal =>
#if NCMA_AI
        _showAiAuthoring;
#else
        false;
#endif
    private string AiHeaderStatus =>
#if NCMA_AI
        _aiAuthoring is null ? "打开项目后可配置" : _aiAuthoring.Status switch {
            "unconfigured" => "未配置", "configured-not-connected" or "idle" => "已配置 · 未验证连接",
            "send-review" => "待外发审批", "pending" => "请求进行中", "review" => "待差异审阅",
            "resource-review" => "待动画资源审阅",
            "diagnostic-review" => "待诊断外发批准", "diagnostic-pending" => "单次诊断进行中",
            "diagnostic-complete" => "本次协议回包通过", "diagnostic-failed" => "本次诊断未通过",
            "diagnostic-draining" => "诊断取消回收中", "diagnostic-cancelled" => "诊断已取消",
            "draining" => "取消回收中", "closed" => "已关闭", "cancelled" => "已取消",
            _ => "请刷新诊断"
        };
#else
        "未包含 AI 模块";
#endif
    private void AiMenuEntry()
    {
#if NCMA_AI
        Add(GuiItemKind.Button, 24, 75, "模型工作区 / 连接与完整审批", new("aiui_toggle"), enabled: _aiAuthoring is not null);
#else
        Add(GuiItemKind.Label, 24, 75, "默认产品无模型模块；需单独构建 AI 可选产品。");
#endif
    }
#if NCMA_AI
    private AiAuthoringWorkspace? _aiAuthoring;
    private readonly WindowsAiCredentialPrompt _aiCredentialPrompt = new();
    private bool _aiRemote;
    private bool _showAiAuthoring, _aiReviewed, _aiBindings;
    private string _aiEndpoint = "http://127.0.0.1:8000/v1/chat/completions", _aiModel = "", _aiInstruction = "", _aiDelete = "";
    private AiAuthoringIntent _aiIntent = AiAuthoringIntent.Scene;
    private AiDomainRequestReview? _aiSend;
    private DomainProposalReview? _aiResult;
    private AiAnimationResourceReview? _aiResources;
    private MetadataContentReview? _aiMetadata;
    private AiConnectionDiagnosticReview? _aiConnectionReview;
    private string _aiResourceGraph = "";
    private string[] _aiPages = [];
    private int _aiPage;
    private readonly HashSet<int> _aiSeen = [];
    public void AttachAiAuthoring(AiAuthoringWorkspace controller) => _aiAuthoring = controller;
    private void ClearAiReview(bool cancel)
    {
        if (cancel) _aiAuthoring?.Cancel();
        _aiSend = null; _aiResult = null; _aiPages = []; _aiPage = 0; _aiSeen.Clear(); _aiReviewed = _aiBindings = false; _aiDelete = "";
        _aiResources = null; _aiResourceGraph = ""; _aiMetadata = null; _aiConnectionReview = null;
    }
    private void AiPages(string text)
    {
        _aiPages = InspectionText.Split(text, 1024 * 1024); _aiPage = 0; _aiSeen.Clear(); _aiReviewed = _aiBindings = false; _aiDelete = "";
    }
#endif
    private void BuildAiAuthoring(float width, float height, ref ulong label)
    {
#if NCMA_AI
        if (!_showAiAuthoring || _aiAuthoring is not { } ai) return;
        Panel(415, "模型工作区 · 本机 / DeepSeek远程 · 显式审批", width * .05f, height * .06f, width * .9f, height * .88f, AiHeaderStatus);
        var overlay = _items[^1]; overlay.Value = 2; _items[^1] = overlay;
        Add(GuiItemKind.Label, 81, label++, "无自动启动/下载/连接探测；配置不等于已连接。元数据本地查看不构成外发授权；选用settings草稿后须独立审阅完整请求。动画仍要求原MCP图读授权，不自动配对。请勿把密钥输入普通文本框。");
        Add(GuiItemKind.Button, 81, 19, _aiRemote ? "本机无凭据" : "> 本机无凭据", new("aiui_local")); Line();
        Add(GuiItemKind.Button, 81, 20, _aiRemote ? "> DeepSeek远程" : "DeepSeek远程", new("aiui_remote"));
        if (_aiRemote) Add(GuiItemKind.Label, 81, label++, $"固定接收方 {AiProvider.DeepSeek.RemoteEndpoint}\n配置按钮打开Windows凭据对话框：用户名填写deepseek，密码栏填写API密钥，不是Windows密码。仅内存会话、不保存；取消后保持断开。不是安全桌面，不能保证托管内存物理擦除。");
        else Add(GuiItemKind.Text, 81, 1, "本机 literal-loopback 端点（无凭据）", new("aiui_endpoint"), _aiEndpoint);
        Add(GuiItemKind.Text, 81, 2, "精确模型名称（由用户填写）", new("aiui_model"), _aiModel);
        Add(GuiItemKind.Button, 81, 3, _aiRemote ? "输入会话密钥并配置（不联网）" : "应用配置（不联网；旧会话须排空）", new("aiui_configure"));
        Add(GuiItemKind.Button, 81, 4, "撤销凭据与批准 / 断开 / 重试回收", new("aiui_disconnect"));
        Add(GuiItemKind.Label, 81, label++, "断开立即撤销本机会话批准，但不能收回已发送内容，也不会吊销服务商账号中的API密钥；服务商密钥撤销需自行操作。");
        if (ai.Profile is { } profile) Add(GuiItemKind.Label, 81, label++, $"实际配置：{profile.Endpoint}\n模型 {profile.Model} · {profile.ConfigurationId:D}\n最多1次调用/4096以内输出预算，60秒上限，无自动重试。界面输入与实际配置分开，修改后须重新应用。");
        Add(GuiItemKind.Button, 81, 5, _aiIntent == AiAuthoringIntent.Scene ? "> 场景所选对象" : "场景所选对象", new("aiui_scene")); Line();
        Add(GuiItemKind.Button, 81, 6, _aiIntent == AiAuthoringIntent.UiDocument ? "> 当前UI文档" : "当前UI文档", new("aiui_ui"), enabled: ai.UiAvailable);
        Add(GuiItemKind.Button, 81, 21, _aiIntent == AiAuthoringIntent.AnimationGraph ? "> 当前动画图" : "当前动画图", new("aiui_animation"), enabled: ai.AnimationAvailable);
        Add(GuiItemKind.Button, 81, 25, "查看所选资产完整元数据（仅本机）", new("aiui_metadata_local", _selectedAsset?.AssetId ?? Guid.Empty),
            enabled: ai.MetadataContentAvailable && _selectedAsset is not null);
        Add(GuiItemKind.Button, 81, 27, _aiIntent == AiAuthoringIntent.AssetMetadata ? "> 所选资产settings草稿" : "所选资产settings草稿", new("aiui_metadata"), enabled: ai.MetadataContentAvailable);
        Add(GuiItemKind.Label, 81, label++, "元数据只支持无generation/subassets/dependencies的根资产。按精确UUID重新读取，不使用浏览器缓存；外发审阅会包含完整record/相对路径/哈希，可能含业务数据，无源blob。选择不是批准，不授予MCP读取或文件写入权限；模型仅返回settings，最终写入仍需原资产授权。");
        Guid target = _aiIntent switch { AiAuthoringIntent.Scene => _page!.State.Selection ?? Guid.Empty,
            AiAuthoringIntent.AnimationGraph => _graphAuthor?.Stamp.Graph ?? Guid.Empty,
            AiAuthoringIntent.AssetMetadata => _selectedAsset?.AssetId ?? Guid.Empty, _ => _ui?.DocumentId ?? Guid.Empty };
        if (_aiIntent == AiAuthoringIntent.AnimationGraph) Add(GuiItemKind.Label, 81, label++, "本入口仅发送当前图摘要，默认模板为重命名，不自动读取全图/骨骼/资源。请先在原动画面板完成配对读授权；本地未提交草稿须先处理。保存仍受原文件/资源授权控制；收到草稿后须显式准备/审阅精确资源，再单独确认最终差异。");
        Add(GuiItemKind.Label, 81, label++, $"精确目标：{target:D}。不自动选择其他对象或读取全项目。");
        Add(GuiItemKind.Text, 81, 7, "任务（界面≤1023 UTF-8字节）", new("aiui_instruction"), _aiInstruction);
        bool writable = _page is { State.Frozen: false, State.EditBusy: false, State.HistoryInvalidated: false };
        bool applied = ai.Profile is { } appliedProfile && appliedProfile.Model == _aiModel &&
            (_aiRemote ? appliedProfile.Deployment == AiDeployment.RemoteHttps : appliedProfile.Deployment == AiDeployment.LocalLoopback && appliedProfile.Endpoint == _aiEndpoint);
        Add(GuiItemKind.Button, 81, 28, "准备单次连接诊断审阅（不发送）", new("aiui_diagnostic_prepare"), enabled: applied);
        Add(GuiItemKind.Label, 81, label++, "连接诊断只发送下方固定正文和模型名称，无项目/场景/资产内容；最多1次、64输出token、10秒、16KiB响应，无自动重试。可能计费，密钥仅在认证头发送。成功只代表本次协议回包，不保证持续可用或编辑任务正确。");
        Add(GuiItemKind.Button, 81, 8, "生成完整外发审阅（不发送）", new("aiui_prepare", target), enabled: writable && applied && target != Guid.Empty && _aiInstruction.Length != 0);
        Add(GuiItemKind.Button, 81, 9, "取消未执行请求 / 提案", new("aiui_cancel"));
        Add(GuiItemKind.Button, 81, 10, "显式刷新完整差异", new("aiui_refresh"), enabled: writable && ai.Status == "review");
        Add(GuiItemKind.Button, 81, 22, "显式准备并查看动画资源（本机读取，不写文件）", new("aiui_resource_review"), enabled: writable && ai.Status == "resource-review");
        Add(GuiItemKind.Label, 81, label++, "显示的是显式捕获的缓存页，不每帧重读文档。发送/提交时重新校验，失效不会自动重试。MCP与外发/文件/资源/写审批相互独立。");
        if (_aiPages.Length != 0) {
            if (_aiMetadata is { } metadata) {
                Add(GuiItemKind.Label, 81, label++, ai.MetadataContentCurrent(metadata) ? "本地元数据缓存 · 身份有效（文件需显式重校验）" : "本地元数据已失效 · 请重新选择捕获");
                Add(GuiItemKind.Button, 81, 26, "重新校验原文件与版本（不发送/不写入）", new("aiui_metadata_validate", Operation: metadata), enabled: ai.MetadataContentCurrent(metadata));
            }
            Add(GuiItemKind.Label, 81, label++, $"完整审阅 {_aiPage + 1}/{_aiPages.Length}");
            Add(GuiItemKind.Label, 81, label++, _aiPages[_aiPage]); _aiSeen.Add(_aiPage);
            Add(GuiItemKind.Button, 81, 11, "上一页", new("aiui_previous"), enabled: _aiPage > 0); Line();
            Add(GuiItemKind.Button, 81, 12, "下一页", new("aiui_next"), enabled: _aiPage + 1 < _aiPages.Length);
            bool all = _aiSeen.Count == _aiPages.Length;
            if (_aiMetadata is null) Add(GuiItemKind.Checkbox, 81, 13, "已审阅全部页、接收方、完整内容及风险", new("aiui_reviewed"), number: _aiReviewed ? 1 : 0, max: 1, enabled: all);
            if (_aiResources is { } resources) {
                Add(GuiItemKind.Label, 81, label++, $"资源批准仅针对图 {resources.Graph:D}、显示的完整候选/路径/依赖hash/受众，不会提交文件。之后还需重新审阅原事务完整差异。");
                Add(GuiItemKind.Text, 81, 23, "另行输入确认的动画图UUID", new("aiui_resource_graph"), _aiResourceGraph);
                Add(GuiItemKind.Button, 81, 24, "批准此动画资源并生成原事务差异（不提交）",
                    new("aiui_resource_approve", Field: resources.Fingerprint, Operation: resources),
                    enabled: writable && all && _aiReviewed && Guid.TryParse(_aiResourceGraph, out var exact) && exact == resources.Graph);
            }
            if (_aiSend is { } send) {
                Add(GuiItemKind.Label, 81, label++, "本机/远程服务可能转发或计费。批准仅针对本次完整外发，不批准写入；无隐藏上下文。远程请求另含会话密钥Authorization认证头，密钥不显示在正文审阅页中。");
                Add(GuiItemKind.Button, 81, 14, "允许外发并调用一次", new("aiui_send", Field: send.Observation.Fingerprint, Operation: send), enabled: writable && all && _aiReviewed);
            }
            if (_aiConnectionReview is { } connectionReview) {
                Add(GuiItemKind.Button, 81, 29, "批准固定诊断外发与可能费用，调用一次", new("aiui_diagnostic_send", Field: connectionReview.Fingerprint, Operation: connectionReview),
                    enabled: all && _aiReviewed);
            }
            if (_aiResult is { } result) {
                if (result.DestructiveTarget is not null) Add(GuiItemKind.Text, 81, 15, "另行确认删除UUID", new("aiui_delete"), _aiDelete);
                if (result.CopyImpact().Bindings.Length != 0) Add(GuiItemKind.Checkbox, 81, 16, "另行确认C#绑定变更（仍需原信任）", new("aiui_bindings"), number: _aiBindings ? 1 : 0, max: 1);
                Add(GuiItemKind.Button, 81, 17, "确认差异并交给原事务一次", new("aiui_confirm", Field: result.Fingerprint, Operation: result), enabled: writable && all && _aiReviewed);
            }
        }
        if (ai.Diagnostic is { } diagnostic) Add(GuiItemKind.Label, 81, label++, $"调用诊断 {diagnostic.Code} | HTTP {diagnostic.HttpStatus} | {diagnostic.ElapsedMilliseconds:F0}ms | 发送 {diagnostic.RequestBytes}B / 收到 {diagnostic.ResponseBytes}B\n用量 {(diagnostic.Usage is { } usage ? usage.TotalTokens.ToString() : "未知（服务未报告）")}，不推算费用。");
        if (workspace.Owner.DomainProposals is { } domains)
            foreach (var r in domains.Receipts.TakeLast(4)) Add(GuiItemKind.Label, 81, label++, $"原事务 {r.ProposalId:D}: {r.Status}/{r.Code} changed={r.Changed} revision={r.ExecutionRevision} recovery={r.RecoveryRequired}");
        Add(GuiItemKind.Label, 81, label++, "提交后从原编辑菜单 Undo/Redo；取消不撤销已提交修改。故障恢复仍使用原事务流程。");
        Add(GuiItemKind.Button, 81, 18, "关闭面板（取消未执行请求）", new("aiui_close")); End();
#endif
    }
    private bool ApplyAiAuthoring(ActionView action, double value, string text)
    {
#if NCMA_AI
        if (!action.Kind.StartsWith("aiui_", StringComparison.Ordinal)) return false;
        if (_aiAuthoring is not { } ai) return true;
        try {
            switch (action.Kind) {
                case "aiui_toggle": ClearAiReview(true); _showAiAuthoring = !_showAiAuthoring; _showDomains = _showWorkflows = false; _activeMenu = -1; break;
                case "aiui_close": ClearAiReview(true); _showAiAuthoring = false; break;
                case "aiui_endpoint": case "aiui_model": case "aiui_instruction":
                    if (Encoding.UTF8.GetByteCount(text) > 1023) throw new ArgumentException();
                    ClearAiReview(true);
                    if (action.Kind == "aiui_endpoint") _aiEndpoint = text; else if (action.Kind == "aiui_model") _aiModel = text; else _aiInstruction = text; break;
                case "aiui_local": case "aiui_remote":
                    ClearAiReview(true); _aiRemote = action.Kind == "aiui_remote";
                    if (!ai.Disconnect()) Record("ai_draining_retry_explicitly"); break;
                case "aiui_configure":
                    ClearAiReview(true);
                    bool configured = _aiRemote ? ai.ConfigureRemote(_aiModel, _aiCredentialPrompt.Acquire) : ai.ConfigureLocal(_aiEndpoint, _aiModel);
                    if (!configured) Record(ai.Status == "draining" ? "ai_draining_retry_explicitly" : "ai_configuration_cancelled");
                    else { if (!_aiRemote) _aiEndpoint = ai.Profile!.Endpoint; _aiModel = ai.Profile!.Model; } break;
                case "aiui_disconnect": ClearAiReview(true); if (!ai.Disconnect()) Record("ai_draining_retry_explicitly"); break;
                case "aiui_scene": case "aiui_ui": case "aiui_animation": case "aiui_metadata":
                    ClearAiReview(true); _aiIntent = action.Kind switch { "aiui_scene" => AiAuthoringIntent.Scene, "aiui_animation" => AiAuthoringIntent.AnimationGraph, "aiui_metadata" => AiAuthoringIntent.AssetMetadata, _ => AiAuthoringIntent.UiDocument }; break;
                case "aiui_prepare":
                    ClearAiReview(true);
                    if (_aiIntent == AiAuthoringIntent.AssetMetadata && (_selectedAsset is null || _selectedAsset.AssetId != action.Object)) throw new EditRejectedException("metadata_selection_stale");
                    var send = _aiIntent == AiAuthoringIntent.AssetMetadata ? ai.PrepareMetadata(action.Object, _selectedAsset!.Kind, _aiInstruction) : ai.Prepare(_aiIntent, action.Object, _aiInstruction);
                    AiPages(JsonSerializer.Serialize(send.Observation.Target) + "\n完整逻辑请求\n" + Encoding.UTF8.GetString(send.Observation.CopyBytes()) +
                        "\n完整HTTP JSON正文（不含认证头）\n" + Encoding.UTF8.GetString(send.WireRequest.CopyBytes()) +
                        $"\n正文SHA256 {send.WireRequest.Sha256}\n审批指纹 {send.Observation.Fingerprint}"); _aiSend = send; break;
                case "aiui_diagnostic_prepare":
                    ClearAiReview(true); var diagnosticReview = ai.PrepareConnectionDiagnostic();
                    AiPages(JsonSerializer.Serialize(new { diagnosticReview.RequestId, diagnosticReview.Endpoint, diagnosticReview.Model,
                        diagnosticReview.ConfigurationId, diagnosticReview.MaximumOutputTokens, diagnosticReview.TimeoutMilliseconds,
                        maximumCalls = 1, maximumResponseBytes = AiConnectionDiagnosticSession.MaximumResponseBytes, automaticRetries = 0 }) +
                        "\n完整HTTP正文（认证头密钥不显示）\n" + Encoding.UTF8.GetString(diagnosticReview.Body.CopyBytes()) +
                        $"\n正文SHA256 {diagnosticReview.Body.Sha256}\n审批指纹 {diagnosticReview.Fingerprint}");
                    _aiConnectionReview = diagnosticReview; break;
                case "aiui_diagnostic_send":
                    RequireAiPages(); if (!ReferenceEquals(_aiConnectionReview, action.Operation)) throw new ArgumentException();
                    ai.BeginConnectionDiagnostic(_aiConnectionReview!, action.Field, _aiReviewed); ClearAiReview(false); break;
                case "aiui_metadata_local":
                    ClearAiReview(true);
                    if (_selectedAsset is not { } selected || selected.AssetId != action.Object) throw new EditRejectedException("metadata_selection_stale");
                    var metadata = ai.ReviewMetadataContent(action.Object, selected.Kind);
                    AiPages("仅本机完整元数据；不构成外发/写入批准\n" + Encoding.UTF8.GetString(metadata.CopyBytes())); _aiMetadata = metadata; break;
                case "aiui_metadata_validate":
                    if (!ReferenceEquals(_aiMetadata, action.Operation)) throw new ArgumentException();
                    ai.ValidateMetadataContent(_aiMetadata!); Record("metadata_local_content_revalidated_not_exported"); break;
                case "aiui_previous": _aiPage = Math.Max(0, _aiPage - 1); break;
                case "aiui_next": _aiPage = Math.Min(_aiPages.Length - 1, _aiPage + 1); break;
                case "aiui_reviewed": RequireAiPages(); if (value is not (0 or 1)) throw new ArgumentException(); _aiReviewed = value == 1; break;
                case "aiui_bindings": if (value is not (0 or 1)) throw new ArgumentException(); _aiBindings = value == 1; break;
                case "aiui_delete": _aiDelete = text; break;
                case "aiui_send": RequireAiPages(); if (!ReferenceEquals(_aiSend, action.Operation)) throw new ArgumentException(); ai.Send(_aiSend!, action.Field, _aiReviewed); ClearAiReview(false); break;
                case "aiui_resource_review":
                    ClearAiReview(false); var resources = ai.RefreshAnimationResources();
                    AiPages("完整动画候选与原资源审阅\n" + Encoding.UTF8.GetString(resources.CopyBytes())); _aiResources = resources; break;
                case "aiui_resource_graph": _aiResourceGraph = text; break;
                case "aiui_resource_approve":
                    RequireAiPages(); if (!ReferenceEquals(_aiResources, action.Operation) || !Guid.TryParse(_aiResourceGraph, out var graph)) throw new ArgumentException();
                    var resourceResult = ai.ApproveAnimationResources(_aiResources!, action.Field, graph, _aiReviewed);
                    ShowAiResult(resourceResult); break;
                case "aiui_refresh":
                    var result = ai.RefreshReview() ?? throw new EditRejectedException("ai_result_missing_or_stale");
                    ShowAiResult(result); break;
                case "aiui_confirm":
                    RequireAiPages(); if (!ReferenceEquals(_aiResult, action.Operation)) throw new ArgumentException();
                    var receipt = ai.Confirm(_aiResult!, action.Field, _aiReviewed, Guid.TryParse(_aiDelete, out var id) ? id : null, _aiBindings);
                    ClearAiReview(false); if (receipt.Status != "ok") Record("ai_original_transaction_rejected"); break;
                case "aiui_cancel": ClearAiReview(true); break;
                default: throw new ArgumentException();
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            // Never copy model/user content, endpoints, response bodies or arbitrary exception text into logs.
            ClearAiReview(false); Record("ai_action_rejected_refresh_or_cancel");
        }
        finally { _generation = checked(_generation + 1); }
        return true;
#else
        return false;
#endif
    }
#if NCMA_AI
    private void ShowAiResult(DomainProposalReview result) {
        ClearAiReview(false);
        AiPages(JsonSerializer.Serialize(new { result.Source, result.DomainCapability, result.DestructiveTarget, impact = result.CopyImpact() }) +
            "\n原输入\n" + Encoding.UTF8.GetString(result.CopyPayload()) + "\n完整差异\n" + Encoding.UTF8.GetString(result.CopyChanges()) + "\n" + result.Fingerprint);
        _aiResult = result;
    }
    private void RequireAiPages() {
        if (_aiPages.Length == 0 || _aiSeen.Count != _aiPages.Length) throw new EditRejectedException("ai_review_incomplete");
    }
#endif
}
