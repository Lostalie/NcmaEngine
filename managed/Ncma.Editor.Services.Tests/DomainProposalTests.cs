using System.Text;
using System.Text.Json;
using Ncma.Editor.Core;
using Ncma.Editor.Services;

internal static class DomainProposalTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Domain proposal assertion failed."); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or EditRejectedException or JsonException) { return; }
        throw new Exception("Expected proposal rejection.");
    }
    private sealed class Clock : TimeProvider
    {
        internal long Now;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Now;
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly EditorSessionOwner Owner = new("Proposal preflight");
        internal readonly EditorDomainProposals Proposals;
        internal readonly Clock Time = new();
        internal readonly Guid Object;
        internal Fixture()
        {
            Object = Owner.Document.CreateObject("Original"); Owner.Edit!.Resynchronize();
            Proposals = new(Owner.Edit, Guid.NewGuid(), Guid.NewGuid(), Time);
        }
        internal byte[] Payload(Guid? id = null) => JsonSerializer.SerializeToUtf8Bytes(new {
            version = 1, capability = "ncma.scene.transaction", input = new {
                operations = new[] { new { op = "rename", objectId = id ?? Object, name = "提案 😀" } }
            }
        });
        public void Dispose() => Owner.Dispose();
    }
    internal static IEnumerable<(string, Action)> Cases()
    {
        yield return ("M8.3-A scene preflight is copied and leaves authoring/history unchanged", () => {
            using var f = new Fixture(); byte[] before = f.Owner.Document.CaptureBytes(); var history = f.Owner.Edit!.State;
            byte[] input = f.Payload(); var source = f.Proposals.CaptureSource();
            var review = f.Proposals.Review(Guid.NewGuid(), source, input);
            Check(review.CopyImpact().Objects.SequenceEqual([f.Object]));
            input[0] = 0; var copy = review.CopyPayload(); copy[0] = 0;
            var impact = review.CopyImpact(); impact.Objects[0] = Guid.Empty;
            Check(review.CopyPayload()[0] == (byte)'{' && review.CopyImpact().Objects[0] == f.Object);
            Check(f.Proposals.IsCurrent(review) && review.Source == source && review.Fingerprint.Length == 64);
            Check(before.SequenceEqual(f.Owner.Document.CaptureBytes()) && f.Owner.Edit.State == history);
        });
        yield return ("M8.3-A exact source, foreign owner, revocation and expiry reject old reviews", () => {
            using var f = new Fixture(); var s = f.Proposals.CaptureSource();
            DomainProposalSource[] wrong = [s with { ProjectId = Guid.NewGuid() }, s with { HostSessionId = Guid.NewGuid() },
                s with { EditSessionId = Guid.NewGuid() }, s with { DocumentSessionId = Guid.NewGuid() },
                s with { DocumentGeneration = s.DocumentGeneration + 1 }, s with { Revision = s.Revision + 1 },
                s with { BehaviourCatalogGeneration = Guid.NewGuid() }, s with { ReviewEpoch = s.ReviewEpoch + 1 },
                s with { Selection = f.Object }, s with { EndpointId = Guid.NewGuid() }, s with { AudienceRevision = 1 }];
            foreach (var value in wrong) Reject(() => f.Proposals.Review(Guid.NewGuid(), value, f.Payload()));
            var review = f.Proposals.Review(Guid.NewGuid(), s, f.Payload());
            var foreign = new EditorDomainProposals(f.Owner.Edit!, s.ProjectId, s.HostSessionId);
            Check(!foreign.IsCurrent(review));
            f.Owner.Edit!.Select(f.Object); Check(!f.Proposals.IsCurrent(review)); f.Owner.Edit.Select(null);
            f.Owner.Edit.SetFrozen(true); Reject(() => f.Proposals.Review(Guid.NewGuid(), s, f.Payload()));
            f.Owner.Edit.SetFrozen(false);
            f.Owner.Edit.SetBehaviourCatalog(new(Guid.NewGuid(), [])); Check(!f.Proposals.IsCurrent(review));
            f.Owner.RefreshCatalog(); Check(f.Proposals.IsCurrent(review));
            f.Time.Now = 60_000; Check(!f.Proposals.IsCurrent(review)); f.Time.Now = 0;
            f.Proposals.Invalidate(); Check(!f.Proposals.IsCurrent(review));
            Reject(() => f.Proposals.Review(Guid.NewGuid(), s, f.Payload()));
            f.Proposals.Close(); Reject(() => f.Proposals.CaptureSource());
            using var changed = new Fixture(); var old = changed.Proposals.CaptureSource();
            changed.Owner.Document.CreateObject("External mutation");
            Reject(() => changed.Proposals.Review(Guid.NewGuid(), old, changed.Payload()));
            changed.Owner.Edit!.Resynchronize();
            Reject(() => changed.Proposals.Review(Guid.NewGuid(), old, changed.Payload()));
        });
        yield return ("M8.3-A strict JSON, unsupported domain, duplicate IDs and unknown objects fail closed", () => {
            using var f = new Fixture(); var s = f.Proposals.CaptureSource(); byte[] before = f.Owner.Document.CaptureBytes();
            string good = Encoding.UTF8.GetString(f.Payload());
            string[] bad = ["[]", good.Replace("\"version\":1", "\"version\":1,\"version\":1"),
                good.Replace("\"version\":1", "\"version\":1,\"approved\":true"), good.Replace("\"version\":1", "\"version\":2"),
                good.Replace("ncma.scene.transaction", "ncma.scene.restore"),
                good.Replace("\"op\":\"rename\"", "\"op\":\"rename\",\"op\":\"rename\"")];
            foreach (string input in bad) Reject(() => f.Proposals.Review(Guid.NewGuid(), s, Encoding.UTF8.GetBytes(input)));
            Reject(() => f.Proposals.Review(Guid.NewGuid(), s, new byte[65_537]));
            Reject(() => f.Proposals.Review(Guid.NewGuid(), s, [0xff, 0xff]));
            Reject(() => f.Proposals.Review(Guid.NewGuid(), s, f.Payload(Guid.NewGuid())));
            Guid id = Guid.NewGuid(); _ = f.Proposals.Review(id, s, f.Payload());
            Reject(() => f.Proposals.Review(id, s, f.Payload()));
            Check(before.SequenceEqual(f.Owner.Document.CaptureBytes()) && f.Owner.Edit!.State.UndoCount == 0);
        });
        yield return ("M8.3-A bounded lifetime and owner-thread access preserve original document", () => {
            using var f = new Fixture(); var s = f.Proposals.CaptureSource();
            Task.Run(() => Reject(() => f.Proposals.CaptureSource())).GetAwaiter().GetResult();
            for (int i = 0; i < EditorDomainProposals.MaximumLifetimeProposals; ++i)
                _ = f.Proposals.Review(Guid.NewGuid(), s, f.Payload());
            f.Proposals.Invalidate();
            Reject(() => f.Proposals.Review(Guid.NewGuid(), f.Proposals.CaptureSource(), f.Payload()));
            Check(f.Owner.Edit!.State.UndoCount == 0);
        });
    }
}
