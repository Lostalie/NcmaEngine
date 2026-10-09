using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Ncma.Animation;
using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Gui;
using Ncma.Platform;
using Ncma.Rendering;
namespace Ncma.Editor.App;

internal sealed unsafe partial class EditorPresenter
{
    private EditorAnimationGraphWorkspace? _graphAuthor; private EditorAnimationGraphPreview? _graphPreview;
    private bool _graphMode,_animReviewed,_animFileReviewed,_animPreviewRunning,_animPan,_animMarquee;
    private readonly AnimationGraphCanvas _animCanvas=new(); private AnimationGraphCanvas? _animGesture;
    private Vector2 _animStart,_animLast; private AnimationGraphPin? _animFrom;
    private AnimationGraphDefinition? _animDefinition; private AnimationGraphEditorStamp _animStamp;
    private Guid _animProposal,_animElement; private string _animSection="node",_animSearch="",_animRig="",_animClip="";
    private JsonTextPages? _animJson,_animJsonEdit; private int _animJsonPage,_animReviewPage,_animOffset,_animPreviewInsert;
    private int _animElementOffset;
    private (float X,float Y,float Width,float Height) _animArea,_animPreviewArea;
    private readonly Dictionary<Guid,ulong> _animRows=[];private ulong _animRow;
    private AnimationParameter[] _animPreviewParameters=[];private readonly Dictionary<Guid,double> _animPreviewValues=[];
    private readonly record struct GraphIntent(AnimationGraphEditorStamp Stamp,object? Payload=null);
    public bool GraphWorkspaceActive=>_graphMode;
    public bool GraphPreviewSubmitted {get;private set;}
    internal void AttachGraphAuthoring(EditorAnimationGraphWorkspace service,EditorAnimationGraphPreview? preview=null){_graphAuthor=service;_graphs=service.Reads;_graphPreview=preview;}
    public void SynchronizeGraph()
    {
        if(_graphAuthor is null)return;
        try { _graphAuthor.Synchronize();if(_animStamp!=_graphAuthor.Stamp){_animStamp=_graphAuthor.Stamp;_animDefinition=_graphAuthor.Capture();if(_animDefinition is not null)_animCanvas.Load(_animDefinition);if(_animGesture is null)_animJson=null;_animReviewed=false;_animPreviewRunning=false;_graphPreview?.Close();} }
        catch(Exception e) when(e is ArgumentException or InvalidOperationException or System.IO.IOException){Record("Animation graph: "+e.Message);}
    }
    public void AdvanceGraphPreview(double delta){if(_graphMode&&_animPreviewRunning)_graphPreview?.Advance(delta);}
    public GuiFrame AttachGraphPreview(ulong frame)
    {
        GraphPreviewSubmitted=false;
        if(!_graphMode||_graphPreview?.Prepared!=true)return _frame;
        var area=_animPreviewArea;var token=_graphPreview.Draw(frame,(uint)Math.Clamp(area.Width,1,4096),(uint)Math.Clamp(area.Height,1,4096));
        if(token is {} image){GraphPreviewSubmitted=true;_items.Insert(_animPreviewInsert,GuiItem.Image(51,999,image,area.X,area.Y,area.Width,area.Height));}
        _frame.ItemCount=(uint)_items.Count;_frame.TextBytes=(uint)_text.Count;return _frame;
    }
    private void CancelGraph(){_graphAuthor?.Cancel();_animGesture=null;_animEventDrag=_spaceDrag=_montageDrag=Guid.Empty;_animJsonEdit=null;_animFrom=null;_animPan=_animMarquee=false;_animProposal=Guid.Empty;_animReviewed=false;_animPreviewRunning=false;_graphPreview?.Close();}
    private void AnimButton(ulong id,string label,string kind,object? payload=null,bool enabled=true)=>Add(GuiItemKind.Button,50,id,label,new(kind,Operation:new GraphIntent(_graphAuthor!.Stamp,payload)),enabled:enabled);
    private ulong AnimRow(Guid id){if(!_animRows.TryGetValue(id,out ulong row)){if(_animRows.Count>=4096)_animRows.Clear();_animRows[id]=row=++_animRow;}return row;}
    private void AnimText(ulong id,string label,string action,string value)=>Add(GuiItemKind.Text,50,id,label,new(action,Operation:new GraphIntent(_graphAuthor!.Stamp)),value:value);
    private GuiFrame BuildGraphEditor(ulong frameId)
    {
        GraphPreviewSubmitted=false;
        ulong label=52000;var author=_graphAuthor!;var d=_animDefinition;bool writable=author.Writable;
        Region(401,"动画图 · 元素",_geometry.Objects);
        AnimButton(1,"返回场景","anim_switch");AnimButton(2,"打开 .ncmaanim","anim_open",enabled:filePicker is not null&&!_page!.State.Frozen&&!author.HasDraft);
        AnimText(3,"新图骨架 UUID","anim_rig",_animRig);AnimText(4,"新图 / 新 Clip UUID","anim_clip",_animClip);AnimButton(5,"新建图文件…","anim_new",enabled:filePicker is not null&&!_page!.State.Frozen&&!author.HasDraft);
        if(d is not null){
            Add(GuiItemKind.Label,50,label++,author.Path??"");Add(GuiItemKind.Label,50,label++,d.AssetId.ToString("D"));
            Add(GuiItemKind.Checkbox,50,6,"已审阅上述精确文件和图 UUID",new("anim_file_checked",Operation:new GraphIntent(author.Stamp)),number:_animFileReviewed?1:0,max:1);
            AnimButton(7,"批准本机文件编辑","anim_file_approve",enabled:_animFileReviewed&&!author.HasDraft&&!_page!.State.Frozen);
            AnimButton(8,"开始草稿","anim_begin",enabled:writable&&!author.HasDraft);AnimButton(9,"取消草稿","anim_cancel",enabled:author.HasDraft);
            AnimButton(10,"准备保存 / 完整验证","anim_prepare",enabled:author.HasDraft);AnimButton(11,"撤销","anim_undo",enabled:!author.HasDraft&&_page!.State.UndoCount>0);Line();AnimButton(12,"重做","anim_redo",enabled:!author.HasDraft&&_page!.State.RedoCount>0);
            AnimText(13,"搜索节点","anim_search",_animSearch);
            var matches=_animCanvas.Search(_animSearch);_animOffset=Math.Clamp(_animOffset,0,Math.Max(0,(matches.Length-1)/16*16));
            foreach(var n in d.Nodes.Where(n=>matches.Contains(n.Id)).Skip(_animOffset).Take(16))Add(GuiItemKind.SelectionButton,51,1000+AnimRow(n.Id),(_animCanvas.Selection.Contains(n.Id)?"[x] ":"")+n.Name,new("anim_select",Operation:new GraphIntent(author.Stamp,n.Id)));
            AnimButton(14,"上一页节点","anim_previous",enabled:_animOffset>0);Line();AnimButton(15,"下一页节点","anim_next",enabled:_animOffset+16<matches.Length);
            foreach(var kind in Enum.GetValues<AnimationNodeKind>())AnimButton((int)kind<6?20+(ulong)kind:80+(ulong)kind,"+ "+kind,"anim_add_node",kind,author.HasDraft&&writable&&(kind is not (AnimationNodeKind.LayerOverride or AnimationNodeKind.LayerAdditive)||author.Skeletons.Prepared));
            AnimButton(90,"骨骼清单 / 精确NCA审批","anim_bones_toggle");
            AnimButton(26,"删除选中节点及关联边","anim_delete",enabled:author.HasDraft&&_animCanvas.Selection.Length>0);AnimButton(27,"自动排列","anim_arrange",enabled:author.HasDraft&&d.Nodes.Length is >0 and <=64);
            foreach(string section in new[]{"parameter","state","transition","link","event"})AnimButton(30+(ulong)Array.IndexOf(new[]{"parameter","state","transition","link","event"},section),"查看 / 编辑 "+section,"anim_section",section);
            foreach(var (section,id) in new[]{("montage",100UL),("slot",101UL),("section",102UL)})AnimButton(id,"查看 / 编辑 "+section,"anim_section",section);
            Add(GuiItemKind.Checkbox,50,35,"允许从已提交姿态中断过渡",new("anim_interruptions",Operation:new GraphIntent(author.Stamp)),number:d.InterruptTransitions?1:0,max:1,enabled:author.HasDraft&&writable&&d.Nodes.Any(n=>n.Kind==AnimationNodeKind.StateMachine));
        }End();
        _animArea=(Viewport.X,Viewport.Y+42,Viewport.Width,Math.Max(1,Viewport.Height-42));
        Region(402,"动画节点画布",new(Viewport.X-8,Viewport.Y-42,Viewport.Width+16,Viewport.Height+50));var panel=_items[^1];panel.Value=4;_items[^1]=panel;
        AnimButton(40,"复位视图","anim_focus");Line();AnimButton(41,"检查与只读 MCP 审批","anim_reads");
        Add(GuiItemKind.Label,50,label++,"拖动节点 / Ctrl 多选 / 空白框选 / 中键平移 / 滚轮缩放 / 输出引脚拖到同类型输入");
        if(d is not null)BuildAnimCanvas(d,ref label);End();
        Region(403,"图元素检查器",_geometry.Inspector);
        if(d is not null){
            Add(GuiItemKind.Label,50,label++,"草稿诊断（不完整图不能保存或预览）");
            try{AnimationGraphValidation.Validate(d);Add(GuiItemKind.Label,50,label++,"结构完整；实际资源须单独审阅验证。");}catch(AnimationGraphValidationException diagnostic){Add(GuiItemKind.Label,50,label++,diagnostic.Code+": "+diagnostic.Message);}catch(ArgumentException diagnostic){Add(GuiItemKind.Label,50,label++,diagnostic.Message);}
            if(_animSection=="node"&&_animCanvas.Selection.Length==1)_animElement=_animCanvas.Selection[0];
            var rows=AnimElements(d,_animSection);
            _animElementOffset=Math.Clamp(_animElementOffset,0,Math.Max(0,(rows.Length-1)/16*16));
            if(_animSection!="node") {
                foreach(var row in rows.Skip(_animElementOffset).Take(16))AnimButton(10000+AnimRow(row.Id),AnimDebugLabel(row.Id,row.Name),"anim_element",row.Id);
                AnimButton(36,"上一页元素","anim_element_previous",enabled:_animElementOffset>0);Line();AnimButton(37,"下一页元素","anim_element_next",enabled:_animElementOffset+16<rows.Length);
            }
            if(_animSection is "parameter" or "state" or "transition" or "event")AnimButton(44,"+ "+_animSection,"anim_add_element",_animSection,author.HasDraft&&writable&&(_animSection!="event"||d.Events.Length<AnimationGraphCodec.MaxEvents));
            if(_animSection is "slot" or "section")AnimButton(103,"为所选 Slot 新增 Section（草稿）","anim_montage_section",enabled:author.HasDraft&&writable&&d.Montage is not null&&d.Montage.Sections.Length<64&&MontageSelectedSlot(d)!=Guid.Empty);
            if(rows.Any(r=>r.Id==_animElement)){
                BuildAnimProperties(d,author.HasDraft&&writable);
                if(_animJson is null)_animJson=new(AnimOperation(d,_animSection,_animElement).GetRawText());_animJsonPage=Math.Clamp(_animJsonPage,0,_animJson.Count-1);
                AnimButton(45,"上一页语义","anim_json_previous",enabled:_animJsonPage>0);Line();AnimButton(46,"下一页语义","anim_json_next",enabled:_animJsonPage+1<_animJson.Count);
                Add(GuiItemKind.Text,50,47,"元素语义 JSON（严格类型，不是代码）",new("anim_json",Operation:new GraphIntent(author.Stamp)),value:(_animJsonEdit??_animJson).Text(_animJsonPage),enabled:author.HasDraft&&writable);
                if(_animSection!="node")AnimButton(48,"删除此元素","anim_delete_element",enabled:author.HasDraft&&writable);
                if(_animSection=="state")AnimButton(49,"设为入口状态","anim_entry",enabled:author.HasDraft&&writable);
            }
            if(author.Review is {} review){
                Add(GuiItemKind.Label,50,label++,$"保存提案 {review.Proposal:D}\n文件 {review.Path}\n旧 {review.BeforeHash??"不存在"}\n新 {review.AfterHash}\n指纹 {review.Fingerprint}\nAgent={review.Agent} / 受众版本 {review.AudienceEpoch}");
                for(int i=0;i<review.Dependencies.Length;i++)Add(GuiItemKind.Label,50,label++,review.Dependencies[i]+" "+review.ResourceHashes[i]);
                foreach(var audience in review.Audience)Add(GuiItemKind.Label,50,label++,"配对受众 "+audience);
                var pages=InspectionText.Split(System.Text.Encoding.UTF8.GetString(AnimationGraphCodec.Encode(author.CandidateCopy(review.Proposal))),AnimationGraphCodec.MaxBytes).ToArray();
                _animReviewPage=Math.Clamp(_animReviewPage,0,Math.Max(0,pages.Length-1));Add(GuiItemKind.Label,50,label++,pages[_animReviewPage]);AnimButton(50,"上一页完整候选","anim_review_previous",enabled:_animReviewPage>0);Line();AnimButton(51,"下一页完整候选","anim_review_next",enabled:_animReviewPage+1<pages.Length);
                Add(GuiItemKind.Checkbox,50,52,"已审阅完整候选、文件、依赖代次及受众",new("anim_review_checked",Operation:new GraphIntent(author.Stamp)),number:_animReviewed?1:0,max:1);
                AnimButton(53,"批准显示的精确提案","anim_approve",review,_animReviewed);AnimButton(54,"本机提交保存","anim_commit",review.Proposal,!review.Agent&&_animReviewed);
                AnimButton(55,"准备独立真实角色预览","anim_preview",review.Proposal,_graphPreview is not null&&_animReviewed);
            }
            AnimButton(56,_animPreviewRunning?"暂停独立预览":"运行独立预览","anim_preview_run",enabled:_graphPreview?.Prepared==true);AnimButton(57,"预览单步 1/60","anim_preview_step",enabled:_graphPreview?.Prepared==true&&!_animPreviewRunning);
            if(_graphPreview?.GraphDebug is {} debug) {
                Add(GuiItemKind.Label,50,label++,$"独立预览 valid={debug.SnapshotValid} / {debug.Outcome}");
                if(debug.SnapshotValid){var frame=debug.Frame;Add(GuiItemKind.Label,50,label++,$"独立状态 {frame.StateId:D}\n源 {frame.FromStateId:D} / frozen {frame.FrozenPoseGeneration}\n转换 {frame.TransitionId:D} / {frame.TransitionWeight:F3}\n图时钟 {frame.Context.Tick}; World tick {_graphPreview.Tick}");foreach(var receipt in debug.Events.Take(8))Add(GuiItemKind.Label,50,label++,$"事件 {receipt.MarkerId:D} {receipt.UnwrappedTime:F3}s {receipt.Name}");}
                else Add(GuiItemKind.Label,50,label++,"当前准备失败，不把旧成功帧或事件作为本次结果；关闭后显式重新准备。");
            }
            if(_graphPreview?.Prepared==true)foreach(var p in _animPreviewParameters.Take(16)){
                var kind=p.Kind==AnimationParameterKind.Trigger?GuiItemKind.Button:p.Kind==AnimationParameterKind.Bool?GuiItemKind.Checkbox:GuiItemKind.Number;
                Add(kind,51,20000+AnimRow(p.Id),"预览参数 "+p.Name,new("anim_control",Operation:new GraphIntent(author.Stamp,p)),number:p.Kind==AnimationParameterKind.Trigger?1:_animPreviewValues[p.Id],min:p.Kind is AnimationParameterKind.Bool or AnimationParameterKind.Trigger?0:p.Kind==AnimationParameterKind.Int?int.MinValue:-1000000,max:p.Kind is AnimationParameterKind.Bool or AnimationParameterKind.Trigger?1:p.Kind==AnimationParameterKind.Int?int.MaxValue:1000000);
            }
        }End();
        _animPreviewArea=(_geometry.Assets.X+8,_geometry.Assets.Y+34,Math.Max(1,_geometry.Assets.Width-16),Math.Max(1,_geometry.Assets.Height-42));
        Region(404,"角色预览 · 独立时钟 / 无碰撞根运动",_geometry.Assets);_animPreviewInsert=_items.Count;if(_graphPreview?.Prepared!=true)Add(GuiItemKind.Label,50,label++,"实际 NCA / 共享姿态、蒙皮、阴影路径；独立时钟不推进场景 Play。\n审阅精确依赖后显式准备；图结构变化关闭旧预览。\n无图参数 seek、碰撞根运动或推理服务。");End();
        Region(405,"图提案 / 采样空间 / 事件轨道",_geometry.Console);if(d is not null&&_animSection=="event")BuildAnimEventTrack(d,ref label);if(d is not null&&_animSection is "montage" or "slot" or "section")BuildMontageTrack(d,ref label);if(d is not null&&_animSection=="node"&&d.Nodes.SingleOrDefault(n=>n.Id==_animElement)?.BlendSpace is{} selectedSpace)BuildAnimSpace(d,selectedSpace,ref label);foreach(var id in author.Pending)AnimButton(30000+AnimRow(id),"审阅提案 "+id.ToString("D"),"anim_review_proposal",id);
        foreach(var message in _messages.TakeLast(4))Add(GuiItemKind.Label,50,label++,message);AnimButton(58,"撤销全部图文件/Agent批准","anim_revoke");AnimButton(60,"独立序列 / AI用例审批","anim_sequence_toggle");End();
        if(_geometry.Ai.Width>0){Region(406,"AI 图工具",_geometry.Ai,"未接入推理服务");Add(GuiItemKind.Label,50,label++,"inspect / validate / propose / transaction\n图语义审阅和端点请求授权是两道独立批准；默认拒绝。切换到场景区可配对/审阅端点，不授予文件权限。");AnimButton(59,"返回场景端点审批（保留 Agent 提案）","anim_endpoint");End();}
        Region(408,"",_geometry.Status);Add(GuiItemKind.Label,50,label++,$"动画图 | zoom {_animCanvas.Zoom:F2} | 选择 {_animCanvas.Selection.Length} | {(author.HasDraft?"未保存草稿":"已保存 / 只读")} | C# authoring / ImGui presentation");End();
        WorkspaceMenu(ref label,!author.HasDraft&&!_page!.State.Frozen);BuildGraphReads(_geometry.Status.Width,_geometry.Status.Y+28,ref label);BuildAnimSequences(_geometry.Status.Width,_geometry.Status.Y+28,ref label);BuildAnimBones(_geometry.Status.Width,_geometry.Status.Y+28,ref label);
        _frame=new(){StructSize=(uint)Marshal.SizeOf<GuiFrame>(),Frame=frameId,ViewGeneration=_generation,DocumentGeneration=_page!.Stamp.Generation,Revision=_page.Stamp.Revision,ItemCount=(uint)_items.Count,TextBytes=(uint)_text.Count};return _frame;
    }
    private static (Guid Id,string Name)[] AnimElements(AnimationGraphDefinition d,string section)=>section switch{"montage"=>d.Montage is{} m?[(m.AssetId,m.Name)]:[],"slot"=>d.Montage?.Slots.Select(v=>(v.Id,v.Name)).ToArray()??[],"section"=>d.Montage?.Sections.OrderBy(v=>v.SlotId).ThenBy(v=>v.Id).Select(v=>(v.Id,$"{v.Start:F3}–{v.End:F3}s · {v.Name}")).ToArray()??[],"parameter"=>d.Parameters.Select(v=>(v.Id,v.Name)).ToArray(),"state"=>d.States.Select(v=>(v.Id,v.Name)).ToArray(),"transition"=>d.Transitions.Select(v=>(v.Id,"Transition "+v.Id.ToString("D"))).ToArray(),"link"=>d.Links.Select(v=>(v.Id,v.FromPin+" → "+v.ToPin)).ToArray(),"event"=>d.Events.OrderBy(v=>v.Time).ThenBy(v=>v.Id).Select(v=>(v.Id,$"{v.Time:F3}s · {v.Name}")).ToArray(),_=>d.Nodes.Select(v=>(v.Id,v.Name)).ToArray()};
    private static JsonElement AnimOperation(AnimationGraphDefinition d,string section,Guid id)=>AnimationGraphEdits.Operations(section switch{"montage"=>(object)new{op="montage.upsert",montage=d.Montage},"slot"=>new{op="montage.slot.upsert",slot=d.Montage!.Slots.Single(v=>v.Id==id)},"section"=>new{op="montage.section.upsert",section=d.Montage!.Sections.Single(v=>v.Id==id)},"parameter"=>new{op="parameter.upsert",parameter=d.Parameters.Single(v=>v.Id==id)},"state"=>new{op="state.upsert",state=d.States.Single(v=>v.Id==id)},"transition"=>new{op="transition.upsert",transition=d.Transitions.Single(v=>v.Id==id)},"link"=>new{op="link.upsert",link=d.Links.Single(v=>v.Id==id)},"event"=>new{op="event.upsert",marker=d.Events.Single(v=>v.Id==id)},_=>new{op="node.upsert",node=d.Nodes.Single(v=>v.Id==id)}});
    private void BuildAnimCanvas(AnimationGraphDefinition d,ref ulong label)
    {
        var area=_animArea;Add(GuiItemKind.OverlayBegin,51,1,"");SetRect(area.X,area.Y,area.Width,area.Height);
        var pins=_animCanvas.Pins;
        foreach(var link in d.Links){var a=pins.FirstOrDefault(p=>p.Output&&p.Node==link.From&&p.Name==link.FromPin);var b=pins.FirstOrDefault(p=>!p.Output&&p.Node==link.To&&p.Name==link.ToPin);if(a.Node!=Guid.Empty&&b.Node!=Guid.Empty){var pa=Normalized(a.Position);var pb=Normalized(b.Position);if(ClipLine(ref pa,ref pb))Add(GuiItemKind.CanvasLines,51,label++,"",value:FormattableString.Invariant($"{pa.X} {pa.Y} {pb.X} {pb.Y}"),number:0xffd7a050,min:2,max:2);}}
        foreach(var box in _animCanvas.Boxes){var p=Normalized(box.Position);var size=box.Size*_animCanvas.Zoom/new Vector2(area.Width,area.Height);if(p.X>1||p.Y>1||p.X+size.X<0||p.Y+size.Y<0)continue;var clippedMin=Vector2.Max(p,Vector2.Zero);var clippedMax=Vector2.Min(p+size,Vector2.One);if(clippedMax.X<=clippedMin.X||clippedMax.Y<=clippedMin.Y)continue;Add(GuiItemKind.CanvasRect,51,label++,"",number:_animCanvas.Selection.Contains(box.Id)?0xff634b25:0xff34271a,min:5,max:5);SetRect(clippedMin.X,clippedMin.Y,clippedMax.X-clippedMin.X,clippedMax.Y-clippedMin.Y);var n=d.Nodes.Single(v=>v.Id==box.Id);if(p.X>=-.5&&p.Y>=-.5){Add(GuiItemKind.CanvasText,51,label++,n.Name+" · "+n.Kind,number:0xffe3d7c5,min:Math.Clamp(14*_animCanvas.Zoom,8,28),max:28);SetRect(p.X+8/area.Width,p.Y+8/area.Height,0,0);}}
        foreach(var pin in pins){var p=Normalized(pin.Position);if(p.X<-.05||p.X>1.05||p.Y<-.05||p.Y>1.05)continue;Add(GuiItemKind.CanvasRect,51,label++,"",number:pin.Type==AnimationPinType.Pose?0xffd7a050:0xff70c388,min:4,max:4);SetRect(p.X-4/area.Width,p.Y-4/area.Height,8/area.Width,8/area.Height);Add(GuiItemKind.CanvasText,51,label++,pin.Name,number:0xffcbbdad,min:10,max:10);SetRect(p.X+(pin.Output?-40:8)/area.Width,p.Y-5/area.Height,0,0);}
        Add(GuiItemKind.CanvasEnd,51,label++,"");Add(GuiItemKind.CanvasInput,51,2,"",new("anim_pointer",Operation:new GraphIntent(_graphAuthor!.Stamp)),enabled:!_page!.State.Frozen);SetRect(area.X,area.Y,area.Width,area.Height);
        Vector2 Normalized(Vector2 p)=>(_animCanvas.ScreenPoint(p,new(area.X,area.Y))-new Vector2(area.X,area.Y))/new Vector2(area.Width,area.Height);
    }
    private static bool ClipLine(ref Vector2 a,ref Vector2 b){Vector2 delta=b-a;float lo=0,hi=1;bool Clip(float p,float q){if(p==0)return q>=0;float r=q/p;if(p<0){if(r>hi)return false;lo=Math.Max(lo,r);}else{if(r<lo)return false;hi=Math.Min(hi,r);}return true;}if(!Clip(-delta.X,a.X)||!Clip(delta.X,1-a.X)||!Clip(-delta.Y,a.Y)||!Clip(delta.Y,1-a.Y))return false;b=a+delta*hi;a+=delta*lo;return true;}
    private bool ApplyGraphEditor(ActionView action,GuiEvent e,string text)
    {
        if(!action.Kind.StartsWith("anim_",StringComparison.Ordinal))return false;
        var author=_graphAuthor??throw new EditRejectedException("graph_service_missing");
        if(action.Kind=="anim_switch"){if(e.Phase==3){CancelInteraction();_uiMode=false;_graphMode=!_graphMode;_activeMenu=-1;SynchronizeGraph();}return true;}
        if(action.Operation is not GraphIntent intent||intent.Stamp!=author.Stamp)throw new EditRejectedException("graph_view_stale");
        if(ApplyAnimSequence(action,e,text))return true;
        if(ApplyAnimBones(action,e,text))return true;
        if(action.Kind=="anim_property"){if(e.Phase==3){ApplyAnimProperty((GraphProperty)intent.Payload!,e.Value,text);SynchronizeGraph();}return true;}
        if(action.Kind=="anim_pointer"){AnimPointer(e,text);return true;}
        if(action.Kind=="anim_event_pointer"){AnimEventPointer(e,text);return true;}
        if(ApplyAnimSpace(action,e,text))return true;
        if(ApplyMontageAction(action,e,text))return true;
        if(action.Kind=="anim_json"){
            if(e.Phase==1){_animJsonEdit=_animJson;return true;} if(_animJsonEdit is null)throw new EditRejectedException("graph_json_activation_missing");
            if(e.Phase==3){using var json=JsonDocument.Parse(_animJsonEdit.Replace(_animJsonPage,text));author.ApplyDraft(json.RootElement);_animJsonEdit=_animJson=null;SynchronizeGraph();}return true;
        }
        if(e.Phase!=3)return true;
        var d=author.Capture();
        switch(action.Kind){
            case "anim_open":CancelGraph();string? file=filePicker?.Invoke(LocalFileKind.OpenAnimationGraph);if(file is not null){author.Open(System.IO.Path.GetRelativePath(projectRoot!,file).Replace('\\','/'));_animFileReviewed=false;_animRows.Clear();}break;
            case "anim_rig":_animRig=text;break;case "anim_clip":_animClip=text;break;case "anim_search":if(text.Length>128)throw new ArgumentException("Graph search budget.");_animSearch=text;break;
            case "anim_new":string? output=filePicker?.Invoke(LocalFileKind.SaveAnimationGraph);if(output is not null){var clip=AnimationGraphNode.Create(Guid.NewGuid(),"Clip",AnimationNodeKind.Clip) with{ClipId=Guid.Parse(_animClip),Speed=1,Loop=true};var end=AnimationGraphNode.Create(Guid.NewGuid(),"Output",AnimationNodeKind.Output) with{X=300};author.New(System.IO.Path.GetRelativePath(projectRoot!,output).Replace('\\','/'),new(AnimationGraphCodec.CurrentVersion,Guid.NewGuid(),"New Animator",Guid.Parse(_animRig),Guid.Empty,[],[clip,end],[new(Guid.NewGuid(),clip.Id,"pose",end.Id,"pose")],[],[]));_animFileReviewed=false;}break;
            case "anim_interruptions":if(e.Value is not (0 or 1))throw new ArgumentException("Boolean interruption policy.");author.ApplyDraft(AnimationGraphEdits.Operations(new{op="graph.interruptions",enabled=e.Value==1}));break;
            case "anim_file_checked":if(e.Value is not (0 or 1))throw new ArgumentException("Review checkbox.");_animFileReviewed=e.Value==1;break;
            case "anim_file_approve":author.ApproveFileWrite(intent.Stamp,author.Path!,d!.AssetId,_animFileReviewed);break;
            case "anim_begin":author.Begin(intent.Stamp);break;case "anim_cancel":CancelGraph();break;
            case "anim_prepare":_animProposal=author.PrepareLocal();author.CaptureReview(_animProposal);_animReviewed=false;break;
            case "anim_review_proposal":_animProposal=(Guid)intent.Payload!;author.CaptureReview(_animProposal);_animReviewed=false;break;
            case "anim_review_checked":if(e.Value is not (0 or 1))throw new ArgumentException("Review checkbox.");_animReviewed=e.Value==1;break;
            case "anim_approve":var review=(AnimationGraphWriteReview)intent.Payload!;author.Approve(review,review.Fingerprint,review.Graph,_animReviewed);break;
            case "anim_commit":author.CommitLocal((Guid)intent.Payload!);_animProposal=Guid.Empty;_animReviewed=false;_refreshRenderAssets=true;break;
            case "anim_select":Guid id=(Guid)intent.Payload!;_animCanvas.Select(id,((int)e.Value&3)!=0);_animSection="node";_animElement=id;_animJson=null;break;
            case "anim_element":_animElement=(Guid)intent.Payload!;_animJson=null;break;
            case "anim_section":_animSection=(string)intent.Payload!;_animElement=Guid.Empty;_animJson=null;_animElementOffset=0;break;
            case "anim_element_previous":_animElementOffset=Math.Max(0,_animElementOffset-16);break;case "anim_element_next":_animElementOffset+=16;break;
            case "anim_previous":_animOffset=Math.Max(0,_animOffset-16);break;case "anim_next":_animOffset+=16;break;
            case "anim_focus":_animCanvas.ResetView();break;
            case "anim_delete":author.ApplyDraft(_animCanvas.DeleteSelected());break;
            case "anim_delete_element":author.ApplyDraft(AnimationGraphEdits.Operations(new{op=(_animSection is "slot" or "section"?"montage.":"")+_animSection+".delete",id=_animElement}));break;
            case "anim_entry":author.ApplyDraft(AnimationGraphEdits.Operations(new{op="graph.entry",stateId=_animElement}));break;
            case "anim_add_node":var kind=(AnimationNodeKind)intent.Payload!;if(kind==AnimationNodeKind.Slot){AddMontageSlot(d!);break;}if(kind==AnimationNodeKind.BlendSpace){AddAnimSpace(d!);break;}if(kind is AnimationNodeKind.LayerOverride or AnimationNodeKind.LayerAdditive){AddAnimLayer(d!,kind);break;}var node=AnimationGraphNode.Create(Guid.NewGuid(),kind.ToString(),kind) with{X=64+d!.Nodes.Length*12,Y=96};if(kind==AnimationNodeKind.Clip)node=node with{ClipId=Guid.Parse(_animClip.Length>0?_animClip:d.Nodes.First(n=>n.Kind==AnimationNodeKind.Clip).ClipId.ToString()),Speed=1,Loop=true};if(kind==AnimationNodeKind.Parameter)node=node with{ParameterId=d.Parameters.FirstOrDefault()?.Id??Guid.Empty};if(kind==AnimationNodeKind.Blend)node=node with{Weight=.5};author.ApplyDraft(AnimationGraphEdits.Operations(new{op="node.upsert",node}));break;
            case "anim_add_element":
                var definition=d??throw new ArgumentException("Graph missing.");Guid added=Guid.NewGuid();
                object op=(string)intent.Payload! switch{
                    "parameter"=>new{op="parameter.upsert",parameter=new AnimationParameter(added,"Parameter "+definition.Parameters.Length,AnimationParameterKind.Float,0,0,false)},
                    "state"=>new{op="state.upsert",state=new AnimationGraphState(added,"State "+definition.States.Length,AnimStatePose(definition))},
                    "transition"=>new{op="transition.upsert",transition=new AnimationTransition(added,definition.States.First().Id,definition.States.Skip(1).First().Id,0,.15,null,[])},
                    "event"=>new{op="event.upsert",marker=new AnimationEventMarker(added,AnimEventClip(definition),.1,"Event "+definition.Events.Length)},
                    _=>throw new ArgumentException("Element kind.")};
                author.ApplyDraft(AnimationGraphEdits.Operations(op));_animElement=added;_animJson=null;break;
            case "anim_arrange":author.ApplyDraft(AnimationGraphEdits.Operations(d!.Nodes.Select((n,i)=>(object)new{op="node.upsert",node=n with{X=(i%3)*250,Y=(i/3)*160}}).ToArray()));break;
            case "anim_json_previous":_animJsonPage--;break;case "anim_json_next":_animJsonPage++;break;case "anim_review_previous":_animReviewPage--;break;case "anim_review_next":_animReviewPage++;break;
            case "anim_reads":_showGraphReads=!_showGraphReads;break;
            case "anim_endpoint":if(author.HasDraft)throw new EditRejectedException("cancel_local_draft_before_endpoint");_graphMode=false;_generation++;break;
            case "anim_revoke":author.Revoke();CancelGraph();break;
            case "anim_preview":using(var resources=author.PreparePreview((Guid)intent.Payload!))_graphPreview!.Prepare(resources,d!.AssetId);_animPreviewParameters=author.CandidateCopy((Guid)intent.Payload!).Parameters;_animPreviewValues.Clear();foreach(var p in _animPreviewParameters)_animPreviewValues.Add(p.Id,p.Kind==AnimationParameterKind.Float?p.FloatDefault:p.Kind==AnimationParameterKind.Int?p.IntDefault:p.BoolDefault?1:0);break;
            case "anim_preview_run":_animPreviewRunning=!_animPreviewRunning;break;case "anim_preview_step":_graphPreview!.Advance(1.0/60);break;
            case "anim_control":var parameter=(AnimationParameter)intent.Payload!;_graphPreview!.Control(parameter.Id,parameter.Kind,e.Value);if(parameter.Kind!=AnimationParameterKind.Trigger)_animPreviewValues[parameter.Id]=e.Value;break;
            case "anim_undo":case "anim_redo":workspace.History(workspace.Stamp,action.Kind=="anim_redo");_refreshRenderAssets=true;break;
            default:throw new ArgumentException("Graph action.");
        }
        SynchronizeGraph();return true;
    }
    private Guid AnimStatePose(AnimationGraphDefinition d)
    {
        var poses=d.Nodes.Where(n=>n.Kind is AnimationNodeKind.Clip or AnimationNodeKind.Blend or AnimationNodeKind.BlendSpace or AnimationNodeKind.LayerOverride or AnimationNodeKind.LayerAdditive or AnimationNodeKind.CachePose).ToArray();
        return poses.FirstOrDefault(n=>n.Id==_animElement)?.Id??poses.FirstOrDefault()?.Id??throw new ArgumentException("明确添加姿态节点后再创建状态。");
    }
    private static Guid AnimEventClip(AnimationGraphDefinition d)=>AnimationGraphValidation.ClipIds(d).FirstOrDefault() is var id&&id!=Guid.Empty?id:throw new ArgumentException("明确添加 Clip 或 BlendSpace 采样后再创建事件。");
    private void AnimPointer(GuiEvent e,string text)
    {
        string[] parts=text.Split(' ',StringSplitOptions.RemoveEmptyEntries);if(parts.Length!=3||!float.TryParse(parts[0],NumberStyles.Float,CultureInfo.InvariantCulture,out float u)||!float.TryParse(parts[1],NumberStyles.Float,CultureInfo.InvariantCulture,out float v)||!float.TryParse(parts[2],NumberStyles.Float,CultureInfo.InvariantCulture,out float wheel)||!float.IsFinite(u)||!float.IsFinite(v)||!float.IsFinite(wheel)||Math.Abs(u)>64||Math.Abs(v)>64||Math.Abs(wheel)>32||e.Value<0||e.Value>127||e.Value!=Math.Truncate(e.Value))throw new ArgumentException("Bounded graph pointer.");
        Vector2 origin=new(_animArea.X,_animArea.Y),screen=origin+new Vector2(u*_animArea.Width,v*_animArea.Height);int flags=(int)e.Value;
        if((flags&64)!=0){if(_animGesture is not null)return;_animCanvas.ZoomAt(screen,origin,MathF.Pow(1.1f,wheel));return;}
        if(e.Phase==1){if(_animDefinition is null)return;_animStart=_animLast=screen;_animPan=(flags&24)!=0;_animFrom=_animCanvas.HitPin(_animCanvas.GraphPoint(screen,origin));_animMarquee=false;
            if(_animPan)return;if(_animFrom.HasValue)return;var hit=_animCanvas.HitNode(_animCanvas.GraphPoint(screen,origin));
            if(hit is {} id){if(!_animCanvas.Selection.Contains(id)||((flags&3)!=0))_animCanvas.Select(id,(flags&3)!=0);if(_graphAuthor!.Writable&&_animCanvas.Selection.Length>0){if(!_graphAuthor.HasDraft)_graphAuthor.Begin(_graphAuthor.Stamp);_animGesture=new();_animGesture.Load(_animDefinition);foreach(var chosen in _animCanvas.Selection)_animGesture.Select(chosen,true);}}
            else _animMarquee=true;return;
        }
        if(_animPan){_animCanvas.TranslatePan(screen-_animLast);_animLast=screen;if(e.Phase==3)_animPan=false;return;}
        if(_animFrom is {} from){if(e.Phase==3){var target=_animCanvas.HitPin(_animCanvas.GraphPoint(screen,origin));if(target is {} to&&from.Output&&_graphAuthor!.Writable){if(!_graphAuthor.HasDraft)_graphAuthor.Begin(_graphAuthor.Stamp);_graphAuthor.ApplyDraft(_animCanvas.Connect(from,to,Guid.NewGuid()));}_animFrom=null;}SynchronizeGraph();return;}
        if(_animMarquee){if(e.Phase==3){_animCanvas.Marquee(_animCanvas.GraphPoint(_animStart,origin),_animCanvas.GraphPoint(screen,origin),(flags&3)!=0);_animMarquee=false;}return;}
        if(_animGesture is {} gesture){_graphAuthor!.ApplyDraft(gesture.Move((screen-_animStart)/_animCanvas.Zoom));if(e.Phase==3)_animGesture=null;SynchronizeGraph();}
    }
}
