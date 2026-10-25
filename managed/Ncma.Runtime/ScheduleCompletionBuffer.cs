using System.Collections.Frozen;

namespace Ncma.Runtime;

// One append-only table per execution attempt, shared by every Context. The cold immutable
// task index is reusable; result slots and their sequence stamps are NEVER reused across ticks.
// Only the owner publishes, after Done + success. Workers see a read-only API and a fixed cutoff.
internal sealed class ScheduleCompletionBuffer
{
    private readonly FrozenDictionary<string, int> _slots;
    private readonly ScheduleContext?[] _contexts;
    private readonly int[] _sequences;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private int _published;
    internal ReadView View { get; }

    internal ScheduleCompletionBuffer(FrozenDictionary<string, int> slots)
    {
        _slots = slots;
        if (slots.Count > ScheduleGraph.MaxTasks) throw new ArgumentException("Completion table task budget.");
        _contexts = new ScheduleContext?[slots.Count]; _sequences = new int[slots.Count];
        View = new(this);
    }
    private void Owner()
    { if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("Only the schedule owner publishes completion."); }
    internal void Publish(string id, ScheduleContext context)
    {
        Owner();
        if (!_slots.TryGetValue(id, out int slot) || context.TaskId != id || !context.CompletedSuccessfully ||
            !ReferenceEquals(context.CompletionView, View) || _sequences[slot] != 0)
            throw new InvalidOperationException("Completion must be successful, same-attempt, exact-task and append-only.");
        int sequence = checked(_published + 1);
        _contexts[slot] = context;
        // Release completed output before publishing its stamp. Readers acquire the stamp
        // before touching that output; dictionary buckets are immutable, never concurrently added.
        Volatile.Write(ref _sequences[slot], sequence);
        Volatile.Write(ref _published, sequence);
    }
    internal ScheduleContext GetCompleted(string id)
    {
        Owner();
        int slot = _slots[id];
        if (_sequences[slot] == 0) throw new InvalidOperationException("Task is not completed.");
        return _contexts[slot]!;
    }
    internal sealed class ReadView(ScheduleCompletionBuffer buffer)
    {
        // Completion publication is not the World/step commit. This is an attempt-local watermark.
        internal int Sequence => Volatile.Read(ref buffer._published);
        internal bool TryReadOutput(string producer, string resource, int visibleThrough, out byte[] value)
        {
            value = [];
            if (visibleThrough < 0 || visibleThrough > Sequence || !buffer._slots.TryGetValue(producer, out int slot)) return false;
            int sequence = Volatile.Read(ref buffer._sequences[slot]);
            if (sequence == 0 || sequence > visibleThrough) return false;
            var context = buffer._contexts[slot]!;
            if (context.Error is not null || !context.ResourceWrites.TryGetValue(resource, out var output)) return false;
            value = output; return true;
        }
    }
}
