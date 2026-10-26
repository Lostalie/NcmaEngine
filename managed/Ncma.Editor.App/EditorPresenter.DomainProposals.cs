using System.Text;
using System.Text.Json;
using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Gui;

namespace Ncma.Editor.App;

internal sealed unsafe partial class EditorPresenter
{
    private bool _showDomains, _domainChecked, _domainBindings;
    private string _domainDraft = "", _domainDelete = "", _domainKey = "";
    private string[] _domainPages = [];
    private int _domainPage;
    private readonly HashSet<int> _domainSeen = [];
    private DomainProposalReview? _domainReview;
    private DomainObservationReview? _domainObservation;
    private void DomainPages(string key, string text)
    {
        _domainKey = key; _domainPages = InspectionText.Split(text, 256 * 1024).ToArray();
        _domainPage = 0; _domainSeen.Clear(); _domainChecked = _domainBindings = false; _domainDelete = "";
    }
    private void BuildDomains(float width, float height, ref ulong label)
    {
        if (!_showDomains || workspace.Owner.DomainProposals is not { } service) return;
        Panel(414, "AI 领域提案 · 观察 / 差异 / 原事务", width * .06f, height * .08f, width * .88f, height * .84f);
        var overlay = _items[^1]; overlay.Value = 2; _items[^1] = overlay;
        Add(GuiItemKind.Label, 80, label++, "模型状态：" + AiHeaderStatus + "。观察外发和写入是两次独立人工确认；模块不能自批。文件域仍需原资源/文件审批。");
        bool writable = !_page!.State.Frozen && !_page.State.EditBusy && !_page.State.HistoryInvalidated;
        if (!writable) { _domainKey = ""; _domainPages = []; _domainReview = null; _domainObservation = null; }
        if (writable) {
            if (_domainReview is not null && !service.IsCurrent(_domainReview) ||
                _domainObservation is not null && !ReferenceEquals(service.LatestObservation, _domainObservation)) {
                _domainKey = ""; _domainPages = []; _domainReview = null; _domainObservation = null;
                _domainChecked = _domainBindings = false;
            }
            Add(GuiItemKind.Text, 80, 1, "本地草稿 JSON（输入1023B；模块 API 64KiB）", new("domain_draft"), _domainDraft);
            Add(GuiItemKind.Button, 80, 2, "预检草稿（不提交）", new("domain_review"), enabled: _domainDraft.Length != 0);
            if (service.LatestReview is { } latest)
                Add(GuiItemKind.Button, 80, 3, "审阅最新完整差异", new("domain_latest", Operation: latest));
            ulong moduleRow = 100;
            foreach (var module in service.RegisteredModules) {
                Add(GuiItemKind.Label, 80, label++, $"模块 {module.Operation} / {module.ModuleVersion}\n配置 {module.ConfigurationId:D} · {service.Module(module).Status}");
                Add(GuiItemKind.Button, 80, moduleRow++, "审阅所选对象的 summary / components 观察", new("domain_observe", Operation: module), enabled: _page.State.Selection is not null);
                Add(GuiItemKind.Button, 80, moduleRow++, "领取回包 / 刷新模块", new("domain_pump", Operation: module));
                Add(GuiItemKind.Button, 80, moduleRow++, "取消并尝试回收（不撤销已提交）", new("domain_cancel_module", Operation: module));
            }
            if (service.RegisteredModules.Length == 0) Add(GuiItemKind.Label, 80, label++, "没有可信宿主注册的模块；默认不加载 Python 或联网。");
            if (service.PendingRequest is { } request)
                Add(GuiItemKind.Button, 80, 4, "向精确模块发送一次已批准观察", new("domain_send", Operation: request));
        }
        if (_domainKey.Length != 0 && _domainPages.Length != 0) {
            Add(GuiItemKind.Label, 80, label++, $"指纹 {_domainKey}\n完整审阅 {_domainPage + 1}/{_domainPages.Length}");
            Add(GuiItemKind.Label, 80, label++, _domainPages[_domainPage]); _domainSeen.Add(_domainPage);
            Add(GuiItemKind.Button, 80, 5, "上一页", new("domain_previous"), enabled: _domainPage > 0); Line();
            Add(GuiItemKind.Button, 80, 6, "下一页", new("domain_next"), enabled: _domainPage + 1 < _domainPages.Length);
            bool all = _domainSeen.Count == _domainPages.Length;
            Add(GuiItemKind.Checkbox, 80, 7, "已审阅所有页、来源、精确内容与风险", new("domain_checked"), number: _domainChecked ? 1 : 0, max: 1, enabled: all);
            if (_domainReview is { } review) {
                if (review.DestructiveTarget is not null) Add(GuiItemKind.Text, 80, 8, "单独确认删除 UUID", new("domain_delete"), _domainDelete);
                if (review.CopyImpact().Bindings.Length != 0) Add(GuiItemKind.Checkbox, 80, 9, "另行确认可执行 C# 绑定变更（仍需原目录信任）", new("domain_bindings"), number: _domainBindings ? 1 : 0, max: 1);
                Add(GuiItemKind.Button, 80, 10, "确认一次并交给原事务", new("domain_confirm", Field: review.Fingerprint, Operation: review), enabled: writable && all && _domainChecked);
                Add(GuiItemKind.Button, 80, 11, "丢弃此提案", new("domain_discard", Operation: review));
            }
            else if (_domainObservation is { } observation)
                Add(GuiItemKind.Button, 80, 12, "允许精确观察向此模块发送一次（60秒）", new("domain_approve_read", Field: observation.Fingerprint, Operation: observation), enabled: writable && all && _domainChecked);
        }
        foreach (var receipt in service.Receipts.TakeLast(6)) Add(GuiItemKind.Label, 80, label++,
            $"{receipt.ProposalId:D} | {receipt.Status}/{receipt.Code} | changed={receipt.Changed} | revision={receipt.ExecutionRevision}\n{(receipt.RecoveryRequired ? "需要原事务人工恢复；不能假定没有写入，不自动 Undo。" : "保持原事务回执，不自动重试。")}");
        Add(GuiItemKind.Button, 80, 13, "撤销全部未执行提案/观察", new("domain_revoke"));
        Add(GuiItemKind.Button, 80, 14, "关闭领域提案面板", new("domain_toggle")); End();
    }
    private void ShowDomain(DomainProposalReview review)
    {
        _domainReview = review; _domainObservation = null;
        DomainPages(review.Fingerprint, JsonSerializer.Serialize(new { review.Source, review.DomainCapability,
            review.DestructiveTarget, impact = review.CopyImpact() }) + "\n原输入\n" + Encoding.UTF8.GetString(review.CopyPayload()) +
            "\n完整前后差异\n" + Encoding.UTF8.GetString(review.CopyChanges()));
    }
    private bool ApplyDomain(ActionView action, double value, string text)
    {
        if (!action.Kind.StartsWith("domain_", StringComparison.Ordinal)) return false;
        var service = workspace.Owner.DomainProposals ?? throw new EditRejectedException("domain_service_missing");
        switch (action.Kind) {
            case "domain_toggle": _showDomains = !_showDomains; _activeMenu = -1; break;
            case "domain_draft": if (Encoding.UTF8.GetByteCount(text) > 1023) throw new ArgumentException("Draft input budget."); _domainDraft = text; break;
            case "domain_review": ShowDomain(service.Review(Guid.NewGuid(), service.CaptureSource(), Encoding.UTF8.GetBytes(_domainDraft))); break;
            case "domain_latest": ShowDomain((DomainProposalReview)action.Operation!); break;
            case "domain_previous": _domainPage = Math.Max(0, _domainPage - 1); break;
            case "domain_next": _domainPage = Math.Min(_domainPages.Length - 1, _domainPage + 1); break;
            case "domain_checked":
                if (value is not (0 or 1) || _domainSeen.Count != _domainPages.Length || _domainPages.Length == 0) throw new EditRejectedException("domain_review_incomplete");
                _domainChecked = value == 1; break;
            case "domain_bindings": if (value is not (0 or 1)) throw new ArgumentException(); _domainBindings = value == 1; break;
            case "domain_delete": _domainDelete = text; break;
            case "domain_confirm":
                if (_domainSeen.Count != _domainPages.Length || _domainPages.Length == 0) throw new EditRejectedException("domain_review_incomplete");
                var result = service.Confirm((DomainProposalReview)action.Operation!, action.Field, _domainChecked,
                    Guid.TryParse(_domainDelete, out var id) ? id : null, _domainBindings);
                _domainKey = ""; _domainReview = null; _domainChecked = false;
                if (result.Status != "ok") throw new EditRejectedException(result.Code); break;
            case "domain_discard": service.Discard((DomainProposalReview)action.Operation!); break;
            case "domain_observe":
                var target = (DomainModuleTarget)action.Operation!;
                Guid selected = _page!.State.Selection ?? throw new EditRejectedException("domain_selection_required");
                _domainObservation = service.ReviewObservation(target, [new("ncma.scene.object.inspect", JsonSerializer.SerializeToElement(new { objectId = selected, section = "summary" })),
                    new("ncma.scene.object.inspect", JsonSerializer.SerializeToElement(new { objectId = selected, section = "components" }))]);
                _domainReview = null;
                DomainPages(_domainObservation.Fingerprint, JsonSerializer.Serialize(target) + "\n将发送的完整字节\n" + Encoding.UTF8.GetString(_domainObservation.CopyBytes())); break;
            case "domain_approve_read":
                if (_domainSeen.Count != _domainPages.Length || _domainPages.Length == 0) throw new EditRejectedException("domain_review_incomplete");
                service.ApproveObservation((DomainObservationReview)action.Operation!, action.Field, _domainChecked); _domainKey = ""; _domainObservation = null; break;
            case "domain_send": var request = (ApprovedDomainRequest)action.Operation!; service.Module(request.Target).Begin(request); break;
            case "domain_pump": service.Module((DomainModuleTarget)action.Operation!).Pump(); break;
            case "domain_cancel_module": var module = service.Module((DomainModuleTarget)action.Operation!); module.Cancel(); module.TryClose(); break;
            case "domain_revoke": service.Invalidate(); _domainKey = ""; _domainReview = null; _domainObservation = null; break;
            default: throw new EditRejectedException("domain_intent_invalid");
        }
        return true;
    }
}
