using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Ncma.Extensions;
using Ncma.Extensions.Hosting;

internal static class ExtensionSourceCaptureTests
{
    private static ExtensionDescriptor Descriptor() => new("test.capture",new(1,0,0),new(1,0,0));
    private static void Check(bool value) { if(!value)throw new Exception("Source capture assertion failed"); }
    private static void Reject(string code,Action action) { try{action();}catch(ExtensionRejectedException e)when(e.Code==code){return;}throw new Exception("Expected "+code); }
    private sealed record Fixture(string Root,string Source,string Candidate);
    private static Fixture Create()
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Source file boundary fixtures require Windows; not skipped.");
        string root=Path.Combine(Environment.CurrentDirectory,"out","verification","m8-5","source-capture-fixtures",Guid.NewGuid().ToString("N"));
        string source=Path.Combine(root,"source"),candidate=Path.Combine(root,"candidate");
        Directory.CreateDirectory(source);Directory.CreateDirectory(candidate);
        File.WriteAllText(Path.Combine(source,"A.cs"),"class Before {}",new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(candidate,"A.cs"),"class After {}",new UTF8Encoding(false));
        return new(root,source,candidate); // Evidence retained. No recursive cleanup or links to user data.
    }
    private static ExtensionSourceCapture Capture(Fixture f,params string[] paths)=>ExtensionSourceCapture.Capture(Descriptor(),f.Source,f.Candidate,paths.Length==0?["A.cs"]:paths);
    private static void Exclusive(string file) { using var stream=new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.None); }
    internal static int Run()=>CaseHarness.Run("Windows selected source capture and complete escaped pages",[
        ("build export uses retained bytes, not changed origin and never overwrites",()=>{
            var f=Create();var capture=Capture(f);byte[] expected=capture.CopyBuildBundle();
            File.WriteAllText(Path.Combine(f.Source,"A.cs"),"changed original");File.WriteAllText(Path.Combine(f.Candidate,"A.cs"),"changed candidate");
            Check(capture.CopyBuildBundle().SequenceEqual(expected));
            string output=Path.Combine(f.Root,"review.nccapture");string hash=capture.ExportBuildBundle(output);
            Check(hash==Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(expected))&&File.ReadAllBytes(output).SequenceEqual(expected));
            bool rejected=false;try{capture.ExportBuildBundle(output);}catch(IOException){rejected=true;}Check(rejected);
            using var data=JsonDocument.Parse(expected);var file=data.RootElement.GetProperty("files")[0];
            Check(Encoding.UTF8.GetString(file.GetProperty("after").GetBytesFromBase64())=="class After {}");
        }),
        ("real selected capture exact bytes and explicit freshness (not full build closure)",()=>{
            var f=Create();string[] paths=["A.cs"];var capture=Capture(f,paths);paths[0]="Other.cs";
            Check(capture.Patch.Changes[0].Before.Text=="class Before {}"&&capture.Patch.Changes[0].After.Text=="class After {}");
            capture.RequireCurrent();File.WriteAllText(Path.Combine(f.Source,"Unselected.cs"),"not in declared scope");capture.RequireCurrent();
            File.WriteAllText(Path.Combine(f.Source,"A.cs"),"class Changed {}");Reject("source_stamp_stale",capture.RequireCurrent);
            Exclusive(Path.Combine(f.Source,"A.cs"));Exclusive(Path.Combine(f.Candidate,"A.cs"));
        }),
        ("identical-byte file replacement and malformed replacement rejected by identity before read",()=>{
            var f=Create();var capture=Capture(f);string file=Path.Combine(f.Source,"A.cs");
            File.Move(file,Path.Combine(f.Source,"retained-original.cs"));File.WriteAllText(file,"class Before {}");
            Reject("source_identity_stale",capture.RequireCurrent);
            var g=Create();var other=Capture(g);string candidate=Path.Combine(g.Candidate,"A.cs");
            File.Move(candidate,Path.Combine(g.Candidate,"retained-candidate.cs"));File.WriteAllBytes(candidate,[0xFF]);
            Reject("source_identity_stale",other.RequireCurrent);Exclusive(candidate);
        }),
        ("candidate content and source directory replacement stale",()=>{
            var f=Create();var capture=Capture(f);File.WriteAllText(Path.Combine(f.Candidate,"A.cs"),"class Later {}");Reject("source_candidate_stale",capture.RequireCurrent);
            var g=Create();var old=Capture(g);Directory.Move(g.Source,Path.Combine(g.Root,"retained-source"));Directory.CreateDirectory(g.Source);
            File.WriteAllText(Path.Combine(g.Source,"A.cs"),"class Before {}");Reject("source_identity_stale",old.RequireCurrent);
        }),
        ("explicit existing-file scope and path validation before reads",()=>{
            var f=Create();Reject("source_change_empty",()=>Capture(f,"Missing.cs"));
            Reject("source_path_invalid",()=>Capture(f,"../A.cs"));Reject("source_path_duplicate",()=>Capture(f,"A.cs","a.cs"));
            Reject("source_roots_same",()=>ExtensionSourceCapture.Capture(Descriptor(),f.Source,f.Source,"A.cs"));
            Reject("source_root_invalid",()=>ExtensionSourceCapture.Capture(Descriptor(),"relative",f.Candidate,"A.cs"));
            Reject("source_root_invalid",()=>ExtensionSourceCapture.Capture(Descriptor(),f.Source+"\\..",f.Candidate,"A.cs"));
            Reject("source_path_casing",()=>Capture(f,"a.cs"));Exclusive(Path.Combine(f.Source,"A.cs"));
            File.WriteAllText(Path.Combine(f.Candidate,"A.cs"),"class Before {}");Reject("source_change_empty",()=>Capture(f));
        }),
        ("hardlinks rejected and failure releases all earlier handles",()=>{
            var f=Create();Check(CreateHardLinkW(Path.Combine(f.Root,"owned-alias.cs"),Path.Combine(f.Candidate,"A.cs"),0));
            Reject("source_link_or_type",()=>Capture(f));Exclusive(Path.Combine(f.Source,"A.cs"));Exclusive(Path.Combine(f.Candidate,"A.cs"));
        }),
        ("active writer rejected no lock leaks after either result",()=>{
            var f=Create();using(var write=new FileStream(Path.Combine(f.Candidate,"A.cs"),FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite))
                Reject("source_open_failed",()=>Capture(f));
            Exclusive(Path.Combine(f.Source,"A.cs"));Capture(f).RequireCurrent();Exclusive(Path.Combine(f.Candidate,"A.cs"));
        }),
        ("owned junction root and descendant rejected before reading target",()=>{
            var f=Create();string link=Path.Combine(f.Root,"owned-junction");
            if((link+f.Source).IndexOfAny(['&','|','<','>','^','%','!','\r','\n','"'])>=0)throw new InvalidOperationException("Unsafe fixture shell path");
            var start=new ProcessStartInfo("cmd.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(string arg in new[]{"/d","/c","mklink","/J",link,f.Source})start.ArgumentList.Add(arg);
            using(var process=Process.Start(start)??throw new InvalidOperationException("Junction fixture start failed")){
                if(!process.WaitForExit(5000))throw new InvalidOperationException("Junction helper timed out; evidence retained");
                if(process.ExitCode!=0)throw new InvalidOperationException("Junction fixture failed; not skipped: "+process.StandardError.ReadToEnd());
            }
            Reject("source_link_or_type",()=>ExtensionSourceCapture.Capture(Descriptor(),link,f.Candidate,"A.cs"));
            Reject("source_link_or_type",()=>ExtensionSourceCapture.Capture(Descriptor(),f.Root,f.Candidate,"owned-junction/A.cs"));
            Exclusive(Path.Combine(f.Source,"A.cs"));Check(Directory.Exists(link));
        }),
        ("size and combined budgets reject before complete payload allocation",()=>{
            var f=Create();File.WriteAllText(Path.Combine(f.Source,"A.cs"),new string('a',65537));Reject("source_file_budget",()=>Capture(f));
            File.WriteAllText(Path.Combine(f.Source,"A.cs"),new string('a',65536));File.WriteAllText(Path.Combine(f.Candidate,"A.cs"),new string('b',65536));
            File.WriteAllText(Path.Combine(f.Source,"B.cs"),new string('a',65536));File.WriteAllText(Path.Combine(f.Candidate,"B.cs"),new string('b',65536));
            Check(Capture(f,"A.cs","B.cs").Patch.TotalBytes==262144);
            File.WriteAllText(Path.Combine(f.Source,"C.cs"),"a");File.WriteAllText(Path.Combine(f.Candidate,"C.cs"),"b");
            Reject("source_patch_budget",()=>Capture(f,"A.cs","B.cs","C.cs"));Exclusive(Path.Combine(f.Source,"A.cs"));
        }),
        ("all escaped Unicode BOM newline pages reconstruct original bytes",()=>{
            var f=Create();string text="\uFEFF// 中文🙂\r\n\t\"\\\u202E\r\n"+new string('中',10000)+"\n\r";
            File.WriteAllBytes(Path.Combine(f.Source,"A.cs"),Encoding.UTF8.GetBytes(text));var capture=Capture(f);var pages=new ExtensionSourcePages(capture.Patch);
            var before=capture.Patch.Changes[0].Before;Check(before.HasUtf8Bom&&before.CrLfCount==2&&before.LineFeedCount==1&&before.CarriageReturnCount==1);
            foreach(bool side in new[]{true,false}){
                var subset=pages.Pages.Where(p=>p.Before==side).ToArray();Check(subset.All(p=>p.Text.Length<=900&&p.Text.All(c=>c<128)));
                string decoded=JsonSerializer.Deserialize<string>(string.Concat(subset.Select(p=>p.Text)))!;
                Check(Encoding.UTF8.GetBytes(decoded).SequenceEqual(side?before.CopyBytes():capture.Patch.Changes[0].After.CopyBytes()));
                foreach(var page in subset){int at=0;while(at<page.Text.Length){int count=page.Text[at]=='\\'?(page.Text[at+1]=='u'?6:2):1;at+=count;}Check(at==page.Text.Length);}
            }
        }),
        ("absent versus empty escaped representation and immutable page list",()=>{
            var patch=ExtensionSourcePatch.Create(Descriptor(),new(Guid.NewGuid(),1,new string('A',64)),new ExtensionSourceChange(ExtensionSourceFile.Absent("A.cs"),ExtensionSourceFile.Present("A.cs",[])));
            var pages=new ExtensionSourcePages(patch);Check(pages.Pages.Count==2&&pages.Pages[0].Text=="null"&&pages.Pages[1].Text=="\"\"");
            bool blocked=false;try{((IList<ExtensionSourcePage>)pages.Pages)[0]=new(0,false,"changed");}catch(NotSupportedException){blocked=true;}Check(blocked);
        }),
        ("real add delete explicit absence and later creation invalidate review",()=>{
            var f=Create();File.WriteAllText(Path.Combine(f.Candidate,"Added.cs"),"");File.WriteAllText(Path.Combine(f.Source,"Deleted.cs"),"old");
            var capture=Capture(f,"Added.cs","Deleted.cs");Check(capture.Patch.Changes[0].Kind==ExtensionSourceChangeKind.Add&&capture.Patch.Changes[1].Kind==ExtensionSourceChangeKind.Delete);
            capture.RequireCurrent();File.WriteAllText(Path.Combine(f.Source,"Added.cs"),"");Reject("source_identity_stale",capture.RequireCurrent);
            var g=Create();File.WriteAllText(Path.Combine(g.Source,"Deleted.cs"),"old");var deletion=Capture(g,"Deleted.cs");
            File.WriteAllText(Path.Combine(g.Candidate,"Deleted.cs"),"new");Reject("source_identity_stale",deletion.RequireCurrent);
        })
    ]);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)] private static extern bool CreateHardLinkW(string path,string existing,nint security);
}
