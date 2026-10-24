using System.Text;
using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Gameplay;
using Ncma.Gui;
using Ncma.Scene;

namespace Ncma.Editor.App;

internal sealed unsafe partial class EditorPresenter
{
    private bool _showKeepPlay, _keepCommands, _keepConfirmed;
    private int _keepAuthorOffset, _keepRuntimeOffset, _keepTextPage;
    private readonly HashSet<Guid> _keepObjects = [], _keepDeletes = [];
    private readonly HashSet<int> _keepReadPages = [];
    private KeepPlayPage? _keepPage;
    private KeepPlayReview? _keepReview;
    private sealed record KeepPlaySource(EditorViewStamp Edit, Guid Play, RuntimeInspectionStamp Runtime, Guid Catalog);
    private sealed record KeepPlayPage(KeepPlaySource Source, EditorObjectRow[] Author, RuntimeObjectInfo[] Runtime,
        int AuthorTotal, int RuntimeTotal);
    private sealed record KeepPlayReview(PreparedRuntimeChanges Prepared, string Diff, string Commands, Guid[] Deletes);
    private sealed record KeepPlayText(KeepPlayReview Review, bool Commands, int Page);

    private void ClearKeepPlayReview()
    { _keepReview = null; _keepReadPages.Clear(); _keepDeletes.Clear(); _keepConfirmed = false; _keepCommands = false; _keepTextPage = 0; }

    private KeepPlaySource KeepPlaySourceNow()
    {
        var owner = workspace.Owner; var state = owner.Edit!.State;
        if (owner.Play is not { State: PlayState.Paused } play || !state.Frozen || state.EditBusy || state.HistoryInvalidated ||
            owner.PlayAuthoringSource is not { } source || source.DocumentSession != owner.Document.SessionId || source.Revision != owner.Document.Revision)
            throw new EditRejectedException("keep_play_requires_paused_source");
        return new(workspace.Stamp, play.SessionId, play.Scene.GetInspectionStamp(), owner.Catalog.Snapshot.Generation);
    }
    private bool KeepPlayCurrent(KeepPlaySource source)
    {
        try { return source == KeepPlaySourceNow(); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or EditRejectedException) { return false; }
    }

