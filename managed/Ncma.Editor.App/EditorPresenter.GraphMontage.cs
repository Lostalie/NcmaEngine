using System.Globalization;
using Ncma.Animation;
using Ncma.Editor.Core;
using Ncma.Gui;
namespace Ncma.Editor.App;

internal sealed unsafe partial class EditorPresenter
{
    private Guid _montageDrag;
    private bool _montageDragEnd;
    private double _montageExtent;
    private int _montageTrackPage;
    private static Guid MontageId(string text,bool empty=false)=>Guid.TryParseExact(text,"D",out Guid id)&&id.ToString("D")==text&&(empty||id!=Guid.Empty)?id:throw new ArgumentException("Canonical Montage UUID required.");
    private static bool MontageBool(double value)=>value is 0?false:value is 1?true:throw new ArgumentException("Boolean Montage property.");
    private Guid MontageSelectedSlot(AnimationGraphDefinition d)=>_animSection=="slot"?d.Montage?.Slots.SingleOrDefault(s=>s.Id==_animElement)?.Id??Guid.Empty:d.Montage?.Sections.SingleOrDefault(s=>s.Id==_animElement)?.SlotId??Guid.Empty;
    private Guid MontageClip(AnimationGraphDefinition d)
    {
        if(_animClip.Length>0)return MontageId(_animClip);
        if(d.Nodes.SingleOrDefault(n=>n.Id==_animElement&&n.Kind==AnimationNodeKind.Clip) is{} selected)return selected.ClipId;
        Guid[] clips=AnimationGraphValidation.ClipIds(d).Distinct().ToArray();
        return clips.Length==1?clips[0]:throw new ArgumentException("请明确输入或选择 Clip UUID，不猜测攻击/闪避素材。");
    }
    private void AddMontageSlot(AnimationGraphDefinition d)
    {
        var output=d.Nodes.Single(n=>n.Kind==AnimationNodeKind.Output);var edge=d.Links.Single(l=>l.To==output.Id&&l.ToPin=="pose");
        Guid slotId=Guid.NewGuid(),sectionId=Guid.NewGuid();string suffix=slotId.ToString("N")[..8];
        var m=d.Montage??new(1,Guid.NewGuid(),"Montage",d.SkeletonId,[],[]);
        var slot=new AnimationMontageSlot(slotId,"Slot "+suffix,sectionId,0,true,false,.1,.1);
        var section=new AnimationMontageSection(sectionId,"Section "+sectionId.ToString("N")[..8],slotId,MontageClip(d),0,.1,Guid.Empty);
        double slotX=Math.Max(output.X,d.Nodes.Single(n=>n.Id==edge.From).X+220);
        var node=AnimationGraphNode.Create(Guid.NewGuid(),slot.Name,AnimationNodeKind.Slot) with{SlotId=slotId,X=slotX,Y=output.Y,PlayOnStart=false};
        _graphAuthor!.ApplyDraft(AnimationGraphEdits.Operations(new{op="montage.upsert",montage=m with{Slots=[..m.Slots,slot],Sections=[..m.Sections,section]}},new{op="node.upsert",node},new{op="node.upsert",node=output with{X=slotX+220}},new{op="link.delete",id=edge.Id},new{op="link.upsert",link=new AnimationGraphLink(Guid.NewGuid(),edge.From,edge.FromPin,node.Id,"pose")},new{op="link.upsert",link=new AnimationGraphLink(Guid.NewGuid(),node.Id,"pose",output.Id,"pose")}));
        _animSection="slot";_animElement=slotId;_animJson=null;
    }
    private bool BuildMontageProperties(AnimationGraphDefinition d,bool writable)
    {
        if(_animSection is not ("montage" or "slot" or "section"))return false;
        ulong id=600;string kind=_animSection;Guid element=_animElement;var m=d.Montage!;
        void Text(string field,string label,string value)=>Add(GuiItemKind.Text,50,id++,label,new("anim_property",Operation:new GraphIntent(_graphAuthor!.Stamp,new GraphProperty(kind,element,field))),value:value,enabled:writable);
        void Number(string field,string label,double value,double min,double max)=>Add(GuiItemKind.Number,50,id++,label,new("anim_property",Operation:new GraphIntent(_graphAuthor!.Stamp,new GraphProperty(kind,element,field))),number:value,min:min,max:max,enabled:writable);
        void Bool(string field,string label,bool value)=>Add(GuiItemKind.Checkbox,50,id++,label,new("anim_property",Operation:new GraphIntent(_graphAuthor!.Stamp,new GraphProperty(kind,element,field))),number:value?1:0,max:1,enabled:writable);
        if(kind=="montage")Text("name","Montage 名称（同图骨架）",m.Name);
        else if(kind=="slot"){
            var s=m.Slots.Single(s=>s.Id==element);Text("name","Slot 名称",s.Name);Text("entry","入口 Section UUID",s.EntrySection.ToString("D"));Number("priority","优先级",s.Priority,0,255);Bool("interruptible","可中断 / 高优先级替换",s.Interruptible);Bool("root","经唯一 Movement 的 root 意图",s.RootMotion);Number("blendIn","淡入秒数",s.BlendIn,0,10);Number("blendOut","淡出秒数",s.BlendOut,0,10);
        }else{
            var s=m.Sections.Single(s=>s.Id==element);Text("name","Section 名称",s.Name);Text("slot","所属 Slot UUID",s.SlotId.ToString("D"));Text("clip","实际 Clip UUID",s.ClipId.ToString("D"));Number("start","源 Clip 开始秒数",s.Start,0,600);Number("end","源 Clip 结束秒数（实际长度保存时验证）",s.End,.001,600);Text("next","后继 Section UUID（全零表示结束）",s.NextSection.ToString("D"));
        }
        return true;
    }
    private bool ApplyMontageProperty(GraphProperty f,double value,string text)
    {
        if(f.Section is not ("montage" or "slot" or "section"))return false;
        var m=_graphAuthor!.Capture()!.Montage!;object op;
        if(f.Section=="montage"){if(f.Field!="name")throw new ArgumentException("Montage property.");op=new{op="montage.upsert",montage=m with{Name=text}};}
        else if(f.Section=="slot"){
            var s=m.Slots.Single(s=>s.Id==f.Id);s=f.Field switch{"name"=>s with{Name=text},"entry"=>s with{EntrySection=MontageId(text,true)},"priority"=>double.IsFinite(value)&&value==Math.Truncate(value)&&value is >=0 and <=255?s with{Priority=(int)value}:throw new ArgumentException("Integral priority."),"interruptible"=>s with{Interruptible=MontageBool(value)},"root"=>s with{RootMotion=MontageBool(value)},"blendIn"=>s with{BlendIn=value},"blendOut"=>s with{BlendOut=value},_=>throw new ArgumentException("Slot property.")};op=new{op="montage.slot.upsert",slot=s};
        }else{
            var s=m.Sections.Single(s=>s.Id==f.Id);s=f.Field switch{"name"=>s with{Name=text},"slot"=>s with{SlotId=MontageId(text)},"clip"=>s with{ClipId=MontageId(text)},"start"=>s with{Start=value},"end"=>s with{End=value},"next"=>s with{NextSection=MontageId(text,true)},_=>throw new ArgumentException("Section property.")};op=new{op="montage.section.upsert",section=s};
        }
        _graphAuthor.ApplyDraft(AnimationGraphEdits.Operations(op));_animJson=null;return true;
    }
    private bool ApplyMontageAction(ActionView action,GuiEvent e,string text)
    {
        if(action.Kind=="anim_montage_pointer"){MontagePointer(e,text);return true;}
        if(action.Kind is "anim_montage_previous" or "anim_montage_next"){if(e.Phase==3){_montageTrackPage+=action.Kind=="anim_montage_previous"?-1:1;_montageDrag=Guid.Empty;}return true;}
        if(action.Kind!="anim_montage_section")return false;
        if(e.Phase!=3)return true;var d=_graphAuthor!.Capture()!;Guid slot=MontageSelectedSlot(d);
        if(slot==Guid.Empty)throw new ArgumentException("Select an exact Slot first.");Guid id=Guid.NewGuid();
        _graphAuthor.ApplyDraft(AnimationGraphEdits.Operations(new{op="montage.section.upsert",section=new AnimationMontageSection(id,"Section "+id.ToString("N")[..8],slot,MontageClip(d),0,.1,Guid.Empty)}));
        _animSection="section";_animElement=id;_animJson=null;SynchronizeGraph();return true;
    }
    private AnimationMontageSection[] MontageTrackRows(AnimationGraphDefinition d)=>d.Montage?.Sections.OrderBy(s=>s.SlotId).ThenBy(s=>s.Id).Skip(_montageTrackPage*4).Take(4).ToArray()??[];
    private void BuildMontageTrack(AnimationGraphDefinition d,ref ulong label)
    {
        int pages=Math.Max(1,((d.Montage?.Sections.Length??0)+3)/4);_montageTrackPage=Math.Clamp(_montageTrackPage,0,pages-1);
        var rows=MontageTrackRows(d);double extent=Math.Max(1,rows.Select(s=>s.End).DefaultIfEmpty(0).Max());
        AnimButton(104,"上一页轨道","anim_montage_previous",enabled:_montageTrackPage>0);Line();AnimButton(105,"下一页轨道","anim_montage_next",enabled:_montageTrackPage+1<pages);
        Add(GuiItemKind.Label,50,label++,$"Section 源 Clip 0–{extent:F3}s · {_montageTrackPage+1}/{pages}页，每页4段\n拖动端点仅编辑草稿；金线 Notify 数据，不 seek。\n\n\n\n\n\n");
        if(rows.Length==0)return;float x=_geometry.Console.X+10,y=_geometry.Console.Y+112,w=Math.Max(1,_geometry.Console.Width-20),h=Math.Max(1,Math.Min(96,_geometry.Console.Height-124));
        Add(GuiItemKind.OverlayBegin,59,1,"");SetRect(x,y,w,h);
        for(int i=0;i<rows.Length;i++){
            var s=rows[i];float top=(float)i/rows.Length,height=1f/rows.Length;
            Add(GuiItemKind.CanvasRect,59,1000+AnimRow(s.Id),"",number:s.Id==_animElement?0xffa77739:0xff734b24,min:1,max:1);SetRect((float)(s.Start/extent),top,(float)((s.End-s.Start)/extent),height*.75f);
            foreach(var marker in d.Events.Where(e=>e.ClipId==s.ClipId&&e.Time>=s.Start&&e.Time<=s.End).Take(64)){float u=(float)(marker.Time/extent);Add(GuiItemKind.CanvasLines,59,label++,"",value:FormattableString.Invariant($"{u} {top} {u} {top+height*.75f}"),number:0xff70d7ef,min:1,max:1);}
            Add(GuiItemKind.CanvasText,59,label++,s.Name+" · "+s.Start.ToString("F3",CultureInfo.InvariantCulture)+"–"+s.End.ToString("F3",CultureInfo.InvariantCulture),number:0xffe3d7c5,min:10,max:10);SetRect(0,top,0,0);
        }
        Add(GuiItemKind.CanvasEnd,59,label++,"");Add(GuiItemKind.CanvasInput,59,2,"",new("anim_montage_pointer",Operation:new GraphIntent(_graphAuthor!.Stamp)),enabled:_graphAuthor.HasDraft&&_graphAuthor.Writable);SetRect(x,y,w,h);
    }
    private void MontagePointer(GuiEvent e,string text)
    {
        if(!_graphAuthor!.HasDraft||!_graphAuthor.Writable)throw new EditRejectedException("montage_draft_readonly");
        var parts=text.Split(' ',StringSplitOptions.RemoveEmptyEntries);
        if(parts.Length!=3||!double.TryParse(parts[0],NumberStyles.Float,CultureInfo.InvariantCulture,out double u)||!double.IsFinite(u)||u is < -1 or >2||!double.TryParse(parts[1],NumberStyles.Float,CultureInfo.InvariantCulture,out double v)||!double.IsFinite(v)||v is < -1 or >2||!double.TryParse(parts[2],NumberStyles.Float,CultureInfo.InvariantCulture,out double wheel)||!double.IsFinite(wheel)||Math.Abs(wheel)>32||e.Value is <0 or >127||e.Value!=Math.Truncate(e.Value))throw new ArgumentException("Bounded Section track input.");
        var d=_graphAuthor.Capture()!;var rows=MontageTrackRows(d);
        if(e.Phase==1){_montageDrag=Guid.Empty;if(v is >=0 and <1&&rows.Length>0){var s=rows[(int)(v*rows.Length)];_montageExtent=Math.Max(1,rows.Select(r=>r.End).Max());double a=Math.Abs(s.Start/_montageExtent-u),b=Math.Abs(s.End/_montageExtent-u);if(Math.Min(a,b)<=.03){_montageDrag=s.Id;_montageDragEnd=b<=a;_animSection="section";_animElement=s.Id;_animJson=null;}}}
        if(_montageDrag!=Guid.Empty){var s=d.Montage?.Sections.SingleOrDefault(s=>s.Id==_montageDrag);if(s is null)_montageDrag=Guid.Empty;else{double time=Math.Clamp(u*_montageExtent,0,600);s=_montageDragEnd?s with{End=Math.Max(s.Start+.001,time)}:s with{Start=Math.Min(s.End-.001,time)};_graphAuthor.ApplyDraft(AnimationGraphEdits.Operations(new{op="montage.section.upsert",section=s}));}}
        if(e.Phase==3)_montageDrag=Guid.Empty;SynchronizeGraph();
    }
}
