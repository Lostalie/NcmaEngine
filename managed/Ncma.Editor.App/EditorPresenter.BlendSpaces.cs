using System.Globalization;
using System.Numerics;
using Ncma.Animation;
using Ncma.Editor.Core;
using Ncma.Gui;
namespace Ncma.Editor.App;

internal sealed unsafe partial class EditorPresenter
{
    private readonly record struct SpaceProperty(Guid Node,string Field,Guid Sample=default,int Axis=0);
    private Guid _spaceSelected,_spaceDrag,_spaceQueryOwner;
    private int _spaceOffset;private double _spaceQueryX,_spaceQueryY;
    private void AddAnimSpace(AnimationGraphDefinition d)
    {
        if(!Guid.TryParseExact(_animClip,"D",out Guid clip)||clip==Guid.Empty)throw new ArgumentException("先明确输入新 Clip UUID；不猜测缺失资源。");
        var axis=new AnimationParameter(Guid.NewGuid(),"Space axis "+d.Parameters.Length,AnimationParameterKind.Float,0,0,false);
        var node=AnimationGraphNode.Create(Guid.NewGuid(),"BlendSpace",AnimationNodeKind.BlendSpace) with{X=64+d.Nodes.Length*12,Y=96,Loop=true,Speed=1,BlendSpace=new(Guid.NewGuid(),1,new(axis.Id,"X","normalized",0,1),null,1,Guid.Empty,[new(Guid.NewGuid(),clip,0,0),new(Guid.NewGuid(),clip,1,0)])};
        _graphAuthor!.ApplyDraft(AnimationGraphEdits.Operations(new{op="parameter.upsert",parameter=axis},new{op="node.upsert",node}));
        SynchronizeGraph();_animCanvas.Select(node.Id);_animElement=node.Id;_animSection="node";_animJson=null;
    }
    private void BuildSpaceProperties(AnimationGraphNode node,bool writable)
    {
        var s=node.BlendSpace!;SpaceQueryOwner(s);ulong item=100;
        void Field(string field,string label,string text,Guid sample=default,int axis=0)=>Add(GuiItemKind.Text,56,item++,label,new("anim_space_property",Operation:new GraphIntent(_graphAuthor!.Stamp,new SpaceProperty(node.Id,field,sample,axis))),value:text,enabled:writable);
        void Number(string field,string label,double value,double min,double max,Guid sample=default,int axis=0)=>Add(GuiItemKind.Number,56,item++,label,new("anim_space_property",Operation:new GraphIntent(_graphAuthor!.Stamp,new SpaceProperty(node.Id,field,sample,axis))),number:value,min:min,max:max,enabled:writable);
        Number("dimensions","维度 1/2（改1D会清除Y）",s.Dimensions,1,2);Number("cycle","周期秒数（显式）",s.CycleSeconds,.001,600);Field("group","同步组 UUID（全零=独立）",s.SyncGroup.ToString("D"));
        void Axis(BlendSpaceAxis? a,int index){Field("axis_parameter",index==0?"X Float参数 UUID":"Y Float参数 UUID",(a?.ParameterId??Guid.Empty).ToString("D"),axis:index);Field("axis_name","轴名称",a?.Name??"Y",axis:index);Field("axis_unit","控制单位标签（不转换素材）",a?.Unit??"normalized",axis:index);Number("axis_min","轴最小",a?.Minimum??0,-1000000,1000000,axis:index);Number("axis_max","轴最大",a?.Maximum??1,-1000000,1000000,axis:index);}
        Axis(s.AxisX,0);if(s.Dimensions==2)Axis(s.AxisY,1);
        _spaceOffset=Math.Clamp(_spaceOffset,0,Math.Max(0,(s.Samples.Length-1)/4*4));
        foreach(var p in s.Samples.OrderBy(p=>p.Id).Skip(_spaceOffset).Take(4)){Field("sample_clip","采样 "+p.Id.ToString("D")+" / Clip UUID",p.ClipId.ToString("D"),p.Id);Number("sample_x","采样 X",p.X,-1000000,1000000,p.Id);if(s.Dimensions==2)Number("sample_y","采样 Y",p.Y,-1000000,1000000,p.Id);Add(GuiItemKind.Button,56,item++,"删除采样",new("anim_space_delete",Operation:new GraphIntent(_graphAuthor!.Stamp,new SpaceProperty(node.Id,"delete",p.Id))),enabled:writable);}
        Add(GuiItemKind.Button,56,70,"上一页采样",new("anim_space_previous",Operation:new GraphIntent(_graphAuthor!.Stamp)),enabled:_spaceOffset>0);Line();Add(GuiItemKind.Button,56,71,"下一页采样",new("anim_space_next",Operation:new GraphIntent(_graphAuthor!.Stamp)),enabled:_spaceOffset+4<s.Samples.Length);
        Add(GuiItemKind.Button,56,72,"添加中点采样（需输入Clip UUID）",new("anim_space_add",Operation:new GraphIntent(_graphAuthor!.Stamp,new SpaceProperty(node.Id,"add"))),enabled:writable&&s.Samples.Length<32);
        Add(GuiItemKind.Number,56,60,"几何查询 X（不写草稿）",new("anim_space_query_x",Operation:new GraphIntent(_graphAuthor!.Stamp)),number:_spaceQueryX,min:-1000000,max:1000000);if(s.Dimensions==2)Add(GuiItemKind.Number,56,61,"几何查询 Y（不写草稿）",new("anim_space_query_y",Operation:new GraphIntent(_graphAuthor!.Stamp)),number:_spaceQueryY,min:-1000000,max:1000000);
    }
    private void BuildAnimSpace(AnimationGraphDefinition d,BlendSpaceDefinition definition,ref ulong label)
    {
        BlendSpaceProgram program;try{program=new(definition);}catch(ArgumentException e){Add(GuiItemKind.Label,56,label++,"采样空间未完整："+e.Message);return;}
        var s=program.CopyDefinition();SpaceQueryOwner(s);
        Add(GuiItemKind.Label,56,label++,"采样三角连线 / 几何权重查询（无Clip/GPU/World）\n拖动点只修改获批草稿，不控制Live Play。");
        float x=_geometry.Console.X+12,y=_geometry.Console.Y+78,w=Math.Max(1,_geometry.Console.Width-24),h=Math.Max(48,Math.Min(120,_geometry.Console.Height-150));
        Add(GuiItemKind.OverlayBegin,56,1,"");SetRect(x,y,w,h);
        Vector2 Position(BlendSpaceSample p)=>new((float)((p.X-s.AxisX.Minimum)/(s.AxisX.Maximum-s.AxisX.Minimum)),s.AxisY is{} a?(float)(1-(p.Y-a.Minimum)/(a.Maximum-a.Minimum)):.5f);
        foreach(var triangle in program.CopyTriangles()){var ids=new[]{triangle.A,triangle.B,triangle.C,triangle.A};for(int i=0;i<3;i++){var a=Position(s.Samples.Single(p=>p.Id==ids[i]));var b=Position(s.Samples.Single(p=>p.Id==ids[i+1]));Add(GuiItemKind.CanvasLines,56,label++,"",value:FormattableString.Invariant($"{a.X} {a.Y} {b.X} {b.Y}"),number:0xff95765c,min:1,max:1);}}
        if(s.Dimensions==1)Add(GuiItemKind.CanvasLines,56,label++,"",value:"0 0.5 1 0.5",number:0xff95765c,min:1,max:1);
        var weights=program.Evaluate(_spaceQueryX,s.Dimensions==1?0:_spaceQueryY);
        foreach(var p in s.Samples){var at=Position(p);Add(GuiItemKind.CanvasRect,56,label++,"",number:p.Id==_spaceSelected?0xffeeb45f:p.Id==weights.PrimarySample?0xff77d699:0xffbe9870,min:4,max:4);SetRect(at.X-4/w,at.Y-4/h,8/w,8/h);}
        var query=Position(new(Guid.Empty,Guid.Empty,weights.X,weights.Y));Add(GuiItemKind.CanvasRect,56,label++,"",number:0xfff0d1a5,min:2,max:2);SetRect(query.X-2/w,query.Y-2/h,4/w,4/h);
        Add(GuiItemKind.CanvasEnd,56,label++,"");Add(GuiItemKind.CanvasInput,56,2,"",new("anim_space_pointer",Operation:new GraphIntent(_graphAuthor!.Stamp)),enabled:_graphAuthor.HasDraft&&_graphAuthor.Writable);SetRect(x,y,w,h);
        Add(GuiItemKind.Label,56,label++,$"主采样 {weights.PrimarySample:D} / 投影={weights.Projected}\nA={weights.A.Weight:F4} B={weights.B.Weight:F4} C={weights.C.Weight:F4}");
    }
    private void SpaceQueryOwner(BlendSpaceDefinition s){if(_spaceQueryOwner==s.Id)return;_spaceQueryOwner=s.Id;_spaceQueryX=s.AxisX.Minimum;_spaceQueryY=s.AxisY?.Minimum??0;_spaceOffset=0;_spaceSelected=Guid.Empty;}
    private bool ApplyAnimSpace(ActionView action,GuiEvent e,string text)
    {
        if(!action.Kind.StartsWith("anim_space_",StringComparison.Ordinal))return false;var d=_graphAuthor!.Capture()!;
        if(action.Kind=="anim_space_pointer"){SpacePointer(d,e,text);return true;}if(e.Phase!=3)return true;
        if(action.Kind is "anim_space_query_x" or "anim_space_query_y"){if(!double.IsFinite(e.Value)||e.Value is <-1000000 or >1000000)throw new ArgumentException("Bounded query coordinate.");if(action.Kind.EndsWith("_x",StringComparison.Ordinal))_spaceQueryX=e.Value;else _spaceQueryY=e.Value;return true;}
        if(action.Kind=="anim_space_previous"){_spaceOffset=Math.Max(0,_spaceOffset-4);return true;}if(action.Kind=="anim_space_next"){_spaceOffset+=4;return true;}
        var p=(SpaceProperty)((GraphIntent)action.Operation!).Payload!;var n=d.Nodes.Single(v=>v.Id==p.Node);var s=n.BlendSpace!;
        static Guid Id(string t,bool empty=false)=>Guid.TryParseExact(t,"D",out Guid id)&&id.ToString("D")==t&&(empty||id!=Guid.Empty)?id:throw new ArgumentException("Canonical UUID required.");
        if(action.Kind=="anim_space_add"){Guid clip=Id(_animClip);var sample=new BlendSpaceSample(Guid.NewGuid(),clip,(s.AxisX.Minimum+s.AxisX.Maximum)/2,s.AxisY is{} a?(a.Minimum+a.Maximum)/2:0);_graphAuthor.ApplyDraft(AnimationGraphEdits.Operations(new{op="blendspace.sample.upsert",nodeId=n.Id,sample}));}
        else if(action.Kind=="anim_space_delete")_graphAuthor.ApplyDraft(AnimationGraphEdits.Operations(new{op="blendspace.sample.delete",nodeId=n.Id,sampleId=p.Sample}));
        else if(p.Field.StartsWith("sample_",StringComparison.Ordinal)){var sample=s.Samples.Single(v=>v.Id==p.Sample);sample=p.Field switch{"sample_clip"=>sample with{ClipId=Id(text)},"sample_x"=>sample with{X=e.Value},"sample_y"=>sample with{Y=e.Value},_=>throw new ArgumentException("Sample property.")};_graphAuthor.ApplyDraft(AnimationGraphEdits.Operations(new{op="blendspace.sample.upsert",nodeId=n.Id,sample}));}
        else{
            if(p.Field.StartsWith("axis_",StringComparison.Ordinal)){var a=(p.Axis==0?s.AxisX:s.AxisY)??new(Guid.Empty,"Y","normalized",0,1);a=p.Field switch{"axis_parameter"=>a with{ParameterId=Id(text)},"axis_name"=>a with{Name=text},"axis_unit"=>a with{Unit=text},"axis_min"=>a with{Minimum=e.Value},"axis_max"=>a with{Maximum=e.Value},_=>throw new ArgumentException("Axis property.")};s=p.Axis==0?s with{AxisX=a}:s with{AxisY=a};}
            else s=p.Field switch{"cycle"=>s with{CycleSeconds=e.Value},"group"=>s with{SyncGroup=Id(text,true)},"dimensions"=>e.Value is 1?s with{Dimensions=1,AxisY=null,Samples=s.Samples.Select(v=>v with{Y=0}).ToArray()}:e.Value is 2?s with{Dimensions=2,AxisY=s.AxisY??new(Guid.Empty,"Y","normalized",0,1)}:throw new ArgumentException("Integral dimensions."),_=>throw new ArgumentException("Space property.")};
            _graphAuthor.ApplyDraft(AnimationGraphEdits.Operations(new{op="node.upsert",node=n with{BlendSpace=s}}));
        }
        _animJson=null;SynchronizeGraph();return true;
    }
    private void SpacePointer(AnimationGraphDefinition d,GuiEvent e,string text)
    {
        if(!_graphAuthor!.HasDraft||!_graphAuthor.Writable)throw new EditRejectedException("space_draft_readonly");var n=d.Nodes.Single(v=>v.Id==_animElement);var s=n.BlendSpace!;
        var parts=text.Split(' ',StringSplitOptions.RemoveEmptyEntries);if(parts.Length!=3||!double.TryParse(parts[0],NumberStyles.Float,CultureInfo.InvariantCulture,out double u)||!double.TryParse(parts[1],NumberStyles.Float,CultureInfo.InvariantCulture,out double v)||!double.TryParse(parts[2],NumberStyles.Float,CultureInfo.InvariantCulture,out double wheel)||!double.IsFinite(u)||!double.IsFinite(v)||!double.IsFinite(wheel)||u is <-1 or >2||v is <-1 or >2||Math.Abs(wheel)>32||e.Value is <0 or >127||e.Value!=Math.Truncate(e.Value))throw new ArgumentException("Bounded space pointer.");
        if(e.Phase==1){_spaceDrag=Guid.Empty;var nearest=s.Samples.OrderBy(p=>p.Id).OrderBy(p=>Distance(p)).FirstOrDefault();if(nearest is not null&&Distance(nearest)<.0064){_spaceDrag=_spaceSelected=nearest.Id;_spaceOffset=Array.IndexOf(s.Samples.OrderBy(p=>p.Id).ToArray(),nearest)/4*4;}}
        if(_spaceDrag!=Guid.Empty&&s.Samples.Any(p=>p.Id==_spaceDrag)){var sample=s.Samples.Single(p=>p.Id==_spaceDrag) with{X=s.AxisX.Minimum+Math.Clamp(u,0,1)*(s.AxisX.Maximum-s.AxisX.Minimum),Y=s.AxisY is{} a?a.Minimum+(1-Math.Clamp(v,0,1))*(a.Maximum-a.Minimum):0};_graphAuthor.ApplyDraft(AnimationGraphEdits.Operations(new{op="blendspace.sample.upsert",nodeId=n.Id,sample}));_animJson=null;}
        if(e.Phase==3)_spaceDrag=Guid.Empty;SynchronizeGraph();
        double Distance(BlendSpaceSample p){double a=(p.X-s.AxisX.Minimum)/(s.AxisX.Maximum-s.AxisX.Minimum)-u,b=s.AxisY is{} y?1-(p.Y-y.Minimum)/(y.Maximum-y.Minimum)-v:.5-v;return a*a+b*b;}
    }
}
