using System.Text.Json;
using Ncma.Modules;
using Ncma.Modules.Grpc;

internal static partial class Program
{
    private static void MalformedReplies(string root)
    {
        var before = checks;
        using var worker = new WorkerFixture(root, WorkerProbe.MalformedReplies);
        var adapter = worker.Connect();
        var context = new ModuleContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, 0, Guid.Empty, 0);
        var session = new ModuleSession(new ModuleCatalog([TextStatisticsModule.Operation]), adapter, context);
        void Expect(string kind, ModuleOutcomeStatus expected)
        {
            var id = Guid.NewGuid();
            Check(session.Submit(id, TextStatisticsModule.Operation.Name,
                JsonSerializer.SerializeToUtf8Bytes(new { text = kind })) == ModuleAdmission.Accepted, "Reply probe admitted.");
            ModuleOutcome? outcome = null;
            Wait(() => session.TryTake(id, context, out outcome));
            Check(outcome!.Status == expected, $"Actual malformed peer {kind}: expected {expected}, got {outcome.Status}.");
            Check((outcome.Proposal is not null) == (expected == ModuleOutcomeStatus.NeedsDomainValidation),
                "Rejected wire result cannot expose a proposal: " + kind);
            Check(!session.TryTake(id, context, out _), "Even rejected replies deliver only once.");
        }
        try
        {
            foreach (var kind in new[] { "unknown", "missing", "duplicate", "escaped-duplicate", "utf8", "oversize", "version",
                "boolean", "instance", "noncanonical-uuid", "lowercase-fingerprint", "bad-status", "unavailable-with-payload",
                "bad-base64", "noncanonical-base64", "empty-proposal", "duplicate-proposal", "utf8-proposal", "oversize-proposal", "deep-proposal" })
            {
                Expect(kind, ModuleOutcomeStatus.AdapterFault);
                Expect("healthy", ModuleOutcomeStatus.NeedsDomainValidation);
            }
            foreach (var kind in new[] { "requestId", "sessionId", "epoch", "fingerprint", "outputSchema" })
            {
                Expect(kind, ModuleOutcomeStatus.InvalidReply);
                Expect("healthy", ModuleOutcomeStatus.NeedsDomainValidation);
            }
            Expect("unavailable", ModuleOutcomeStatus.Unavailable);
            Expect("rejected", ModuleOutcomeStatus.Rejected);
        }
        finally
        {
            session.Stop(); Wait(session.TryClose);
            adapter.Stop(); Wait(adapter.TryClose);
        }
        worker.Close();
        Check(worker.ExitCode == 0 && worker.ErrorBytes == 0, "Malformed-reply test peer drains without a worker error.");
        Console.WriteLine($"PASS C# strict real-wire reply refusal / exact-context validation: {checks - before} checks.");
    }
}
