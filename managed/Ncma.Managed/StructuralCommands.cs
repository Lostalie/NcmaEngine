using Ncma.Runtime;

namespace Ncma;

/// <summary>A committed Entity, an earlier creation in this batch, or a same-step pending creation.</summary>
public readonly struct StructuralTarget
{
    private StructuralTarget(byte kind, Entity entity, int operation, PendingEntity? pending)
    { Kind = kind; Entity = entity; Operation = operation; Pending = pending; }
    internal byte Kind { get; }
    internal Entity Entity { get; }
    internal int Operation { get; }
    internal PendingEntity? Pending { get; }
    public static StructuralTarget Existing(Entity entity) => new(1, entity, -1, null);
    public static StructuralTarget CreatedAt(int operation) => new(2, default, operation, null);
    public static StructuralTarget PendingCreation(PendingEntity pending) => new(3, default, -1, pending);
}

/// <summary>Declarative input, copied and fully checked by the fixed-step command owner.</summary>
public abstract record StructuralCommand
{
    private protected StructuralCommand() { }
    public sealed record Create(Guid ObjectId, string Name) : StructuralCommand;
    public sealed record Destroy(StructuralTarget Target) : StructuralCommand;
    public sealed record Rename(StructuralTarget Target, string Name) : StructuralCommand;
    public sealed record Add(StructuralTarget Target, ComponentSnapshot Component) : StructuralCommand;
    public sealed record Remove(StructuralTarget Target, string TypeId) : StructuralCommand;
    public sealed record Attach(StructuralTarget Target, RuntimeBinding Binding) : StructuralCommand;
    public sealed record Detach(StructuralTarget Target, Guid BindingId) : StructuralCommand;
    public sealed record Enable(StructuralTarget Target, Guid BindingId, bool Enabled) : StructuralCommand;
}

/// <summary>Submission is not a commit receipt. Tokens resolve only after the enclosing step commits.</summary>
public sealed class StructuralSubmission
{
    private readonly CommandToken[] _tokens;
    private readonly PendingEntity?[] _created;
    internal StructuralSubmission(CommandToken[] tokens, PendingEntity?[] created)
    { _tokens = tokens; _created = created; }
    public int Count => _tokens.Length;
    public CommandToken this[int operation] => _tokens[operation];
    public PendingEntity Created(int operation) => _created[operation] ?? throw new ArgumentException("Operation is not a creation.");
}
