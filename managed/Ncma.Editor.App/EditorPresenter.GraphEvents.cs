using System.Globalization;
using Ncma.Animation;
using Ncma.Editor.Core;
using Ncma.Gui;
namespace Ncma.Editor.App;

internal sealed unsafe partial class EditorPresenter
{
    private Guid _animEventDrag;
    private double _animEventExtent;
    private void BuildAnimEventTrack(AnimationGraphDefinition d,ref ulong label)
    {
        double extent=Math.Max(1,d.Events.Select(e=>e.Time).DefaultIfEmpty(0).Max());
        Add(GuiItemKind.Label,50,label++,$"事件绝对秒时标 0–{extent:F3}s（不是Clip长度）；当前页16个标记\n拖动仅编辑已批准草稿，不seek、不发送玩法回调。\n\n\n");
        float x=_geometry.Console.X+10,y=_geometry.Console.Y+62,w=Math.Max(1,_geometry.Console.Width-20),h=32;
        Add(GuiItemKind.OverlayBegin,54,1,"");SetRect(x,y,w,h);
        Add(GuiItemKind.CanvasLines,54,label++,"",value:"0 0.5 1 0.5",number:0xffa79580,min:1,max:1);
        var fired=_graphPreview?.GraphDebug?.Events.Select(e=>e.MarkerId).ToHashSet();
        foreach(var e in d.Events.OrderBy(e=>e.Time).ThenBy(e=>e.Id).Skip(_animElementOffset).Take(16)) {
            float u=(float)(e.Time/extent);Add(GuiItemKind.CanvasRect,54,1000+AnimRow(e.Id),"",number:e.Id==_animElement?0xffeeb45f:fired?.Contains(e.Id)==true?0xff77d699:0xffbe9870,min:3,max:3);SetRect(u-3/w,.25f,6/w,.5f);
        }
        Add(GuiItemKind.CanvasEnd,54,label++,"");
        Add(GuiItemKind.CanvasInput,54,2,"",new("anim_event_pointer",Operation:new GraphIntent(_graphAuthor!.Stamp)),enabled:_graphAuthor.HasDraft&&_graphAuthor.Writable);SetRect(x,y,w,h);
    }
    private void AnimEventPointer(GuiEvent e,string text)
    {
        if(!_graphAuthor!.HasDraft||!_graphAuthor.Writable)throw new EditRejectedException("event_draft_readonly");
        var parts=text.Split(' ',StringSplitOptions.RemoveEmptyEntries);
        if(parts.Length!=3||!double.TryParse(parts[0],NumberStyles.Float,CultureInfo.InvariantCulture,out double u)||!double.IsFinite(u)||u is < -1 or >2||
            !double.TryParse(parts[1],NumberStyles.Float,CultureInfo.InvariantCulture,out double v)||!double.IsFinite(v)||Math.Abs(v)>64||
            !double.TryParse(parts[2],NumberStyles.Float,CultureInfo.InvariantCulture,out double wheel)||!double.IsFinite(wheel)||Math.Abs(wheel)>32||e.Value is <0 or >127||e.Value!=Math.Truncate(e.Value))throw new ArgumentException("Bounded event track input.");
        var d=_graphAuthor.Capture()!;
        if(e.Phase==1) {
            _animEventExtent=Math.Max(1,d.Events.Select(m=>m.Time).DefaultIfEmpty(0).Max());
            var closest=d.Events.OrderBy(m=>m.Time).ThenBy(m=>m.Id).Skip(_animElementOffset).Take(16).OrderBy(m=>Math.Abs(m.Time/_animEventExtent-u)).FirstOrDefault();
            _animEventDrag=closest is not null&&Math.Abs(closest.Time/_animEventExtent-u)<=.03?closest.Id:Guid.Empty;
            if(_animEventDrag!=Guid.Empty){_animElement=_animEventDrag;_animJson=null;}
        }
        if(_animEventDrag!=Guid.Empty&&!d.Events.Any(m=>m.Id==_animEventDrag))_animEventDrag=Guid.Empty;
        if(_animEventDrag!=Guid.Empty) {
            var marker=d.Events.Single(m=>m.Id==_animEventDrag) with{Time=Math.Clamp(u*_animEventExtent,.000001,600)};
            _graphAuthor.ApplyDraft(AnimationGraphEdits.Operations(new{op="event.upsert",marker}));
        }
        if(e.Phase==3)_animEventDrag=Guid.Empty;
        SynchronizeGraph();
    }
    private string AnimDebugLabel(Guid id,string name)
    {
        var debug=_graphPreview?.GraphDebug;if(debug?.SnapshotValid!=true)return name;
        var f=debug.Frame;
        return (id==f.StateId?"[当前/目标] ":id==f.FromStateId?"[源] ":id==f.TransitionId?"[活动转换] ":debug.Events.Any(e=>e.MarkerId==id)?"[本量子事件] ":"")+name;
    }
}
