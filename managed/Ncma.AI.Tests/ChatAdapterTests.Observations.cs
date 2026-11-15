using System.Diagnostics;
using System.Text.Json;
using Ncma.AI;
using Ncma.Modules;

internal static partial class ChatAdapterTests
{
    private static ObservationIdentity TransportIdentity() => new(Guid.NewGuid(), new('A', 64), new('B', 64),
        ObservationConfiguration.Release, ObservationVariant.Ai, Stopwatch.Frequency);
    private static IEnumerable<(string, Action)> TransportObservationCases()
    {
        yield return ("M8.7-E2B2 transport default off and owner once before invocation", () => {
            using var f = new Fixture(); var handler = new Handler((_, _) => Task.FromResult(Http()));
            using var adapter = new DeepSeekChatAdapter(f.Snapshot, f.Operation, f.Credential, handler);
            Reject(() => adapter.SealObservation());
            Reject(() => adapter.BeginObservation(TransportIdentity(), 0));
            Reject(() => adapter.BeginObservation(TransportIdentity(), 4097));
            Reject(() => adapter.BeginObservation(TransportIdentity() with { TimestampFrequency = 1 }));
            Task.Run(() => Reject(() => adapter.BeginObservation(TransportIdentity()))).GetAwaiter().GetResult();
            _ = adapter.InvokeAsync(f.Request, TimeSpan.Zero, default).GetAwaiter().GetResult();
            Reject(() => adapter.BeginObservation(TransportIdentity())); Check(handler.Calls == 0);
            using var next = new DeepSeekChatAdapter(f.Snapshot, f.Operation, f.Credential, new Handler((_, _) => Task.FromResult(Http())));
            next.BeginObservation(TransportIdentity()); Reject(() => next.BeginObservation(TransportIdentity()));
            Task.Run(() => Reject(() => next.SealObservation())).GetAwaiter().GetResult();
            var empty = next.SealObservation(); Check(empty.CompleteObservedCoverage && empty.Invoked == 0 && empty.Samples.Length == 0);
            Reject(() => next.SealObservation()); Reject(() => next.BeginObservation(TransportIdentity()));
        });
        yield return ("M8.7-E2B2 transport retains sequential diagnostics exact identities and body metrics", () => {
            using var f = new Fixture(); var next = Requests(f, 1)[0];
            using var adapter = new DeepSeekChatAdapter(f.Snapshot, f.Operation, f.Credential, new Handler((_, _) => Task.FromResult(Http())));
            var identity = TransportIdentity(); adapter.BeginObservation(identity);
            foreach (var request in new[] { f.Request, next })
                Check(adapter.InvokeAsync(request, TimeSpan.FromSeconds(2), default).GetAwaiter().GetResult().Status == ModuleReplyStatus.Proposal);
            var report = adapter.SealObservation(); Check(report.Identity == identity && report.Invoked == 2 && report.Completed == 2 && report.Pending == 0 && report.CompleteObservedCoverage);
            Check(report.Samples.Length == 2 && report.Samples[0].Request == new ObservationRequest(f.Request.SessionId, f.Request.RequestId));
            Check(report.Samples[1].Request == new ObservationRequest(next.SessionId, next.RequestId));
            foreach (var sample in report.Samples) Check(sample.OwnedActiveSlot && sample.AdapterElapsedTicks >= 0 && sample.CompletedTimestamp >= sample.StartedTimestamp &&
                sample.Diagnostic.RequestBytes == f.Review.WireRequest.CopyBytes().Length && sample.Diagnostic.ResponseBytes == Response().Length && sample.Diagnostic.Usage!.TotalTokens == 5);
            Check(adapter.LastDiagnostic == report.Samples[1].Diagnostic && !report.PerformanceAccepted);
            string text = JsonSerializer.Serialize(report); Check(!text.Contains("fixture-key", StringComparison.Ordinal) && !text.Contains("Rename fixture", StringComparison.Ordinal) && !text.Contains(f.Profile.Endpoint, StringComparison.Ordinal));
            report.Samples[1] = report.Samples[0]; Check(adapter.LastDiagnostic!.RequestId == next.RequestId); // Detached array.
        });
        yield return ("M8.7-E2B2 transport busy duplicate calls retain ordinal and do not release active owner", () => {
            using var f = new Fixture();
            var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            var handler = new Handler((_, _) => release.Task);
            using var adapter = new DeepSeekChatAdapter(f.Snapshot, f.Operation, f.Credential, handler);
            adapter.BeginObservation(TransportIdentity()); var running = adapter.InvokeAsync(f.Request, TimeSpan.FromSeconds(2), default);
            try {
                Check(adapter.InvokeAsync(f.Request, TimeSpan.FromSeconds(2), default).GetAwaiter().GetResult().Status == ModuleReplyStatus.Rejected);
                Check(adapter.LastDiagnostic!.Code == "adapter_busy" && !running.IsCompleted && handler.Calls == 1); Reject(adapter.Dispose);
            } finally { release.TrySetResult(Http()); _ = running.GetAwaiter().GetResult(); }
            Check(adapter.InvokeAsync(f.Request, TimeSpan.FromSeconds(2), default).GetAwaiter().GetResult().Status == ModuleReplyStatus.Rejected);
            var report = adapter.SealObservation(); Check(report.Invoked == 3 && report.Completed == 3 && report.CompleteObservedCoverage);
            Check(report.Samples.Select(s => s.InvocationSequence).SequenceEqual(new long[] { 2, 1, 3 }));
            Check(report.Samples.Select(s => s.CompletionSequence).SequenceEqual(new long[] { 1, 2, 3 }));
            Check(report.Samples[0].Diagnostic.Code == "adapter_busy" && !report.Samples[0].OwnedActiveSlot && report.Samples[1].OwnedActiveSlot &&
                report.Samples[2].Diagnostic.Code == "request_duplicate" && !report.Samples[2].OwnedActiveSlot);
        });
        yield return ("M8.7-E2B2 transport seal pending neither cancels nor retroactively completes", () => {
            using var f = new Fixture(); var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var adapter = new DeepSeekChatAdapter(f.Snapshot, f.Operation, f.Credential, new Handler((_, _) => release.Task));
            adapter.BeginObservation(TransportIdentity()); var running = adapter.InvokeAsync(f.Request, TimeSpan.FromSeconds(2), default);
            AiTransportObservationReport report;
            try { report = adapter.SealObservation(); Check(report.Pending == 1 && report.Completed == 0 && !report.CompleteObservedCoverage && !running.IsCompleted); Reject(adapter.Dispose); }
            finally { release.TrySetResult(Http()); _ = running.GetAwaiter().GetResult(); }
            Check(report.Pending == 1 && report.Completed == 0 && report.Samples.Length == 0 && adapter.LastDiagnostic!.Code == "proposal_requires_domain_validation");
        });
        yield return ("M8.7-E2B2 transport bounded prefix drops every later completion without admission changes", () => {
            using var f = new Fixture(); var handler = new Handler((_, _) => Task.FromResult(Http()));
            using var adapter = new DeepSeekChatAdapter(f.Snapshot, f.Operation, f.Credential, handler);
            adapter.BeginObservation(TransportIdentity(), 1);
            Check(adapter.InvokeAsync(f.Request, TimeSpan.FromSeconds(2), default).GetAwaiter().GetResult().Status == ModuleReplyStatus.Proposal);
            for (int i = 0; i < 3; ++i) Check(adapter.InvokeAsync(f.Request, TimeSpan.FromSeconds(2), default).GetAwaiter().GetResult().Status == ModuleReplyStatus.Rejected);
            var report = adapter.SealObservation(); Check(report.Invoked == 4 && report.Completed == 4 && report.Dropped == 3 && report.Pending == 0 && report.Samples.Length == 1 && !report.CompleteObservedCoverage);
            Check(report.Samples[0].Diagnostic.Code == "proposal_requires_domain_validation" && adapter.LastDiagnostic!.Code == "request_duplicate" && handler.Calls == 1);
        });
        yield return ("M8.7-E2B2 transport rejected and disposed invocations retained without raw exception data", () => {
            using var f = new Fixture(); var handler = new Handler((_, _) => Task.FromException<HttpResponseMessage>(new IOException("SECRET_BODY")));
            using var adapter = new DeepSeekChatAdapter(f.Snapshot, f.Operation, f.Credential, handler);
            adapter.BeginObservation(TransportIdentity()); _ = adapter.InvokeAsync(f.Request, TimeSpan.FromSeconds(2), default).GetAwaiter().GetResult();
            adapter.Dispose(); _ = adapter.InvokeAsync(f.Request, TimeSpan.Zero, default).GetAwaiter().GetResult();
            var report = adapter.SealObservation(); Check(report.Completed == 2 && report.CompleteObservedCoverage && handler.Calls == 1);
            Check(report.Samples[0].Diagnostic.ExceptionType == nameof(IOException) && report.Samples[1].Diagnostic.Code == "adapter_disposed");
            Check(!JsonSerializer.Serialize(report).Contains("SECRET", StringComparison.Ordinal));
        });
        yield return ("M8.7-E2B2 transport opt-in race boundary cannot claim complete coverage", () => {
            using var f = new Fixture(); var capture = new AiTransportObservations(TransportIdentity(), 1); long ordinal = capture.Begin();
            capture.Complete(ordinal, f.Request, 0, Stopwatch.GetTimestamp(), false, new(f.Request.RequestId, "fixture", null, 0, 0, 0, null));
            var report = capture.Seal(); Check(report.BoundaryOverlaps == 1 && report.Completed == 1 && report.Pending == 0 && !report.CompleteObservedCoverage);
        });
    }
}
