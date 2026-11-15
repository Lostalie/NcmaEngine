using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ncma.Editor.Services;

internal static class WorkflowCaptureBridgeTests
{
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static string H => new('A', 64);
    // Retain failed owners so a failed assertion never silently releases locks.
    private static readonly List<WorkflowCaptureBridge> retained = [];
    private static void Check(bool value) { if (!value) throw new Exception("M8.6-C2b1 bridge assertion"); }
    private static void Reject(Action action) { try { action(); } catch (Exception e) when (e is IOException or InvalidOperationException or ArgumentException) { return; } throw new Exception("Expected rejection"); }
    private static WorkflowVerificationTask TaskFor(WorkflowVerificationBinding? binding = null) => new(binding ?? new(1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), H,
        new(Guid.NewGuid(), 1, 1), Guid.NewGuid(), H, H, H, null, "editor-services-build-v1", "Debug", "default", "unverified-fixture-tool", H), [new("tool.exe", 1, H)], []);
    private static WorkflowBuildEnvironment EnvironmentFor(string root) => new(root, @"C:\fixture\dotnet.exe", @"C:\fixture\powershell.exe", H,
        WorkflowCaptureBridge.RequiredHelpers().Select(p => new WorkflowEvidenceFile(p, 1, H)), TimeSpan.FromSeconds(90));
    private static Dictionary<string, object?> Facts(WorkflowVerificationTask task, WorkflowBuildEnvironment env)
    {
        var b = task.Binding; string capture = Path.Combine(env.WorkspaceRoot, "out", "captured-build", b.CaptureId.ToString("N"));
        return new() { ["kind"]="ncma.workflow.capture-preparation.v1",["schemaVersion"]=1,["workspace"]=env.WorkspaceRoot,
            ["captureId"]=b.CaptureId.ToString("D"),["jobId"]=b.JobId.ToString("D"),["recipeId"]=b.RecipeId,["configuration"]=b.Configuration,["variant"]=b.Variant,
            ["captureRoot"]=capture,["sourceRoot"]=Path.Combine(capture,"source"),["project"]=task.Recipe.Project,
            ["outputRoot"]=Path.Combine(env.WorkspaceRoot,"out","verification","m8-6","jobs",b.JobId.ToString("N")),
            ["sourceInputsSha256"]=b.SourceInputsSha256,["captureManifestSha256"]=b.CaptureManifestSha256,["captureRecipeSha256"]=b.CaptureRecipeSha256,
            ["reviewBundleSha256"]=null,["sourceFileCount"]=2,["processStarted"]=false,["launchAuthorized"]=false,["outputReserved"]=false };
    }
    internal static IEnumerable<(string, Action)> Cases(string output)
    {
        yield return ("M8.6-C2b1 fixed helper closure and diagnostic facts are not execution authority", () => {
            var task=TaskFor();var env=EnvironmentFor(@"F:\NcmaEngineRelease\source"); var bridge=new WorkflowCaptureBridge(task,env);
            Check(!bridge.Snapshot().Ready); Reject(()=>bridge.ReadPreparedFacts()); Check(bridge.TryClose());
            var wrong=new WorkflowBuildEnvironment(env.WorkspaceRoot,env.ToolPath,env.RunnerPath,H,env.CopyHelpers()[..^1],env.Timeout);
            Reject(()=>new WorkflowCaptureBridge(task,wrong));
            var files=WorkflowCaptureBridge.RequiredHelpers();files[0]="evil.ps1";Check(WorkflowCaptureBridge.RequiredHelpers()[0]!="evil.ps1");
        });
        yield return ("M8.6-C2b1 exact capture facts reject identity path permission and duplicate fields", () => {
            var task=TaskFor();var env=EnvironmentFor(@"F:\NcmaEngineRelease\source");var facts=Facts(task,env);
            string json=JsonSerializer.Serialize(facts);Check(WorkflowCaptureBridge.ValidateFacts(json,task,env)==2);
            foreach(var change in new (string,object?)[]{("jobId",Guid.NewGuid().ToString()),("sourceInputsSha256",new string('B',64)),("variant","ai"),
                ("outputRoot",@"F:\outside"),("processStarted",true),("launchAuthorized",true),("outputReserved",true),("sourceFileCount",65537),("unexpected",true)}){
                var candidate=new Dictionary<string,object?>(facts){[change.Item1]=change.Item2};Reject(()=>WorkflowCaptureBridge.ValidateFacts(JsonSerializer.Serialize(candidate),task,env));
            }
            Reject(()=>WorkflowCaptureBridge.ValidateFacts("{\"kind\":\"duplicate\","+json[1..],task,env));
            facts.Remove("reviewBundleSha256");Reject(()=>WorkflowCaptureBridge.ValidateFacts(JsonSerializer.Serialize(facts),task,env));
        });
        yield return ("M8.6-C2b1 Windows argument quoting and local path rejection", () => {
            Check(WorkflowWindowsProcess.Quote("abc")=="\"abc\"");Check(WorkflowWindowsProcess.Quote("a b")=="\"a b\"");
            Check(WorkflowWindowsProcess.Quote("a\"b")=="\"a\\\"b\"");Check(WorkflowWindowsProcess.Quote("a\\")=="\"a\\\\\"");
            Reject(()=>WorkflowWindowsProcess.Quote("a\nb"));Reject(()=>WorkflowPhysicalPins.Absolute(@"F:\root\..\escape"));
            Reject(()=>WorkflowPhysicalPins.Absolute(@"\\server\share\tool.exe"));Reject(()=>WorkflowPhysicalPins.Absolute(@"F:\root\file:stream"));
        });
        yield return ("M8.6-C2b1 real script pin denies writes and hash failure releases file", () => {
            string folder=Path.Combine(output,"workflow-pin-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            string file=Path.Combine(folder,"probe.ps1");File.WriteAllText(file,"# fixture",new UTF8Encoding(false));string hash=Hash(file);
            using(var pins=new WorkflowPhysicalPins()) { pins.File(file,hash,new FileInfo(file).Length); Reject(()=>{using var denied=new FileStream(file,FileMode.Open,FileAccess.Write,FileShare.None);}); }
            using(var writable=new FileStream(file,FileMode.Open,FileAccess.Write,FileShare.None))Check(writable.Length>0);
            using(var pins=new WorkflowPhysicalPins()){Reject(()=>pins.File(file,new string('B',64)));using var writable=new FileStream(file,FileMode.Open,FileAccess.Write,FileShare.None);}
            Check(Hash(file)==hash);
        });
    }
    internal static IEnumerable<(string,Action)> HostCases(string root, Guid captureId)
    {
        (WorkflowVerificationTask,WorkflowBuildEnvironment,string) Inputs(bool wrong)
        {
            string manifestPath=Path.Combine(root,"out","captured-build",captureId.ToString("N"),"capture.json");
            using var document=JsonDocument.Parse(File.ReadAllBytes(manifestPath));var manifest=document.RootElement;
            string runner=Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Windows),"System32","WindowsPowerShell","v1.0","powershell.exe");
            string tool=Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles),"dotnet","dotnet.exe");
            var binding=TaskFor().Binding with {CaptureId=captureId,CaptureManifestSha256=wrong?new string('B',64):Hash(manifestPath),
                SourceInputsSha256=manifest.GetProperty("sourceInputsSha256").GetString()!,CaptureRecipeSha256=manifest.GetProperty("recipeSha256").GetString()!,
                ReviewBundleSha256=manifest.GetProperty("reviewBundleSha256").GetString(),ToolSha256=Hash(tool)};
            var env=new WorkflowBuildEnvironment(root,tool,runner,Hash(runner),WorkflowCaptureBridge.RequiredHelpers().Select(p=>new WorkflowEvidenceFile(p,new FileInfo(Path.Combine(root,p)).Length,Hash(Path.Combine(root,p)))),TimeSpan.FromSeconds(90));
            return (TaskFor(binding),env,manifestPath);
        }
        void Wait(Func<bool> complete)
        {
            var time=Stopwatch.StartNew();while(!complete()){if(time.Elapsed>TimeSpan.FromSeconds(30))throw new TimeoutException("workflow_fixture_deadline_owner_retained");Thread.Sleep(10);}
        }
        yield return ("M8.6-C2b1 actual preparer authenticated lease survives worker normal exit",()=>{
            var (task,env,manifest)=Inputs(false);var bridge=new WorkflowCaptureBridge(task,env);retained.Add(bridge);bridge.BeginPreparation();
            Wait(()=>bridge.Snapshot().Ready||bridge.Snapshot().FailureType.Length>0);Check(bridge.Snapshot().Ready);
            Check(WorkflowCaptureBridge.ValidateFacts(bridge.ReadPreparedFacts(),task,env)>=2);
            Reject(()=>{using var denied=new FileStream(manifest,FileMode.Open,FileAccess.Write,FileShare.None);});
            bridge.RequestClose();Wait(()=>bridge.Snapshot().RootExited&&bridge.Snapshot().ActiveProcesses==0);
            Check(bridge.Snapshot().RetainedCaptureHandles>0); // parent duplicates still hold original file locks
            Reject(()=>{using var denied=new FileStream(manifest,FileMode.Open,FileAccess.Write,FileShare.None);});
            Wait(bridge.TryClose);Check(bridge.Snapshot().ExitCode==0);retained.Remove(bridge);
            using var writable=new FileStream(manifest,FileMode.Open,FileAccess.Write,FileShare.None); // never modify captured bytes
        });
        yield return ("M8.6-C2b1 actual preparer wrong manifest never becomes a ready lease",()=>{
            var (task,env,_)=Inputs(true);var bridge=new WorkflowCaptureBridge(task,env);retained.Add(bridge);bridge.BeginPreparation();
            Wait(()=>bridge.Snapshot().FailureType.Length>0);Check(!bridge.Snapshot().Ready&&bridge.Snapshot().RetainedCaptureHandles==0);
            Reject(()=>bridge.ReadPreparedFacts());Wait(bridge.TryClose);retained.Remove(bridge);
        });
    }
}
