using Ncma.Runtime;
namespace Ncma;

public readonly record struct CommandToken(Guid SessionId, ulong Generation, ulong Sequence);
public sealed class PendingEntity
{
    internal PendingEntity(Guid objectId, CommandToken token, ulong attempt, Entity entity)
    { ObjectId = objectId; Token = token; Attempt = attempt; Entity = entity; }
    public Guid ObjectId { get; }
    public CommandToken Token { get; }
    internal ulong Attempt { get; }
    internal Entity Entity { get; }
}
public sealed record CommandReceipt(CommandToken Token, Guid ObjectId, ulong Tick, string Disposition, Entity? Entity, Guid WorldIdentity);
public sealed record RuntimeExport(string Name, uint Kind, double Value);
public sealed record RuntimeBinding(Guid Id, string TypeName, bool Enabled, RuntimeExport[] Exports);

/// <summary>Fixed-step-only deferred structural changes. UUID reservations are not live objects.</summary>
public interface IRuntimeCommands
{
    PendingEntity SpawnEmpty(string name, Guid? objectId = null);
    StructuralSubmission SubmitBatch(ReadOnlySpan<StructuralCommand> commands);
    Entity Resolve(PendingEntity pending);
    CommandToken Destroy(Guid objectId);
    CommandToken Rename(Guid objectId, string name);
    CommandToken AddComponent(Guid objectId, ComponentSnapshot component);
    CommandToken RemoveComponent(Guid objectId, string typeId);
    CommandToken AttachBehaviour(Guid objectId, RuntimeBinding binding);
    CommandToken RemoveBehaviour(Guid objectId, Guid bindingId);
    CommandToken SetBehaviourEnabled(Guid objectId, Guid bindingId, bool enabled);
    CommandReceipt GetReceipt(CommandToken token);
}
