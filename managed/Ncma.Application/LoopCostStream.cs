using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Ncma.Application;

public sealed record LoopStreamReport(int Version, Guid RunId, string Status, int QueueCapacity,
    ulong MaximumRecords, ulong Produced, ulong Accepted, ulong Written, ulong QueueDrops,
    ulong BudgetDrops, ulong FailureDrops, ulong Unwritten, long PublishTicks, long PublishAllocatedBytes,
    long WriterWallTicks, long WriterAllocatedBytes, long Bytes, string? Failure)
{
    public bool CompleteRawCoverage => Status == "completed" && Produced == Written &&
        QueueDrops == 0 && BudgetDrops == 0 && FailureDrops == 0 && Unwritten == 0;
    public bool TargetPerformanceAccepted => false;
    public string Scope => "full owner-loop samples; publish cost outside sample end, inside successive present intervals; writer wall overlaps owner; startup/shutdown excluded; not display FPS";
}

// Trusted local host diagnostic only. Fixed memory, new file only, no Agent path or gameplay policy.
// No disk IO under the queue lock or on the publishing thread. Pressure drops newest explicitly.
public sealed class LoopCostStream
{
    public const int DefaultQueueCapacity = 4096;
    public const ulong MaximumRecords = 1_000_000;
    public const long MaximumBytes = 1024L * 1024 * 1024;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly object _gate = new();
    private readonly LoopCostSample[] _queue;
    private readonly Stream _output;
    private readonly Thread _worker;
    private readonly ulong _maximumRecords;
    private readonly int? _frameRateLimit;
    private readonly Guid _runId = Guid.NewGuid();
    private int _head, _count;
    private bool _closing, _finished, _attached, _timedOut;
    private ulong _produced, _accepted, _written, _queueDrops, _budgetDrops, _failureDrops;
    private long _publishTicks, _publishAllocated, _writerTicks, _writerAllocated, _bytes;
    private string? _failure;

