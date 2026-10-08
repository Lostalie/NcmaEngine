using System.Text.Json;
using Ncma.Animation;
using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Gui;
namespace Ncma.Editor.App;

internal sealed unsafe partial class EditorPresenter
{
    private bool _animBonesOpen,_animBonesChecked;private int _animBonePage,_animBoneReviewPage,_animLayerPage;
    private readonly HashSet<int> _animBoneVisited=[];private AnimationSkeletonReview? _animBoneReview;private string[] _animBoneRows=[];
    private readonly record struct LayerProperty(Guid Node,string Field,string Path="");
    private void AddAnimLayer(AnimationGraphDefinition d,AnimationNodeKind kind)
    {
        var rig=_graphAuthor!.Skeletons.LocalCopy();Guid reference=Guid.Empty;
        if(kind==AnimationNodeKind.LayerAdditive&&(!Guid.TryParseExact(_animClip,"D",out reference)||reference==Guid.Empty))throw new ArgumentException("明确输入参考Clip UUID，不猜测参考pose。");
        var n=AnimationGraphNode.Create(Guid.NewGuid(),kind.ToString(),kind) with{X=64+d.Nodes.Length*12,Y=96,Weight=1,Layer=new(new(Guid.NewGuid(),rig.Id,rig.ContentHash,[new(rig.Bones[0].Path,0)]),reference,0)};
        _graphAuthor.ApplyDraft(AnimationGraphEdits.Operations(new{op="node.upsert",node=n}));SynchronizeGraph();_animCanvas.Select(n.Id);_animElement=n.Id;_animSection="node";_animJson=null;
    }
    private void BuildLayerProperties(AnimationGraphNode n,bool writable)
    {
        var l=n.Layer!;ulong id=100;
        void Field(string field,string title,string value)=>Add(GuiItemKind.Text,57,id++,title,new("anim_bones_property",Operation:new GraphIntent(_graphAuthor!.Stamp,new LayerProperty(n.Id,field))),value:value,enabled:writable);
        Field("hash","精确骨架SHA256（不自动重绑）",l.Mask.SkeletonHash);
        if(n.Kind==AnimationNodeKind.LayerAdditive){Field("reference","参考Clip UUID（显式）",l.ReferenceClip.ToString("D"));Add(GuiItemKind.Number,57,id++,"参考秒数（不启动clock）",new("anim_bones_property",Operation:new GraphIntent(_graphAuthor!.Stamp,new LayerProperty(n.Id,"time"))),number:l.ReferenceTime,min:0,max:600,enabled:writable);}
        var rows=l.Mask.Bones.OrderBy(b=>b.BonePath,StringComparer.Ordinal).ToArray();_animLayerPage=Math.Clamp(_animLayerPage,0,Math.Max(0,(rows.Length-1)/4));
        foreach(var bone in rows.Skip(_animLayerPage*4).Take(4)){Add(GuiItemKind.Number,57,id++,bone.BonePath+" / 权重",new("anim_bones_weight",Operation:new GraphIntent(_graphAuthor!.Stamp,new LayerProperty(n.Id,"weight",bone.BonePath))),number:bone.Weight,min:0,max:1,enabled:writable);Add(GuiItemKind.Button,57,id++,"移除此骨",new("anim_bones_delete",Operation:new GraphIntent(_graphAuthor!.Stamp,new LayerProperty(n.Id,"delete",bone.BonePath))),enabled:writable);}
        Add(GuiItemKind.Button,57,10,"上一页遮罩",new("anim_bones_layer_previous",Operation:new GraphIntent(_graphAuthor!.Stamp)),enabled:_animLayerPage>0);Line();Add(GuiItemKind.Button,57,11,"下一页遮罩",new("anim_bones_layer_next",Operation:new GraphIntent(_graphAuthor!.Stamp)),enabled:(_animLayerPage+1)*4<rows.Length);
        Add(GuiItemKind.Label,57,12,"未列骨权重0；根只能0。从精确骨清单添加，保存/预览须重新审阅实际资源。");
        if(_graphAuthor.Skeletons.Prepared)foreach(var b in _graphAuthor.Skeletons.LocalPage(_animBonePage*4).Bones){Add(GuiItemKind.Button,57,id++,"加入 / 保留 "+b.Path,new("anim_bones_add",Operation:new GraphIntent(_graphAuthor.Stamp,new LayerProperty(n.Id,"add",b.Path))),enabled:writable&&!rows.Any(v=>v.BonePath==b.Path));}
    }
    private void BuildAnimBones(float width,float height,ref ulong label)
    {
        if(!_animBonesOpen||_graphAuthor is null)return;var service=_graphAuthor.Skeletons;Panel(413,"实际骨骼身份 / 独立只读审批（不是pose内存）",width*.1f,height*.1f,width*.8f,height*.78f);
        void Button(ulong id,string text,string action,bool enabled=true)=>Add(GuiItemKind.Button,58,id,text,new(action,Operation:new GraphIntent(_graphAuthor.Stamp)),enabled:enabled);
        Button(1,"本机准备NCA骨架（不执行）","anim_bones_prepare",!_graphAuthor.HasDraft&&!_page!.State.Frozen);
        if(service.Prepared){var page=service.LocalPage(_animBonePage*4);Add(GuiItemKind.Label,58,label++,$"骨架 {page.SkeletonId:D}\n{page.Hash} / {page.Total}bones，数据复制无World/pose/native handles");foreach(var b in page.Bones)Add(GuiItemKind.Label,58,label++,b.Path+" / parent="+b.Parent);
            Button(2,"上一页骨骼","anim_bones_previous",_animBonePage>0);Line();Button(3,"下一页骨骼","anim_bones_next",page.NextOffset.HasValue);Button(4,"审阅骨架/NCA/精确图/配对受众","anim_bones_review",!_graphAuthor.HasDraft);
        }else{_animBoneReview=null;Add(GuiItemKind.Label,58,label++,"尚未准备或身份已变化；无可用旧结果。");}
        if(_animBoneReview is{} review){if(!service.IsCurrent(review)){_animBoneReview=null;_animBonesChecked=false;}else{
            int pages=Math.Max(1,(_animBoneRows.Length+11)/12);_animBoneReviewPage=Math.Clamp(_animBoneReviewPage,0,pages-1);_animBoneVisited.Add(_animBoneReviewPage);foreach(string row in _animBoneRows.Skip(_animBoneReviewPage*12).Take(12))Add(GuiItemKind.Label,58,label++,row);
            Button(5,"上一页完整审阅","anim_bones_review_previous",_animBoneReviewPage>0);Line();Button(6,"下一页完整审阅","anim_bones_review_next",_animBoneReviewPage+1<pages);bool complete=_animBoneVisited.Count==pages;
            Add(GuiItemKind.Checkbox,58,7,"已审阅全部骨骼/代次/图/受众（不批准修改）",new("anim_bones_checked",Operation:new GraphIntent(_graphAuthor.Stamp)),number:_animBonesChecked?1:0,max:1,enabled:complete);Button(8,"批准精确只读骨清单60秒","anim_bones_approve",complete&&_animBonesChecked);
        }}Button(9,"撤销骨清单读取","anim_bones_revoke");Button(10,"关闭骨清单","anim_bones_toggle");End();
    }
    private bool ApplyAnimBones(ActionView action,GuiEvent e,string text)
    {
        if(!action.Kind.StartsWith("anim_bones_",StringComparison.Ordinal))return false;if(e.Phase!=3)return true;var service=_graphAuthor!.Skeletons;
        switch(action.Kind){
            case "anim_bones_toggle":_animBonesOpen=!_animBonesOpen;break;
            case "anim_bones_prepare":service.PrepareTrusted();_animBoneReview=null;_animBonePage=0;break;
            case "anim_bones_previous":_animBonePage=Math.Max(0,_animBonePage-1);break;case "anim_bones_next":_animBonePage++;break;
            case "anim_bones_review":_animBoneReview=service.CaptureReview();_animBoneReviewPage=0;_animBoneVisited.Clear();_animBonesChecked=false;var r=_animBoneReview;_animBoneRows=new[]{$"图 {r.Stamp.Graph:D} / {r.GraphHash}",$"骨架 {r.SkeletonId:D} / {r.SkeletonHash}",$"publication {r.Publication:D} / endpoint {r.Endpoint:D} / epoch {r.AudienceEpoch}",r.Fingerprint}.Concat(r.Resources.Select(v=>$"{v.Id:D} {v.Kind} {v.Generation} {v.ContentHash}")).Concat(r.Audience.Select(v=>"受众 "+v.ToString("D"))).Concat(service.LocalCopy().Bones.Select(b=>b.Path+" parent="+b.Parent)).ToArray();break;
            case "anim_bones_review_previous":_animBoneReviewPage--;break;case "anim_bones_review_next":_animBoneReviewPage++;break;
            case "anim_bones_checked":if(e.Value is not (0 or 1))throw new ArgumentException("Boolean bone review.");_animBonesChecked=e.Value==1;break;
            case "anim_bones_approve":if(_animBoneReview is null||_animBoneVisited.Count!=Math.Max(1,(_animBoneRows.Length+11)/12))throw new EditRejectedException("complete_bone_review_required");service.Approve(_animBoneReview,_animBoneReview.Fingerprint,_animBonesChecked);break;
            case "anim_bones_revoke":service.Revoke();_animBoneReview=null;break;
            case "anim_bones_layer_previous":_animLayerPage=Math.Max(0,_animLayerPage-1);break;case "anim_bones_layer_next":_animLayerPage++;break;
            default:
                var p=(LayerProperty)((GraphIntent)action.Operation!).Payload!;var n=_graphAuthor.Capture()!.Nodes.Single(v=>v.Id==p.Node);var l=n.Layer!;
                if(action.Kind=="anim_bones_delete")_graphAuthor.ApplyDraft(AnimationGraphEdits.Operations(new{op="layer.bone.delete",nodeId=n.Id,bonePath=p.Path}));
                else if(action.Kind is "anim_bones_add" or "anim_bones_weight"){
                    float weight=action.Kind=="anim_bones_add"?0:(float)e.Value;if(!double.IsFinite(e.Value)||e.Value is <0 or >1)throw new ArgumentException("Finite bone weight.");
                    if(action.Kind=="anim_bones_add"&&!service.LocalCopy().Bones.Any(b=>b.Path==p.Path))throw new EditRejectedException("exact_prepared_bone_required");
                    _graphAuthor.ApplyDraft(AnimationGraphEdits.Operations(new{op="layer.bone.upsert",nodeId=n.Id,bone=new AnimationBoneWeight(p.Path,weight)}));
                }else if(action.Kind=="anim_bones_property"){
                    l=p.Field switch{"hash"=>l with{Mask=l.Mask with{SkeletonHash=text}},"reference"=>Guid.TryParseExact(text,"D",out Guid id)&&id!=Guid.Empty&&id.ToString("D")==text?l with{ReferenceClip=id}:throw new ArgumentException("Explicit reference Clip UUID."),"time"=>l with{ReferenceTime=e.Value},_=>throw new ArgumentException("Layer field.")};
                    _graphAuthor.ApplyDraft(AnimationGraphEdits.Operations(new{op="node.upsert",node=n with{Layer=l}}));
                }else throw new ArgumentException("Bone action.");
                _animJson=null;SynchronizeGraph();break;
        }return true;
    }
}
