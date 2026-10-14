using System.Globalization;
using Ncma.Assets;
using Ncma.Editor.Services;
using Ncma.Gui;

namespace Ncma.Editor.App;

internal sealed unsafe partial class EditorPresenter
{
    private EditorUiPackagePreparation? _uiPackagePreparation;
    private EditorUiPublication? _uiPublication;
    private UiPackageReview? _uiPackageReview;
    private UiSelectionReview? _uiSelectionReview;
    private UiPackageReference? _uiPreparedPackage;
    private string _uiPackageGeneration="1",_uiPublishConfirm="";

    public void AttachUiPublication(EditorUiPackagePreparation preparation,EditorUiPublication publication)
    { _uiPackagePreparation=preparation;_uiPublication=publication; }

    private void BuildUiPublication(ref ulong label,bool writable)
    {
        if(_uiPackagePreparation is null||_uiPublication is null)return;
        bool idle=writable&&!_ui!.HasDraft&&!_page!.State.EditBusy&&!_page.State.Frozen&&_page.Play is null;
        Guid? target=_page!.State.Selection;
        Add(GuiItemKind.Label,30,label++,"场景 HUD 发布（独立于作者预览）\n目标对象："+(target?.ToString("D")??"请先在场景中选择对象"));
        Add(GuiItemKind.Text,31,600,"新代次（正整数；不可覆盖已有代次）",new("ui_publish_generation",Operation:new UiIntent(_ui!.Stamp)),_uiPackageGeneration,enabled:idle);
        UiButton(601,"审阅运行包","ui_publish_review_package",enabled:idle);
        if(_uiPackageReview is {} package){
            Add(GuiItemKind.Label,30,label++,$"项目 {package.Project}\n文档 {package.Package.DocumentId}\n代次 {package.Package.Generation}\n路径 {package.Package.Path}\nSHA256 {package.Package.Hash}\n审阅指纹 {package.Fingerprint}");
            Confirm();UiButton(603,"批准并准备此精确运行包","ui_publish_prepare",package,idle&&_uiPublishConfirm==package.Fingerprint);
        }
        if(_uiPreparedPackage is {} prepared)Add(GuiItemKind.Label,30,label++,$"已准备：{prepared.DocumentId} / {prepared.Generation}\n{prepared.Hash}\n准备不会修改场景，Undo 不删除不可变运行包。");
        UiButton(604,"审阅选择此运行包","ui_publish_review_selection",target,idle&&target is not null&&_uiPreparedPackage is not null);
        UiButton(605,"审阅关闭场景 HUD","ui_publish_review_off",target,!_uiTest&&!_page.State.Frozen&&!_page.State.EditBusy&&_page.Play is null&&target is not null&&!_ui.HasDraft);
        if(_uiSelectionReview is {} selection){
            Add(GuiItemKind.Label,30,label++,$"精确目标 {selection.Target}\n场景修订 {selection.Edit.Revision}\n新选择 {(selection.After.Enabled?$"{selection.After.DocumentId} / {selection.After.Generation} / {selection.After.Hash}":"Off：不使用项目默认 HUD")}\n审阅指纹 {selection.Fingerprint}");
            Confirm();UiButton(606,"批准并应用场景选择（可撤销）","ui_publish_apply",selection,!_uiTest&&!_page.State.Frozen&&!_page.State.EditBusy&&_page.Play is null&&!_ui.HasDraft&&_uiPublishConfirm==selection.Fingerprint);
        }
        UiButton(607,"取消发布审阅","ui_publish_cancel");
        void Confirm()=>Add(GuiItemKind.Text,31,602,"输入完整审阅指纹确认（60 秒有效）",new("ui_publish_confirm",Operation:new UiIntent(_ui!.Stamp)),_uiPublishConfirm);
    }

    private bool ApplyUiPublication(string kind,UiIntent intent,string text)
    {
        if(!kind.StartsWith("ui_publish_",StringComparison.Ordinal))return false;
        var prepare=_uiPackagePreparation??throw new InvalidOperationException("UI package preparation unavailable.");
        var publication=_uiPublication??throw new InvalidOperationException("UI publication unavailable.");
        switch(kind){
            case "ui_publish_generation":_uiPackageGeneration=text;ClearReviews();break;
            case "ui_publish_confirm":_uiPublishConfirm=text;break;
            case "ui_publish_review_package":
                ClearReviews();_uiPackageReview=prepare.Review(_ui!.Stamp,ulong.Parse(_uiPackageGeneration,NumberStyles.None,CultureInfo.InvariantCulture));break;
            case "ui_publish_prepare":
                var package=(UiPackageReview)intent.Payload!;
                if(package!=_uiPackageReview||_uiPublishConfirm!=package.Fingerprint)throw new InvalidOperationException("Exact displayed package review required.");
                _uiPreparedPackage=prepare.Approve(package,_uiPublishConfirm,true);ClearReviews();Record("不可变 UI 运行包已准备；场景尚未改变。");break;
            case "ui_publish_review_selection":case "ui_publish_review_off":
                var target=(Guid)intent.Payload!;
                if(workspace.Owner.Edit!.State.Selection!=target)throw new InvalidOperationException("Scene selection changed after presentation.");
                ClearReviews();_uiSelectionReview=publication.Review(workspace.Stamp,target,kind=="ui_publish_review_off"?null:_uiPreparedPackage??throw new InvalidOperationException("Prepared package required."));break;
            case "ui_publish_apply":
                var selection=(UiSelectionReview)intent.Payload!;
                if(selection!=_uiSelectionReview||_uiPublishConfirm!=selection.Fingerprint||workspace.Owner.Edit!.State.Selection!=selection.Target)throw new InvalidOperationException("Exact displayed scene selection review required.");
                publication.ApplyLocal(publication.Approve(selection,_uiPublishConfirm,true));ClearReviews();Record("场景 HUD 选择已提交；可使用原 Undo/Redo。请保存场景以持久化选择。");break;
            case "ui_publish_cancel":ClearReviews();break;
            default:throw new ArgumentException("Unknown UI publication action.");
        }
        return true;
        void ClearReviews(){_uiPackageReview=null;_uiSelectionReview=null;_uiPublishConfirm="";}
    }
}
