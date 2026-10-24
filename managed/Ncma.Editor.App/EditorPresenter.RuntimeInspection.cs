using Ncma.Editor.Core;
using Ncma.Gameplay;
using Ncma.Gui;

namespace Ncma.Editor.App;

internal sealed unsafe partial class EditorPresenter
{
    private RuntimeInspectionService? _runtimeInspection;
    private bool _showRuntimeInspection, _runtimeReadReviewed;
    private readonly HashSet<Guid> _runtimeReadSelection = [];
    private RuntimeReadReview? _runtimeReadReview;
    private int _runtimeReadOffset;
    private RuntimePanelScope? _runtimePanelScope;
    private sealed record RuntimePanelScope(Guid Play, Guid Scene, Guid World);
    internal void AttachRuntimeInspection(RuntimeInspectionService service) => _runtimeInspection = service;
    private void BuildRuntimeInspection(float width, float height, ref ulong label)
    { if (_showRuntimeInspection) BuildRuntimeInspectionVisible(width, height, ref label); }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void BuildRuntimeInspectionVisible(float width, float height, ref ulong label)
    {
        if (_runtimeInspection is null) return;
        Panel(420, "运行对象检查 / 独立只读授权", width * .1f, height * .1f, width * .8f, height * .8f);
        Add(GuiItemKind.Button, 62, 2, "关闭运行检查", new("runtime_close"));
        Add(GuiItemKind.Button, 62, 3, "撤销运行数据读取", new("runtime_revoke"));
        try
        {
            var view = _runtimeInspection.LocalObjects(_runtimeReadOffset);
            var scope = new RuntimePanelScope(view.PlaySessionId, view.Page.Stamp.SceneSessionId, view.Page.Stamp.WorldId);
            if (_runtimePanelScope != scope)
            {
                _runtimePanelScope = scope; _runtimeReadSelection.Clear(); _runtimeReadReview = null;
                ClearRuntimeDetails();
                _runtimeReadReviewed = false; _runtimeReadOffset = 0; view = _runtimeInspection.LocalObjects();
            }
            if (_runtimeReadReview is { } old && !_runtimeInspection.IsCurrent(old)) { _runtimeReadReview = null; _runtimeReadReviewed = false; }
            Add(GuiItemKind.Label, 3, label++, $"Play {view.PlaySessionId:D} / World {scope.World:D} / revision {view.Page.Stamp.Revision} / tick {view.Page.Stamp.Tick}");
            Add(GuiItemKind.Label, 3, label++, "只读已提交状态，不写组件、不推进模拟、不控制推理服务。");
            Add(GuiItemKind.Label, 3, label++, "批准范围：所选对象的全部组件值、脚本绑定与 Export，含60秒内新增字段和变化值。数据可能含业务信息/引用，不作秘密脱敏。");
            Add(GuiItemKind.Button, 62, 4, "上一页运行对象", new("runtime_previous", Operation: scope), enabled: _runtimeReadOffset > 0);
            Add(GuiItemKind.Button, 62, 5, "下一页运行对象", new("runtime_next", Operation: scope), enabled: _runtimeReadOffset + 16 < view.Page.TotalCount);
            Add(GuiItemKind.Label, 3, label++, $"本地对象 {view.Page.TotalCount} / 已选 {_runtimeReadSelection.Count}/32；远程只可列出获批对象。");
            for (int i = 0; i < view.Page.Items.Length; i++)
            {
                var row = view.Page.Items[i];
                Add(GuiItemKind.Checkbox, 62, (ulong)i + 100, $"{BoundMessage(row.Name, 80)} / {row.Id:D}",
                    new("runtime_select", row.Id, Operation: scope), number: _runtimeReadSelection.Contains(row.Id) ? 1 : 0, max: 1);
                Add(GuiItemKind.Button, 63, (ulong)i + 100, $"本地查看 {BoundMessage(row.Name, 80)}",
                    new("runtime_detail_open", Operation: new RuntimeDetailRequest(view.PlaySessionId, view.Page.Stamp, row.Id)));
            }
            BuildRuntimeDetails(ref label);
            var grant = _runtimeInspection.Grant;
            Add(GuiItemKind.Label, 3, label++, $"运行读取获批 {grant.Objects}/32，剩余 {grant.Seconds}s；与角色/动画授权相互独立。");
            Add(GuiItemKind.Button, 62, 6, "审阅运行对象与配对受众", new("runtime_review", Operation: scope),
                enabled: _runtimeReadSelection.Count > 0 && workspace.Owner.Endpoint is { } endpoint && endpoint.View.Connections.Any(c => c.Paired && c.Connected));
            if (_runtimeReadReview is { } review)
            {
                Add(GuiItemKind.Label, 3, label++, $"运行会话 {review.PlaySessionId:D}；端点 {review.EndpointId:D}；受众版本 {review.AudienceRevision}");
                foreach (var row in review.Objects) Add(GuiItemKind.Label, 3, label++, $"批准对象 {row.Id:D} / {BoundMessage(row.Name, 80)}");
                foreach (Guid id in review.Audience) Add(GuiItemKind.Label, 3, label++, "共享配对受众 " + id.ToString("D"));
                Add(GuiItemKind.Label, 3, label++, "所有显示的配对客户端共享此只读范围，并非逐客户端 ACL；Stop/Reload、对象重建或配对变更均失效。");
                Add(GuiItemKind.Checkbox, 62, 7, "已审阅全部对象、数据范围和完整受众", new("runtime_reviewed", Field: review.Fingerprint), number: _runtimeReadReviewed ? 1 : 0, max: 1);
                Add(GuiItemKind.Button, 62, 8, "批准运行数据只读60秒", new("runtime_approve", Field: review.Fingerprint), enabled: _runtimeReadReviewed);
            }
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or EditCommandRejectedException)
        {
            Add(GuiItemKind.Label, 3, label++, "运行快照不可用；请启动或暂停 Play 并在已提交边界读取。");
            _runtimeReadReview = null; _runtimeReadReviewed = false; _runtimeReadSelection.Clear(); _runtimePanelScope = null; _runtimeReadOffset = 0;
            ClearRuntimeDetails();
        }
        End();
    }
    private bool ApplyRuntimeInspection(ActionView action, double value)
    {
        if (!action.Kind.StartsWith("runtime_", StringComparison.Ordinal)) return false;
        if (_runtimeInspection is null) throw new EditRejectedException("runtime_service_missing");
        if (ApplyRuntimeDetails(action)) return true;
        if (action.Operation is RuntimePanelScope scope && (workspace.Owner.Play is not { } play ||
            play.State is not (PlayState.Running or PlayState.Paused) || play.SessionId != scope.Play ||
            play.Scene.SessionId != scope.Scene || play.Scene.World.Identity != scope.World)) throw new EditRejectedException("runtime_view_stale");
        switch (action.Kind)
        {
            case "runtime_toggle": _showRuntimeInspection = !_showRuntimeInspection; if (!_showRuntimeInspection) ClearRuntimeDetails(); break;
            case "runtime_close": _showRuntimeInspection = false; ClearRuntimeDetails(); break;
            case "runtime_previous": _runtimeReadOffset = Math.Max(0, _runtimeReadOffset - 16); break;
            case "runtime_next": _runtimeReadOffset = Math.Min(65536, _runtimeReadOffset + 16); break;
            case "runtime_select":
                if (value is not (0 or 1)) throw new EditRejectedException("runtime_selection_invalid");
                if (value == 0) _runtimeReadSelection.Remove(action.Object);
                else { if (_runtimeReadSelection.Count >= RuntimeInspectionService.MaxObjects && !_runtimeReadSelection.Contains(action.Object)) throw new EditRejectedException("runtime_selection_budget"); _runtimeReadSelection.Add(action.Object); }
                _runtimeReadReview = null; _runtimeReadReviewed = false; break;
            case "runtime_review": _runtimeReadReview = _runtimeInspection.Capture(_runtimeReadSelection.ToArray()); _runtimeReadReviewed = false; break;
            case "runtime_reviewed":
                if (value is not (0 or 1) || _runtimeReadReview is not { } displayed || displayed.Fingerprint != action.Field || !_runtimeInspection.IsCurrent(displayed)) throw new EditRejectedException("runtime_review_stale");
                _runtimeReadReviewed = value == 1; break;
            case "runtime_approve":
                var review = _runtimeReadReview ?? throw new EditRejectedException("runtime_review_missing");
                if (!_runtimeReadSelection.SetEquals(review.Objects.Select(o => o.Id))) throw new EditRejectedException("runtime_review_stale");
                _runtimeInspection.Approve(review, action.Field, _runtimeReadReviewed); _runtimeReadReview = null; _runtimeReadReviewed = false; break;
            case "runtime_revoke": _runtimeInspection.Revoke(); _runtimeReadReview = null; _runtimeReadReviewed = false; break;
            default: throw new EditRejectedException("runtime_intent_invalid");
        }
        return true;
    }
}