    public static void ValidateTarget(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !path.EndsWith(".ncloop", StringComparison.OrdinalIgnoreCase) ||
            File.Exists(path) || Directory.Exists(path)) throw new ArgumentException("loop_stream_target_invalid_or_exists");
        var directory = new DirectoryInfo(Path.GetDirectoryName(path)!);
        if (!directory.Exists) throw new ArgumentException("loop_stream_parent_required");
        for (var part = directory; part is not null; part = part.Parent)
            if ((part.Attributes & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("loop_stream_reparse_path");
    }
    public static LoopCostStream Open(string path, int? frameRateLimit = null)
    {
        if (frameRateLimit is int rate) FramePacer.ValidateRate(rate);
        ValidateTarget(path);
        var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read,
            65536, FileOptions.SequentialScan);
        try { return new(output, DefaultQueueCapacity, MaximumRecords, frameRateLimit); }
        catch { output.Dispose(); throw; }
    }
    // Test-owned streams provide deterministic pressure/IO failures; no product callback surface.
    internal LoopCostStream(Stream output, int capacity, ulong maximumRecords, int? frameRateLimit = null)
    {
        if (capacity is < 1 or > DefaultQueueCapacity || maximumRecords is 0 or > MaximumRecords ||
            !output.CanWrite) throw new ArgumentException("loop_stream_bounds");
        if (frameRateLimit is int rate) FramePacer.ValidateRate(rate);
        _output = output; _queue = new LoopCostSample[capacity]; _maximumRecords = maximumRecords;
        _frameRateLimit = frameRateLimit;
        _worker = new Thread(Write) { IsBackground = true, Name = "Ncma loop evidence" };
        _worker.Start();
    }
    private void VerifyOwner()
    {
        if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("Loop stream requires owner thread.");
    }
    internal void Attach(int? frameRateLimit)
    {
        VerifyOwner();
        lock (_gate) {
            if (_attached || _closing || frameRateLimit != _frameRateLimit) throw new InvalidOperationException("Loop stream binding.");
            _attached = true;
        }
    }
    internal void Publish(in LoopCostSample sample)
    {
        VerifyOwner();
        long start = Stopwatch.GetTimestamp(), allocated = GC.GetAllocatedBytesForCurrentThread();
        lock (_gate) {
            if (!_attached || _closing || sample.Sequence != _produced + 1) throw new InvalidOperationException("Loop stream sequence/lifetime.");
            _produced++;
            if (_failure is not null) _failureDrops++;
            else if (_produced > _maximumRecords) _budgetDrops++;
            else if (_count == _queue.Length) _queueDrops++;
            else {
                _queue[(_head + _count) % _queue.Length] = sample; _count++; _accepted++;
                Monitor.Pulse(_gate);
            }
        }
        _publishAllocated += GC.GetAllocatedBytesForCurrentThread() - allocated;
        _publishTicks += Stopwatch.GetTimestamp() - start;
    }
    public LoopStreamReport Snapshot()
    {
        VerifyOwner();
        lock (_gate) return Report();
    }
    private LoopStreamReport Report() => new(1, _runId,
        _timedOut ? "close_timeout" : _failure is not null ? "failed" : _finished ? "completed" : "pending",
        _queue.Length, _maximumRecords, _produced, _accepted, _written, _queueDrops, _budgetDrops,
        _failureDrops, _accepted - _written, _publishTicks, _publishAllocated, _writerTicks, _writerAllocated,
        _bytes, _failure);
    public LoopStreamReport Complete() => Complete(TimeSpan.FromSeconds(5));
    internal LoopStreamReport Complete(TimeSpan timeout)
    {
        VerifyOwner();
        if (timeout < TimeSpan.Zero || timeout > TimeSpan.FromSeconds(5)) throw new ArgumentOutOfRangeException(nameof(timeout));
        lock (_gate) { _closing = true; Monitor.Pulse(_gate); }
        // Shutdown only. A timeout is sticky and never relabeled complete; worker owns the
        // stream until IO returns, then closes it. Never dispose a stream under pending IO.
        if (!_worker.Join(timeout)) { lock (_gate) _timedOut = true; }
        return Snapshot();
    }
    private void Write()
    {
        long allocation = GC.GetAllocatedBytesForCurrentThread();
        try {
            using var writer = new StreamWriter(_output, new UTF8Encoding(false), 65536, leaveOpen: true);
            void Line<T>(T value)
            {
                long start = Stopwatch.GetTimestamp();
                string line = JsonSerializer.Serialize(value);
                int bytes = Encoding.UTF8.GetByteCount(line) + 1;
                if (bytes > 8192 || _bytes > MaximumBytes - bytes) throw new IOException("loop_stream_byte_budget");
                writer.Write(line); writer.Write('\n');
                lock (_gate) { _bytes += bytes; _writerTicks += Stopwatch.GetTimestamp() - start; }
            }
            Line(new { kind = "header", version = 1, runId = _runId, frequency = Stopwatch.Frequency,
                queueCapacity = _queue.Length, maximumRecords = _maximumRecords, frameRateLimit = _frameRateLimit,
                scope = "owner-loop-not-display; retain gaps/faults; observation perturbs timings; startup/shutdown excluded" });
            long flushStart = Stopwatch.GetTimestamp();
            writer.Flush();
            lock (_gate) _writerTicks += Stopwatch.GetTimestamp() - flushStart;
            while (true) {
                LoopCostSample sample;
                lock (_gate) {
                    while (_count == 0 && !_closing) Monitor.Wait(_gate);
                    if (_count == 0) break;
                    sample = _queue[_head]; _head = (_head + 1) % _queue.Length; _count--;
                }
                Line(new { kind = "sample", sample });
                lock (_gate) _written++;
            }
            LoopStreamReport footer;
            lock (_gate) footer = Report();
            // "sealed" indicates logical queue drain only. Host report must ALSO confirm
            // successful flush/close; truncation or absence of that report is never acceptance.
            Line(new { kind = "sealed", report = footer });
            flushStart = Stopwatch.GetTimestamp();
            writer.Flush();
            if (_output is FileStream file) file.Flush(true); else _output.Flush();
            lock (_gate) _writerTicks += Stopwatch.GetTimestamp() - flushStart;
        }
        catch (Exception error) when (error is not OutOfMemoryException) {
            lock (_gate) _failure = "loop_stream_write_failed";
        }
        finally {
            long closeStart = Stopwatch.GetTimestamp();
            try { _output.Dispose(); }
            catch (Exception error) when (error is not OutOfMemoryException) { lock (_gate) _failure = "loop_stream_close_failed"; }
            lock (_gate) {
                _writerTicks += Stopwatch.GetTimestamp() - closeStart;
                _writerAllocated = GC.GetAllocatedBytesForCurrentThread() - allocation; _finished = true;
            }
        }
    }
}
