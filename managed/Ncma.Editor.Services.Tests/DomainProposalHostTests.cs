using System.Text.Json;
using Ncma.Editor.Core;
using Ncma.Editor.Services;
using Ncma.Runtime;

// Registered for whole-stage validation only. No test/IPC/Play execution during compilation.
internal static class DomainProposalHostTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Proposal host assertion."); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or EditRejectedException or IOException) { return; }
        throw new Exception("Expected host proposal rejection.");
    }
    private static byte[] Payload(Guid id) => JsonSerializer.SerializeToUtf8Bytes(new {
        version = 1, capability = "ncma.scene.transaction", input = new {
            operations = new[] { new { op = "rename", objectId = id, name = "Draft only" } }
        }
    });
    private static (EditorDomainProposals Service, Guid Id) Configure(EditorSessionOwner owner)
    {
        Guid id = owner.Document.CreateObject("Original"); owner.Edit!.Resynchronize();
        owner.ConfigureDomainProposals(Guid.NewGuid()); return (owner.DomainProposals!, id);
    }
    private static DomainProposalReview Review(EditorDomainProposals service, Guid id) =>
        service.Review(Guid.NewGuid(), service.CaptureSource(), Payload(id));
    private readonly record struct CallbackValue(int Value) : IComponent;

    internal static IEnumerable<(string, Action)> Cases(string output, string repository)
    {
        string sample = Path.Combine(repository, "out/managed/Ncma.Gameplay.Sample.dll");
        yield return ("M8.3-A host project identity is explicit and disposed references cannot review", () => {
            using var owner = new EditorSessionOwner("Projectless");
            Check(owner.DomainProposals is null);
            Reject(() => owner.ConfigureDomainProposals(Guid.Empty));
            var (service, id) = Configure(owner); var review = Review(service, id);
            byte[] before = owner.Document.CaptureBytes(); var history = owner.Edit!.State;
            Check(review.Source.ProjectId != Guid.Empty && review.Source.HostSessionId != Guid.Empty &&
                review.Source.EditSessionId == owner.Edit.SessionId && review.Source.DocumentSessionId == owner.Document.SessionId &&
                review.Source.EndpointId == Guid.Empty && review.Source.AudienceRevision == 0);
            Reject(() => owner.ConfigureDomainProposals(review.Source.ProjectId));
            Reject(() => owner.ConfigureAssets(output, Guid.NewGuid(), 1));
            Reject(() => owner.PrepareRenderAssets(output, Guid.NewGuid()));
            Check(before.SequenceEqual(owner.Document.CaptureBytes()) && owner.Edit.State == history && service.IsCurrent(review));
            Task.Run(() => Reject(() => service.CaptureSource())).GetAwaiter().GetResult();
            owner.Dispose(); owner.Dispose(); Reject(() => service.CaptureSource()); Reject(() => service.IsCurrent(review));
        });
        yield return ("M8.3-A real Play Stop invalidates retained drafts without touching authoring", () => {
            using var owner = new EditorSessionOwner("Play source"); owner.LoadGameplay(sample);
            var (service, id) = Configure(owner); var review = Review(service, id);
            byte[] before = owner.Document.CaptureBytes(); int history = owner.Edit!.State.UndoCount;
            var play = owner.StartPlay(); Check(ReferenceEquals(play, owner.Play));
            Reject(() => service.CaptureSource()); owner.StopPlay();
            Check(!service.IsCurrent(review) && service.CaptureSource().ReviewEpoch > review.Source.ReviewEpoch);
            Check(before.SequenceEqual(owner.Document.CaptureBytes()) && owner.Edit.State.UndoCount == history);
            Check(service.IsCurrent(Review(service, id)));
        });
        yield return ("M8.3-A failed Play composition rejects reentry and never revives pre-start reviews", () => {
            EditorDomainProposals? service = null; bool blocked = false;
            using var owner = new EditorSessionOwner("Failed start", composePlay: _ => {
                Reject(() => service!.CaptureSource()); blocked = true; throw new InvalidOperationException("Injected composition failure.");
            });
            owner.LoadGameplay(sample); var configured = Configure(owner); service = configured.Service;
            var review = Review(service, configured.Id); byte[] before = owner.Document.CaptureBytes();
            Reject(() => owner.StartPlay());
            Check(blocked && owner.Play is null && !service.IsCurrent(review) && before.SequenceEqual(owner.Document.CaptureBytes()));
            Check(service.IsCurrent(Review(service, configured.Id)));
        });
        yield return ("M8.3-A failed load, catalog refresh and document replacement invalidate old sources", () => {
            using var owner = new EditorSessionOwner("Source changes"); var (service, id) = Configure(owner);
            var review = Review(service, id); owner.RefreshCatalog(); Check(!service.IsCurrent(review));
            string absent = Path.Combine(output, Guid.NewGuid().ToString("N") + ".dll"); Check(!File.Exists(absent));
            review = Review(service, id); Reject(() => owner.LoadGameplay(absent)); Check(!service.IsCurrent(review));
            review = Review(service, id); Reject(() => owner.ReloadGameplay(absent)); Check(!service.IsCurrent(review));
            review = Review(service, id); var workspace = new EditorWorkspace(owner); workspace.New(workspace.Stamp, true);
            Check(!service.IsCurrent(review) && service.CaptureSource().DocumentGeneration != review.Source.DocumentGeneration);
        });
        yield return ("M8.3-A original validator callbacks cannot reenter host lifecycle or proposal review", () => {
            EditorSessionOwner? owner = null; EditorDomainProposals? service = null; bool probe = false; int checkedCallbacks = 0;
            var registry = global::Ncma.Runtime.CompositionRegistry.Build();
            registry.Register<CallbackValue>("proposal.callback", 1,
                """{"type":"object","required":["value"],"properties":{"value":{"type":"integer"}}}""", v => {
                    if (probe) {
                        Reject(() => owner!.StopPlay()); Reject(() => owner!.Dispose());
                        Reject(() => owner!.ConfigureEndpoint(false, output)); Reject(() => service!.CaptureSource());
                        checkedCallbacks++;
                    }
                    return v;
                });
            using (owner = new EditorSessionOwner("Reentry", components: registry)) {
                var setup = Configure(owner); service = setup.Service; owner.Document.SetComponent(setup.Id, new CallbackValue(1)); owner.Edit!.Resynchronize();
                byte[] before = owner.Document.CaptureBytes(); var history = owner.Edit.State;
                var source = service.CaptureSource(); probe = true;
                var review = Review(service, setup.Id); probe = false;
                Check(checkedCallbacks > 0 && source == review.Source && service.IsCurrent(review));
                Check(before.SequenceEqual(owner.Document.CaptureBytes()) && owner.Edit.State == history && owner.Play is null);
            }
        });
        yield return ("M8.3-A real paired endpoint epoch and replacement invalidate without granting tools", () => {
            using var fixture = new GraphInspectionTests.Fixture(output, repository);
            var owner = fixture.Owner; var (service, id) = Configure(owner); var endpoint = owner.Endpoint!;
            int capabilities = owner.Edit!.Describe().Count(); byte[] before = owner.Document.CaptureBytes();
            var review = Review(service, id); Guid connection = endpoint.View.Connections.Single(c => c.Paired).ConnectionId;
            Check(review.Source.EndpointId == endpoint.View.InstanceId && review.Source.AudienceRevision == endpoint.AudienceRevision);
            endpoint.Revoke(connection); endpoint.Pair(connection, true); // No intermediate service read.
            Check(!service.IsCurrent(review)); review = Review(service, id);
            owner.ConfigureEndpoint(false, fixture.Root); Check(!service.IsCurrent(review));
            Check(service.CaptureSource().EndpointId == Guid.Empty && before.SequenceEqual(owner.Document.CaptureBytes()) &&
                owner.Edit.Describe().Count() == capabilities && owner.Edit.State.UndoCount == 0);
        });
    }
}
