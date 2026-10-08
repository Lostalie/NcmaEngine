using Ncma.Animation;
using Ncma.Editor.Services;
using Ncma.Gui;
namespace Ncma.Editor.App;

internal sealed unsafe partial class EditorPresenter
{
    private readonly record struct GraphProperty(string Section,Guid Id,string Field,int Index=-1);
    private void BuildAnimProperties(AnimationGraphDefinition d,bool writable)
    {
        ulong id=600; string section=_animSection;Guid element=_animElement;
        void Text(string field,string label,string value,int index=-1)=>Add(GuiItemKind.Text,50,id++,label,new("anim_property",Operation:new GraphIntent(_graphAuthor!.Stamp,new GraphProperty(section,element,field,index))),value:value,enabled:writable);
        void Number(string field,string label,double value,double min,double max,int index=-1)=>Add(GuiItemKind.Number,50,id++,label,new("anim_property",Operation:new GraphIntent(_graphAuthor!.Stamp,new GraphProperty(section,element,field,index))),number:value,min:min,max:max,enabled:writable);
        void Bool(string field,string label,bool value,int index=-1)=>Add(GuiItemKind.Checkbox,50,id++,label,new("anim_property",Operation:new GraphIntent(_graphAuthor!.Stamp,new GraphProperty(section,element,field,index))),number:value?1:0,max:1,enabled:writable);
        if(section=="node"){
            var n=d.Nodes.Single(v=>v.Id==element);Text("name","节点名称",n.Name);Number("x","画布 X",n.X,-65536,65536);Number("y","画布 Y",n.Y,-65536,65536);
            if(n.Kind==AnimationNodeKind.Clip){Text("clip","Clip UUID",n.ClipId.ToString("D"));Bool("loop","循环",n.Loop);Number("speed","速度",n.Speed,0,8);}
            if(n.Kind==AnimationNodeKind.Blend)Number("weight","混合权重",n.Weight,0,1);
            if(n.Kind==AnimationNodeKind.Parameter)Text("parameter","参数 UUID",n.ParameterId.ToString("D"));
        }else if(section=="parameter"){
            var p=d.Parameters.Single(v=>v.Id==element);Text("name","参数名称",p.Name);Number("kind","类型 0 Float / 1 Int / 2 Bool / 3 Trigger",(int)p.Kind,0,3);
            if(p.Kind==AnimationParameterKind.Float)Number("float","Float 默认值",p.FloatDefault,-1000000,1000000);
            if(p.Kind==AnimationParameterKind.Int)Number("int","Int 默认值",p.IntDefault,int.MinValue,int.MaxValue);
            if(p.Kind==AnimationParameterKind.Bool)Bool("bool","Bool 默认值",p.BoolDefault);
        }else if(section=="state"){
            var s=d.States.Single(v=>v.Id==element);Text("name","状态名称",s.Name);Text("pose","状态姿态节点 UUID",s.PoseNode.ToString("D"));
        }else if(section=="transition"){
            var t=d.Transitions.Single(v=>v.Id==element);Text("from","源状态 UUID",t.From.ToString("D"));Text("to","目标状态 UUID",t.To.ToString("D"));Number("priority","优先级",t.Priority,0,255);Number("duration","过渡秒数",t.Duration,0,10);Bool("exit_on","使用 Exit Time",t.ExitTime.HasValue);if(t.ExitTime is {} exit)Number("exit","Exit Time",exit,0,1);
            for(int i=0;i<t.Conditions.Length;i++){var c=t.Conditions[i];Text("condition_parameter","条件参数 UUID "+i,c.ParameterId.ToString("D"),i);Number("condition_comparison","比较 0== 1!= 2> 3>= 4< 5<= 6 Triggered",(int)c.Comparison,0,6,i);var p=d.Parameters.SingleOrDefault(v=>v.Id==c.ParameterId);
                if(p?.Kind==AnimationParameterKind.Float)Number("condition_float","条件 Float",c.FloatValue,-1000000,1000000,i);if(p?.Kind==AnimationParameterKind.Int)Number("condition_int","条件 Int",c.IntValue,int.MinValue,int.MaxValue,i);if(p?.Kind==AnimationParameterKind.Bool)Bool("condition_bool","条件 Bool",c.BoolValue,i);
                Add(GuiItemKind.Button,50,id++,"删除条件 "+i,new("anim_property",Operation:new GraphIntent(_graphAuthor!.Stamp,new GraphProperty(section,element,"condition_delete",i))),enabled:writable);
            }
            Add(GuiItemKind.Button,50,id++,"添加条件",new("anim_property",Operation:new GraphIntent(_graphAuthor!.Stamp,new GraphProperty(section,element,"condition_add"))),enabled:writable&&t.Conditions.Length<AnimationGraphCodec.MaxConditions&&d.Parameters.Length>0);
        }else if(section=="event"){
            var marker=d.Events.Single(v=>v.Id==element);Text("name","事件名称（数据，不是回调）",marker.Name);Text("clip","Clip UUID",marker.ClipId.ToString("D"));Number("time","事件时间（秒，实际长度保存时验证）",marker.Time,.000001,600);
        }else if(section=="link"){
            var l=d.Links.Single(v=>v.Id==element);Text("from","输出节点 UUID",l.From.ToString("D"));Text("to","输入节点 UUID",l.To.ToString("D"));Text("from_pin","输出引脚",l.FromPin);Text("to_pin","输入引脚",l.ToPin);
        }
    }
    private void ApplyAnimProperty(GraphProperty field,double value,string text)
    {
        var d=_graphAuthor!.Capture()!;
        static int Integer(double value,int min,int max){if(!double.IsFinite(value)||value!=Math.Truncate(value)||value<min||value>max)throw new ArgumentException("Integral graph property.");return (int)value;}
        static bool Boolean(double value)=>value is 0?false:value is 1?true:throw new ArgumentException("Boolean graph property.");
        static Guid Id(string text)=>Guid.TryParseExact(text,"D",out Guid id)&&id!=Guid.Empty&&id.ToString("D")==text?id:throw new ArgumentException("Exact graph UUID.");
        object op;
        switch(field.Section){
            case "node":var n=d.Nodes.Single(v=>v.Id==field.Id);n=field.Field switch{"name"=>n with{Name=text},"x"=>n with{X=value},"y"=>n with{Y=value},"clip"=>n with{ClipId=Id(text)},"parameter"=>n with{ParameterId=Id(text)},"loop"=>n with{Loop=Boolean(value)},"speed"=>n with{Speed=value},"weight"=>n with{Weight=value},_=>throw new ArgumentException("Node property.")};op=new{op="node.upsert",node=n};break;
            case "parameter":var p=d.Parameters.Single(v=>v.Id==field.Id);p=field.Field switch{"name"=>p with{Name=text},"kind"=>p with{Kind=(AnimationParameterKind)Integer(value,0,3),FloatDefault=0,IntDefault=0,BoolDefault=false},"float"=>p with{FloatDefault=value},"int"=>p with{IntDefault=Integer(value,int.MinValue,int.MaxValue)},"bool"=>p with{BoolDefault=Boolean(value)},_=>throw new ArgumentException("Parameter property.")};op=new{op="parameter.upsert",parameter=p};break;
            case "state":var s=d.States.Single(v=>v.Id==field.Id);s=field.Field switch{"name"=>s with{Name=text},"pose"=>s with{PoseNode=Id(text)},_=>throw new ArgumentException("State property.")};op=new{op="state.upsert",state=s};break;
            case "link":var l=d.Links.Single(v=>v.Id==field.Id);l=field.Field switch{"from"=>l with{From=Id(text)},"to"=>l with{To=Id(text)},"from_pin"=>l with{FromPin=text},"to_pin"=>l with{ToPin=text},_=>throw new ArgumentException("Link property.")};op=new{op="link.upsert",link=l};break;
            case "event":var marker=d.Events.Single(v=>v.Id==field.Id);marker=field.Field switch{"name"=>marker with{Name=text},"clip"=>marker with{ClipId=Id(text)},"time"=>marker with{Time=value},_=>throw new ArgumentException("Event property.")};op=new{op="event.upsert",marker};break;
            case "transition":var t=d.Transitions.Single(v=>v.Id==field.Id);
                if(field.Field.StartsWith("condition_",StringComparison.Ordinal)){
                    var conditions=t.Conditions.ToList();if(field.Field=="condition_add"){var first=d.Parameters.First();conditions.Add(new(first.Id,first.Kind==AnimationParameterKind.Trigger?AnimationComparison.Triggered:AnimationComparison.Equal,0,0,false));}
                    else if(field.Field=="condition_delete")conditions.RemoveAt(field.Index);
                    else{var c=conditions[field.Index];c=field.Field switch{"condition_parameter"=>c with{ParameterId=Id(text),FloatValue=0,IntValue=0,BoolValue=false},"condition_comparison"=>c with{Comparison=(AnimationComparison)Integer(value,0,6)},"condition_float"=>c with{FloatValue=value},"condition_int"=>c with{IntValue=Integer(value,int.MinValue,int.MaxValue)},"condition_bool"=>c with{BoolValue=Boolean(value)},_=>throw new ArgumentException("Condition property.")};conditions[field.Index]=c;}
                    t=t with{Conditions=conditions.ToArray()};
                }else t=field.Field switch{"from"=>t with{From=Id(text)},"to"=>t with{To=Id(text)},"priority"=>t with{Priority=Integer(value,0,255)},"duration"=>t with{Duration=value},"exit_on"=>t with{ExitTime=Boolean(value)?0:null},"exit"=>t with{ExitTime=value},_=>throw new ArgumentException("Transition property.")};op=new{op="transition.upsert",transition=t};break;
            default:throw new ArgumentException("Graph property section.");
        }
        _graphAuthor.ApplyDraft(AnimationGraphEdits.Operations(op));_animJson=null;
    }
}
