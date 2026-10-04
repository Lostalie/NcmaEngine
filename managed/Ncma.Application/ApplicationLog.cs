using System.Text;
using System.Text.Json;
namespace Ncma.Application;
public sealed record LogEvent(ulong Sequence, DateTimeOffset Time, string Level, string Code, string Message, Guid Correlation);
public interface IApplicationLog { void Write(string level, string code, string message, Guid correlation); }
public sealed class ApplicationLog : IApplicationLog, IDisposable
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly Queue<LogEvent> _events = [];
    private readonly int _capacity, _maxBytes;
    private readonly string _path;
    private bool _disposed;
    private ulong _sequence;
    public ApplicationLog(string path, int capacity = 512, int maxBytes = 1024 * 1024)
    {
        if (capacity is < 1 or > 4096 || maxBytes < 65536) throw new ArgumentException("Invalid log bounds.");
        _path = Path.GetFullPath(path); _capacity = capacity; _maxBytes = maxBytes;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }
    private void Verify()
    {
        if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("Log requires owner thread.");
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
    public LogEvent[] Snapshot { get { Verify(); return _events.ToArray(); } }
    public ulong Sequence { get { Verify(); return _sequence; } }
    public void Write(string level, string code, string message, Guid correlation)
    {
        Verify();
        if (level is not ("info" or "warning" or "error") || string.IsNullOrWhiteSpace(code) || code.Length > 128 ||
            message is null || Encoding.UTF8.GetByteCount(message) > 8192) throw new ArgumentException("Invalid log event.");
        var item = new LogEvent(checked(_sequence + 1), DateTimeOffset.UtcNow, level, code, message, correlation);
        string line = JsonSerializer.Serialize(item) + "\n";
        if (File.Exists(_path) && new FileInfo(_path).Length + Encoding.UTF8.GetByteCount(line) > _maxBytes)
            File.Move(_path, _path + ".1", true); // One bounded previous segment.
        File.AppendAllText(_path, line, new UTF8Encoding(false));
        _sequence = item.Sequence;
        if (_events.Count == _capacity) _events.Dequeue();
        _events.Enqueue(item);
    }
    public void Dispose()
    {
        if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("Log requires owner thread.");
        _disposed = true; _events.Clear();
    }
}
