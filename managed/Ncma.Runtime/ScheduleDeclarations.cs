namespace Ncma.Runtime;

public enum ScheduleAccessKind { Component, Resource, Structure }
public enum ScheduleAccessMode { Read, Write }
public enum ScheduleAffinity { OwnerThread, Worker }

// Write includes read permission. These are trusted planning declarations, NOT capabilities.
public sealed record ScheduleAccess(ScheduleAccessKind Kind, string Id, int Version, ScheduleAccessMode Mode)
{
    public static ScheduleAccess Read<T>(ComponentRegistry registry) where T : struct, IComponent => Component<T>(registry, ScheduleAccessMode.Read);
    public static ScheduleAccess Write<T>(ComponentRegistry registry) where T : struct, IComponent => Component<T>(registry, ScheduleAccessMode.Write);
    private static ScheduleAccess Component<T>(ComponentRegistry registry, ScheduleAccessMode mode) where T : struct, IComponent
    { ArgumentNullException.ThrowIfNull(registry); var d = registry.Describe(typeof(T)); return new(ScheduleAccessKind.Component, d.TypeId, d.Version, mode); }
    public static ScheduleAccess Resource(string id, ScheduleAccessMode mode) => new(ScheduleAccessKind.Resource, id, 0, mode);
    public static ScheduleAccess StructuralWrite() => new(ScheduleAccessKind.Structure, "*", 0, ScheduleAccessMode.Write);
}

// WorkerSafe applies to detached/synchronized host resources only, never to a live World.
public sealed record ScheduleResource(string Id, bool WorkerSafe = false);
public sealed record ScheduleTask(string Id, int Phase, ScheduleAffinity Affinity,
    ScheduleAccess[] Access, string[] Before, string[] After);
public sealed record ScheduleEdge(string From, string To, string Reason);
// Synthetic completion barrier between occupied phases; never a task/kernel or capability.
// '$' is excluded from caller task identifiers, so anchors cannot alias a user task.
public sealed record SchedulePhaseAnchor(string Id, int CompletedPhase, int NextPhase);
