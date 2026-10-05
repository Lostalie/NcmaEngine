using System.Security.Cryptography;
using Ncma.Asset.Import;
using Ncma.Assets;

// One request, one context, one immutable candidate. No scene/editor/GUI/runtime dependency.
// Synchronous Main deliberately retains native thread affinity. Only cancel/status use other threads.
if (args.Length != 2) return 2;
ImportWorkerRequest? request = null;
var input = Console.OpenStandardInput(); var output = Console.OpenStandardOutput();
using var cancellation = new CancellationTokenSource();
using var progressStop = new CancellationTokenSource();
using var outputGate = new SemaphoreSlim(1);
ImportKernel? kernel = null; Task? progress = null;
async Task Send(ImportWorkerMessage message)
{
    await outputGate.WaitAsync();
    try { await ImportProtocol.WriteAsync(output, message); }
    finally { outputGate.Release(); }
}
try
{
    request = ImportProtocol.ReadAsync<ImportWorkerRequest>(input).GetAwaiter().GetResult(); ImportProtocol.Validate(request);
    ImportKernel.ValidateAbsolute(request.SourcePath); ImportKernel.ValidateAbsolute(request.ResultPath);
    // Parent pins approved source/parents. This read lease also prevents source rewrites throughout this process.
    using var source = new FileStream(request.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
    if (source.Length is < 1 or > 256 * 1024 * 1024) throw new ArgumentException("Source byte budget exceeded.");
    if (Convert.ToHexString(SHA256.HashData(source)) != request.SourceHash) throw new ArgumentException("source_changed");
    kernel = new ImportKernel(args[0], args[1]); var activeKernel = kernel; var activeRequest = request;
    // stdin EOF means abandoned parent; one matching cancel is the only accepted follow-up.
    _ = Task.Run(async () =>
    {
        try
        {
            var command = await ImportProtocol.ReadAsync<ImportWorkerCancel>(input);
            if (command.Version != 1 || command.JobId != activeRequest.JobId || command.Operation != "cancel") throw new ArgumentException("Invalid cancel frame.");
        }
        catch (Exception) { /* Broken/invalid parent stream is cancellation, never arbitrary control. */ }
        cancellation.Cancel(); activeKernel.Cancel();
    });
    progress = Task.Run(async () =>
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            while (await timer.WaitForNextTickAsync(progressStop.Token))
            {
                var status = activeKernel.ReadProgress();
                await Send(new(1, activeRequest.JobId, "Progress", status.Phase, status.BytesRead, status.BytesTotal, "ok", null, 0));
            }
        }
        catch (OperationCanceledException) { }
    });
    ImportedModel model = kernel.LoadAndCopy(request.SourcePath, request.SampleRate, cancellation.Token, request.StaticOnly);
    byte[] bytes = ImportedModelCodec.Encode(model); cancellation.Token.ThrowIfCancellationRequested();
    // No path replacement, no UUID/metadata/generation publication. Orphan diagnostics belong to the parent.
    using (var result = new FileStream(request.ResultPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
    { result.Write(bytes); result.Flush(flushToDisk: true); }
    cancellation.Token.ThrowIfCancellationRequested();
    progressStop.Cancel(); progress.GetAwaiter().GetResult();
    Send(new(1, request.JobId, "Ready", 4, (ulong)source.Length, (ulong)source.Length, "ok", Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length)).GetAwaiter().GetResult();
    return 0;
}
catch (Exception e)
{
    progressStop.Cancel();
    if (progress is not null) { try { progress.GetAwaiter().GetResult(); } catch (Exception) { } }
    if (request is not null)
    {
        string state = e is OperationCanceledException ? "Cancelled" : "Failed";
        try { Send(new(1, request.JobId, state, state == "Cancelled" ? 5u : 6u, 0, 0,
            e is ArgumentException ? "validation_failed" : state == "Cancelled" ? "cancelled" : "worker_failed", null, 0)).GetAwaiter().GetResult(); }
        catch (Exception) { }
    }
    return e is OperationCanceledException ? 3 : 1;
}
finally { kernel?.Dispose(); }
