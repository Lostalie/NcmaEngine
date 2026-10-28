#if NCMA_EXTENSIONS
using System.Text;
using Ncma.Extensions;
using Ncma.Extensions.Hosting;
using Ncma.Gui;

namespace Ncma.Editor.App;

internal sealed unsafe partial class EditorPresenter
{
    private string _extensionSourceRoot = "", _extensionCandidateRoot = "", _extensionSourcePaths = "";
    private sealed record SourceReviewView(ExtensionSourceCapture Capture, ExtensionSourcePages Text);
    private SourceReviewView? _extensionSourceReview;
    private readonly HashSet<int> _extensionSourceRead = [];
    private int _extensionSourcePage;
    private bool _extensionSourceConfirmed, _extensionSourceChecked;
    private string _extensionBundleDestination = "", _extensionBundleHash = "";
    private void ClearExtensionSources()
    { _extensionSourceReview = null; _extensionSourceRead.Clear(); _extensionSourcePage = 0; _extensionSourceConfirmed = false; _extensionSourceChecked = false; _extensionBundleHash = ""; }
    private bool ApplyExtensionSources(ActionView action, double value, string text)
    {
        if (!action.Kind.StartsWith("extensions_source_",StringComparison.Ordinal)) return false;
        if (_trustedExtensions is not {} host || host.Status.State != ExtensionGroupState.Running) {
            ClearExtensionSources(); throw new ExtensionRejectedException("source_host_unavailable");
        }
        if (action.Operation is SourceReviewView expected && !ReferenceEquals(expected,_extensionSourceReview))
            throw new ExtensionRejectedException("source_review_stale");
        switch (action.Kind) {
            case "extensions_source_root":
            case "extensions_source_candidate":
            case "extensions_source_paths":
                ClearExtensionSources();
                if (Encoding.UTF8.GetByteCount(text)>1023) throw new ExtensionRejectedException("source_input_budget");
                if (action.Kind=="extensions_source_root")_extensionSourceRoot=text;
                else if(action.Kind=="extensions_source_candidate")_extensionCandidateRoot=text;
                else _extensionSourcePaths=text;
                break;
            case "extensions_source_capture":
                ClearExtensionReview();ClearExtensionSources();
                // This real local UI action is the only production caller. No remote
                // capability, model output or project metadata chooses these paths.
                var capture=ExtensionSourceCapture.Capture(EditorTrustedExtensions.Descriptor,
                    _extensionSourceRoot,_extensionCandidateRoot,_extensionSourcePaths.Split(';'));
                _extensionSourceReview=new(capture,new(capture.Patch));break;
            case "extensions_source_previous":_extensionSourcePage--;_extensionSourceConfirmed=false;_extensionSourceChecked=false;break;
            case "extensions_source_next":_extensionSourcePage++;_extensionSourceConfirmed=false;_extensionSourceChecked=false;break;
            case "extensions_source_read":
                if(value!=0)_extensionSourceRead.Add(_extensionSourcePage);else _extensionSourceRead.Remove(_extensionSourcePage);
                _extensionSourceConfirmed=false;_extensionSourceChecked=false;break;
            case "extensions_source_confirm":_extensionSourceConfirmed=value!=0;_extensionSourceChecked=false;break;
            case "extensions_source_check":
                var review=_extensionSourceReview??throw new ExtensionRejectedException("source_review_missing");
                if(!_extensionSourceConfirmed||_extensionSourceRead.Count!=review.Text.Pages.Count)throw new ExtensionRejectedException("source_review_incomplete");
                _extensionSourceChecked=false;_extensionSourceConfirmed=false;
                try { review.Capture.RequireCurrent(); _extensionSourceChecked=true; }
                catch { ClearExtensionSources();throw; } // do not show old contents/result after failed explicit recheck
                break;
            case "extensions_source_cancel":ClearExtensionSources();break;
            case "extensions_source_bundle_path":
                _extensionBundleDestination=text;_extensionBundleHash="";break;
            case "extensions_source_export":
                if(!_extensionSourceChecked || _extensionSourceReview is not {} captured)throw new ExtensionRejectedException("source_review_incomplete");
                _extensionBundleHash="";
                _extensionBundleHash=captured.Capture.ExportBuildBundle(_extensionBundleDestination);break;
            default:throw new ExtensionRejectedException("source_ui_intent_invalid");
        }
        _generation=checked(_generation+1);return true;
    }
    private void BuildExtensionSources(EditorTrustedExtensions host,ref ulong label)
    {
        bool active=host.Status.State==ExtensionGroupState.Running;
        if(!active)ClearExtensionSources();
        Add(GuiItemKind.Label,3,label++,"本地 .cs 新增/替换/删除审阅：两个目录的父目录须已存在，逐项完整前后内容；不写入、不上传、不隐式创建目录。");
        Add(GuiItemKind.Text,84,20,"原源码绝对目录",new("extensions_source_root"),_extensionSourceRoot,enabled:active);
        Add(GuiItemKind.Text,84,21,"候选源码绝对目录",new("extensions_source_candidate"),_extensionCandidateRoot,enabled:active);
        Add(GuiItemKind.Text,84,22,"精确相对 .cs 路径（; 分隔，最多16项）",new("extensions_source_paths"),_extensionSourcePaths,enabled:active);
        Add(GuiItemKind.Button,84,23,"读取指定文件并审阅（仅本地）",new("extensions_source_capture"),enabled:active&&_extensionSourceRoot.Length>0&&_extensionCandidateRoot.Length>0&&_extensionSourcePaths.Length>0);
        if(_extensionSourceReview is not {} review)return;
        var patch=review.Capture.Patch;var page=review.Text.Pages[_extensionSourcePage];var change=patch.Changes[page.FileIndex];
        var file=page.Before?change.Before:change.After;
        Add(GuiItemKind.Label,3,label++,"局部源码快照，不是完整工程/依赖闭包；磁盘不会每帧重读。任何内容确认不产生可执行票据。");
        Add(GuiItemKind.Label,3,label++,"补丁指纹 "+patch.Fingerprint);
        Add(GuiItemKind.Label,3,label++,$"来源会话 {patch.Source.WorkspaceSession:D} / Revision {patch.Source.Revision} / {patch.Changes.Count} 文件 / {patch.TotalBytes} 字节");
        Add(GuiItemKind.Label,3,label++,"原来源指纹 "+patch.Source.InputSha256);
        Add(GuiItemKind.Label,3,label++,"候选来源指纹 "+review.Capture.CandidateFingerprint);
        Add(GuiItemKind.Label,3,label++,$"文件 {page.FileIndex+1}/{patch.Changes.Count}: {change.Path} / {change.Kind} / {(page.Before?"修改前":"修改后")} / 存在 {file.Exists} / {file.ByteCount} 字节 / SHA256 {file.Sha256??"不存在"}");
        Add(GuiItemKind.Label,3,label++,$"UTF-8 BOM {file.HasUtf8Bom} / CRLF {file.CrLfCount} / LF {file.LineFeedCount} / CR {file.CarriageReturnCount}。完整 JSON 转义文本；非 ASCII、换行和不可见字符均显式转义。");
        Add(GuiItemKind.Label,3,label++,$"第 {_extensionSourcePage+1}/{review.Text.Pages.Count} 页，已读 {_extensionSourceRead.Count} 页；同文件同侧分页拼接为一个完整 JSON 字符串。");
        Add(GuiItemKind.Label,3,label++,page.Text);
        Add(GuiItemKind.Button,84,24,"上一页源码",new("extensions_source_previous",Operation:review),enabled:_extensionSourcePage>0);
        Add(GuiItemKind.Button,84,25,"下一页源码",new("extensions_source_next",Operation:review),enabled:_extensionSourcePage+1<review.Text.Pages.Count);
        Add(GuiItemKind.Checkbox,84,26,"已审阅本页完整源码",new("extensions_source_read",Operation:review),number:_extensionSourceRead.Contains(_extensionSourcePage)?1:0,max:1);
        bool all=_extensionSourceRead.Count==review.Text.Pages.Count;
        Add(GuiItemKind.Checkbox,84,27,"确认仅记录本次内容审阅，不授权执行",new("extensions_source_confirm",Operation:review),number:_extensionSourceConfirmed?1:0,max:1,enabled:all);
        Add(GuiItemKind.Button,84,28,"重新核对来源并记录本次已审阅",new("extensions_source_check",Operation:review),enabled:all&&_extensionSourceConfirmed);
        Add(GuiItemKind.Button,84,29,"放弃源码审阅",new("extensions_source_cancel",Operation:review));
        Add(GuiItemKind.Text,84,30,"捕获副本输出绝对路径（新 .nccapture 文件）",new("extensions_source_bundle_path",Operation:review),_extensionBundleDestination,enabled:_extensionSourceChecked);
        Add(GuiItemKind.Button,84,31,"导出已审阅副本（不构建、不安装）",new("extensions_source_export",Operation:review),enabled:_extensionSourceChecked&&_extensionBundleDestination.Length>0);
        if(_extensionBundleHash.Length>0)Add(GuiItemKind.Label,3,label++,"捕获包 SHA256 "+_extensionBundleHash+"；构建只使用此副本，不回读原源码。");
        if(_extensionSourceChecked)Add(GuiItemKind.Label,3,label++,"本次人工审阅已记录，核对时来源一致。随后磁盘仍可能变化；没有写入/编译/安装授权，关闭即清除本地记录。");
    }
}
#endif
