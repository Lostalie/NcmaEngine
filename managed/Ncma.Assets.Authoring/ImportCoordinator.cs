using System.Diagnostics;
using System.Security.Cryptography;
using Ncma.Asset.Import;
using Ncma.Assets.Authoring.Storage;
using Ncma.Editor.Core;

namespace Ncma.Assets.Authoring;

// Trusted bootstrap specification, not Agent/deserialized input. Hashes pin worker/kernel code.
public sealed record ImportWorkerLaunch(string DotnetPath, string WorkerAssembly, string WorkerHash,
    string KernelPath, string KernelHash, ImportToolFile[]? Dependencies = null);
public sealed record ImportToolFile(string Path, string Hash);
public sealed record ImportLimits(TimeSpan Timeout, long MaxPrivateBytes)
{
    public static ImportLimits Default { get; } = new(TimeSpan.FromMinutes(2), 1536L * 1024 * 1024);
}
public enum ImportJobState { Queued, Preparing, Parsing, Converting, Validating, Ready, Cancelled, Failed }
public sealed record ImportCandidateInfo(Guid JobId, Guid ProjectId, ulong ProjectGeneration, ulong AssetRevision,
    string SourcePath, string SourceHash, int SampleRate, string ResultPath, string ResultHash, long ResultBytes,
    int Meshes, int Bones, int Clips, bool StaticOnly = false);
public sealed record ImportJobSnapshot(Guid JobId, ImportJobState State, string Code, ImportProgress Progress, ImportCandidateInfo? Candidate);

