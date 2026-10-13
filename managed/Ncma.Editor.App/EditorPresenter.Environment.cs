using Ncma.Gui;
using Ncma.Scene.Rendering;
using Ncma.Editor.Services;
using Ncma.Editor.Core;

namespace Ncma.Editor.App;

internal sealed unsafe partial class EditorPresenter
{
    private sealed record EnvironmentPreparationView(Guid World, ulong Revision, Guid Play, bool Ready, bool Failed, EnvironmentLightingData? Prepared);
    private EnvironmentPreparationView? _environmentPreparation;

    // Copied presentation state only. Never an approval, resource handle or Agent capability.
    internal void ShowEnvironmentPreparation(Guid world, ulong revision, Guid play, bool ready, bool failed, EnvironmentLightingData? prepared) =>
        _environmentPreparation = new(world, revision, play, ready, failed, prepared);

    internal string EnvironmentPreparationStatus
    {
        get {
            var play = workspace.Owner.Play;
            var world = play?.Document.World ?? workspace.Owner.Document.World;
            if (_environmentPreparation is not { } state || state.World != world.Identity || state.Revision != world.Revision || state.Play != (play?.SessionId ?? Guid.Empty))
                return "环境准备：等待当前版本";
            string prefix = play is null ? "环境准备：" : "环境准备（Play，编辑冻结）：";
            if (state.Failed) return prefix + "失败；视口已清空，详见控制台";
            if (!state.Ready || state.Prepared is null) return prefix + "等待当前版本";
            return prefix + (state.Prepared.Value.Enabled ? $"就绪 / 代次 {state.Prepared.Value.Generation}" : "关闭");
        }
    }

    private void BuildEnvironmentInspector(Guid objectId, EnvironmentLightingData value, ref ulong control, ref ulong label, bool writable)
    {
        Add(GuiItemKind.Label, 3, label++, "环境光 / IBL");
        Add(GuiItemKind.Label, 3, label++, value.Enabled ? $"环境 UUID：{value.AssetId:D}" : "未绑定环境；关闭状态不保留资源身份");
        if (value.Enabled) {
            Add(GuiItemKind.Label, 3, label++, $"资源代次：{value.Generation}");
            Add(GuiItemKind.Label, 3, label++, "SHA256：" + value.ContentHash);
        }
        Add(GuiItemKind.Label, 3, label++, EnvironmentPreparationStatus);
        Add(GuiItemKind.Checkbox, 2, control++, "环境启用（关闭后可撤销恢复）", new("environment_field", objectId, "enabled"),
            number: value.Enabled ? 1 : 0, max: 1, enabled: writable && value.Enabled);
        Add(GuiItemKind.Number, 2, control++, "环境光强度", new("environment_field", objectId, "strength"),
            number: value.Strength, min: 0, max: 16, enabled: writable && value.Enabled);
        Add(GuiItemKind.Number, 2, control++, "环境 Y 旋转（度）", new("environment_field", objectId, "rotationDegrees"),
            number: Math.Clamp(value.RotationRadians * 180 / Math.PI, -180, 180), min: -180, max: 180, enabled: writable && value.Enabled);
        if(workspace.Owner.Environments is null)Add(GuiItemKind.Label, 3, label++, "当前未配置源项目环境作者服务；不自动导入或重新启用资源");
    }

