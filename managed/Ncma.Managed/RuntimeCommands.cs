using Ncma.Runtime;
namespace Ncma;

public readonly record struct CommandToken(Guid SessionId, ulong Generation, ulong Sequence);
public sealed record PendingObject(Guid ObjectId, CommandToken Token);
public sealed record CommandReceipt(CommandToken Token, Guid ObjectId, ulong Tick, string Disposition);
public sealed record RuntimeExport(string Name, uint Kind, double Value);
public sealed record RuntimeBinding(Guid Id, string TypeName, bool Enabled, RuntimeExport[] Exports);

/// <summary>Fixed-step-only deferred structural changes. UUID reservations are not live objects.</summary>
public interface IRuntimeCommands
{
    PendingObject SpawnEmpty(string name, Guid? objectId = null);
    CommandToken Destroy(Guid objectId);
    CommandToken Rename(Guid objectId, string name);
    CommandToken AddComponent(Guid objectId, ComponentSnapshot component);
    CommandToken RemoveComponent(Guid objectId, string typeId);
    CommandToken AttachBehaviour(Guid objectId, RuntimeBinding binding);
    CommandToken RemoveBehaviour(Guid objectId, Guid bindingId);
    CommandToken SetBehaviourEnabled(Guid objectId, Guid bindingId, bool enabled);
    CommandReceipt GetReceipt(CommandToken token);
}
