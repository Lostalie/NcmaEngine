using System.Text.Json;
using Ncma.Editor.Services;
using Ncma.Editor.Core;
using Ncma.Gui;
namespace Ncma.Editor.App;

internal sealed unsafe partial class EditorPresenter
{
    private bool _showWorkflows, _workflowReviewed;
    private WorkflowRegistryReview? _workflowRegistry;
    private WorkflowReview? _workflowReview;
    private string[] _workflowPages = [];
    private readonly HashSet<int> _workflowSeen = [];
    private int _workflowPage;
    private int _workflowReceiptPage;
    private Guid _workflowSelected;
    private string? _workflowReviewKey;
    private void WorkflowPages(string key, string text)
    {
        _workflowReviewKey = key; _workflowPages = InspectionText.Split(text, 512 * 1024).ToArray();
        _workflowPage = 0; _workflowSeen.Clear(); _workflowReviewed = false;
    }
    private void BuildWorkflows(float width, float height, ref ulong label)
    {
        if (!_showWorkflows || workspace.Owner.Workflows is not { } service) return;
        Panel(413, "AI 工作流 · 精确审批与回执", width * .08f, height * .1f, width * .84f, height * .8f);
        var overlay = _items[^1]; overlay.Value = 2; _items[^1] = overlay;
        Add(GuiItemKind.Label, 61, label++, "未接入推理服务。工作流不代替工具/资源/独立测试审批，不自动执行或 Undo。");
        Add(GuiItemKind.Button, 61, 1, "审阅已注册能力元数据 / 配对受众", new("wf_registry"), enabled: workspace.Owner.Endpoint is not null);
        Add(GuiItemKind.Button, 61, 2, "撤销工作流权限（保留回执）", new("wf_revoke"));
        ulong row = 1000;
        foreach (var plan in service.Capture()) {
            Add(GuiItemKind.Button, 61, row++, $"{plan.WorkflowId:D} | {plan.Status} | {plan.Index}/{plan.StepCount}", new("wf_select", plan.WorkflowId));
        }
        if (_workflowSelected != Guid.Empty && service.Capture().SingleOrDefault(p => p.WorkflowId == _workflowSelected) is { } selected) {
            Add(GuiItemKind.Button, 61, 3, "审阅精确完整计划", new("wf_review", selected.WorkflowId), enabled: selected.Status == "proposed");
            Add(GuiItemKind.Button, 61, 4, "取消未执行步骤（不回滚已提交）", new("wf_cancel", selected.WorkflowId), enabled: selected.Status != "completed");
            Add(GuiItemKind.Label, 61, label++, $"计划 {selected.WorkflowId:D}\n状态 {selected.Status} · 完成 {selected.Index}/{selected.StepCount} · 已用修复 {selected.RepairCount}\n全文 SHA256 {selected.Hash}");
            if (selected.Ticket is { } copied) {
                var request = JsonSerializer.Deserialize<CapabilityRequest>(copied, Ncma.Editor.Protocol.Wire.Json)!;
                Add(GuiItemKind.Label, 61, label++, $"待执行原工具 {request.Capability}\n请求 {request.RequestId:D} · expected revision {request.ExpectedRevision}\n请由原工具独立审批执行；工作流面板不自动调用。");
            }
            _workflowReceiptPage = Math.Clamp(_workflowReceiptPage, 0, Math.Max(0, (selected.Receipts.Length - 1) / 4 * 4));
            Add(GuiItemKind.Button, 61, 10, "上一页步骤回执", new("wf_receipt_previous"), enabled: _workflowReceiptPage > 0); Line();
            Add(GuiItemKind.Button, 61, 11, "下一页步骤回执", new("wf_receipt_next"), enabled: _workflowReceiptPage + 4 < selected.Receipts.Length);
            foreach (var receipt in selected.Receipts.Skip(_workflowReceiptPage).Take(4)) {
                Add(GuiItemKind.Label, 61, label++, $"{receipt.Capability} · {receipt.Status}/{receipt.Code} · 断言 {(receipt.AssertionPassed ? "通过" : "未通过")}\n请求 {receipt.RequestId:D} · 提交版本 {receipt.ExecutionRevision} · 修改 {receipt.Changed}\n结果 SHA256 {receipt.ResultHash}");
            }
        }
        if (_workflowReviewKey is not null) {
            Add(GuiItemKind.Label, 61, label++, $"审阅指纹 {_workflowReviewKey}\n页 {_workflowPage + 1}/{_workflowPages.Length}");
            foreach (string chunk in _workflowPages.Skip(_workflowPage).Take(1)) Add(GuiItemKind.Label, 61, label++, chunk);
            _workflowSeen.Add(_workflowPage);
            Add(GuiItemKind.Button, 61, 5, "上一页完整审阅", new("wf_previous"), enabled: _workflowPage > 0); Line();
            Add(GuiItemKind.Button, 61, 6, "下一页完整审阅", new("wf_next"), enabled: _workflowPage + 1 < _workflowPages.Length);
            bool all = _workflowSeen.Count == _workflowPages.Length;
            Add(GuiItemKind.Checkbox, 61, 7, "已审阅全部精确输入 / 风险 / 受众 / 期限", new("wf_checked"), number: _workflowReviewed ? 1 : 0, max: 1, enabled: all);
            Add(GuiItemKind.Button, 61, 8, "批准显示范围60秒（不授予原工具执行权限）", new("wf_approve", Field: _workflowReviewKey), enabled: all && _workflowReviewed);
        }
        Add(GuiItemKind.Button, 61, 9, "关闭工作流", new("wf_toggle")); End();
    }
    private bool ApplyWorkflow(ActionView action, double value)
    {
        if (!action.Kind.StartsWith("wf_", StringComparison.Ordinal)) return false;
        var service = workspace.Owner.Workflows ?? throw new EditRejectedException("workflow_missing");
        switch (action.Kind) {
            case "wf_toggle": _showWorkflows = !_showWorkflows; _activeMenu = -1; break;
            case "wf_select": _workflowSelected = action.Object; _workflowReceiptPage = 0; _workflowReviewKey = null; _workflowReviewed = false; break;
            case "wf_registry":
                _workflowReview = null; _workflowRegistry = service.CaptureRegistry();
                WorkflowPages(_workflowRegistry.Fingerprint, JsonSerializer.Serialize(_workflowRegistry, Ncma.Editor.Protocol.Wire.Json)); break;
            case "wf_review":
                _workflowRegistry = null; _workflowReview = service.CaptureReview(action.Object);
                WorkflowPages(_workflowReview.Fingerprint, JsonSerializer.Serialize(_workflowReview, Ncma.Editor.Protocol.Wire.Json)); break;
            case "wf_previous": _workflowPage = Math.Max(0, _workflowPage - 1); break;
            case "wf_next": _workflowPage = Math.Min(_workflowPages.Length - 1, _workflowPage + 1); break;
            case "wf_receipt_previous": _workflowReceiptPage = Math.Max(0, _workflowReceiptPage - 4); break;
            case "wf_receipt_next": _workflowReceiptPage += 4; break;
            case "wf_checked":
                if (value is not (0 or 1) || _workflowSeen.Count != _workflowPages.Length) throw new EditRejectedException("workflow_review_incomplete");
                _workflowReviewed = value == 1; break;
            case "wf_approve":
                if (_workflowSeen.Count != _workflowPages.Length) throw new EditRejectedException("workflow_review_incomplete");
                if (_workflowRegistry is { } registry) service.ApproveRegistry(registry, action.Field, _workflowReviewed);
                else service.Approve(_workflowReview ?? throw new EditRejectedException("workflow_review_missing"), action.Field, _workflowReviewed);
                _workflowRegistry = null; _workflowReview = null; _workflowReviewKey = null; _workflowReviewed = false; break;
            case "wf_cancel": service.Cancel(action.Object); break;
            case "wf_revoke": service.Revoke(); _workflowReviewKey = null; _workflowReviewed = false; break;
            default: throw new EditRejectedException("workflow_intent_invalid");
        }
        return true;
    }
}