    // Capture summaries only on an explicit local click, never serialize runtime differences per frame.
    private void CaptureKeepPlayObjects(bool refresh)
    {
        var source = KeepPlaySourceNow();
        if (refresh) { _keepAuthorOffset = 0; _keepRuntimeOffset = 0; }
        else if (_keepPage is not { } previous || previous.Source != source) throw new EditRejectedException("keep_play_view_stale");
        var author = workspace.Capture(_keepAuthorOffset, 8);
        var runtime = workspace.Owner.Play!.Scene.InspectObjects(source.Runtime, _keepRuntimeOffset, 8);
        if (!KeepPlayCurrent(source)) throw new EditRejectedException("keep_play_view_stale");
        _keepPage = new(source, author.Rows, runtime.Items, author.Total, runtime.TotalCount);
        if (refresh) { _keepObjects.Clear(); ClearKeepPlayReview(); }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void BuildKeepPlayChanges(float width, float height, ref ulong label)
    {
        Panel(421, "保留 Play 改动 / 本地人工确认", width * .08f, height * .08f, width * .84f, height * .84f);
        Add(GuiItemKind.Button, 64, 2, "关闭并放弃本次审阅", new("keep_play_close"));
        Add(GuiItemKind.Label, 3, label++, "Stop 默认丢弃运行改动。此处仅在暂停时审阅指定 UUID；不会授予 AI 权限或自动同步。");
        Add(GuiItemKind.Button, 64, 3, "重新读取对象（清空审阅）", new("keep_play_refresh"), enabled: workspace.Owner.Play?.State == PlayState.Paused);
        if (_keepPage is not { } page || !KeepPlayCurrent(page.Source))
        {
            ClearKeepPlayReview(); _keepObjects.Clear();
            Add(GuiItemKind.Label, 3, label++, "请暂停 Play 后读取；来源已变化时旧对象列表与差异隐藏，所有确认失效。");
            End(); return;
        }
        var source = page.Source;
        Add(GuiItemKind.Label, 3, label++, $"Play {source.Play:D} / World {source.Runtime.WorldId:D}");
        Add(GuiItemKind.Label, 3, label++, $"编辑 revision {source.Edit.Revision} / 运行 revision {source.Runtime.Revision} / tick {source.Runtime.Tick}");
        Add(GuiItemKind.Label, 3, label++, $"已选 {_keepObjects.Count}/32；两栏同 UUID 共用选择。编辑栏可选已在运行时删除的对象，运行栏可选新建对象。");
        Add(GuiItemKind.Label, 3, label++, $"编辑对象 {_keepAuthorOffset}/{page.AuthorTotal}");
        Add(GuiItemKind.Button, 64, 4, "上一页编辑对象", new("keep_play_author_previous", Operation: page), enabled: _keepAuthorOffset > 0);
        Add(GuiItemKind.Button, 64, 5, "下一页编辑对象", new("keep_play_author_next", Operation: page), enabled: _keepAuthorOffset + 8 < page.AuthorTotal);
        for (int i = 0; i < page.Author.Length; ++i)
        {
            var row = page.Author[i];
            Add(GuiItemKind.Checkbox, 64, (ulong)i + 100, $"编辑 {BoundMessage(row.Name, 60)} / {row.Id:D}",
                new("keep_play_select", row.Id, Operation: page), number: _keepObjects.Contains(row.Id) ? 1 : 0, max: 1);
        }
        Add(GuiItemKind.Label, 3, label++, $"运行对象 {_keepRuntimeOffset}/{page.RuntimeTotal}");
        Add(GuiItemKind.Button, 64, 6, "上一页运行对象", new("keep_play_runtime_previous", Operation: page), enabled: _keepRuntimeOffset > 0);
        Add(GuiItemKind.Button, 64, 7, "下一页运行对象", new("keep_play_runtime_next", Operation: page), enabled: _keepRuntimeOffset + 8 < page.RuntimeTotal);
        for (int i = 0; i < page.Runtime.Length; ++i)
        {
            var row = page.Runtime[i];
            Add(GuiItemKind.Checkbox, 64, (ulong)i + 200, $"运行 {BoundMessage(row.Name, 60)} / {row.Id:D}",
                new("keep_play_select", row.Id, Operation: page), number: _keepObjects.Contains(row.Id) ? 1 : 0, max: 1);
        }
        Add(GuiItemKind.Button, 64, 8, "捕获完整差异并预检原命令", new("keep_play_review", Operation: page), enabled: _keepObjects.Count > 0);
        Add(GuiItemKind.Button, 64, 9, "清空选择与审阅", new("keep_play_clear", Operation: page));
        if (_keepReview is { } review)
        {
            if (!workspace.Owner.RuntimeChanges!.IsPreparedCurrent(review.Prepared)) ClearKeepPlayReview();
            else BuildKeepPlayReview(review, ref label);
        }
        End();
    }

    private static int KeepTextPages(string text) => Math.Max(1, (text.Length + RuntimeTextPageChars - 1) / RuntimeTextPageChars);
    private bool KeepAllReviewed(KeepPlayReview review) => _keepReadPages.Count == KeepTextPages(review.Diff) && _keepDeletes.SetEquals(review.Deletes);
    private void BuildKeepPlayReview(KeepPlayReview review, ref ulong label)
    {
        var view = new KeepPlayText(review, _keepCommands, _keepTextPage);
        string text = _keepCommands ? review.Commands : review.Diff; int pages = KeepTextPages(text);
        Add(GuiItemKind.Label, 3, label++, "审阅指纹 " + review.Prepared.Fingerprint);
        Add(GuiItemKind.Label, 3, label++, "完整差异含 before/after、对象与来源戳；null 表示该侧不存在。命令仅展开为原事务和独立删除。");
        Add(GuiItemKind.Button, 65, 1, "查看完整差异", new("keep_play_diff", Operation: view));
        Add(GuiItemKind.Button, 65, 2, "查看展开的原命令", new("keep_play_commands", Operation: view));
        Add(GuiItemKind.Label, 3, label++, $"{(_keepCommands ? "原命令" : "差异")} {_keepTextPage + 1}/{pages}；已确认差异页 {_keepReadPages.Count}/{KeepTextPages(review.Diff)}，完整字段分段展示，不截断。");
        Add(GuiItemKind.Button, 65, 3, "上一段审阅文本", new("keep_play_text_previous", Operation: view), enabled: _keepTextPage > 0);
        Add(GuiItemKind.Button, 65, 4, "下一段审阅文本", new("keep_play_text_next", Operation: view), enabled: _keepTextPage + 1 < pages);
        int start = _keepTextPage * RuntimeTextPageChars;
        for (int at = start; at < Math.Min(start + RuntimeTextPageChars, text.Length); at += 80)
            Add(GuiItemKind.Label, 3, label++, RuntimeDetailTextSlice(text, at, 80));
        if (!_keepCommands) Add(GuiItemKind.Checkbox, 65, 5, "我已审阅这一页完整差异", new("keep_play_read_page", Operation: view),
            number: _keepReadPages.Contains(_keepTextPage) ? 1 : 0, max: 1);
        for (int i = 0; i < review.Deletes.Length; ++i)
        {
            Guid id = review.Deletes[i];
            Add(GuiItemKind.Checkbox, 65, (ulong)i + 100, "确认从编辑场景删除 " + id.ToString("D"),
                new("keep_play_delete", id, Operation: view), number: _keepDeletes.Contains(id) ? 1 : 0, max: 1);
        }
        Add(GuiItemKind.Label, 3, label++, "必须审阅所有差异页并逐个确认删除。应用将停止 Play，写入一条原 Undo 历史；停止/提交失败不自动重试。");
        Add(GuiItemKind.Checkbox, 65, 6, "确认停止 Play 并应用以上完整差异", new("keep_play_confirm", Operation: view),
            number: _keepConfirmed ? 1 : 0, max: 1, enabled: KeepAllReviewed(review));
        Add(GuiItemKind.Button, 65, 7, "停止 Play 并应用已确认改动", new("keep_play_apply", Operation: view),
            enabled: _keepConfirmed && KeepAllReviewed(review));
    }

    private bool ApplyKeepPlayChanges(ActionView action, double value)
    {
        if (!action.Kind.StartsWith("keep_play_", StringComparison.Ordinal)) return false;
        var service = workspace.Owner.RuntimeChanges ?? throw new EditRejectedException("keep_play_service_missing");
        if (action.Kind is "keep_play_toggle" or "keep_play_close")
        {
            _showKeepPlay = action.Kind != "keep_play_close" && !_showKeepPlay;
            ClearKeepPlayReview(); _keepObjects.Clear(); _keepPage = null; service.Discard();
            _activeMenu = -1; _generation = checked(_generation + 1);
            if (_showKeepPlay && workspace.Owner.Play?.State == PlayState.Paused) CaptureKeepPlayObjects(true);
            return true;
        }
        if (!_showKeepPlay) throw new EditRejectedException("keep_play_view_stale");
        if (action.Kind == "keep_play_refresh") { CaptureKeepPlayObjects(true); service.Discard(); return true; }
        if (action.Operation is KeepPlayPage page)
        {
            if (!ReferenceEquals(page, _keepPage) || !KeepPlayCurrent(page.Source)) throw new EditRejectedException("keep_play_view_stale");
            switch (action.Kind)
            {
                case "keep_play_author_previous": _keepAuthorOffset = Math.Max(0, _keepAuthorOffset - 8); CaptureKeepPlayObjects(false); break;
                case "keep_play_author_next": if (_keepAuthorOffset + 8 >= page.AuthorTotal) throw new EditRejectedException("keep_play_page_end"); _keepAuthorOffset += 8; CaptureKeepPlayObjects(false); break;
                case "keep_play_runtime_previous": _keepRuntimeOffset = Math.Max(0, _keepRuntimeOffset - 8); CaptureKeepPlayObjects(false); break;
                case "keep_play_runtime_next": if (_keepRuntimeOffset + 8 >= page.RuntimeTotal) throw new EditRejectedException("keep_play_page_end"); _keepRuntimeOffset += 8; CaptureKeepPlayObjects(false); break;
                case "keep_play_select":
                    if (value is not (0 or 1)) throw new EditRejectedException("keep_play_selection_invalid");
                    if (value == 0) _keepObjects.Remove(action.Object);
                    else { if (_keepObjects.Count >= EditorRuntimeChanges.MaxObjects && !_keepObjects.Contains(action.Object)) throw new EditRejectedException("keep_play_selection_budget"); _keepObjects.Add(action.Object); }
                    ClearKeepPlayReview(); service.Discard(); break;
                case "keep_play_clear": _keepObjects.Clear(); ClearKeepPlayReview(); service.Discard(); break;
                case "keep_play_review":
                    ClearKeepPlayReview();
                    var prepared = service.PrepareApply(service.Capture(_keepObjects.Order().ToArray()));
                    _keepReview = new(prepared, Encoding.UTF8.GetString(prepared.Review.CopyBytes()), Encoding.UTF8.GetString(prepared.CopyOriginalCommands()), prepared.CopyDeletionTargets());
                    break;
                default: throw new EditRejectedException("keep_play_action_invalid");
            }
            return true;
        }
        if (action.Operation is not KeepPlayText view || !ReferenceEquals(view.Review, _keepReview) ||
            view.Commands != _keepCommands || view.Page != _keepTextPage || !service.IsPreparedCurrent(view.Review.Prepared))
            throw new EditRejectedException("keep_play_review_stale");
        var review = view.Review;
        if (!_keepObjects.SetEquals(review.Prepared.Review.CopyData().ObjectIds)) throw new EditRejectedException("keep_play_selection_stale");
        switch (action.Kind)
        {
            case "keep_play_diff": _keepCommands = false; _keepTextPage = 0; _keepConfirmed = false; break;
            case "keep_play_commands": _keepCommands = true; _keepTextPage = 0; _keepConfirmed = false; break;
            case "keep_play_text_previous": _keepTextPage = Math.Max(0, _keepTextPage - 1); _keepConfirmed = false; break;
            case "keep_play_text_next":
                if (_keepTextPage + 1 >= KeepTextPages(_keepCommands ? review.Commands : review.Diff)) throw new EditRejectedException("keep_play_page_end");
                _keepTextPage++; _keepConfirmed = false; break;
            case "keep_play_read_page":
                if (_keepCommands || value is not (0 or 1)) throw new EditRejectedException("keep_play_confirmation_invalid");
                if (value == 1) _keepReadPages.Add(_keepTextPage); else _keepReadPages.Remove(_keepTextPage); _keepConfirmed = false; break;
            case "keep_play_delete":
                if (value is not (0 or 1) || !review.Deletes.Contains(action.Object)) throw new EditRejectedException("keep_play_confirmation_invalid");
                if (value == 1) _keepDeletes.Add(action.Object); else _keepDeletes.Remove(action.Object); _keepConfirmed = false; break;
            case "keep_play_confirm":
                if (value is not (0 or 1) || !KeepAllReviewed(review)) throw new EditRejectedException("keep_play_confirmation_required");
                _keepConfirmed = value == 1; break;
            case "keep_play_apply":
                if (!_keepConfirmed || !KeepAllReviewed(review)) throw new EditRejectedException("keep_play_confirmation_required");
                Guid[] deletes = _keepDeletes.Order().ToArray(); ClearKeepPlayReview();
                var result = service.ConfirmStopAndApply(review.Prepared, review.Prepared.Fingerprint, deletes);
                if (result.Status != "ok") throw new EditRejectedException(result.Code);
                _keepObjects.Clear(); _keepPage = null; _showKeepPlay = false; _generation = checked(_generation + 1);
                break;
            default: throw new EditRejectedException("keep_play_action_invalid");
        }
        return true;
    }
}
