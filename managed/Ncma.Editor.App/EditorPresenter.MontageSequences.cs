using System.Text.Json;
using Ncma.Animation;
using Ncma.Editor.Services;
using Ncma.Gui;
namespace Ncma.Editor.App;

internal sealed unsafe partial class EditorPresenter
{
    private int _sequenceControlStep=1,_sequenceControlKind,_sequenceControlPriority,_sequenceControlPage,_sequenceControlIndex=-1;
    private string _sequenceControlSlot=Guid.Empty.ToString("D"),_sequenceControlSection=Guid.Empty.ToString("D");
    private int _sequenceCheckStep=1,_sequenceCheckKind=8;
    private string _sequenceCheckSubject=Guid.Empty.ToString("D");private double _sequenceCheckValue;
    private AnimationSequenceCase SequenceEditorCase(){using var json=JsonDocument.Parse(_animSequenceJson.Replace(_animSequencePage,_animSequenceJson.Text(_animSequencePage)));return AnimationSequenceCodec.Decode(json.RootElement);}
    private void SequenceEditorSet(AnimationSequenceCase value){_animSequenceJson=new(AnimationSequenceCodec.Encode(value).GetRawText());_animSequencePage=0;_animSequenceSelected=Guid.Empty;_animSequenceReview=null;_animSequenceResult=null;_animSequenceReviewed=false;}
    private void BuildMontageSequenceEditor(ref ulong label)
    {
        if(_graphAuthor!.Capture()?.Montage is null)return;
        Add(GuiItemKind.Label,60,label++,"隔离用例请求（只改测试数据，必须加入新用例后运行；没有Live控制）");
        void Number(ulong id,string title,string kind,double value,double min,double max)=>Add(GuiItemKind.Number,60,id,title,new("anim_sequence_control_"+kind,Operation:new GraphIntent(_graphAuthor.Stamp)),number:value,min:min,max:max);
        void Text(ulong id,string title,string kind,string value)=>Add(GuiItemKind.Text,60,id,title,new("anim_sequence_control_"+kind,Operation:new GraphIntent(_graphAuthor.Stamp)),value:value);
        void Button(ulong id,string title,string kind,bool enabled=true,int? index=null)=>Add(GuiItemKind.Button,60,id,title,new("anim_sequence_control_"+kind,Operation:new GraphIntent(_graphAuthor.Stamp,index)),enabled:enabled);
        Number(1,"请求量子","step",_sequenceControlStep,1,256);Number(2,"0 Play / 1 Cancel / 2 Jump","kind",_sequenceControlKind,0,2);
        Text(3,"明确 Slot UUID","slot",_sequenceControlSlot);Text(4,"Section UUID（Play可空UUID，Cancel必须空）","section",_sequenceControlSection);Number(5,"请求优先级","priority",_sequenceControlPriority,0,255);
        AnimationSequenceCase input;try{input=SequenceEditorCase();}catch(Exception e)when(e is ArgumentException or JsonException){Add(GuiItemKind.Label,60,label++,"先修复闭合用例 JSON，再编辑 typed 请求。");return;}
        _sequenceControlPage=Math.Clamp(_sequenceControlPage,0,Math.Max(0,(input.MontageRequests.Length-1)/4));
        Button(6,"追加隔离请求","add",input.MontageRequests.Length<64);Line();Button(7,"替换选中请求","replace",_sequenceControlIndex>=0&&_sequenceControlIndex<input.MontageRequests.Length);
        Button(8,"删除选中请求","delete",_sequenceControlIndex>=0&&_sequenceControlIndex<input.MontageRequests.Length);
        foreach(var row in input.MontageRequests.Select((r,i)=>(r,i)).Skip(_sequenceControlPage*4).Take(4))Button((ulong)(1000+row.i),$"[{row.i}] #{row.r.Step} {row.r.Kind} Slot={row.r.SlotId:D}","select",index:row.i);
        Button(9,"上一页请求","previous",_sequenceControlPage>0);Line();Button(10,"下一页请求","next",(_sequenceControlPage+1)*4<input.MontageRequests.Length);
        Add(GuiItemKind.Label,60,label++,"闭合 Slot 断言：8 Active(0/1), 9 Time, 10 Weight, 11 Section(value=0), 12 Outcome(0 Started/1 Cancelled/2 Jumped/3 PriorityRejected/4 NonInterruptibleRejected/5 NotPlaying), 13 ReceiptCount");
        Number(20,"断言量子","check_step",_sequenceCheckStep,1,256);Number(21,"断言类型","check_kind",_sequenceCheckKind,8,13);Text(22,"断言 Slot UUID（Section类型填Section UUID）","check_subject",_sequenceCheckSubject);Number(23,"断言数值","check_value",_sequenceCheckValue,0,600);Button(24,"追加闭合断言","check_add",input.Assertions.Length<64);
    }
    private bool ApplyMontageSequenceEditor(ActionView action,GuiEvent e,string text)
    {
        const string prefix="anim_sequence_control_";if(!action.Kind.StartsWith(prefix,StringComparison.Ordinal))return false;
        static int Integer(double value,int min,int max)=>double.IsFinite(value)&&value==Math.Truncate(value)&&value>=min&&value<=max?(int)value:throw new ArgumentException("Integral isolated control field.");
        static Guid Id(string value,bool empty=false)=>Guid.TryParseExact(value,"D",out Guid id)&&id.ToString("D")==value&&(empty||id!=Guid.Empty)?id:throw new ArgumentException("Exact isolated UUID.");
        switch(action.Kind[prefix.Length..]){
            case "step":_sequenceControlStep=Integer(e.Value,1,256);break;case "kind":_sequenceControlKind=Integer(e.Value,0,2);break;
            case "priority":_sequenceControlPriority=Integer(e.Value,0,255);break;case "slot":_=Id(text);_sequenceControlSlot=text;break;case "section":_=Id(text,true);_sequenceControlSection=text;break;
            case "previous":_sequenceControlPage--;break;case "next":_sequenceControlPage++;break;
            case "select":_sequenceControlIndex=(int)((GraphIntent)action.Operation!).Payload!;var selected=SequenceEditorCase().MontageRequests[_sequenceControlIndex];_sequenceControlStep=selected.Step;_sequenceControlKind=(int)selected.Kind;_sequenceControlSlot=selected.SlotId.ToString("D");_sequenceControlSection=selected.SectionId.ToString("D");_sequenceControlPriority=selected.Priority;break;
            case "delete":var removed=SequenceEditorCase();SequenceEditorSet(removed with{MontageRequests=removed.MontageRequests.Where((_,i)=>i!=_sequenceControlIndex).ToArray()});_sequenceControlIndex=-1;break;
            case "add":case "replace":
                var input=SequenceEditorCase();var request=new AnimationSequenceMontageRequest(_sequenceControlStep,Id(_sequenceControlSlot),(MontageRequestKind)_sequenceControlKind,Id(_sequenceControlSection,true),_sequenceControlPriority);
                if(request.Step>input.Steps)throw new ArgumentException("Request exceeds authored steps.");var m=_graphAuthor!.Capture()!.Montage!;
                if(!m.Slots.Any(s=>s.Id==request.SlotId)||(request.Kind==MontageRequestKind.Cancel?request.SectionId!=Guid.Empty:request.SectionId!=Guid.Empty&&!m.Sections.Any(s=>s.Id==request.SectionId&&s.SlotId==request.SlotId))||request.Kind==MontageRequestKind.Jump&&request.SectionId==Guid.Empty)throw new ArgumentException("Exact isolated Slot/Section required.");
                var rows=input.MontageRequests.ToList();if(action.Kind.EndsWith("replace",StringComparison.Ordinal))rows[_sequenceControlIndex]=request;else{if(rows.Count>=64)throw new ArgumentException("Request budget.");rows.Add(request);}SequenceEditorSet(input with{MontageRequests=rows.ToArray()});break;
            case "check_step":_sequenceCheckStep=Integer(e.Value,1,256);break;case "check_kind":_sequenceCheckKind=Integer(e.Value,8,13);break;
            case "check_subject":_=Id(text);_sequenceCheckSubject=text;break;case "check_value":if(!double.IsFinite(e.Value)||e.Value is <0 or >600)throw new ArgumentException("Finite isolated assertion value.");_sequenceCheckValue=e.Value;break;
            case "check_add":var check=SequenceEditorCase();if(check.Assertions.Length>=64||_sequenceCheckStep>check.Steps)throw new ArgumentException("Assertion budget/step.");SequenceEditorSet(check with{Assertions=[..check.Assertions,new(_sequenceCheckStep,(AnimationSequenceAssertionKind)_sequenceCheckKind,Id(_sequenceCheckSubject),_sequenceCheckValue)]});break;
            default:throw new ArgumentException("Closed isolated UI control.");
        }
        return true;
    }
}
