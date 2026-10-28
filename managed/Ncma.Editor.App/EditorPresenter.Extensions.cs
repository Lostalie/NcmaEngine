using Ncma.Gui;
#if NCMA_EXTENSIONS
using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Extensions.Hosting;
#endif

namespace Ncma.Editor.App;

internal sealed unsafe partial class EditorPresenter
{
    private bool _showExtensions;
#if NCMA_EXTENSIONS
    private EditorTrustedExtensions? _trustedExtensions;
    private ExtensionSceneSummary? _extensionSummary;
    private sealed record ExtensionReviewView(ExtensionTransformReview Ticket,string[] Pages);
    private ExtensionReviewView? _extensionReview;
    private readonly HashSet<int> _extensionReadPages=[];
    private int _extensionPage;
    private bool _extensionConfirmed;
    private void ClearExtensionReview()
    { _trustedExtensions?.Commands.Cancel();_extensionReview=null;_extensionReadPages.Clear();_extensionPage=0;_extensionConfirmed=false; }
    internal void AttachTrustedExtensions(EditorTrustedExtensions extensions)
    {
        if (_trustedExtensions is not null) throw new InvalidOperationException("extension_presenter_already_attached");
        extensions.RequireOwner(workspace.Owner);
        _trustedExtensions = extensions;
    }
#endif
    private void ExtensionMenuEntry() => Add(GuiItemKind.Button,24,76,"可信扩展 / 本地状态与命令审阅",new("extensions_toggle"));
    private bool ApplyExtensions(ActionView action,double value,string text)
    {
        if (!action.Kind.StartsWith("extensions_",StringComparison.Ordinal)) return false;
#if NCMA_EXTENSIONS
        if(ApplyExtensionSources(action,value,text))return true;
        if(action.Operation is ExtensionReviewView expected &&
            (!ReferenceEquals(expected,_extensionReview)||!_trustedExtensions!.Commands.IsCurrent(expected.Ticket))) {
            ClearExtensionReview();throw new EditRejectedException("extension_review_stale");
        }
        if(action.Kind is "extensions_toggle" or "extensions_close" or "extensions_refresh") { ClearExtensionReview();ClearExtensionSources(); }
#endif
        switch (action.Kind) {
            case "extensions_toggle": _showExtensions = !_showExtensions; _activeMenu = -1; break;
            case "extensions_close": _showExtensions = false; break;
#if NCMA_EXTENSIONS
            case "extensions_refresh":
                _extensionSummary = null; // never leave previous data visible after failed refresh
                _extensionSummary = _trustedExtensions!.Capture(); break;
            case "extensions_prepare":
                ClearExtensionSources();
                ClearExtensionReview();
                var ticket=_trustedExtensions!.Commands.PrepareAddTransform();
                try {
                    var commands=new JsonTextPages(ticket.Commands);var changes=new JsonTextPages(ticket.Changes);
                    _extensionReview=new(ticket,Enumerable.Range(0,commands.Count).Select(i=>"原命令\n"+commands.Text(i))
                        .Concat(Enumerable.Range(0,changes.Count).Select(i=>"完整差异\n"+changes.Text(i))).ToArray());
                } catch { ClearExtensionReview();throw; }
                break;
            case "extensions_previous":_extensionPage--;_extensionConfirmed=false;break;
            case "extensions_next":_extensionPage++;_extensionConfirmed=false;break;
            case "extensions_read":
                if(value!=0)_extensionReadPages.Add(_extensionPage);else _extensionReadPages.Remove(_extensionPage);
                _extensionConfirmed=false;break;
            case "extensions_confirm":_extensionConfirmed=value!=0;break;
            case "extensions_cancel":ClearExtensionReview();break;
            case "extensions_apply":
                var review=_extensionReview??throw new EditRejectedException("extension_review_missing");
                if(!_extensionConfirmed||_extensionReadPages.Count!=review.Pages.Length)throw new EditRejectedException("extension_review_incomplete");
                // Clear presentation approval BEFORE callbacks, without cancelling the exact service ticket.
                _extensionReview=null;_extensionReadPages.Clear();_extensionConfirmed=false;
                var result=_trustedExtensions!.Commands.Confirm(review.Ticket,review.Ticket.Fingerprint);
                if(result.Status!="ok")throw new EditRejectedException(result.Code);
                _extensionSummary=null;break;
#endif
            default: throw new InvalidOperationException("extension_ui_intent_invalid");
        }
        _generation=checked(_generation+1);
        return true;
    }
    private void BuildExtensions(float width,float height,ref ulong label)
    {
        if (!_showExtensions) return;
        Panel(430,"可信托管扩展 / 本地状态与命令审阅",width*.12f,height*.12f,width*.76f,height*.76f);
        var panel=_items[^1];panel.Value=2;_items[^1]=panel;
        Add(GuiItemKind.Button,84,1,"关闭",new("extensions_close"));
        Add(GuiItemKind.Label,3,label++,"仅显式编译的可信源码。不是安全沙箱，不扫描 DLL，不授予 AI/MCP 权限。");
#if NCMA_EXTENSIONS
        if (_trustedExtensions is {} extensions) {
            var status=extensions.Status;var descriptor=EditorTrustedExtensions.Descriptor;
            Add(GuiItemKind.Label,3,label++,$"{descriptor.Id} | {descriptor.Version.Major}.{descriptor.Version.Minor}.{descriptor.Version.Patch} | Host API 1.0.0 | 依赖 {descriptor.Dependencies.Count}");
            Add(GuiItemKind.Label,3,label++,$"扩展状态 {status.State} | 扩展持有 {status.RetainedServices} | 扩展关闭需显式重试 {status.CloseRetryRequired}");
            Add(GuiItemKind.Label,3,label++,status.OuterRetained
                ? "外层持有：是（宿主关闭未完成；需由宿主显式重试关闭）"
                : "外层持有：否（无未完成的宿主关闭；不代表运行中资源已释放）");
            foreach(var failure in new[]{status.StartupFailure,status.CloseFailure})
                if(failure is not null)Add(GuiItemKind.Label,3,label++,$"{failure.Code} | {failure.ExtensionId} | {failure.ExceptionType}");
            Add(GuiItemKind.Button,84,2,"手动读取编辑场景摘要",new("extensions_refresh"),enabled:status.State==ExtensionGroupState.Running);
            if (_extensionSummary is {} previous && !extensions.IsCurrent(previous)) _extensionSummary=null;
            if (_extensionSummary is {} summary) {
                Add(GuiItemKind.Label,3,label++,$"编辑文档 {summary.Document:D} | Revision {summary.Source.Revision}");
                Add(GuiItemKind.Label,3,label++,$"对象 {summary.Objects} | 选择 {summary.Selection?.ToString("D")??"无"}");
            } else Add(GuiItemKind.Label,3,label++,"未读取或已失效；仅显式刷新，不读取 Play 的运行值。");
            Add(GuiItemKind.Button,84,3,"预检：为选中对象补充缺失 Transform",new("extensions_prepare"),
                enabled:status.State==ExtensionGroupState.Running&&_page is {State.Frozen:false,State.EditBusy:false,State.HistoryInvalidated:false,State.Selection:not null,Play:null});
            if(_extensionReview is {} old&&!extensions.Commands.IsCurrent(old.Ticket))ClearExtensionReview();
            if(_extensionReview is {} review) {
                Add(GuiItemKind.Label,3,label++,"仅新增缺失组件，不覆盖现有值；应用为一条原 Undo 历史，不停止或修改 Play。");
                Add(GuiItemKind.Label,3,label++,"指纹 "+review.Ticket.Fingerprint);
                Add(GuiItemKind.Label,3,label++,$"文档 {review.Ticket.Source.Document:D} / Revision {review.Ticket.Source.Source.Revision} / 目标 {review.Ticket.Source.Selection:D}");
                Add(GuiItemKind.Label,3,label++,$"扩展会话 {review.Ticket.Source.ExtensionSession:D} / 脚本目录 {review.Ticket.Batch.BehaviourCatalogGeneration:D}");
                Add(GuiItemKind.Label,3,label++,$"审阅 {_extensionPage+1}/{review.Pages.Length}，已确认 {_extensionReadPages.Count} 页；需审阅所有原命令与完整差异。");
                Add(GuiItemKind.Label,3,label++,review.Pages[_extensionPage]);
                Add(GuiItemKind.Button,84,4,"上一页",new("extensions_previous",Operation:review),enabled:_extensionPage>0);
                Add(GuiItemKind.Button,84,5,"下一页",new("extensions_next",Operation:review),enabled:_extensionPage+1<review.Pages.Length);
                Add(GuiItemKind.Checkbox,84,6,"我已审阅本页",new("extensions_read",Operation:review),number:_extensionReadPages.Contains(_extensionPage)?1:0,max:1);
                bool all=_extensionReadPages.Count==review.Pages.Length;
                Add(GuiItemKind.Checkbox,84,7,"确认只执行上述原事务",new("extensions_confirm",Operation:review),number:_extensionConfirmed?1:0,max:1,enabled:all);
                Add(GuiItemKind.Button,84,8,"应用已审阅改动",new("extensions_apply",Operation:review),enabled:all&&_extensionConfirmed);
                Add(GuiItemKind.Button,84,9,"放弃本次审阅",new("extensions_cancel",Operation:review));
            }
            BuildExtensionSources(extensions,ref label);
        } else Add(GuiItemKind.Label,3,label++,"本构建包含扩展模块；当前宿主尚未装配。");
#else
        Add(GuiItemKind.Label,3,label++,"默认产品未包含扩展模块。需显式 NcmaEnableExtensions=true 的隔离构建。");
#endif
        Add(GuiItemKind.Label,3,label++,"示例只沿原组件聚合与事务入口；源码审阅不是写入/构建/安装授权；完整工程闭包、自定义组件发布、安装重启尚未实现。");
        End();
    }
}
