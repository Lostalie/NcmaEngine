using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ncma.Editor.Services;
#if NCMA_EXTENSIONS
using Ncma.Extensions;
using Ncma.Extensions.Hosting;
#endif

internal static partial class WorkflowTests
{
#if NCMA_EXTENSIONS
    // Retain failed owners, including locks and clients. No finally Dispose, kill,
    // directory deletion or success-by-retry. Local checkpoint gives exact state.
    private sealed class SdkChainState(string output)
    {
        internal readonly WorkflowPhysicalPins Pins = new();
        internal WorkflowManagedRuntime? Mcp;
        internal GraphInspectionTests.Fixture? Fixture;
        internal readonly List<object> Steps = [];
        internal void Save(string name, object value) {
            using var file = new FileStream(Path.Combine(output,name),FileMode.CreateNew,FileAccess.Write,FileShare.Read);
            JsonSerializer.Serialize(file,value,new JsonSerializerOptions { WriteIndented=true }); file.Flush(true);
        }
    }
    private static readonly List<SdkChainState> sdkRetained = [];

    // Test-only join over original immutable objects. This grants no approval,
    // creates no product ledger and never turns a source export into acceptance.
    private static bool SdkAuditMatches(WorkflowAuditEntry entry, WorkflowVerificationTask task, WorkflowVerificationReceipt receipt)
    {
        var b = task.Binding; var link = entry.Verification;
        return link is not null && entry.Kind == WorkflowAuditKind.Verification &&
            entry.SchemaVersion == (b.Assets is null ? 2 : 3) &&
            entry.WorkflowId == b.WorkflowId && entry.PlanHash == b.PlanHash &&
            entry.ExpectedSource == b.Source && entry.ObservedSource == b.Source &&
            entry.ExpectedAssets == b.Assets && entry.ObservedAssets == b.Assets &&
            link.JobId == b.JobId && link.CaptureId == b.CaptureId &&
            link.Recipe == b.RecipeId && link.Configuration == b.Configuration && link.Variant == b.Variant &&
            link.TaskFingerprint == task.Fingerprint && link.TaskFingerprint == receipt.TaskFingerprint &&
            link.ReceiptFingerprint == receipt.Fingerprint && link.Conclusion == receipt.Conclusion &&
            link.Complete == receipt.EvidenceComplete && link.InputsObserved == receipt.InputsObserved;
    }
#endif
    internal static IEnumerable<(string, Action)> SdkChainCases(string workspace, string output, string inputFile)
    {
        yield return ("M8.6-D real reviewed source offline SDK build native and original host chain", () => RunSdkChain(workspace,output,inputFile));
    }
    private static void RunSdkChain(string workspace, string output, string inputFile)
    {
#if !NCMA_EXTENSIONS
        throw new InvalidOperationException("sdk_chain_requires_explicit_extensions_harness_no_skip");
#else
        var state = new SdkChainState(output); sdkRetained.Add(state);
        string[] stages = ["prerequisites","original-scene-workflow","reviewed-source-capture","offline-restore","managed-build",
            "compiled-reviewed-marker","contract-tests","native-preparation","original-host-tests","audit-and-unchanged-scene","explicit-reverse-close"];
        int completed=0; string failure="";
        void Stage(string name,Action run) {
            Check(stages[completed]==name); Console.WriteLine("BEGIN SDK chain / "+name);
            var watch=Stopwatch.StartNew(); run();
            state.Steps.Add(new { name, status="passed", seconds=watch.Elapsed.TotalSeconds }); completed++;
            state.Save($"step-{completed:00}.json",state.Steps); Console.WriteLine("PASS SDK chain / "+name);
        }
        void Wait(Func<bool> condition,int seconds) {
            var watch=Stopwatch.StartNew(); while (!condition()) {
                if(watch.Elapsed.TotalSeconds>seconds) throw new TimeoutException("sdk_chain_owner_retained"); Thread.Sleep(10);
            }
        }
        WorkflowSdkChainInputs inputs=null!; JsonElement baseline=default; byte[] before=[]; byte[] after=[];
        Guid workflow=default, captureOwner=default, captureId=default, restore=default, build=default, contracts=default, native=default, hosts=default;
        GraphInspectionTests.Fixture f=null!; EditorWorkflowJobs jobs=null!; string marker=Guid.NewGuid().ToString("N");
        const string relative="managed/Ncma.Extensions.Hosting/ExtensionSampleTag.cs";
        const string markerKey="Ncma.M86.ReviewedSource";
        WorkflowSourceReview reviewed=null!; byte[] scene=[]; string editState=""; string planHash=""; string createdManifest="", createdSource="", createdRecipe="";
        WorkflowVerificationReceipt compiled=null!; var reviews=new Dictionary<Guid,WorkflowLocalJobReview>();
        var receipts=new Dictionary<Guid,WorkflowVerificationReceipt>();
        string JobRoot(Guid id,bool offline=false) => Path.Combine(workspace,"out","verification","m8-6",offline?"restores":"jobs",id.ToString("N"));
        WorkflowLocalJobView View(Guid id) => jobs.Capture().Single(j=>j.JobId==id);
        void Run(Guid id,bool offline=false) {
            Wait(()=> { jobs.Pump(); var v=View(id); if(v.Diagnostic.Length!=0) throw new IOException("sdk_chain_preparation_"+v.Diagnostic); return v.Prepared; },45);
            string path=JobRoot(id,offline); Check(!Directory.Exists(path),"preapproval output exists");
            var review=jobs.Review(id); reviews.Add(id,review); state.Save("review-"+id.ToString("N")+".json",JsonDocument.Parse(review.Display).RootElement);
            var binding=review.Original.Task.Binding;
            Check(binding.WorkflowId==workflow && binding.CaptureId==captureId && binding.PlanHash==planHash && binding.Source==reviewed.Source &&
                binding.ReviewBundleSha256==reviewed.BundleSha256 && binding.CaptureManifestSha256==createdManifest && binding.SourceInputsSha256==createdSource &&
                binding.Configuration==inputs.Configuration && binding.Variant=="extensions");
            Check(binding.JobId==id && binding.WorkspaceSession==reviewed.Source.SessionId &&
                binding.Assets==reviewed.Assets && binding.CaptureRecipeSha256==createdRecipe,
                "review must bind exact job, session, assets and captured recipe");
            Check(!jobs.StartApproved() && !View(id).Started && !Directory.Exists(path));
            jobs.Approve(review,review.Fingerprint,true); Check(!View(id).Started && !Directory.Exists(path),"approval is not start");
            Check(jobs.StartApproved());
            Wait(()=> { jobs.Pump(); return View(id).Drained; },615);
            var done=View(id); state.Save("job-"+id.ToString("N")+".json",done);
            Check(done.Started && done.Outcome=="ProcessExited" && done.Diagnostic.Length==0, "actual SDK job failed: "+done.Outcome);
        }
        WorkflowVerificationReceipt Receipt(Guid id,WorkflowVerificationConclusion conclusion) {
            var receipt=jobs.CaptureEvidence(id); state.Save("receipt-"+id.ToString("N")+".json",new { receipt,
                inputs=receipt.CopyObservedInputs(),outputs=receipt.CopyOutputs(),logs=receipt.CopyLogs(),cases=receipt.CopyCases() });
            Check(receipt.Conclusion==conclusion && receipt.EvidenceComplete && receipt.InputsObserved && receipt.InputFilesMatch && receipt.ExitCode==0 && !receipt.InstallationAuthorized);
            receipt.RequireTask(reviews[id].Original.Task);
            Check(receipt.ProcessOutcome==WorkflowProcessOutcome.Exited && receipt.DiagnosticCode.Length==0 &&
                receipt.CopyObservedInputs().SequenceEqual(reviews[id].Original.Task.CopyInputs()), "receipt input set differs from approved task");
            Check(ReferenceEquals(receipt,jobs.CaptureEvidence(id)),"evidence must not rerun");
            if(conclusion==WorkflowVerificationConclusion.TestsPassed) Check(receipt.CopyCases().Select(c=>c.Id).SequenceEqual(reviews[id].Original.Task.CopyTestInventory()) && receipt.CopyCases().All(c=>c.Outcome==WorkflowCaseOutcome.Passed));
            receipts.Add(id,receipt);
            return receipt;
        }
        try {
            Stage("prerequisites",()=> {
                var observed=state.Pins.Observe(inputFile,16384);
                inputs=WorkflowSdkChainInputs.Parse(state.Pins.ReadFile(inputFile,observed.Sha256,observed.Bytes,16384),workspace);
                state.Save("inputs.json",new { observed.Sha256, declaration=inputs, trustedStageEDeclaration=true, isBuildReceipt=false });
                state.Pins.File(typeof(WorkflowTests).Assembly.Location,inputs.HarnessSha256);
                _=state.Pins.Observe(inputs.DotnetExecutable,64*1024*1024);
                string manifest=Path.Combine(workspace,"out","captured-build",inputs.BaselineCaptureId.ToString("N"),"capture.json");
                var m=state.Pins.Observe(manifest,4194304); Check(m.Sha256==inputs.BaselineManifestSha256);
                using var doc=JsonDocument.Parse(state.Pins.ReadFile(manifest,m.Sha256,m.Bytes,4194304)); baseline=doc.RootElement.Clone();
                Check(baseline.GetProperty("sourceInputsSha256").GetString()==inputs.BaselineSourceSha256 && baseline.GetProperty("reviewBundleSha256").ValueKind==JsonValueKind.Null);
                state.Mcp=new(inputs.McpRoot,"Ncma.Editor.Mcp",true); state.Mcp.Prepare(); Check(state.Mcp.Fingerprint==inputs.McpFingerprint);
                f=new(output,workspace,mcpAssembly:Path.Combine(inputs.McpRoot,"Ncma.Editor.Mcp.dll"),dotnetExecutable:inputs.DotnetExecutable);
                state.Fixture=f; jobs=f.Owner.WorkflowJobs!;
            });
            Stage("original-scene-workflow",()=> {
                Registry(f); Guid obj=Guid.NewGuid(); workflow=Propose(f,[Step("ncma.scene.transaction",new { operations=new[] { new { op="create",objectId=obj,name="SDK reviewed scene" } } }),Step("ncma.scene.validate",new { },"valid")]);
                Approve(f,workflow); var ticket=Next(f,workflow); Check(Execute(f,ticket).GetProperty("isError").GetBoolean());
                ApproveTool(f); Data(Execute(f,ticket)); Data(Execute(f,Next(f,workflow)));
                var plan=f.Owner.Workflows!.Capture().Single(); Check(plan.Status=="completed" && plan.Receipts.Length==2 && plan.Receipts.All(r=>r.AssertionPassed));
                Check(f.Owner.Document.ObjectCount==1 && f.Owner.Edit!.State.UndoCount==1); planHash=plan.Hash;
                scene=f.Owner.Document.CaptureBytes(); editState=JsonSerializer.Serialize(f.Owner.Edit!.State);
            });
            Stage("reviewed-source-capture",()=> {
                string original=Path.Combine(workspace,relative.Replace('/',Path.DirectorySeparatorChar)); before=File.ReadAllBytes(original);
                var row=baseline.GetProperty("files").EnumerateArray().Single(x=>x.GetProperty("path").GetString()==relative);
                Check(Convert.ToHexString(SHA256.HashData(before))==row.GetProperty("sha256").GetString());
                string text=new UTF8Encoding(false,true).GetString(before); const string anchor="using Ncma.Runtime;";
                Check(text.Split(anchor,StringSplitOptions.None).Length==2);
                after=Encoding.UTF8.GetBytes(text.Replace(anchor,anchor+"\n[assembly: System.Reflection.AssemblyMetadata(\""+markerKey+"\", \""+marker+"\")]",StringComparison.Ordinal));
                string candidate=Path.Combine(output,"candidate"); string target=Path.Combine(candidate,relative.Replace('/',Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using(var file=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None)) file.Write(after);
                var source=ExtensionSourceCapture.Capture(new ExtensionDescriptor("ncma.editor.summary",new(1,1,0),new(1,0,0)),workspace,candidate,relative);
                source.RequireCurrent(); byte[] bundle=source.CopyBuildBundle(); string exported=Path.Combine(output,"review.nccapture");
                Check(source.ExportBuildBundle(exported)==Convert.ToHexString(SHA256.HashData(bundle)));
                reviewed=jobs.BindReviewedSource(workflow,workspace,bundle); captureOwner=jobs.BeginSourceCapture(reviewed,exported,inputs.GitExecutable);
                Wait(()=> { jobs.Pump();var c=jobs.SourceCaptures().Single();if(c.Failure.Length!=0)throw new IOException("sdk_chain_capture_"+c.Failure);return c.Ready; },125);
                var created=jobs.SourceCaptures().Single(); captureId=created.CaptureId; createdManifest=created.ManifestSha256; createdSource=created.SourceSha256; state.Save("created.json",created);
                string captured=Path.Combine(workspace,"out","captured-build",captureId.ToString("N")); byte[] manifest=File.ReadAllBytes(Path.Combine(captured,"capture.json"));
                Check(Convert.ToHexString(SHA256.HashData(manifest))==createdManifest);
                using var doc=JsonDocument.Parse(manifest); var actual=doc.RootElement.GetProperty("files").EnumerateArray().ToDictionary(x=>x.GetProperty("path").GetString()!,StringComparer.Ordinal);
                Check(doc.RootElement.GetProperty("reviewBundleSha256").GetString()==reviewed.BundleSha256 &&
                    doc.RootElement.GetProperty("sourceInputsSha256").GetString()==createdSource);
                createdRecipe=doc.RootElement.GetProperty("recipeSha256").GetString()!;
                Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(captured,"capture.props"))))==createdRecipe);
                var expected=baseline.GetProperty("files").EnumerateArray().ToArray(); Check(actual.Count==expected.Length);
                foreach(var item in expected) { string path=item.GetProperty("path").GetString()!; var value=actual[path];
                    Check(value.GetProperty("sha256").GetString()==(path==relative?Convert.ToHexString(SHA256.HashData(after)):item.GetProperty("sha256").GetString()));
                    Check(value.GetProperty("size").GetInt64()==(path==relative?after.Length:item.GetProperty("size").GetInt64())); }
                Check(File.ReadAllBytes(Path.Combine(captured,"source",relative.Replace('/',Path.DirectorySeparatorChar))).SequenceEqual(after));
            });
            Stage("offline-restore",()=> {
                restore=jobs.PrepareCreatedOffline(captureOwner,inputs.OfflineCache,inputs.Configuration,"extensions"); Run(restore,true);
                // Both public entry points must share one immutable, audited receipt.
                if(inputs.Configuration=="Debug") Check(jobs.SealRestore(restore).Length==64);
                var restored=Receipt(restore,WorkflowVerificationConclusion.RestorePrepared);
                Check(restored.WarningCount==0 && restored.ErrorCount==0 && ReferenceEquals(restored,jobs.Evidence(restore)));
                string fingerprint=jobs.SealRestore(restore); Check(fingerprint.Length==64 && jobs.SealRestore(restore)==fingerprint);
            });
            Stage("managed-build",()=> { build=jobs.PrepareBuild(restore); Run(build); compiled=Receipt(build,WorkflowVerificationConclusion.CompileSucceeded); Check(compiled.WarningCount==0 && compiled.ErrorCount==0); });
            Stage("compiled-reviewed-marker",()=> {
                string dll=Path.Combine(JobRoot(build),"artifacts","bin","Ncma.Editor.Services.Tests",inputs.Configuration.ToLowerInvariant(),"Ncma.Extensions.Hosting.dll");
                var evidence=compiled.CopyOutputs().Single(x=>x.Path=="Ncma.Extensions.Hosting.dll");
                using var file=File.OpenRead(dll); Check(file.Length==evidence.Bytes && Convert.ToHexString(SHA256.HashData(file))==evidence.Sha256); file.Position=0;
                using var pe=new PEReader(file); var reader=pe.GetMetadataReader(); int matches=0;
                foreach(var handle in reader.GetAssemblyDefinition().GetCustomAttributes()) { var a=reader.GetCustomAttribute(handle);
                    if(a.Constructor.Kind!=HandleKind.MemberReference)continue; var member=reader.GetMemberReference((MemberReferenceHandle)a.Constructor);
                    if(member.Parent.Kind!=HandleKind.TypeReference)continue; var type=reader.GetTypeReference((TypeReferenceHandle)member.Parent);
                    if(reader.GetString(type.Namespace)!="System.Reflection" || reader.GetString(type.Name)!="AssemblyMetadataAttribute")continue;
                    var blob=reader.GetBlobReader(a.Value); Check(blob.ReadUInt16()==1); if(blob.ReadSerializedString()==markerKey) { Check(blob.ReadSerializedString()==marker); matches++; } }
                Check(matches==1,"reviewed afterimage was not compiled"); state.Save("compiled-marker.json",new { markerKey,marker,evidence });
            });
            Stage("contract-tests",()=> { contracts=jobs.PrepareTests(build,"workflow-contracts-v1"); Run(contracts); Receipt(contracts,WorkflowVerificationConclusion.TestsPassed); });
            Stage("native-preparation",()=> { native=jobs.PrepareNative(build,inputs.VisualStudioRoot,inputs.ToolsetVersion,inputs.WindowsSdkRoot,inputs.WindowsSdkVersion); Run(native); Receipt(native,WorkflowVerificationConclusion.NativeInputsPrepared); });
            Stage("original-host-tests",()=> { hosts=jobs.PrepareHostTests(native); Run(hosts); Receipt(hosts,WorkflowVerificationConclusion.TestsPassed); });
            Stage("audit-and-unchanged-scene",()=> {
                var audit=f.Owner.Workflows!.CaptureAudit(workflow); CheckAudit(audit); var links=audit.Entries.Where(e=>e.Verification is not null).Select(e=>e.Verification!).ToArray();
                Check(links.Length==5 && links.Select(e=>e.JobId).ToHashSet().SetEquals(new[] {restore,build,contracts,native,hosts}));
                Check(links.All(e=>e.CaptureId==captureId && e.Complete && e.InputsObserved && e.Configuration==inputs.Configuration && e.Variant=="extensions"));
                Check(receipts.Count==5 && receipts.Keys.ToHashSet().SetEquals(links.Select(e=>e.JobId)));
                byte[] auditBefore=JsonSerializer.SerializeToUtf8Bytes(audit); var joined=new List<object>();
                foreach(Guid id in new[] {restore,build,contracts,native,hosts}) {
                    var task=reviews[id].Original.Task; var receipt=receipts[id];
                    var entry=audit.Entries.Single(e=>e.Verification?.JobId==id); var link=entry.Verification!;
                    Check(SdkAuditMatches(entry,task,receipt),"SDK audit/approval/receipt join mismatch");
                    Check(audit.Plans.Single(p=>p.Hash==task.Binding.PlanHash).Revision==entry.PlanRevision);
                    Check(ReferenceEquals(receipt,jobs.CaptureEvidence(id)),"audit viewing must reuse sealed evidence");
                    // Single-field negative controls keep this assertion from silently
                    // regressing to a job-set/count check. Never modify the real audit.
                    WorkflowVerificationAudit[] invalidLinks = [
                        link with { JobId=Guid.NewGuid() }, link with { CaptureId=Guid.NewGuid() },
                        link with { Recipe="foreign-recipe" }, link with { Configuration=inputs.Configuration=="Debug"?"Release":"Debug" },
                        link with { Variant="default" }, link with { TaskFingerprint=new string('0',64) },
                        link with { ReceiptFingerprint=new string('0',64) }, link with { Conclusion=WorkflowVerificationConclusion.Failed },
                        link with { Complete=false }, link with { InputsObserved=false }
                    ];
                    foreach(var invalid in invalidLinks) Check(!SdkAuditMatches(entry with { Verification=invalid },task,receipt),"mismatched audit link accepted");
                    WorkflowAuditEntry[] invalidEntries = [
                        entry with { Verification=null }, entry with { Kind=WorkflowAuditKind.Receipt },
                        entry with { SchemaVersion=0 }, entry with { WorkflowId=Guid.NewGuid() },
                        entry with { PlanHash=new string('0',64) }, entry with { ExpectedSource=default },
                        entry with { ObservedSource=default }
                    ];
                    foreach(var invalid in invalidEntries) Check(!SdkAuditMatches(invalid,task,receipt),"mismatched outer audit accepted");
                    Guid other=id==build?contracts:build;
                    Check(!SdkAuditMatches(entry,reviews[other].Original.Task,receipt) &&
                        !SdkAuditMatches(entry,task,receipts[other]),"foreign task or receipt accepted");
                    joined.Add(new { jobId=id, workflowId=workflow, planHash, reviewed.Source, reviewed.Assets,
                        reviewed.BundleSha256, reviewed.Fingerprint, captureId, createdManifest, createdSource, createdRecipe,
                        task.Binding.RecipeId, task.Binding.Configuration, task.Binding.Variant,
                        taskFingerprint=task.Fingerprint, receiptFingerprint=receipt.Fingerprint, receipt.Conclusion,
                        receipt.EvidenceComplete, receipt.InputsObserved, receipt.InputFilesMatch,
                        entry.Sequence, auditHash=entry.Hash, negativeControls=invalidLinks.Length+invalidEntries.Length+2 });
                }
                Check(auditBefore.SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(f.Owner.Workflows.CaptureAudit(workflow))),
                    "repeated sealed receipt reads changed original audit");
                state.Save("joint-receipts.json",new { kind="ncma.m87.extension-receipt-join.v1", entries=joined,
                    installAuthorized=false, stageAccepted=false, executionWindow="after-F-PS5.1" });
                Check(scene.SequenceEqual(f.Owner.Document.CaptureBytes()) && editState==JsonSerializer.Serialize(f.Owner.Edit!.State));
                Check(f.Owner.Workflows.Capture().Single().Status=="completed" && f.Owner.Play is null);
                Check(File.ReadAllBytes(Path.Combine(workspace,relative.Replace('/',Path.DirectorySeparatorChar))).SequenceEqual(before)); state.Mcp!.RequireCurrent(); state.Save("audit.json",audit);
            });
            Stage("explicit-reverse-close",()=> {
                foreach(Guid id in new[] {hosts,native,contracts,build,restore}) { Wait(()=>jobs.Close(id),35); Check(View(id).Phase=="Closed" && !View(id).Retained); }
                Wait(()=>jobs.CloseSourceCapture(captureOwner),35); Check(jobs.SourceCaptures().Single().Closed);
                state.Save("closed.json",new { jobs=jobs.Capture(),captures=jobs.SourceCaptures() });
                f.Dispose(); state.Fixture=null; state.Mcp!.Dispose(); state.Pins.Dispose();
            });
            sdkRetained.Remove(state);
        } catch(Exception error) when(error is not OutOfMemoryException) {
            failure=error.GetType().Name; state.Steps.Add(new { name=stages[completed],status="failed",exceptionType=failure });
            foreach(string name in stages.Skip(completed+1)) state.Steps.Add(new { name,status="not-run",reason="prior-dependent-stage-failed" }); throw;
        } finally {
            state.Save("result.json",new { kind="ncma.m86.sdk-chain.v1",scope="reviewed-extension-real-sdk-chain",passed=completed==stages.Length,
                failure,steps=state.Steps,ownersRetained=sdkRetained.Contains(state),stageAccepted=false,installationAuthorized=false });
            if(state.Fixture is not null) state.Save("retained.json",new { jobs=state.Fixture.Owner.WorkflowJobs!.Capture(),captures=state.Fixture.Owner.WorkflowJobs.SourceCaptures() });
        }
#endif
    }
}