// Owner sees copied, bounded snapshots only. One background worker slot, five total retained jobs,
// at most four Ready candidates. No World, EditSession mutation, parsing, hash or IPC wait in Pump.
public sealed class ImportCoordinator : IDisposable
{
    public const int MaxJobs = 5, MaxCandidates = 4, MaxRetainedStagingJobs = 64;
    public const long MaxStagingBytes = 1024L * 1024 * 1024;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly AssetProjectPaths _paths;
    private readonly Guid _project;
    private readonly ulong _generation;
    private readonly AssetRevisionClock _clock;
    private readonly Func<string, bool> _approvedSource;
    private readonly Func<bool> _current;
    private readonly ImportWorkerLaunch _launch;
    private readonly ImportLimits _limits;
    private readonly SemaphoreSlim _workerSlot = new(1);
    private readonly Dictionary<Guid, Job> _jobs = [];
    private readonly object _readyGate = new();
    private int _readyCount, _closed;
    private sealed class Prepared(ImportCandidateInfo info, ImportedModel model, WindowsAssetFile source, WindowsAssetFile result, AssetDirectoryLease parents) : IDisposable
    {
        public ImportCandidateInfo Info { get; } = info;
        public ImportedModel Model { get; } = model;
        private int _references = 1;
        public void Retain() => Interlocked.Increment(ref _references);
        public void Dispose() { if (Interlocked.Decrement(ref _references) == 0) { result.Dispose(); source.Dispose(); parents.Dispose(); } }
    }
    private sealed class Job(Guid id, string path, int rate, ulong revision, string sourceHash, bool staticOnly)
    {
        public readonly object Gate = new();
        public readonly Guid Id = id;
        public readonly string Source = path;
        public readonly int Rate = rate;
        public readonly ulong Revision = revision;
        public readonly string SourceHash = sourceHash;
        public readonly bool StaticOnly = staticOnly;
        public readonly CancellationTokenSource Cancel = new();
        public Task Work = Task.CompletedTask;
        public ImportJobState State = ImportJobState.Queued;
        public string Code = "queued";
        public ImportProgress Progress;
        public Prepared? Candidate;
    }
    public ImportCoordinator(AssetProjectPaths paths, Guid projectId, ulong generation, AssetRevisionClock clock,
        ImportWorkerLaunch launch, Func<string, bool> approvedSource, Func<bool> isCurrent, ImportLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(paths); ArgumentNullException.ThrowIfNull(clock); ArgumentNullException.ThrowIfNull(launch);
        ArgumentNullException.ThrowIfNull(approvedSource); ArgumentNullException.ThrowIfNull(isCurrent);
        if (projectId == Guid.Empty || generation == 0) throw new ArgumentException("Invalid import project identity.");
        _limits = limits ?? ImportLimits.Default;
        if (_limits.Timeout < TimeSpan.FromMilliseconds(100) || _limits.Timeout > TimeSpan.FromMinutes(10) || _limits.MaxPrivateBytes is < 16 * 1024 * 1024 or > 4L * 1024 * 1024 * 1024)
            throw new ArgumentException("Invalid import watchdog limits.");
        _paths = paths; _project = projectId; _generation = generation; _clock = clock;
        if (launch.Dependencies is { Length: > 16 }) throw new ArgumentException("Tool dependency budget.");
        _launch = launch with { Dependencies = launch.Dependencies is null ? null : (ImportToolFile[])launch.Dependencies.Clone() }; _approvedSource = approvedSource; _current = isCurrent;
    }
    public Guid Enqueue(string sourcePath, int sampleRate, ulong expectedRevision, ulong projectGeneration, string expectedSourceHash, bool staticOnly = false)
    {
        Verify(); AssetPaths.Validate(sourcePath); AssetRecordCodec.ValidateHash(expectedSourceHash);
        if (!sourcePath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) || sampleRate is < 1 or > 120) throw new ArgumentException("Invalid FBX request.");
        if (!_current() || projectGeneration != _generation || expectedRevision != _clock.Revision) throw new EditCommandRejectedException("asset_project_stale");
        if (!_approvedSource(sourcePath)) throw new EditCommandRejectedException("asset_source_denied", denied: true);
        if (_jobs.Count == MaxJobs) throw new EditCommandRejectedException("asset_import_queue_full");
        var job = new Job(Guid.NewGuid(), sourcePath, sampleRate, expectedRevision, expectedSourceHash, staticOnly); _jobs.Add(job.Id, job);
        job.Work = Task.Run(() => Run(job)); return job.Id;
    }
    public ImportJobSnapshot[] Pump()
    {
        Verify();
        foreach (var job in _jobs.Values)
            if (!_current() || job.Revision != _clock.Revision || !_approvedSource(job.Source)) CancelCore(job);
        return _jobs.Values.Select(Snapshot).OrderBy(j => j.JobId).ToArray();
    }
    public ImportJobSnapshot Inspect(Guid jobId) { Verify(); Pump(); return Snapshot(Find(jobId)); }
    public ImportCandidateInfo RequireReady(Guid jobId, ulong expectedRevision, ulong generation)
    {
        Verify(); var job = Find(jobId);
        if (!_current() || generation != _generation || expectedRevision != _clock.Revision || job.Revision != expectedRevision)
            throw new EditCommandRejectedException("asset_project_stale");
        if (!_approvedSource(job.Source)) throw new EditCommandRejectedException("asset_source_denied", denied: true);
        lock (job.Gate)
        {
            if (job.Cancel.IsCancellationRequested || job.State != ImportJobState.Ready || job.Candidate is null) throw new EditCommandRejectedException("asset_import_not_ready");
            return job.Candidate.Info;
        }
    }
    public void Cancel(Guid jobId) { Verify(); CancelCore(Find(jobId)); }
    // Cancellation changes the visible state immediately; cleanup may still own leases/process IO.
    public bool CanForget(Guid jobId) { Verify(); return Find(jobId).Work.IsCompleted; }
    internal Task<T> PrepareAsync<T>(Guid jobId, Func<ImportedModel, ImportCandidateInfo, CancellationToken, T> prepare)
    {
        Verify(); var job = Find(jobId); RequireReady(jobId, _clock.Revision, _generation);
        Prepared candidate;
        lock (job.Gate) { candidate = job.Candidate ?? throw new EditCommandRejectedException("asset_import_not_ready"); candidate.Retain(); }
        CancellationToken token = job.Cancel.Token;
        return Task.Run(() => { try { token.ThrowIfCancellationRequested(); var result = prepare(candidate.Model, candidate.Info, token);
            token.ThrowIfCancellationRequested(); return result; } finally { candidate.Dispose(); } });
    }
    // Forget diagnostics only after completion. Files are retained for explicit scoped recovery/GC, not recursively removed.
    public void Forget(Guid jobId)
    {
        Verify(); var job = Find(jobId); CancelCore(job);
        if (!job.Work.IsCompleted) throw new EditCommandRejectedException("asset_import_busy");
        _jobs.Remove(jobId); job.Cancel.Dispose();
    }
    private Job Find(Guid id) => _jobs.TryGetValue(id, out var job) ? job : throw new EditCommandRejectedException("asset_import_unknown");
    private static ImportJobSnapshot Snapshot(Job job)
    { lock (job.Gate) return new(job.Id, job.State, job.Code, job.Progress, job.Candidate?.Info); }
    private void CancelCore(Job job)
    {
        job.Cancel.Cancel();
        lock (job.Gate)
        {
            if (job.Candidate is not null) { job.Candidate.Dispose(); job.Candidate = null; lock (_readyGate) _readyCount--; }
            if (job.State != ImportJobState.Failed) { job.State = ImportJobState.Cancelled; job.Code = "cancelled"; }
        }
    }
    private async Task Run(Job job)
    {
        bool slot = false; WindowsAssetFile? source = null, result = null; AssetDirectoryLease? parents = null;
        Process? process = null; Task? watch = null, stderr = null;
        using var stopWatch = new CancellationTokenSource();
        using var io = CancellationTokenSource.CreateLinkedTokenSource(job.Cancel.Token);
        string failure = "import_failed";
        try
        {
            await _workerSlot.WaitAsync(job.Cancel.Token); slot = true; job.Cancel.Token.ThrowIfCancellationRequested();
            Set(ImportJobState.Preparing, "preparing");
            string jobRoot = $"out/asset-authoring/{_project:N}/imports";
            _paths.EnsureCacheDirectory(jobRoot);
            using (var stagingParents = new AssetDirectoryLease(_paths, [jobRoot + "/.probe"]))
            {
                int count = 0; long retained = 0;
                foreach (string entry in Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(_paths.ResolveCache(jobRoot + "/.probe"))!))
                {
                    AssetProjectPaths.RejectReparse(entry);
                    if (++count >= MaxRetainedStagingJobs) throw new ArgumentException("Import staging budget exhausted; explicit cleanup required.");
                    string name = Path.GetFileName(entry);
                    if (!Directory.Exists(entry) || !Guid.TryParseExact(name, "N", out var stagingId) || stagingId == Guid.Empty || name != stagingId.ToString("N"))
                        throw new ArgumentException("Unknown staging occupant; explicit inspection required.");
                    using var existingParents = new AssetDirectoryLease(_paths, [jobRoot + "/" + name + "/.probe"]);
                    int files = 0;
                    foreach (string file in Directory.EnumerateFileSystemEntries(entry))
                    {
                        if (++files > 1 || Path.GetFileName(file) != "model.nim") throw new ArgumentException("Unknown staging output; explicit inspection required.");
                        using var existing = WindowsAssetFile.OpenReadLease(file); retained = checked(retained + existing.Length);
                        if (existing.Length > ImportedModelCodec.MaxBytes || retained > MaxStagingBytes - ImportedModelCodec.MaxBytes)
                            throw new ArgumentException("Retained staging byte budget exhausted; explicit cleanup required.");
                    }
                }
                _paths.EnsureCacheDirectory(jobRoot + $"/{job.Id:N}");
            }
            string resultRelative = jobRoot + $"/{job.Id:N}/model.nim";
            parents = new(_paths, [job.Source, resultRelative]);
            source = WindowsAssetFile.OpenReadLease(_paths.Resolve(job.Source, requireFile: true));
            string hash = source.Hash(AssetDiskTransaction.MaxSourceBytes, job.Cancel.Token); job.Cancel.Token.ThrowIfCancellationRequested();
            if (hash != job.SourceHash) { failure = "source_changed"; throw new ArgumentException("Approved source changed before preparation."); }
            string resultPath = _paths.ResolveCache(resultRelative);
            ImportKernel.ValidateAbsolute(_launch.DotnetPath); ImportKernel.ValidateAbsolute(_launch.WorkerAssembly); ImportKernel.ValidateAbsolute(_launch.KernelPath);
            AssetRecordCodec.ValidateHash(_launch.WorkerHash); AssetRecordCodec.ValidateHash(_launch.KernelHash);
            // Keep the launch assembly immutable through child startup/lifetime; kernel is separately pinned in child.
            using var assemblyPin = new ToolCodePin(_launch.WorkerAssembly);
            if (Convert.ToHexString(SHA256.HashData(assemblyPin.Stream)) != _launch.WorkerHash) throw new ArgumentException("Worker integrity mismatch.");
            using var dependencyPins = PinDependencies();
            var start = new ProcessStartInfo(_launch.DotnetPath) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(_launch.WorkerAssembly)! };
            start.ArgumentList.Add(_launch.WorkerAssembly); start.ArgumentList.Add(_launch.KernelPath); start.ArgumentList.Add(_launch.KernelHash);
            process = Process.Start(start) ?? throw new IOException("Worker did not start.");
            DateTime identity = process.StartTime; var owned = process; var started = Stopwatch.StartNew();
            stderr = DrainErrors(owned.StandardError.BaseStream);
            watch = Task.Run(async () =>
            {
                try
                {
                    while (!stopWatch.IsCancellationRequested && !owned.HasExited)
                    {
                        if (job.Cancel.IsCancellationRequested || started.Elapsed > _limits.Timeout || owned.PrivateMemorySize64 > _limits.MaxPrivateBytes)
                        {
                            if (!job.Cancel.IsCancellationRequested) failure = started.Elapsed > _limits.Timeout ? "worker_timeout" : "worker_memory_budget";
                            io.Cancel();
                            try { await ImportProtocol.WriteAsync(owned.StandardInput.BaseStream, new ImportWorkerCancel(1, job.Id, "cancel"), stopWatch.Token).WaitAsync(TimeSpan.FromMilliseconds(100)); }
                            catch (Exception) { }
                            await Task.Delay(500);
                            if (!owned.HasExited && owned.StartTime == identity) owned.Kill(entireProcessTree: false);
                            break;
                        }
                        await Task.Delay(100, stopWatch.Token); owned.Refresh();
                    }
                }
                catch (OperationCanceledException) { }
                catch (InvalidOperationException) when (owned.HasExited) { }
            });
            var request = new ImportWorkerRequest(1, job.Id, _project, _generation, job.Revision, _paths.Resolve(job.Source), hash, job.Rate, resultPath, job.StaticOnly);
            Set(ImportJobState.Parsing, "working");
            await ImportProtocol.WriteAsync(process.StandardInput.BaseStream, request, io.Token);
            bool ready = false; ImportWorkerMessage? manifest = null; int messages = 0;
            while (!ready)
            {
                var message = await ImportProtocol.ReadAsync<ImportWorkerMessage>(process.StandardOutput.BaseStream, io.Token);
                ImportProtocol.Validate(message, job.Id);
                if (++messages > 6100) throw new ArgumentException("Worker notification budget exceeded.");
                lock (job.Gate) job.Progress = new(message.Phase, message.BytesRead, message.BytesTotal);
                if (message.State == "Progress") { Set(message.Phase < 2 ? ImportJobState.Parsing : ImportJobState.Converting, "working"); continue; }
                if (message.State != "Ready") { failure = message.Code; throw new IOException("Worker returned a failed/cancelled result."); }
                ready = true; manifest = message;
            }
            // No accepting partial/multiple stdout results or nonzero exit behind a Ready frame.
            byte[] trailing = new byte[1]; if (await process.StandardOutput.BaseStream.ReadAsync(trailing, io.Token) != 0) throw new ArgumentException("Unexpected worker trailing output.");
            await process.WaitForExitAsync(io.Token); if (process.ExitCode != 0) { failure = "worker_exit_failed"; throw new IOException("Worker exit failed."); }
            Set(ImportJobState.Validating, "validating"); job.Cancel.Token.ThrowIfCancellationRequested();
            result = WindowsAssetFile.OpenReadLease(_paths.ResolveCache(resultRelative, requireFile: true));
            if (result.Length != manifest!.ResultBytes || result.Hash(ImportedModelCodec.MaxBytes, job.Cancel.Token) != manifest.ResultHash) throw new ArgumentException("Worker manifest content mismatch.");
            ImportedModel model = ImportedModelCodec.Decode(result.Read(ImportedModelCodec.MaxBytes));
            if (model.SampleRate != job.Rate) throw new ArgumentException("Candidate setting mismatch.");
            var info = new ImportCandidateInfo(job.Id, _project, _generation, job.Revision, job.Source, hash, job.Rate, resultRelative,
                manifest.ResultHash!, result.Length, model.Meshes.Length, model.Bones.Length, model.Clips.Length, job.StaticOnly);
            lock (job.Gate)
            {
                job.Cancel.Token.ThrowIfCancellationRequested(); if (Volatile.Read(ref _closed) != 0) throw new OperationCanceledException();
                lock (_readyGate) { if (_readyCount == MaxCandidates) throw new ArgumentException("Candidate budget exceeded."); _readyCount++; }
                job.Candidate = new(info, model, source, result, parents); source = null; result = null; parents = null;
                job.State = ImportJobState.Ready; job.Code = "ok";
            }
        }
        catch (Exception error)
        {
            if (failure == "import_failed") failure = error is EndOfStreamException ? "worker_crashed" : error is ArgumentException ? "validation_failed" : "import_failed";
            lock (job.Gate) { job.State = job.Cancel.IsCancellationRequested || Volatile.Read(ref _closed) != 0 ? ImportJobState.Cancelled : ImportJobState.Failed; job.Code = job.State == ImportJobState.Cancelled ? "cancelled" : failure; }
        }
        finally
        {
            // Malformed output may arrive before watchdog. Kill only this owned process, never a DLL/thread.
            try
            {
                if (process is not null && !process.HasExited) { process.Kill(entireProcessTree: false); await process.WaitForExitAsync(); }
                stopWatch.Cancel(); if (watch is not null) await watch; if (stderr is not null) await stderr;
            }
            finally { process?.Dispose(); result?.Dispose(); source?.Dispose(); parents?.Dispose(); if (slot) _workerSlot.Release(); }
        }
        void Set(ImportJobState state, string code) { lock (job.Gate) { job.Cancel.Token.ThrowIfCancellationRequested(); job.State = state; job.Code = code; } }
    }
    private IDisposable PinDependencies()
    {
        var pins = new List<ToolCodePin>();
        try {
            foreach (var dependency in _launch.Dependencies ?? []) {
                AssetRecordCodec.ValidateHash(dependency.Hash); var pin = new ToolCodePin(dependency.Path); pins.Add(pin);
                if (Convert.ToHexString(SHA256.HashData(pin.Stream)) != dependency.Hash) throw new ArgumentException("Tool dependency integrity mismatch.");
            }
            return new ToolPins(pins);
        } catch { foreach (var pin in pins) pin.Dispose(); throw; }
    }
    private sealed class ToolPins(List<ToolCodePin> pins) : IDisposable { public void Dispose() { foreach (var pin in pins) pin.Dispose(); } }
    private static async Task DrainErrors(Stream stream)
    {
        byte[] buffer = new byte[4096]; // Drain continuously, retain no unbounded stderr transcript.
        while (await stream.ReadAsync(buffer) != 0) { }
    }
    private void Verify()
    {
        if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("Import coordinator requires owner thread.");
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _closed) != 0, this);
    }
    public Task Completion { get { if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Owner required."); return Task.WhenAll(_jobs.Values.Select(j => j.Work)); } }
    public void Dispose()
    {
        if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Owner-thread close required.");
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        foreach (var job in _jobs.Values) CancelCore(job); // Nonblocking. Completion is available to the explicit asynchronous host shutdown.
        _ = Task.WhenAll(_jobs.Values.Select(j => j.Work)).ContinueWith(_ =>
        {
            foreach (var job in _jobs.Values) job.Cancel.Dispose();
            _workerSlot.Dispose();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
