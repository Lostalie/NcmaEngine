using Ncma.Editor.Services;
using Ncma.Rendering.Scene;
using Ncma.Scene.Rendering;
using Ncma.Gui;
using System.Text.Json;
namespace Ncma.Editor.App;

internal sealed unsafe partial class EditorPresenter
{
    private sealed record PostPreparationView(Guid World,ulong Revision,Guid Play,bool Ready,bool Failed,PostProcessData? Prepared,ulong Frame,EditorSceneSourceStamp? Authoring);
    private PostPreparationView? _postPreparation;
    private int _postModuleOffset;
    private readonly HashSet<Guid> _postReadSelection=[];
    private PostModulesReadReview? _postReadReview;private bool _postReadReviewed;
    internal void ShowPostPreparation(Guid world,ulong revision,Guid play,bool ready,bool failed,PostProcessData? prepared,ulong frame=0,EditorSceneSourceStamp? authoring=null)=>_postPreparation=new(world,revision,play,ready,failed,prepared,frame,authoring);
    internal string PostPreparationStatus {
        get {
            var play=workspace.Owner.Play;var world=play?.Scene.World;
            if(_postPreparation is not {} state||state.Play!=(play?.SessionId??Guid.Empty)||
                (world is null ? state.Authoring is not {} source || !source.Matches(workspace.Owner) : state.Authoring is not null || state.World!=world.Identity || state.Revision!=world.Revision))return "后处理：等待当前版本";
            string prefix=play is null?"后处理：":"后处理（Play，编辑冻结）：";
            if(state.Failed)return prefix+"失败；视口已清空，详见控制台";
            if(!state.Ready||state.Prepared is null)return prefix+"等待当前版本";
            return prefix+(state.Prepared.Value.Enabled?"就绪":"关闭")+$" / revision {state.Revision} / 最近提交帧 {state.Frame}";
        }
    }
    private void CancelPostReview(){_postReadReview=null;_postReadReviewed=false;}
    private PostProcessData PostValue()=>_page!.Selected!.Components.Single(c=>c.TypeId==PostProcessData.TypeId).Data.Deserialize<PostProcessData>(DataJson);
    private static bool LegalPost(Func<PostProcessData> build){try{_=build();return true;}catch(ArgumentException){return false;}}
    private void BuildPostInspector(Guid target,PostProcessData value,ref ulong control,ref ulong label,bool writable)
    {
        if(workspace.Owner.PostProcessing is not {} service)return;var modules=service.Modules;var descriptions=modules.Describe();
        Add(GuiItemKind.Label,3,label++,"HDR 后处理 / 线性 Feature → Tone → 显示 UI");Add(GuiItemKind.Label,3,label++,PostPreparationStatus);
        Add(GuiItemKind.Label,3,label++,workspace.HasDraft?"草稿未提交，不代表已渲染；松开提交为一次撤销":"开关用于当前场景对照；Undo 可恢复，不绕过场景事务");
        Add(GuiItemKind.Checkbox,2,control++,"启用后处理 / 关闭对照",new("post_field",target,"enabled"),number:value.Enabled?1:0,max:1,enabled:writable);
        Add(GuiItemKind.Number,2,control++,"曝光 EV",new("post_field",target,"ev"),number:value.ExposureEv,min:-6,max:4,enabled:writable);
        Add(GuiItemKind.Label,3,label++,"Tone："+(descriptions.FirstOrDefault(m=>m.ModuleId==value.ToneModule)?.Name??value.ToneModule.ToString("D")));
        var stack=PostProcessData.DecodeStack(value.StackJson);
        foreach(var feature in stack){
            var m=descriptions.SingleOrDefault(m=>m.ModuleId==feature.ModuleId);if(m is null){Add(GuiItemKind.Label,3,label++,"未知 Feature，等待修正注册配置");continue;}
            Add(GuiItemKind.Label,3,label++,$"{m.Name} / v{m.Version} / 顺序 {m.Order}");
            foreach(var rule in m.Parameters){var p=feature.Parameters.SingleOrDefault(p=>p.Name==rule.Name);if(p is null)continue;
                Add(GuiItemKind.Number,2,control++,m.Name+" / "+rule.Name+(rule.Integer?"（整数）":""),new("post_field",target,feature.ModuleId.ToString("D")+"/"+rule.Name),number:p.Value,min:rule.Minimum,max:rule.Maximum,enabled:writable);}
            foreach(int direction in new[]{-1,1,0})Add(GuiItemKind.Button,2,control++,(direction==-1?"上移 ":direction==1?"下移 ":"移除 ")+m.Name,
                new("post_feature",target,m.ModuleId.ToString("D"),direction),enabled:writable&&LegalPost(()=>RenderingEditorAdapter.PostFeature(modules,value,m.ModuleId,direction)));
        }
    }
    private void BuildPostCatalog(Guid target,ref ulong control,ref ulong label,bool writable)
    {
        if(workspace.Owner.PostProcessing is not {} service)return;var descriptions=service.Modules.Describe();
        bool has=_page!.Selected!.Components.Any(c=>c.TypeId==PostProcessData.TypeId);var value=has?PostValue():PostProcessData.Off;
        Add(GuiItemKind.Label,3,label++,"Low v1：512 软阴影 / 移除可选后处理；保留 Tone 与曝光，可一次撤销");
        Add(GuiItemKind.Button,2,control++,"应用场景画质：Low v1",new("post_quality_low",target),
            enabled:writable&&!workspace.HasDraft&&workspace.Owner.Document.Components.Describe().Any(c=>c.TypeId==SceneShadowData.TypeId));
        if(!workspace.HasSceneComponent(PostProcessData.TypeId))Button(control++,"添加后处理配置（关闭）","transaction",writable,target,RenderingEditorAdapter.PostOperation(service.Modules,target,PostProcessData.Off));
        Add(GuiItemKind.Label,3,label++,"注册后处理模块 / 无源码执行入口");
        _postModuleOffset=Math.Clamp(_postModuleOffset,0,Math.Max(0,(descriptions.Length-1)/8*8));
        Add(GuiItemKind.Button,2,control++,"上一页后处理模块",new("post_page",Index:-8),enabled:_postModuleOffset>0);
        Add(GuiItemKind.Button,2,control++,"下一页后处理模块",new("post_page",Index:8),enabled:_postModuleOffset+8<descriptions.Length);
        foreach(var m in descriptions.Skip(_postModuleOffset).Take(8)){
            Add(GuiItemKind.Label,3,label++,$"{m.Name} [{m.Kind}] v{m.Version} / {m.ModuleId:D}");
            foreach(var dependency in m.Requires)Add(GuiItemKind.Label,3,label++,"需要先于本模块："+dependency.ToString("D"));
            if(m.Kind=="tone")Add(GuiItemKind.Button,2,control++,"选择 Tone："+m.Name,new("post_tone",target,m.ModuleId.ToString("D")),enabled:writable&&has);
            else Add(GuiItemKind.Button,2,control++,"添加 Feature："+m.Name,new("post_feature",target,m.ModuleId.ToString("D"),2),enabled:writable&&has&&LegalPost(()=>RenderingEditorAdapter.PostFeature(service.Modules,value,m.ModuleId,2)));
            Add(GuiItemKind.Checkbox,2,control++,"MCP 只读："+m.Name,new("post_read_select",m.ModuleId),number:_postReadSelection.Contains(m.ModuleId)?1:0,max:1,enabled:writable);
        }
        Add(GuiItemKind.Button,2,control++,"审阅后处理模块读取范围",new("post_read_review"),enabled:writable&&_postReadSelection.Count>0&&workspace.Owner.Endpoint is not null);
        Add(GuiItemKind.Button,2,control++,"撤销后处理目录读取授权",new("post_read_revoke"));
        if(_postReadReview is {} read){
            foreach(var client in read.Audience)Add(GuiItemKind.Label,3,label++,$"客户端 {client.ConnectionId:D} / {client.ClientName}");
            foreach(var id in read.Modules)Add(GuiItemKind.Label,3,label++,"仅可读模块 "+id.ToString("D"));
            Add(GuiItemKind.Label,3,label++,"只读名称/参数/顺序；修改仍需独立审批，不授权文件、编译或 GPU");
            Add(GuiItemKind.Checkbox,2,control++,"已核对后处理模块与客户端",new("post_read_reviewed"),number:_postReadReviewed?1:0,max:1,enabled:writable);
            Add(GuiItemKind.Button,2,control++,"批准后处理目录读取 60 秒",new("post_read_approve",Field:read.Fingerprint),enabled:writable&&_postReadReviewed);
        }
    }
    private bool ApplyPostAction(ActionView action,double number)
    {
        if(!action.Kind.StartsWith("post_",StringComparison.Ordinal)||action.Kind=="post_field")return false;
        var service=workspace.Owner.PostProcessing??throw new ArgumentException("Post authoring unavailable.");
        switch(action.Kind){
            case "post_quality_low":
                // A queued click can predate a draft in the same frame. Ignore it without
                // throwing into the generic error handler, which would cancel that draft.
                if(workspace.HasDraft)return true;
                var request=RenderingEditorAdapter.QualityRequest(workspace.Owner.Edit!,action.Object,
                    SceneQualityPresets.LowV1,service.Modules,_page!.Stamp.Revision);
                workspace.Transaction(_page.Stamp,"Apply scene quality: Low v1",
                    request.Input.GetProperty("operations").EnumerateArray().Select(o=>(object)o.Clone()).ToArray(),action.Object);break;
            case "post_tone":case "post_feature":
                var next=action.Kind=="post_tone"?RenderingEditorAdapter.PostTone(service.Modules,PostValue(),Guid.Parse(action.Field)):
                    RenderingEditorAdapter.PostFeature(service.Modules,PostValue(),Guid.Parse(action.Field),action.Index);
                workspace.Transaction(_page!.Stamp,"Configure post processing",[RenderingEditorAdapter.PostOperation(service.Modules,action.Object,next)],action.Object);break;
            case "post_page":_postModuleOffset=Math.Clamp(_postModuleOffset+action.Index,0,56);break;
            case "post_read_select":if(number is not 0 and not 1)throw new ArgumentException("Module selection boolean.");if(number==0)_postReadSelection.Remove(action.Object);else _postReadSelection.Add(action.Object);CancelPostReview();break;
            case "post_read_review":_postReadReview=service.ReviewRead(_postReadSelection.ToArray());_postReadReviewed=false;break;
            case "post_read_reviewed":if(number is not 0 and not 1)throw new ArgumentException("Review boolean.");_postReadReviewed=number==1;break;
            case "post_read_approve":service.ApproveRead(_postReadReview??throw new ArgumentException("Review required."),action.Field,_postReadReviewed);CancelPostReview();break;
            case "post_read_revoke":service.RevokeRead();CancelPostReview();break;
            default:throw new ArgumentException("Unknown post action.");
        }
        return true;
    }
}
