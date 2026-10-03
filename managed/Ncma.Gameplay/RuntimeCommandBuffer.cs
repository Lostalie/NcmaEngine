using System.Text;
using System.Text.Json;
using Ncma.Runtime;
using Ncma.Scene;
namespace Ncma.Gameplay;

internal sealed class RuntimeCommandBuffer(PlaySession session) : IRuntimeCommands
{
    internal const int Capacity = 1024, PayloadBytes = 1024 * 1024;
    private readonly List<(CommandToken Token, Guid Target)> _pending = [];
    private Dictionary<CommandToken, CommandReceipt> _receipts = [];
    private readonly HashSet<Guid> _created = [];
    private Dictionary<Guid, BehaviourBindingData[]> _bindings = [];
    private ulong _sequence, _generation = 1;
    private int _bytes;
    private bool _active;
    internal void Begin(SceneDocumentSnapshot snapshot)
    {
        _bindings = snapshot.Objects.ToDictionary(o => o.Id, o => o.Behaviours);
        _pending.Clear(); _created.Clear(); _bytes = 0; _active = true;
    }
    internal Dictionary<Guid, BehaviourBindingData[]> Bindings => _bindings;
    private CommandToken Queue(Guid target, Func<int> measure, Action stage)
    {
        session.Document.VerifyAccess(); session.Document.World.VerifyWriteAccess();
        if (!_active) throw new InvalidOperationException("Runtime commands require a fixed step.");
        try
        {
            int bytes = measure();
            if (target == Guid.Empty || _pending.Count >= Capacity || bytes < 0 || bytes > PayloadBytes - _bytes || _sequence == ulong.MaxValue)
                throw new ArgumentException("Runtime command budget or identity rejected.");
            stage();
            var token = new CommandToken(session.SessionId, _generation, ++_sequence);
            _pending.Add((token, target)); _bytes += bytes; return token;
        }
        catch (Exception error) { session.Document.World.RejectStep(error); throw; }
    }
    public PendingObject SpawnEmpty(string name, Guid? objectId = null)
    {
        Guid id = objectId ?? Guid.NewGuid();
        var token = Queue(id, () => Encoding.UTF8.GetByteCount(name), () =>
        {
            if (!_created.Add(id)) throw new ArgumentException("Duplicate reserved UUID.");
            session.Document.World.StageCreate(id, name); _bindings.Add(id, []);
        });
        return new(id, token);
    }
    public CommandToken Destroy(Guid id) => Queue(id, () => 16, () =>
    { session.Document.World.StageDestroy(id); _bindings.Remove(id); });
    public CommandToken Rename(Guid id, string name) => Queue(id, () => Encoding.UTF8.GetByteCount(name), () => session.Document.World.StageRename(id, name));
    public CommandToken AddComponent(Guid id, ComponentSnapshot component) => Queue(id, () =>
        component is null ? 0 : Encoding.UTF8.GetByteCount(component.Data.GetRawText()) + 128, () =>
        session.Document.World.StageAdd(id, component ?? throw new ArgumentNullException(nameof(component))));
    public CommandToken RemoveComponent(Guid id, string typeId) => Queue(id, () => Encoding.UTF8.GetByteCount(typeId), () => session.Document.World.StageRemove(id, typeId));
    public CommandToken AttachBehaviour(Guid id, RuntimeBinding binding) => Queue(id, () =>
        binding is null ? 0 : JsonSerializer.SerializeToUtf8Bytes(binding).Length, () =>
    {
        session.Document.World.RequireCommandTarget(id); ArgumentNullException.ThrowIfNull(binding);
        if (_bindings.Values.SelectMany(b => b).Any(b => b.Id == binding.Id)) throw new ArgumentException("Duplicate binding UUID.");
        if (binding.Exports is null) throw new ArgumentException("Missing Exports.");
        var value = new BehaviourBindingData(binding.Id, binding.TypeName, binding.Enabled,
            binding.Exports.Select(e => new ExportData(e.Name, (ExportKind)e.Kind, e.Value)).ToArray());
        _bindings[id] = [.. _bindings.GetValueOrDefault(id, []), value];
    });
    public CommandToken RemoveBehaviour(Guid id, Guid bindingId) => Queue(id, () => 32, () =>
    {
        session.Document.World.RequireCommandTarget(id); var old = _bindings.GetValueOrDefault(id, []);
        if (!old.Any(b => b.Id == bindingId)) throw new ArgumentException("Unknown binding.");
        _bindings[id] = old.Where(b => b.Id != bindingId).ToArray();
    });
    public CommandToken SetBehaviourEnabled(Guid id, Guid bindingId, bool enabled) => Queue(id, () => 33, () =>
    {
        session.Document.World.RequireCommandTarget(id); var old = _bindings.GetValueOrDefault(id, []);
        if (!old.Any(b => b.Id == bindingId)) throw new ArgumentException("Unknown binding.");
        _bindings[id] = old.Select(b => b.Id == bindingId ? b with { Enabled = enabled } : b).ToArray();
    });
    internal Action PrepareReceipts(WorldSnapshot candidate)
    {
        var alive = candidate.Objects.Select(o => o.PersistentId).ToHashSet();
        ulong tick = checked(session.Tick + 1);
        var values = _receipts.Values.OrderBy(r => r.Token.Sequence)
            .Concat(_pending.Select(p => new CommandReceipt(p.Token, p.Target, tick, alive.Contains(p.Target) ? "committed" : "removed")))
            .TakeLast(Capacity).ToDictionary(r => r.Token);
        return () => { _receipts = values; _pending.Clear(); _active = false; };
    }
    public CommandReceipt GetReceipt(CommandToken token)
    {
        session.Document.VerifyAccess();
        if (token.SessionId != session.SessionId || token.Generation != _generation || !_receipts.TryGetValue(token, out var result))
            throw new ArgumentException("Unknown, pending, expired or stale command receipt.");
        return result;
    }
    internal void Abort() { _pending.Clear(); _bindings = []; _created.Clear(); _active = false; _bytes = 0; }
    internal void Reset() { Abort(); _receipts.Clear(); _generation = checked(_generation + 1); }
}
