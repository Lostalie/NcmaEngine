using System.Text.Json;
using Ncma.Animation;
using Ncma.Editor.Services;
using Ncma.Editor.Core;
using Ncma.Gui;
using Ncma.Platform;

namespace Ncma.Editor.App;

internal sealed unsafe partial class EditorPresenter
{
    private AnimationGraphInspections? _graphs;
    private bool _showGraphReads, _graphReviewed;
    private int _graphPage;
    private string[] _graphReviewText = [];
    private AnimationGraphReadReview? _graphReview;
    internal void AttachGraphReads(AnimationGraphInspections service) => _graphs = service;
    private void BuildGraphReads(float width, float height, ref ulong labelId)
    {
        if (!_showGraphReads || _graphs is null) return;
        Panel(301, "动画图检查 / 本机审批（非节点编辑器）", width * .12f, height * .12f, width * .76f, height * .75f);
        Add(GuiItemKind.Button, 40, 1, "本机选择并读取 .ncmaanim", new("graph_open"), enabled: filePicker is not null && projectRoot is not null && !_page!.State.Frozen && !_page.State.EditBusy);
        var summary = _graphs.LocalSummary;
        if (summary.Id != Guid.Empty) {
            Add(GuiItemKind.Label, 3, labelId++, $"{summary.Name}: {summary.Id:D}\n{summary.Relative}\nSHA256 {summary.Hash}");
            Add(GuiItemKind.Label, 3, labelId++, "只验证严格图结构；资源未准备，当前场景/Play未接入图执行。");
            Add(GuiItemKind.Button, 40, 6, "上一页图数据", new("graph_previous"), enabled: _graphPage > 0); Line();
            Add(GuiItemKind.Button, 40, 7, "下一页图数据", new("graph_next"), enabled: _graphPage + 6 < _graphReviewText.Length);
            foreach (string line in _graphReviewText.Skip(_graphPage).Take(6)) Add(GuiItemKind.Label, 3, labelId++, line);
            Add(GuiItemKind.Button, 40, 2, "审阅精确图 / 依赖 UUID / 配对受众", new("graph_review"), enabled: !_page!.State.Frozen && !_page.State.EditBusy);
        }
        var grant = _graphs.Grant;
        Add(GuiItemKind.Label, 3, labelId++, $"图 MCP 批准 {grant.GraphId:D}, {grant.Seconds}s；共享配对受众，不是每客户端ACL。");
        Add(GuiItemKind.Button, 40, 5, "撤销图读取权限", new("graph_revoke"));
        if (_graphReview is { } review) {
            if (!_graphs.IsCurrent(review)) { _graphReview = null; _graphReviewed = false; }
            else {
                Add(GuiItemKind.Label, 3, labelId++, $"只读图 {review.GraphId:D}; hash {review.Hash}; endpoint {review.EndpointId:D}");
                foreach (Guid dependency in review.Dependencies) Add(GuiItemKind.Label, 3, labelId++, "允许披露依赖 UUID " + dependency.ToString("D"));
                foreach (Guid audience in review.Audience) Add(GuiItemKind.Label, 3, labelId++, "配对受众 " + audience.ToString("D"));
                Add(GuiItemKind.Checkbox, 40, 3, "已审阅完整图数据和所有依赖 / 受众", new("graph_reviewed"), number: _graphReviewed ? 1 : 0, max: 1);
                Add(GuiItemKind.Button, 40, 4, "批准显示范围，只读60秒", new("graph_approve", Field: review.Fingerprint), enabled: _graphReviewed);
            }
        }
        Add(GuiItemKind.Button, 40, 8, "关闭图检查", new("graph_toggle")); End();
    }
    private bool ApplyGraphRead(ActionView action, double value)
    {
        if (!action.Kind.StartsWith("graph_", StringComparison.Ordinal)) return false;
        if (_graphs is null) throw new EditRejectedException("graph_service_missing");
        switch (action.Kind) {
            case "graph_toggle": if(_uiMode){CancelInteraction();_uiMode=false;}_showGraphReads = !_showGraphReads; break;
            case "graph_open":
                CancelInteraction(); string? selected = filePicker?.Invoke(LocalFileKind.OpenAnimationGraph);
                if (selected is not null) {
                    if (projectRoot is null) throw new EditRejectedException("graph_project_missing");
                    _graphs.OpenTrustedRelative(Path.GetRelativePath(projectRoot, selected).Replace('\\', '/'));
                    var graph = _graphs.LocalCopy(); _graphReviewText = InspectionText.Split(System.Text.Encoding.UTF8.GetString(AnimationGraphCodec.Encode(graph)), 1800).ToArray();
                    _graphPage = 0; _graphReview = null; _graphReviewed = false;
                } break;
            case "graph_previous": _graphPage = Math.Max(0, _graphPage - 6); break;
            case "graph_next": _graphPage = Math.Min(Math.Max(0, _graphReviewText.Length - 1), _graphPage + 6); break;
            case "graph_review": _graphReview = _graphs.Capture(); _graphReviewed = false; break;
            case "graph_reviewed": if (value is not (0 or 1)) throw new EditRejectedException("graph_review_value"); _graphReviewed = value == 1; break;
            case "graph_approve": _graphs.Approve(_graphReview ?? throw new EditRejectedException("graph_review_missing"), action.Field, _graphReviewed); _graphReview = null; _graphReviewed = false; break;
            case "graph_revoke": _graphs.Revoke(); _graphReview = null; _graphReviewed = false; break;
            default: throw new EditRejectedException("graph_intent_invalid");
        }
        return true;
    }
}
