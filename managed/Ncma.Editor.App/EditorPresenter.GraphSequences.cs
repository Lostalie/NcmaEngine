using System.Text.Json;
using Ncma.Animation;
using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Gui;
namespace Ncma.Editor.App;

internal sealed unsafe partial class EditorPresenter
{
    private bool _animSequenceOpen,_animSequenceRoot=true,_animSequenceReviewed;
    private Guid _animSequenceSelected;
    private JsonTextPages _animSequenceJson=new(AnimationSequenceCodec.Encode(new(.1,3,[],[])).GetRawText());
    private int _animSequencePage,_animSequenceReviewPage,_animSequenceResultPage;
    private string _animSequenceResultSection="timeline";
    private AnimationSequenceReview? _animSequenceReview;
    private string[] _animSequenceReviewRows=[];
    private readonly HashSet<int> _animSequenceVisited=[];
    private AnimationSequenceResult? _animSequenceResult;
    private void BuildAnimSequences(float width,float height,ref ulong label)
    {
        if(!_animSequenceOpen||_graphAuthor is null)return;var source=_graphAuthor.Sequences;
        Panel(412,"独立序列分析 / 精确用例审批（不控制Live Play）",width*.1f,height*.1f,width*.8f,height*.78f);
        Add(GuiItemKind.Label,55,label++,"实际NCA -> 独立C#实例；事件只返回数据，根意图没有碰撞结果。\n批准图读取不等于批准用例执行；未接入推理服务。");
        Add(GuiItemKind.Checkbox,55,1,"准备根意图（明确root bone 0）",new("anim_sequence_root",Operation:new GraphIntent(_graphAuthor.Stamp)),number:_animSequenceRoot?1:0,max:1);
        AnimSequenceButton(2,"本机准备精确NCA，不执行","anim_sequence_prepare",!_graphAuthor.HasDraft&&!_page!.State.Frozen);
        if(!source.Prepared){_animSequenceResult=null;_animSequenceReview=null;Add(GuiItemKind.Label,55,label++,"资源未准备或Edit/图/资产身份已变化；旧结果不作为当前结果。");}
        AnimSequenceButton(3,"上一页用例JSON","anim_sequence_previous",_animSequencePage>0);Line();AnimSequenceButton(4,"下一页用例JSON","anim_sequence_next",_animSequencePage+1<_animSequenceJson.Count);
        Add(GuiItemKind.Text,55,5,"闭合测试用例（数据，无代码/路径）",new("anim_sequence_json",Operation:new GraphIntent(_graphAuthor.Stamp)),value:_animSequenceJson.Text(_animSequencePage));
        AnimSequenceButton(6,"加入本机用例（不运行）","anim_sequence_local",source.Prepared);
        foreach(Guid id in source.Pending)Add(GuiItemKind.Button,55,1000+AnimRow(id),"选择用例 "+id.ToString("D"),new("anim_sequence_select",Operation:new GraphIntent(_graphAuthor.Stamp,id)));
        if(source.Prepared&&source.Pending.Contains(_animSequenceSelected)) {
            AnimSequenceButton(7,"本机运行独立用例","anim_sequence_run");AnimSequenceButton(8,"审阅精确用例 / NCA / 配对受众","anim_sequence_review");AnimSequenceButton(9,"取消此用例","anim_sequence_cancel");
        }
        if(_animSequenceReview is{} review) {
            if(!source.IsCurrent(review)){_animSequenceReview=null;_animSequenceReviewed=false;}
            else {
                int pages=Math.Max(1,(_animSequenceReviewRows.Length+14)/15);_animSequenceReviewPage=Math.Clamp(_animSequenceReviewPage,0,pages-1);_animSequenceVisited.Add(_animSequenceReviewPage);
                Add(GuiItemKind.Label,55,label++,$"用例 {review.CaseId:D} 指纹 {review.Fingerprint}\n根意图={review.RootSupported} bone={review.RootBone}; 审阅 {_animSequenceReviewPage+1}/{pages}");
                foreach(string row in _animSequenceReviewRows.Skip(_animSequenceReviewPage*15).Take(15))Add(GuiItemKind.Label,55,label++,row);
                AnimSequenceButton(10,"上一页精确审阅","anim_sequence_review_previous",_animSequenceReviewPage>0);Line();AnimSequenceButton(11,"下一页精确审阅","anim_sequence_review_next",_animSequenceReviewPage+1<pages);
                bool complete=_animSequenceVisited.Count==pages;Add(GuiItemKind.Checkbox,55,12,"已审阅完整用例、所有资源代次和受众",new("anim_sequence_checked",Operation:new GraphIntent(_graphAuthor.Stamp)),number:_animSequenceReviewed?1:0,max:1,enabled:complete);
                AnimSequenceButton(13,"批准精确用例60秒","anim_sequence_approve",complete&&_animSequenceReviewed);
            }
        }
        if(_animSequenceResult is{} result) {
            Add(GuiItemKind.Label,55,label++,$"独立检查 passed={result.Passed}，{result.Timeline.Count}量子；未运行碰撞或Live Play。");
            AnimSequenceButton(18,"状态/根意图","anim_sequence_timeline");Line();AnimSequenceButton(19,"提交事件","anim_sequence_events");Line();AnimSequenceButton(20,"全部断言","anim_sequence_checks");
            string[] rows=_animSequenceResultSection switch {
                "events"=>result.Timeline.SelectMany(t=>t.Events).Select(e=>$"#{e.Context.Tick} marker={e.MarkerId:D}\n{e.UnwrappedTime:F4}s {e.Name} state={e.StateId:D}").ToArray(),
                "checks"=>result.Checks.Select(c=>$"断言 {c.Index}: step={c.Step} {c.Code}").ToArray(),
                _=>result.Timeline.Select(step=>$"#{step.Frame.Context.Tick} state={step.Frame.StateId:D} transition={step.Frame.TransitionId:D}\nweight={step.Frame.TransitionWeight:F3} frozen={step.Frame.FrozenPoseGeneration} events={step.Events.Count} rootX={step.Root?.Translation.X:F5} yaw={step.Root?.Yaw:F5}").ToArray()};
            _animSequenceResultPage=Math.Clamp(_animSequenceResultPage,0,Math.Max(0,(rows.Length-1)/8));
            foreach(string row in rows.Skip(_animSequenceResultPage*8).Take(8))Add(GuiItemKind.Label,55,label++,row);
            AnimSequenceButton(14,"上一页结果","anim_sequence_result_previous",_animSequenceResultPage>0);Line();AnimSequenceButton(15,"下一页结果","anim_sequence_result_next",(_animSequenceResultPage+1)*8<rows.Length);
        }
        AnimSequenceButton(16,"撤销全部序列执行批准","anim_sequence_revoke");AnimSequenceButton(17,"关闭序列面板","anim_sequence_toggle");End();
    }
    private void AnimSequenceButton(ulong id,string label,string kind,bool enabled=true)=>Add(GuiItemKind.Button,55,id,label,new(kind,Operation:new GraphIntent(_graphAuthor!.Stamp)),enabled:enabled);
    private bool ApplyAnimSequence(ActionView action,GuiEvent e,string text)
    {
        if(!action.Kind.StartsWith("anim_sequence_",StringComparison.Ordinal))return false;if(e.Phase!=3)return true;
        var service=_graphAuthor!.Sequences;
        switch(action.Kind) {
            case "anim_sequence_toggle":_animSequenceOpen=!_animSequenceOpen;break;
            case "anim_sequence_root":if(e.Value is not (0 or 1))throw new ArgumentException("Boolean root option.");_animSequenceRoot=e.Value==1;break;
            case "anim_sequence_prepare":service.PrepareTrusted(_animSequenceRoot);_animSequenceResult=null;_animSequenceReview=null;break;
            case "anim_sequence_previous":_animSequencePage--;break;case "anim_sequence_next":_animSequencePage++;break;
            case "anim_sequence_json":_animSequenceJson=new(_animSequenceJson.Replace(_animSequencePage,text));_animSequencePage=Math.Min(_animSequencePage,_animSequenceJson.Count-1);break;
            case "anim_sequence_local":using(var parsed=JsonDocument.Parse(_animSequenceJson.Replace(_animSequencePage,_animSequenceJson.Text(_animSequencePage))))_animSequenceSelected=service.ProposeLocal(AnimationSequenceCodec.Decode(parsed.RootElement));_animSequenceReview=null;_animSequenceResult=null;break;
            case "anim_sequence_select":_animSequenceSelected=(Guid)((GraphIntent)action.Operation!).Payload!;_animSequenceResult=null;_animSequenceReview=null;break;
            case "anim_sequence_run":_animSequenceResult=null;_animSequenceResult=service.RunLocal(_animSequenceSelected);_animSequenceResultPage=0;break;
            case "anim_sequence_cancel":service.Cancel(_animSequenceSelected);_animSequenceSelected=Guid.Empty;_animSequenceResult=null;_animSequenceReview=null;break;
            case "anim_sequence_review":
                _animSequenceReview=service.CaptureReview(_animSequenceSelected);_animSequenceReviewed=false;_animSequenceReviewPage=0;_animSequenceVisited.Clear();var r=_animSequenceReview;
                _animSequenceReviewRows=new[]{$"图 {r.GraphId:D} SHA256 {r.GraphHash} 事件SHA256 {r.EventHash}",$"用例SHA256 {r.CaseHash} 发布 {r.Publication:D} Endpoint {r.Endpoint:D} AudienceEpoch {r.AudienceEpoch}"}
                    .Concat(r.Resources.Select(p=>$"{p.Kind} {p.Id:D} generation={p.Generation} SHA256={p.ContentHash}"))
                    .Concat(r.Audience.Select(id=>"共享配对受众 "+id.ToString("D")))
                    .Concat(InspectionText.Split(AnimationSequenceCodec.Encode(service.CaseCopy(_animSequenceSelected)).GetRawText(),48*1024)).ToArray();break;
            case "anim_sequence_review_previous":_animSequenceReviewPage--;break;case "anim_sequence_review_next":_animSequenceReviewPage++;break;
            case "anim_sequence_checked":if(e.Value is not (0 or 1))throw new ArgumentException("Boolean reviewed.");_animSequenceReviewed=e.Value==1;break;
            case "anim_sequence_approve":var review=_animSequenceReview??throw new EditRejectedException("sequence_review_missing");if(_animSequenceVisited.Count!=Math.Max(1,(_animSequenceReviewRows.Length+14)/15))throw new EditRejectedException("sequence_review_incomplete");service.Approve(review,review.Fingerprint,_animSequenceReviewed);_animSequenceReview=null;_animSequenceReviewed=false;break;
            case "anim_sequence_result_previous":_animSequenceResultPage--;break;case "anim_sequence_result_next":_animSequenceResultPage++;break;
            case "anim_sequence_timeline":_animSequenceResultSection="timeline";_animSequenceResultPage=0;break;case "anim_sequence_events":_animSequenceResultSection="events";_animSequenceResultPage=0;break;case "anim_sequence_checks":_animSequenceResultSection="checks";_animSequenceResultPage=0;break;
            case "anim_sequence_revoke":service.Revoke();_animSequenceReview=null;_animSequenceResult=null;_animSequenceReviewed=false;break;
            default:throw new ArgumentException("Sequence UI intent.");
        }
        return true;
    }
}
