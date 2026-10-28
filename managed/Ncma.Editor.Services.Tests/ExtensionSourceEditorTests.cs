#if NCMA_EXTENSIONS
using System.Text;
using System.Text.Json;
using Ncma.Editor.App;
using Ncma.Editor.Services;
using Ncma.Gui;

internal static class ExtensionSourceEditorTests
{
    private static void Check(bool value){if(!value)throw new Exception("Source review presenter assertion");}
    internal static IEnumerable<(string,Action)> Cases(string output)
    {
        yield return("M8.5-C3-B local full source pages then explicit recheck no files or scene mutations",()=>Run(output,0));
        yield return("M8.5-C3-B stale disk recheck hides old contents and acknowledgements",()=>Run(output,1));
        yield return("M8.5-C3-B editing scope cancels review and stale native events cannot restore it",()=>Run(output,2));
        yield return("M8.5 captured build export uses reviewed bytes after candidate disk changes",()=>Run(output,3));
    }
    private static void Run(string output,int scenario)
    {
        string root=Path.Combine(output,"source-review-ui",Guid.NewGuid().ToString("N"));
        string source=Path.Combine(root,"source"),candidate=Path.Combine(root,"candidate");Directory.CreateDirectory(source);Directory.CreateDirectory(candidate);
        byte[] before=Encoding.UTF8.GetBytes("\uFEFF// Before 中文🙂\r\n"+new string('中',600)),after=Encoding.UTF8.GetBytes("// After\n"+new string('a',1300));
        string file=Path.Combine(source,"A.cs"),next=Path.Combine(candidate,"A.cs");File.WriteAllBytes(file,before);File.WriteAllBytes(next,after);
        using var owner=new EditorSessionOwner("Source review");var workspace=new EditorWorkspace(owner);
        using var host=new EditorTrustedExtensions(workspace);host.Start();var view=new EditorPresenter(workspace,null,workspaceStyle:true);view.AttachTrustedExtensions(host);
        byte[] scene=owner.Document.CaptureBytes();var state=owner.Edit!.State;
        ulong frame=0;void Build()=>view.Build(++frame,1280,720);
        GuiEvent Event(ulong high,ulong low,double value=0,int bytes=0){var item=view.Items.ToArray().Single(i=>i.WidgetHigh==high&&i.WidgetLow==low);return new(){Kind=item.Kind,WidgetHigh=high,WidgetLow=low,Phase=3,Value=value,TextLength=(uint)bytes,Frame=view.Frame.Frame,ViewGeneration=view.Frame.ViewGeneration,DocumentGeneration=view.Frame.DocumentGeneration,Revision=view.Frame.Revision};}
        bool Enabled(ulong low)=>view.Items.ToArray().Single(i=>i.WidgetHigh==84&&i.WidgetLow==low).Enabled!=0;
        bool Has(ulong low)=>view.Items.ToArray().Any(i=>i.WidgetHigh==84&&i.WidgetLow==low);
        void Text(ulong low,string text){byte[] bytes=Encoding.UTF8.GetBytes(text);view.Apply([Event(84,low,bytes:bytes.Length)],bytes);Build();}
        string Labels()=>Encoding.UTF8.GetString(view.Text);
        Build();view.Apply([Event(24,12)],[]);Build();view.Apply([Event(24,76)],[]);Build();
        Text(20,source);Text(21,candidate);Text(22,"A.cs");Check(!Has(24)); // input alone never reads
        view.Apply([Event(84,23)],[]);Build();Check(Has(24)&&Labels().Contains("Before",StringComparison.Ordinal));
        Check(!Enabled(28));view.Apply([Event(84,28)],[]);Build();Check(!Labels().Contains("本次人工审阅已记录",StringComparison.Ordinal));
        int pages=0;while(true){Check(++pages<128);view.Apply([Event(84,26,1)],[]);Build();if(!Enabled(25))break;view.Apply([Event(84,25)],[]);Build();}
        Check(pages>2&&!Enabled(28));view.Apply([Event(84,27,1)],[]);Build();Check(Enabled(28));var submit=Event(84,28);
        if(scenario==1)File.WriteAllText(file,"externally changed",new UTF8Encoding(false));
        if(scenario==2){Text(22,"B.cs");view.Apply([submit],[]);Build();Check(!Has(24)&&!Labels().Contains("本次人工审阅已记录",StringComparison.Ordinal));}
        else{
            view.Apply([submit],[]);Build();
            if(scenario==1){Check(!Has(24)&&view.LastMessage=="source_stamp_stale"&&!Labels().Contains("本次人工审阅已记录",StringComparison.Ordinal));}
            else{
                Check(Labels().Contains("本次人工审阅已记录",StringComparison.Ordinal)&&!Enabled(28));view.Apply([submit],[]);Build();Check(!Enabled(28));
                if(scenario==3){
                    File.WriteAllText(next,"candidate edited after review",new UTF8Encoding(false));
                    string bundle=Path.Combine(root,"review.nccapture");Text(30,bundle);Check(Enabled(31));
                    var export=Event(84,31);view.Apply([export],[]);Build();
                    byte[] saved=File.ReadAllBytes(bundle);using var data=JsonDocument.Parse(saved);
                    Check(data.RootElement.GetProperty("files")[0].GetProperty("after").GetBytesFromBase64().AsSpan().SequenceEqual(after));
                    view.Apply([export],[]);Build();Check(File.ReadAllBytes(bundle).AsSpan().SequenceEqual(saved));
                }
            }
        }
        Check(scene.AsSpan().SequenceEqual(owner.Document.CaptureBytes())&&state==owner.Edit.State&&owner.Play is null);
        if(scenario==3)Check(File.ReadAllText(next)=="candidate edited after review");
        else Check(File.ReadAllBytes(next).AsSpan().SequenceEqual(after));
        if(scenario!=1)Check(File.ReadAllBytes(file).AsSpan().SequenceEqual(before));
        view.Apply([Event(84,1)],[]);Build();Check(!Has(24)&&!Labels().Contains("本次人工审阅已记录",StringComparison.Ordinal));
    }
}
#endif
