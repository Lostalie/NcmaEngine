using System.Text.Json;
using Ncma.Editor.Core;
using Ncma.Gui;
namespace Ncma.Editor.App;

internal sealed unsafe partial class EditorPresenter
{
    private AnimationRuntimeInspectionService? _animationRuntime;
    private bool _showAnimationRuntime,_animationRuntimeReviewed;
    private Guid _animationRuntimeSelected;
    private AnimationRuntimeReview? _animationRuntimeReview;
    private string[] _animationRuntimeReviewRows=[];
    private int _animationRuntimeReviewPage;
    private readonly HashSet<int> _animationRuntimeVisited=[];
    internal void AttachAnimationRuntime(AnimationRuntimeInspectionService service)=>_animationRuntime=service;
    private void BuildAnimationRuntime(float width,float height,ref ulong label)
    {
        if(!_showAnimationRuntime||_animationRuntime is null)return;
        Panel(411,"动画图运行观察 / 独立只读审批",width*.1f,height*.12f,width*.8f,height*.75f);
        var bindings=_animationRuntime.LocalBindings();
        if(bindings.Length==0){Add(GuiItemKind.Label,3,label++,"需要已准备的 Animator Play；不会启动或推进 Play。");_animationRuntimeReview=null;End();return;}
        foreach(var binding in bindings.Take(32))Add(GuiItemKind.Button,52,(ulong)Array.IndexOf(bindings,binding)+100,"选择图角色 "+binding.ObjectId.ToString("D"),new("animruntime_select",binding.ObjectId));
        if(bindings.Any(b=>b.ObjectId==_animationRuntimeSelected)) {
            try{var local=JsonSerializer.Serialize(_animationRuntime.LocalObservation(_animationRuntimeSelected),Ncma.Editor.Protocol.Wire.Json);foreach(string line in Ncma.Editor.Services.InspectionText.Split(local,1800).Take(8))Add(GuiItemKind.Label,3,label++,line);}
            catch(InvalidOperationException){Add(GuiItemKind.Label,3,label++,"运行观察已失效；不显示旧帧为当前成功帧。");}
            Add(GuiItemKind.Button,52,1,"审阅精确图 / NCA闭包 / Play / 受众",new("animruntime_review"));
        }
        var grant=_animationRuntime.Grant;Add(GuiItemKind.Label,3,label++,$"图运行读取批准 {grant.Objects}/8，剩余 {grant.Seconds}s；无 live Step / 输入权限。");
        Add(GuiItemKind.Button,52,5,"撤销图运行读取",new("animruntime_revoke"));
        if(_animationRuntimeReview is {} review) {
            if(!_animationRuntime.IsCurrent(review)){_animationRuntimeReview=null;_animationRuntimeReviewed=false;}
            else {
                Add(GuiItemKind.Label,3,label++,$"Play {review.PlaySessionId:D} World {review.WorldId:D} Endpoint {review.EndpointId:D} AudienceEpoch {review.AudienceRevision}");
                _animationRuntimeVisited.Add(_animationRuntimeReviewPage);
                int pages=Math.Max(1,(_animationRuntimeReviewRows.Length+15)/16);
                Add(GuiItemKind.Label,3,label++,$"身份审批第 {_animationRuntimeReviewPage+1}/{pages} 页，已查看 {_animationRuntimeVisited.Count} 页");
                foreach(string row in _animationRuntimeReviewRows.Skip(_animationRuntimeReviewPage*16).Take(16))Add(GuiItemKind.Label,3,label++,row);
                Add(GuiItemKind.Button,52,6,"上一页资源身份",new("animruntime_previous"),enabled:_animationRuntimeReviewPage>0);
                Add(GuiItemKind.Button,52,7,"下一页资源身份",new("animruntime_next"),enabled:_animationRuntimeReviewPage+1<pages);
                bool complete=_animationRuntimeVisited.Count==pages;
                Add(GuiItemKind.Checkbox,52,3,"已审阅全部图 / 资源身份 / Play与受众",new("animruntime_reviewed"),number:_animationRuntimeReviewed?1:0,max:1,enabled:complete);
                Add(GuiItemKind.Button,52,4,"批准图运行只读60秒",new("animruntime_approve",Field:review.Fingerprint),enabled:_animationRuntimeReviewed&&complete);
            }
        }
        End();
    }
    private bool ApplyAnimationRuntime(ActionView action,double value)
    {
        if(!action.Kind.StartsWith("animruntime_",StringComparison.Ordinal))return false;
        if(_animationRuntime is null)throw new EditRejectedException("animator_service_missing");
        switch(action.Kind){
            case "animruntime_toggle":_showAnimationRuntime=!_showAnimationRuntime;break;
            case "animruntime_select":_animationRuntimeSelected=action.Object;_animationRuntimeReview=null;_animationRuntimeReviewed=false;break;
            case "animruntime_review":
                _animationRuntimeReview=_animationRuntime.Capture([_animationRuntimeSelected]);_animationRuntimeReviewed=false;_animationRuntimeReviewPage=0;_animationRuntimeVisited.Clear();
                _animationRuntimeReviewRows=_animationRuntimeReview.Bindings.SelectMany(b=>new[]{$"对象 {b.ObjectId:D} 图 {b.GraphId:D} SHA256 {b.GraphHash} 资源发布 {b.PublicationId:D}"}.Concat(b.Resources.Select(r=>$"{r.Kind} {r.Id:D} generation {r.Generation} SHA256 {r.ContentHash}"))).Concat(_animationRuntimeReview.Audience.Select(id=>"共享配对受众 "+id.ToString("D"))).ToArray();break;
            case "animruntime_previous":_animationRuntimeReviewPage=Math.Max(0,_animationRuntimeReviewPage-1);break;
            case "animruntime_next":_animationRuntimeReviewPage=Math.Min(Math.Max(0,(_animationRuntimeReviewRows.Length-1)/16),_animationRuntimeReviewPage+1);break;
            case "animruntime_reviewed":if(value is not (0 or 1))throw new EditRejectedException("animator_review_invalid");_animationRuntimeReviewed=value==1;break;
            case "animruntime_approve":var page=_animationRuntimeReview??throw new EditRejectedException("animator_review_missing");if(_animationRuntimeVisited.Count!=Math.Max(1,(_animationRuntimeReviewRows.Length+15)/16))throw new EditRejectedException("animator_review_incomplete");_animationRuntime.Approve(page,action.Field,_animationRuntimeReviewed);_animationRuntimeReview=null;_animationRuntimeReviewed=false;break;
            case "animruntime_revoke":_animationRuntime.Revoke();_animationRuntimeReview=null;_animationRuntimeReviewed=false;break;
            default:throw new EditRejectedException("animator_intent_invalid");
        }return true;
    }
}