    private bool _environmentReviewed,_environmentReadReviewed;
    private int _environmentOffset;
    private Guid _environmentProposal;
    private readonly HashSet<Guid> _environmentReadSelection=[];
    private EnvironmentReadReview? _environmentReadReview;
    private void CancelEnvironmentReview(){workspace.Owner.Environments?.CancelReview();_environmentReviewed=false;_environmentReadReview=null;_environmentReadReviewed=false;}
    private void BuildEnvironmentAuthoring(Guid target,ref ulong control,ref ulong label,bool writable)
    {
        if(workspace.Owner.Environments is not {} service)return;
        Add(GuiItemKind.Label,3,label++,"环境预设 / 项目目录");
        Add(GuiItemKind.Button,2,control++,"选择合成中性预设…",new("environment_review_neutral",target),enabled:writable);
        Add(GuiItemKind.Button,2,control++,"刷新环境目录",new("environment_refresh"),enabled:writable);
        Add(GuiItemKind.Button,2,control++,"撤销全部环境文件/目录授权",new("environment_revoke"));
        Add(GuiItemKind.Label,3,label++,"仅描述文件与场景进入撤销；不可变派生 NCE 保留，禁止删除使用中资源");
        if(service.Review is {} review) {
            Add(GuiItemKind.Label,3,label++,review.Synthetic?"合成中性环境（非用户 HDR）":"已有项目环境");
            Add(GuiItemKind.Label,3,label++,$"目标对象 {review.ObjectId:D}");
            Add(GuiItemKind.Label,3,label++,$"环境 {review.AssetId:D} / 代次 {review.Generation}");
            Add(GuiItemKind.Label,3,label++,review.Path);Add(GuiItemKind.Label,3,label++,review.ContentHash);
            Add(GuiItemKind.Checkbox,2,control++,"已核对目标、资源和文件发布范围",new("environment_reviewed"),number:_environmentReviewed?1:0,max:1,enabled:writable);
            Add(GuiItemKind.Button,2,control++,"批准并应用预设（一次撤销）",new("environment_approve_apply",review.Review,review.Fingerprint),enabled:writable&&_environmentReviewed);
            Add(GuiItemKind.Button,2,control++,"仅准备预设，供 AI 提议应用",new("environment_approve_prepare",review.Review,review.Fingerprint),enabled:writable&&_environmentReviewed);
            Add(GuiItemKind.Button,2,control++,"取消预设选择",new("environment_cancel"));
        }
        if(_environmentProposal!=Guid.Empty)Add(GuiItemKind.Label,3,label++,"已准备提案（60秒） "+_environmentProposal.ToString("D"));
        var rows=service.Rows(_environmentOffset,8);
        Add(GuiItemKind.Button,2,control++,"上一页环境",new("environment_previous"),enabled:_environmentOffset>0);
        Add(GuiItemKind.Button,2,control++,"下一页环境",new("environment_next"),enabled:service.Rows(_environmentOffset+8,1).Length>0&&_environmentOffset<248);
        foreach(var row in rows) {
            Add(GuiItemKind.Label,3,label++,$"{row.AssetId:D} / {row.Generation} / {row.State}");
            Add(GuiItemKind.Button,2,control++,"选择此环境",new("environment_review_asset",target,row.AssetId.ToString("D")),enabled:writable&&row.State=="registered");
            Add(GuiItemKind.Checkbox,2,control++,"选择此 UUID 的 MCP 只读信息",new("environment_read_select",row.AssetId),number:_environmentReadSelection.Contains(row.AssetId)?1:0,max:1,enabled:writable);
        }
        Add(GuiItemKind.Button,2,control++,"审阅环境目录读取范围与配对客户端",new("environment_read_review"),enabled:writable&&_environmentReadSelection.Count>0&&workspace.Owner.Endpoint is not null);
        if(_environmentReadReview is {} read) {
            foreach(var client in read.Audience)Add(GuiItemKind.Label,3,label++,$"客户端 {client.ConnectionId:D} / {client.ClientName}");
            foreach(var id in read.Assets)Add(GuiItemKind.Label,3,label++,"仅可读 "+id.ToString("D"));
            Add(GuiItemKind.Label,3,label++,"不授权文件读取、修改、GPU 或推理；应用提案仍需单独审批");
            Add(GuiItemKind.Checkbox,2,control++,"已审阅环境 UUID 与全部客户端",new("environment_read_reviewed"),number:_environmentReadReviewed?1:0,max:1,enabled:writable);
            Add(GuiItemKind.Button,2,control++,"批准环境只读信息 60 秒",new("environment_read_approve",Field:read.Fingerprint),enabled:writable&&_environmentReadReviewed);
        }
    }
    private bool ApplyEnvironmentAction(ActionView action,double value)
    {
        if(!action.Kind.StartsWith("environment_",StringComparison.Ordinal)||action.Kind=="environment_field")return false;
        var service=workspace.Owner.Environments??throw new EditRejectedException("environment_authoring_missing");
        switch(action.Kind) {
            case "environment_review_neutral":service.ReviewNeutral(workspace.Stamp,action.Object);_environmentReviewed=false;break;
            case "environment_review_asset":service.ReviewAsset(workspace.Stamp,action.Object,Guid.Parse(action.Field));_environmentReviewed=false;break;
            case "environment_reviewed":if(value is not 0 and not 1)throw new ArgumentException("Review value.");_environmentReviewed=value==1;break;
            case "environment_approve_apply":case "environment_approve_prepare":
                var review=service.Review??throw new EditRejectedException("environment_review_missing");if(review.Review!=action.Object)throw new EditRejectedException("environment_review_stale");
                _environmentProposal=service.Approve(review,action.Field,_environmentReviewed);_environmentReviewed=false;
                if(action.Kind=="environment_approve_apply")service.ApplyLocal(_environmentProposal);break;
            case "environment_cancel":service.CancelReview();_environmentReviewed=false;break;
            case "environment_refresh":service.Synchronize();break;
            case "environment_revoke":service.Revoke();_environmentReadSelection.Clear();_environmentReadReview=null;_environmentProposal=Guid.Empty;break;
            case "environment_previous":_environmentOffset=Math.Max(0,_environmentOffset-8);break;
            case "environment_next":_environmentOffset=Math.Min(248,_environmentOffset+8);break;
            case "environment_read_select":
                if(value is not 0 and not 1)throw new ArgumentException("Read selection value.");
                if(value==0)_environmentReadSelection.Remove(action.Object);else if(_environmentReadSelection.Count<64)_environmentReadSelection.Add(action.Object);
                _environmentReadReview=null;_environmentReadReviewed=false;break;
            case "environment_read_review":_environmentReadReview=service.ReviewRead(_environmentReadSelection.ToArray());_environmentReadReviewed=false;break;
            case "environment_read_reviewed":_environmentReadReviewed=value==1;break;
            case "environment_read_approve":service.ApproveRead(_environmentReadReview??throw new EditRejectedException("environment_read_review_missing"),action.Field,_environmentReadReviewed);_environmentReadReview=null;_environmentReadReviewed=false;break;
            default:throw new ArgumentException("Unknown environment action.");
        }
        return true;
    }
}
