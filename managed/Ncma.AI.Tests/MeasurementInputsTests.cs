using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ncma.Application;
using Ncma.Editor.Core;
using Ncma.Editor.Services;

internal static class MeasurementInputsTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Measurement input assertion"); }
    private static void Reject(Action action) {
        try { action(); } catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException or EditRejectedException or KeyNotFoundException) { return; }
        throw new Exception("Measurement inputs must reject");
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    internal sealed class Fixture
    {
        internal readonly string Root, Product, Workload, Capture;
        internal string ProductManifest => Path.Combine(Product, DeploymentManifest.FileName);
        internal string WorkloadManifest => Path.Combine(Workload, MeasurementInputLease.WorkloadManifestName);
        internal readonly Guid RunId = Guid.NewGuid();
        internal Fixture()
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows physical input fixture required, not skipped");
            Root=Path.Combine(Path.GetTempPath(),"ncma-measurement-inputs-"+Guid.NewGuid().ToString("N"));
            Product=Path.Combine(Root,"product");Workload=Path.Combine(Root,"workload");Capture=Path.Combine(Root,"capture.json");
            Directory.CreateDirectory(Product);Directory.CreateDirectory(Workload);
            File.WriteAllText(Capture,JsonSerializer.Serialize(new{schemaVersion=1,kind="ncma.captured-build.v1",sourceInputsSha256=new string('A',64),files=Array.Empty<object>()}));
            string[] names=["NcmaEngine.exe","NcmaEngine.dll","NcmaEngine.deps.json","NcmaEngine.runtimeconfig.json"];
            foreach(string name in names) File.WriteAllText(Path.Combine(Product,name),"synthetic bytes only, NEVER EXECUTED: "+name);
            File.WriteAllText(ProductManifest,JsonSerializer.Serialize(new{schemaVersion=1,product="fixture-editor",configuration="Release",rid="win-x64",tfm="net8.0",publishMode="framework-dependent",
                sourceInputsSha256=new string('A',64),captureManifestSha256=Hash(Capture),aiEnabled=true,extensionsEnabled=false,
                files=names.Select(name=>new{path=name,size=new FileInfo(Path.Combine(Product,name)).Length,sha256=Hash(Path.Combine(Product,name))}).ToArray()}));
            File.WriteAllText(Path.Combine(Workload,"route.bin"),"synthetic route descriptor, not representative game content");
            File.WriteAllText(WorkloadManifest,JsonSerializer.Serialize(new{schemaVersion=1,kind="ncma.measurement-workload.v1",scenario="controlled-fixture",
                files=new[]{new{path="route.bin",size=new FileInfo(Path.Combine(Workload,"route.bin")).Length,sha256=Hash(Path.Combine(Workload,"route.bin"))}}}));
        }
        internal ProcessMeasurementIdentity Identity => new(RunId,new('A',64),Hash(Capture),Hash(ProductManifest),Hash(WorkloadManifest),MeasurementConfiguration.Release,MeasurementVariant.Ai,MeasurementHost.Editor);
        internal MeasurementInputsPlan Plan => new(Identity,3000,1000,3,120);
        internal MeasurementInputLease Lease(MeasurementInputsPlan? plan=null) => new(plan??Plan,Capture,Product,Workload);
        internal void Change(string path,Action<JsonObject> edit) { var json=JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();edit(json);File.WriteAllText(path,json.ToJsonString()); }
        // Intentionally no cleanup: complete positive/negative fixture evidence remains.
    }
    internal static IEnumerable<(string,Action)> Cases()
    {
        yield return ("M8.7-E2E plan budgets do not authorize launch or silently truncate coverage",()=>{
            var id=new ProcessMeasurementIdentity(Guid.NewGuid(),new('A',64),new('B',64),new('C',64),new('D',64),MeasurementConfiguration.Release,MeasurementVariant.Ai,MeasurementHost.Editor);
            foreach(var bad in new[]{id with{RunId=Guid.Empty},id with{Host=MeasurementHost.Player},id with{Variant=(MeasurementVariant)99},id with{SourceSha256="secret"}}) Reject(()=>new MeasurementInputsPlan(bad,3000,1000,3,120));
            Reject(()=>new MeasurementInputsPlan(id,3000,1000,2,120));Reject(()=>new MeasurementInputsPlan(id,3600001,1000,8192,120));
            Reject(()=>new MeasurementInputsPlan(id,1000,0,3,120));Reject(()=>new MeasurementInputsPlan(id,1000,1000,1,0));Reject(()=>new MeasurementInputsPlan(id,1000,1000,1,1000001));
            Check(new MeasurementInputsPlan(id,3000,1000,3,120).Fingerprint!=new MeasurementInputsPlan(id,3000,1000,3,121).Fingerprint);
        });
        yield return ("M8.7-E2E declared bytes held readonly original package check and repeated same-handle validation",()=>{
            var f=new Fixture();using var lease=f.Lease();Check(lease.State==MeasurementInputsState.Created);
            var first=lease.Prepare();Check(first.ProductFiles==4&&first.WorkloadFiles==1&&first.CheckSequence==1&&first.Identity==f.Identity);
            for(int i=0;i<3;++i) Check(lease.RequireCurrent().CheckSequence==(ulong)(i+2));
            Reject(()=>{using var writer=new FileStream(Path.Combine(f.Product,"NcmaEngine.exe"),FileMode.Open,FileAccess.Write,FileShare.ReadWrite);});
            Reject(()=>{using var writer=new FileStream(f.Capture,FileMode.Open,FileAccess.Write,FileShare.ReadWrite);});
            Check(!first.SourceContentsVerified&&!first.ExecutedProductVerified&&!first.WorkloadApproved&&!first.LaunchAuthorized&&!first.PerformanceAccepted);
        });
        yield return ("M8.7-E2E payload drift fails closed and partial pins released only on explicit close",()=>{
            var f=new Fixture();File.AppendAllText(Path.Combine(f.Workload,"route.bin"),"changed");using var lease=f.Lease();
            Reject(()=>lease.Prepare());Check(lease.State==MeasurementInputsState.Faulted);Reject(()=>lease.Prepare());Reject(()=>lease.RequireCurrent());
            Reject(()=>{using var writer=new FileStream(f.Capture,FileMode.Open,FileAccess.Write,FileShare.ReadWrite);});
            lease.Dispose();Check(lease.State==MeasurementInputsState.Closed);
            using var access=new FileStream(f.Capture,FileMode.Open,FileAccess.Write,FileShare.ReadWrite); // Open only; no evidence overwritten.
        });
        yield return ("M8.7-E2E exact tree rejects initial extras and later additions without automatic retry",()=>{
            var initial=new Fixture();File.WriteAllText(Path.Combine(initial.Product,"extra.txt"),"unexpected");using(var lease=initial.Lease()) Reject(()=>lease.Prepare());
            var later=new Fixture();using(var lease=later.Lease()) {lease.Prepare();File.WriteAllText(Path.Combine(later.Workload,"extra.txt"),"late child");Reject(()=>lease.RequireCurrent());Check(lease.State==MeasurementInputsState.Faulted);Reject(()=>lease.RequireCurrent());}
            var directory=new Fixture();Directory.CreateDirectory(Path.Combine(directory.Workload,"empty"));using(var lease=directory.Lease()) Reject(()=>lease.Prepare());
            Check(File.Exists(Path.Combine(initial.Product,"extra.txt"))&&File.Exists(Path.Combine(later.Workload,"extra.txt"))); // Never delete failed evidence.
        });
        yield return ("M8.7-E2E manifest hashes source capture variant and original package rules stay mandatory",()=>{
            var f=new Fixture();var identity=f.Identity;using(var lease=f.Lease(new(identity with{ProductManifestSha256=new('0',64)},3000,1000,3,120))) Reject(()=>lease.Prepare());
            foreach(string field in new[]{"sourceInputsSha256","captureManifestSha256","configuration","aiEnabled","rid"}) {
                var bad=new Fixture();bad.Change(bad.ProductManifest,j=>j[field]=field=="aiEnabled"?JsonValue.Create(false):JsonValue.Create(field=="configuration"?"Debug":field=="rid"?"linux-x64":new string('F',64)));
                using var lease=bad.Lease();Reject(()=>lease.Prepare());
            }
            var capture=new Fixture();capture.Change(capture.Capture,j=>j["kind"]="other");capture.Change(capture.ProductManifest,j=>j["captureManifestSha256"]=Hash(capture.Capture));using(var lease=capture.Lease()) Reject(()=>lease.Prepare());
        });
        yield return ("M8.7-E2E duplicate path collision traversal unknown workload and malformed manifest rejected",()=>{
            foreach(string path in new[]{"../escape.bin","CON.bin","route.bin/child","route.bin"}) {
                var f=new Fixture();f.Change(f.WorkloadManifest,j=>j["files"]!.AsArray().Add(new JsonObject{["path"]=path,["size"]=1,["sha256"]=new string('A',64)}));
                using var lease=f.Lease();Reject(()=>lease.Prepare());
            }
            var extra=new Fixture();extra.Change(extra.WorkloadManifest,j=>j["execute"]="never");using(var lease=extra.Lease()) Reject(()=>lease.Prepare());
            var duplicate=new Fixture();string text=File.ReadAllText(duplicate.WorkloadManifest);File.WriteAllText(duplicate.WorkloadManifest,text.Replace("\"schemaVersion\":1","\"schemaVersion\":1,\"schemaVersion\":1"));using(var lease=duplicate.Lease()) Reject(()=>lease.Prepare());
            var malformed=new Fixture();File.WriteAllText(malformed.WorkloadManifest,"{SECRET_BODY");using(var lease=malformed.Lease()) {try{lease.Prepare();throw new Exception("Expected invalid JSON");}catch(IOException error){Check(!error.Message.Contains("SECRET"));}}
        });
        yield return ("M8.7-E2E file count per-file and total-byte budgets fail before payload access",()=>{
            foreach(bool product in new[]{false,true}) {
                var f=new Fixture();f.Change(product?f.ProductManifest:f.WorkloadManifest,j=>{
                    var files=j["files"]!.AsArray();int limit=product?MeasurementInputLease.MaximumProductFiles:MeasurementInputLease.MaximumWorkloadFiles;
                    while(files.Count<=limit) files.Add(new JsonObject{["path"]="unused-"+files.Count+".bin",["size"]=1,["sha256"]=new string('A',64)});
                });using var lease=f.Lease();Reject(()=>lease.Prepare());
            }
            var large=new Fixture();large.Change(large.WorkloadManifest,j=>j["files"]![0]!["size"]=MeasurementInputLease.MaximumFileBytes+1);using(var lease=large.Lease()) Reject(()=>lease.Prepare());
            var total=new Fixture();total.Change(total.WorkloadManifest,j=>{var files=j["files"]!.AsArray();for(int i=0;i<9;++i) files.Add(new JsonObject{["path"]="large-"+i+".bin",["size"]=MeasurementInputLease.MaximumFileBytes,["sha256"]=new string('A',64)});});using(var lease=total.Lease()) Reject(()=>lease.Prepare());
        });
        yield return ("M8.7-E2E owner single preparation close and absent directories do not fallback",()=>{
            var f=new Fixture();using var lease=f.Lease();Task.Run(()=>Reject(()=>lease.Prepare())).GetAwaiter().GetResult();Check(lease.State==MeasurementInputsState.Created);
            lease.Prepare();Reject(()=>lease.Prepare());Task.Run(()=>Reject(()=>lease.Dispose())).GetAwaiter().GetResult();Check(lease.State==MeasurementInputsState.Ready);
            lease.Dispose();lease.Dispose();Reject(()=>lease.RequireCurrent());
            string absent=Path.Combine(f.Root,"missing");using var missing=new MeasurementInputLease(f.Plan,f.Capture,f.Product,absent);Reject(()=>missing.Prepare());Check(!Directory.Exists(absent));
            Reject(()=>new MeasurementInputLease(f.Plan,f.Capture,f.Product,f.Product));Reject(()=>new MeasurementInputLease(f.Plan,f.Capture,f.Product,Path.Combine(f.Product,"nested")));
            Reject(()=>new MeasurementInputLease(f.Plan,"relative.json",f.Product,f.Workload));
        });
        yield return ("M8.7-E2E idle empty workload is explicit and not workload or runtime acceptance",()=>{
            var f=new Fixture();f.Change(f.WorkloadManifest,j=>{j["scenario"]="editor-idle";j["files"]=new JsonArray();});
            // Keep the original populated directory as evidence. Use a distinct declared empty root.
            string empty=Path.Combine(f.Root,"idle");Directory.CreateDirectory(empty);File.Copy(f.WorkloadManifest,Path.Combine(empty,MeasurementInputLease.WorkloadManifestName));
            using var lease=new MeasurementInputLease(f.Plan,f.Capture,f.Product,empty);var receipt=lease.Prepare();
            Check(receipt.WorkloadFiles==0&&receipt.Scenario==MeasurementScenario.EditorIdle&&!receipt.WorkloadApproved&&!receipt.ExecutedProductVerified);
        });
        yield return ("M8.7-E2E hardlinked declared payload rejected without deleting either alias",()=>{
            var f=new Fixture();string original=Path.Combine(f.Workload,"route.bin"),alias=Path.Combine(f.Root,"route-alias.bin");
            if(!CreateHardLinkW(alias,original,0)) throw new IOException("Hardlink fixture unavailable, not skipped");
            using var lease=f.Lease();Reject(()=>lease.Prepare());Check(File.ReadAllBytes(original).SequenceEqual(File.ReadAllBytes(alias)));
        });
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)] private static extern bool CreateHardLinkW(string link,string existing,nint security);
}
