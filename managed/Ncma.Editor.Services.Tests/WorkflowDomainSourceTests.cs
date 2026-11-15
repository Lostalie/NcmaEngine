using System.Text;
using System.Text.Json;
using Ncma.Assets.Authoring;
using Ncma.Editor.Services;
using Ncma.Ui;

internal static partial class WorkflowTests
{
    private sealed class WorkflowUiFixture : IDisposable
    {
        internal readonly GraphInspectionTests.Fixture F;
        internal EditorUiWorkspace Ui = null!;
        internal const string Relative = "assets/workflow.ncmaui";
        internal string Path => System.IO.Path.Combine(F.Root,Relative.Replace('/',System.IO.Path.DirectorySeparatorChar));
        internal WorkflowUiFixture(string output,string repository)
        {
            F = new(output,repository,configureProject:(workspace,root)=> {
                Ui=new(workspace,root);
                File.WriteAllBytes(System.IO.Path.Combine(root,Relative.Replace('/',System.IO.Path.DirectorySeparatorChar)),UiCodec.Encode(UiDefinition.Create(Guid.NewGuid(),"Original UI")));
                workspace.Owner.ConfigureAssets(root,Guid.NewGuid(),1,uiScope:Ui.Scope);
            });
            F.View.AttachUi(Ui,null); Grant();
        }
        internal void Grant() { var r=Ui.ReviewFile(F.Workspace.Stamp,Relative,false);Ui.ConfirmFile(r.Review,r.Document,true); }
        internal Guid Prepare(string name) => F.Owner.Assets!.Ui.PrepareProposal(Relative,Ui.Capture()! with { Name=name },F.Owner.Assets.Clock.Revision);
        public void Dispose() { Ui.Dispose();F.Dispose(); }
    }
    internal static IEnumerable<(string,Action)> DomainSourceCases(string output,string repository)
    {
        // Every combination is an independent harness case, not a loop which
        // loses all later combinations after the first failure.
        foreach(string capability in new[] { "ncma.assets.validate","ncma.animgraph.validate","ncma.ui.document.commit" })
        foreach(bool approved in new[] { false,true }) {
            string domain=capability;bool issued=approved;
            yield return ($"M8.6-D {domain} {(issued?"issued ticket":"unapproved review")} rejects independent asset refresh",()=> {
                using var fixture=new WorkflowUiFixture(output,repository);var f=fixture.F;Registry(f);
                object input=domain switch {
                    "ncma.animgraph.validate"=>new { graphId=f.Graph },
                    "ncma.ui.document.commit"=>new { proposalId=Guid.NewGuid(),expectedAssetRevision=f.Owner.Assets!.Clock.Revision },
                    _=>new { } };
                Guid id=Propose(f,[Step(domain,input)]);var service=f.Owner.Workflows!;
                var review=service.CaptureReview(id);var binding=ContinuationBinding(f,id);var host=f.Owner.WorkflowJobs!;
                Check(review.Assets==binding.Assets && review.Assets?.ProjectId==f.Owner.Assets!.ProjectId);
                var handoff=host.BindReviewedSource(id,repository,HandoffBundle()); // source-binding contract only; no SDK execution
                Ncma.Editor.Core.CapabilityRequest? ticket=null;
                if(issued){Approve(f,id);ticket=Next(f,id);}
                var stamp=f.Workspace.Stamp;byte[] scene=f.Owner.Document.CaptureBytes(),ui=File.ReadAllBytes(fixture.Path);var history=f.Owner.Edit!.State;
                Check(f.Owner.Assets!.Refresh(1,force:true));Check(f.Workspace.Stamp==stamp,"independent asset clock must be the cause");
                Check(!host.CanPrepare(id) && !host.IsSourceCurrent(binding) && !host.IsSourceReviewCurrent(handoff));
                Check(service.Capture().Single().Status=="conflict");
                Reject(()=>service.Approve(review,review.Fingerprint,true));
                if(ticket is not null) {
                    var response=Execute(f,ticket);Check(response.GetProperty("isError").GetBoolean());
                    Check(response.GetProperty("structuredContent").GetProperty("code").GetString()=="workflow_conflict");
                }
                SourceReject(()=>host.PrepareOffline("invalid",Guid.NewGuid(),"invalid",id,"Debug","default"));
                Check(host.Capture().Length==0 && service.Capture().Single().Receipts.Length==0 && scene.SequenceEqual(f.Owner.Document.CaptureBytes()) && ui.SequenceEqual(File.ReadAllBytes(fixture.Path)) && history==f.Owner.Edit.State);
                var audit=service.CaptureAudit(id);CheckAudit(audit);var stopped=audit.Entries[^1];
                Check(stopped.Kind==WorkflowAuditKind.Stopped && stopped.ExpectedSource==stopped.ObservedSource && stopped.ExpectedAssets!=stopped.ObservedAssets);
                // A fresh plan can bind new facts, but cannot revive old source approval.
                Guid next=Propose(f,[Step(domain,input)]);Check(host.CanPrepare(next) && service.CaptureReview(next).Assets!=review.Assets && !host.IsSourceReviewCurrent(handoff));
            });
        }
        yield return ("M8.6-D pure scene plan ignores unrelated asset refresh and has no asset authority",()=> {
            using var fixture=new WorkflowUiFixture(output,repository);var f=fixture.F;Registry(f);
            Guid id=Propose(f,[Step("ncma.scene.validate",new { },"valid")]);Approve(f,id);var ticket=Next(f,id);
            Check(f.Owner.Assets!.Refresh(1,force:true));Check(f.Owner.Workflows!.Capture().Single().Status=="approved");
            var binding=ContinuationBinding(f,id);Check(binding.Assets is null && f.Owner.WorkflowJobs!.IsSourceCurrent(binding));Data(Execute(f,ticket));
            Check(f.Owner.Workflows.Capture().Single().Status=="completed");var audit=f.Owner.Workflows.CaptureAudit(id);CheckAudit(audit);
            Check(audit.Entries.All(e=>e.ExpectedAssets is null && e.ObservedAssets is null) && f.Owner.Edit!.State.UndoCount==0);
        });
        yield return ("M8.6-D real UI workflow needs independent permission and keeps one original Undo",()=> {
            using var fixture=new WorkflowUiFixture(output,repository);var f=fixture.F;Registry(f);
            byte[] before=File.ReadAllBytes(fixture.Path),scene=f.Owner.Document.CaptureBytes();ulong assets=f.Owner.Assets!.Clock.Revision;
            Guid proposal=fixture.Prepare("Workflow UI"),id=Propose(f,[Step(UiCommands.CapabilityName,new { proposalId=proposal,expectedAssetRevision=assets }),Step("ncma.scene.validate",new { },"valid")]);
            var source=ContinuationBinding(f,id);Approve(f,id);var ticket=Next(f,id);
            Check(Execute(f,ticket).GetProperty("isError").GetBoolean());Check(f.Owner.Edit!.State.UndoCount==0 && f.Owner.Workflows!.Capture().Single().Receipts.Length==0);
            ApproveTool(f);Data(Execute(f,ticket));Data(Execute(f,Next(f,id)));
            var completed=f.Owner.Workflows!.Capture().Single();Check(completed.Status=="completed" && completed.Receipts.Length==2 && completed.Receipts.All(r=>r.AssertionPassed));
            var host=f.Owner.WorkflowJobs ?? throw new Exception("Workflow job host missing");
            var bound=ContinuationBinding(f,id);Check(bound.Assets?.Revision==assets+1 && !host.IsSourceCurrent(source) && host.IsSourceCurrent(bound));
            Check(!host.IsSourceCurrent(bound with { Assets=null }));
            Check(!host.IsSourceCurrent(bound with { Assets=bound.Assets!.Value with { ProjectId=Guid.NewGuid() } }));
            Check(UiCodec.Decode(File.ReadAllBytes(fixture.Path)).Name=="Workflow UI" && scene.SequenceEqual(f.Owner.Document.CaptureBytes()) && f.Owner.Edit.State.UndoCount==1 && f.Owner.Play is null);
            var audit=f.Owner.Workflows.CaptureAudit(id);CheckAudit(audit);var receipt=audit.Entries.Single(e=>e.Receipt?.Capability==UiCommands.CapabilityName);
            Check(receipt.ExpectedAssets?.Revision==assets && receipt.ObservedAssets?.Revision==assets+1 && receipt.ExpectedSource.Revision+1==receipt.ObservedSource.Revision);
            f.Owner.Workflows.Cancel(id);Check(File.ReadAllBytes(fixture.Path).SequenceEqual(UiCodec.Encode(fixture.Ui.Capture()! with { Name="Workflow UI" })),"cancel must not Undo external file");
            f.Workspace.History(f.Workspace.Stamp,false);Check(before.SequenceEqual(File.ReadAllBytes(fixture.Path)) && scene.SequenceEqual(f.Owner.Document.CaptureBytes()));
            Check(!host.IsSourceCurrent(bound) && f.Owner.Workflows.Capture().Single().Status=="completed");
        });
        yield return ("M8.6-D UI file scope waits independently then cancellation retains bytes and history",()=> {
            using var fixture=new WorkflowUiFixture(output,repository);var f=fixture.F;Registry(f);
            Guid proposal=fixture.Prepare("Never committed"),id=Propose(f,[Step(UiCommands.CapabilityName,new { proposalId=proposal,expectedAssetRevision=f.Owner.Assets!.Clock.Revision })]);
            Approve(f,id);var ticket=Next(f,id);Check(Execute(f,ticket).GetProperty("isError").GetBoolean());ApproveTool(f);fixture.Ui.Revoke();
            byte[] before=File.ReadAllBytes(fixture.Path),scene=f.Owner.Document.CaptureBytes();var history=f.Owner.Edit!.State;
            Check(Execute(f,ticket).GetProperty("isError").GetBoolean());Check(f.Owner.Workflows!.Capture().Single().Status=="approved" && f.Owner.Workflows.Capture().Single().Receipts.Length==0);
            f.Owner.Workflows.Cancel(id);fixture.Grant();Check(Execute(f,ticket).GetProperty("isError").GetBoolean());
            Check(before.SequenceEqual(File.ReadAllBytes(fixture.Path)) && scene.SequenceEqual(f.Owner.Document.CaptureBytes()) && history==f.Owner.Edit.State && f.Owner.Workflows.Capture().Single().Status=="cancelled");
        });
        yield return ("M8.6-D completed domain keeps historical outcome but cannot adopt refreshed assets",()=> {
            using var fixture=new WorkflowUiFixture(output,repository);var f=fixture.F;Registry(f);
            Guid proposal=fixture.Prepare("Completed"),id=Propose(f,[Step(UiCommands.CapabilityName,new { proposalId=proposal,expectedAssetRevision=f.Owner.Assets!.Clock.Revision })]);
            Approve(f,id);var ticket=Next(f,id);Check(Execute(f,ticket).GetProperty("isError").GetBoolean());ApproveTool(f);Data(Execute(f,ticket));
            var binding=ContinuationBinding(f,id);var host=f.Owner.WorkflowJobs!;Check(host.IsSourceCurrent(binding));
            var stamp=f.Workspace.Stamp;var history=f.Owner.Edit!.State;byte[] bytes=File.ReadAllBytes(fixture.Path);string historical=JsonSerializer.Serialize(f.Owner.Workflows!.Capture());
            Check(f.Owner.Assets!.Refresh(1,force:true));Check(f.Workspace.Stamp==stamp && !host.IsSourceCurrent(binding) && !host.CanPrepare(id));
            // Exact failed/not-started evidence contract, deliberately synthetic;
            // the registered SDK chain is the separate execution acceptance.
            var task=new WorkflowVerificationTask(binding,[new("fixture.cs",1,new string('A',64))],[]);
            var evidence=new WorkflowVerificationReceipt(task,task.Fingerprint,WorkflowProcessOutcome.NotStarted,null,0,0,task.CopyInputs(),[],[],[]);
            f.Owner.Workflows.RecordVerification(task,evidence);var audit=f.Owner.Workflows.CaptureAudit(id);CheckAudit(audit);
            Check(audit.Entries[^1].ExpectedAssets==binding.Assets && audit.Entries[^1].ObservedAssets!=binding.Assets);
            Check(JsonSerializer.Serialize(f.Owner.Workflows.Capture())==historical && history==f.Owner.Edit.State && bytes.SequenceEqual(File.ReadAllBytes(fixture.Path)));
        });
        yield return ("M8.6-D UI later author bytes reject old proposal and repair requires new proposal and approvals",()=> {
            using var fixture=new WorkflowUiFixture(output,repository);var f=fixture.F;Registry(f);
            Guid proposal=fixture.Prepare("Old candidate"),id=Propose(f,[Step(UiCommands.CapabilityName,new { proposalId=proposal,expectedAssetRevision=f.Owner.Assets!.Clock.Revision })]);
            Approve(f,id);var ticket=Next(f,id);Check(Execute(f,ticket).GetProperty("isError").GetBoolean());ApproveTool(f);
            var old=ContinuationBinding(f,id);var stamp=f.Workspace.Stamp;ulong revision=f.Owner.Assets!.Clock.Revision;
            byte[] human=UiCodec.Encode(fixture.Ui.Capture()! with { Name="Later human author bytes" });File.WriteAllBytes(fixture.Path,human);
            Check(f.Workspace.Stamp==stamp && f.Owner.Assets.Clock.Revision==revision,"no watcher refresh may be assumed");
            var rejected=Execute(f,ticket);Check(rejected.GetProperty("isError").GetBoolean() && rejected.GetProperty("structuredContent").GetProperty("code").GetString()=="ui_document_conflict");
            Check(human.SequenceEqual(File.ReadAllBytes(fixture.Path)) && f.Owner.Edit!.State.UndoCount==0 && f.Owner.Workflows!.Capture().Single().Status=="failed");
            fixture.Grant();Guid replacement=fixture.Prepare("New separately reviewed UI");
            Data(f.Call(EditorWorkflows.Capability,new { op="repair",workflowId=id,input=new { proposalId=replacement,expectedAssetRevision=revision } },Guid.NewGuid()));
            var current=ContinuationBinding(f,id);Check(current.PlanHash!=old.PlanHash && !f.Owner.WorkflowJobs!.IsSourceCurrent(old));
            Check(Op(f,"next",id).GetProperty("isError").GetBoolean());Approve(f,id);var next=Next(f,id);
            Check(Execute(f,next).GetProperty("isError").GetBoolean());ApproveTool(f);Data(Execute(f,next));
            Check(UiCodec.Decode(File.ReadAllBytes(fixture.Path)).Name=="New separately reviewed UI" && f.Owner.Edit!.State.UndoCount==1);
            Check(Execute(f,ticket).GetProperty("isError").GetBoolean());
            f.Workspace.History(f.Workspace.Stamp,false);Check(human.SequenceEqual(File.ReadAllBytes(fixture.Path)),"Undo must preserve the later human baseline");
            var audit=f.Owner.Workflows!.CaptureAudit(id);CheckAudit(audit);Check(audit.Plans.Length==2 && audit.Entries.Count(e=>e.Kind==WorkflowAuditKind.Approved)==2);
        });
        yield return ("M8.6-D actual workflow presenter exposes domain source and rejects old review after refresh",()=> {
            using var fixture=new WorkflowUiFixture(output,repository);var f=fixture.F;Registry(f);
            Guid id=Propose(f,[Step("ncma.assets.validate",new { })]);Click(f,40,8);Click(f,24,12);Click(f,24,70);Click(f,61,1000);Click(f,61,3);
            var text=new StringBuilder();int pages=0;
            do { Check(++pages<128);text.Append(Encoding.UTF8.GetString(f.View.Text));if(f.View.Items.ToArray().Single(i=>i.WidgetHigh==61&&i.WidgetLow==6).Enabled==0)break;Click(f,61,6); } while(true);
            Check(text.ToString().Contains("assets",StringComparison.Ordinal) && text.ToString().Contains(f.Owner.Assets!.ProjectId.ToString("D"),StringComparison.Ordinal));
            Click(f,61,7,1);var old=f.Event(61,8);Check(f.Owner.Assets!.Refresh(1,force:true));f.View.Apply([old],[]);f.Build();
            Check(f.Owner.Workflows!.Capture().Single().Status=="conflict" && f.Owner.WorkflowJobs!.Capture().Length==0 && f.Owner.Edit!.State.UndoCount==0);
        });
    }
}
