using Ncma.Editor.Core;
using Ncma.Gui;

namespace Ncma.Editor.App;

internal sealed unsafe partial class EditorPresenter
{
    private RuntimeDetailPage? _runtimeDetail;
    private int _runtimeDetailTextPage;
    private const int RuntimeTextPageChars = 960;
    private void ClearRuntimeDetails() { _runtimeDetail = null; _runtimeDetailTextPage = 0; }

    private void BuildRuntimeDetails(ref ulong label)
    {
        if (_runtimeDetail is not { } page) return;
        bool current = _runtimeInspection!.IsLocalCurrent(page.Source);
        var stamp = page.Source.Stamp;
        Add(GuiItemKind.Label, 3, label++, $"本地只读 / {page.Source.ObjectId:D} / {page.Section}");
        Add(GuiItemKind.Label, 3, label++, $"捕获 Play {page.Source.PlaySessionId:D} / 场景 {stamp.SceneSessionId:D}");
        Add(GuiItemKind.Label, 3, label++, $"World {stamp.WorldId:D} / v{stamp.Version} / revision {stamp.Revision} / tick {stamp.Tick}");
        Add(GuiItemKind.Label, 3, label++, "此处查看不授予 AI 读取权限；不写回作者数据，不访问脚本私有托管状态。");
        Add(GuiItemKind.Button, 63, 1, "关闭属性详情", new("runtime_detail_close"));
        Add(GuiItemKind.Button, 63, 4, "重新读取所选对象", new("runtime_detail_refresh", Operation: page));
        if (!current)
        {
            Add(GuiItemKind.Label, 3, label++, "运行状态已变化，旧属性已隐藏；请暂停 Play 后重新读取。");
            return;
        }
        Add(GuiItemKind.Button, 63, 2, "组件", new("runtime_detail_components", Operation: page));
        Add(GuiItemKind.Button, 63, 3, "脚本绑定", new("runtime_detail_bindings", Operation: page));
        Add(GuiItemKind.Label, 3, label++, $"条目 {Math.Min(page.Offset + 1, page.TotalCount)}/{page.TotalCount}；每页完整读取一个条目");
        Add(GuiItemKind.Button, 63, 5, "上一条属性", new("runtime_detail_previous", Operation: page), enabled: page.Offset > 0);
        Add(GuiItemKind.Button, 63, 6, "下一条属性", new("runtime_detail_next", Operation: page), enabled: page.Offset + 1 < page.TotalCount);
        if (page.Section == "bindings" && page.Data.GetProperty("items").GetArrayLength() == 1)
        {
            var binding = page.Data.GetProperty("items")[0];
            Add(GuiItemKind.Button, 63, 9, "查看该绑定的 Export", new("runtime_detail_exports",
                Object: binding.GetProperty("id").GetGuid(), Operation: page));
        }
        int pages = Math.Max(1, (page.Text.Length + RuntimeTextPageChars - 1) / RuntimeTextPageChars);
        Add(GuiItemKind.Label, 3, label++, $"JSON 文本 {_runtimeDetailTextPage + 1}/{pages}（完整数据分段，不省略字段）");
        Add(GuiItemKind.Button, 63, 7, "上一段文本", new("runtime_detail_text_previous", Operation: page), enabled: _runtimeDetailTextPage > 0);
        Add(GuiItemKind.Button, 63, 8, "下一段文本", new("runtime_detail_text_next", Operation: page), enabled: _runtimeDetailTextPage + 1 < pages);
        int start = _runtimeDetailTextPage * RuntimeTextPageChars;
        for (int at = start; at < Math.Min(start + RuntimeTextPageChars, page.Text.Length); at += 80)
            Add(GuiItemKind.Label, 3, label++, RuntimeDetailTextSlice(page.Text, at, 80));
    }

    // Adjacent fixed-width slices concatenate without losing or splitting a surrogate pair.
    internal static string RuntimeDetailTextSlice(string text, int offset, int length)
    {
        if (offset < 0 || length <= 0 || offset > text.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        int end = offset + Math.Min(length, text.Length - offset);
        if (offset > 0 && offset < text.Length && char.IsLowSurrogate(text[offset]) && char.IsHighSurrogate(text[offset - 1])) offset--;
        if (end > 0 && end < text.Length && char.IsHighSurrogate(text[end - 1]) && char.IsLowSurrogate(text[end])) end--;
        return text[offset..end];
    }

    private bool ApplyRuntimeDetails(ActionView action)
    {
        if (!action.Kind.StartsWith("runtime_detail_", StringComparison.Ordinal)) return false;
        if (action.Kind == "runtime_detail_close") { ClearRuntimeDetails(); return true; }
        if (action.Kind == "runtime_detail_open")
        {
            var request = action.Operation as RuntimeDetailRequest ?? throw new EditRejectedException("runtime_local_input_invalid");
            var next = _runtimeInspection!.LocalDetails(request, "components");
            _runtimeDetail = next; _runtimeDetailTextPage = 0; return true;
        }
        if (action.Operation is not RuntimeDetailPage page || !ReferenceEquals(page, _runtimeDetail))
            throw new EditRejectedException("runtime_local_view_stale");
        if (action.Kind == "runtime_detail_refresh")
        {
            var next = _runtimeInspection!.LocalDetails(_runtimeInspection.RefreshLocal(page.Source), page.Section, 0, page.BindingId);
            _runtimeDetail = next; _runtimeDetailTextPage = 0; return true;
        }
        if (!_runtimeInspection!.IsLocalCurrent(page.Source)) throw new EditRejectedException("runtime_local_view_stale");
        string section = page.Section; Guid? bindingId = page.BindingId; int offset = page.Offset;
        switch (action.Kind)
        {
            case "runtime_detail_components": section = "components"; bindingId = null; offset = 0; break;
            case "runtime_detail_bindings": section = "bindings"; bindingId = null; offset = 0; break;
            case "runtime_detail_exports": section = "exports"; bindingId = action.Object; offset = 0; break;
            case "runtime_detail_previous": offset = Math.Max(0, offset - 1); break;
            case "runtime_detail_next": if (offset + 1 >= page.TotalCount) throw new EditRejectedException("runtime_local_page_end"); offset++; break;
            case "runtime_detail_text_previous": _runtimeDetailTextPage = Math.Max(0, _runtimeDetailTextPage - 1); return true;
            case "runtime_detail_text_next":
                if ((_runtimeDetailTextPage + 1) * RuntimeTextPageChars >= page.Text.Length) throw new EditRejectedException("runtime_local_page_end");
                _runtimeDetailTextPage++; return true;
            default: throw new EditRejectedException("runtime_local_input_invalid");
        }
        _runtimeDetail = _runtimeInspection.LocalDetails(page.Source, section, offset, bindingId);
        _runtimeDetailTextPage = 0; return true;
    }
}
