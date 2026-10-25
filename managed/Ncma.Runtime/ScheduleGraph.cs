using System.Security.Cryptography;
using System.Text.Json;

namespace Ncma.Runtime;

// C1 is a cold immutable plan only. It executes no callback, grants no World access and
// does not imply that ordinary IWorldSystem/Behaviour code has declared or enforced access.
public sealed class ScheduleGraph
{
    public const int ContractVersion = 3, MaxTasks = 128, MaxAccessPerTask = 64,
        MaxLinksPerTask = 64, MaxResources = 128, MaxPhases = 16, MaxEdges = 8192;
    private readonly ScheduleTask[] _tasks;
    private readonly ScheduleEdge[] _edges;
    private readonly string[] _order;
    private readonly ScheduleResource[] _resources;
    private readonly SchedulePhaseAnchor[] _anchors;
    private readonly string[] _executionOrder;
    public string ContentHash { get; }
    public int TaskCount => _tasks.Length;
    public int EdgeCount => _edges.Length;
    public int PhaseAnchorCount => _anchors.Length;

    private ScheduleGraph(ScheduleTask[] tasks, ScheduleResource[] resources, ScheduleEdge[] edges,
        SchedulePhaseAnchor[] anchors, string[] executionOrder)
    {
        _tasks = tasks; _edges = edges; _resources = resources; _anchors = anchors; _executionOrder = executionOrder;
        var taskIds = tasks.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        _order = executionOrder.Where(taskIds.Contains).ToArray();
        ContentHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new { Version = ContractVersion, tasks, resources, edges, anchors, executionOrder })));
    }
    public ScheduleTask[] CopyTasks() => _tasks.Select(Copy).ToArray();
    public ScheduleEdge[] CopyEdges() => (ScheduleEdge[])_edges.Clone();
    public string[] CopySerialOrder() => (string[])_order.Clone();
    public ScheduleResource[] CopyResources() => (ScheduleResource[])_resources.Clone();
    public SchedulePhaseAnchor[] CopyPhaseAnchors() => (SchedulePhaseAnchor[])_anchors.Clone();
    internal string[] CopyExecutionOrder() => (string[])_executionOrder.Clone();
    private static ScheduleTask Copy(ScheduleTask task) => task with
        { Access = (ScheduleAccess[])task.Access.Clone(), Before = (string[])task.Before.Clone(), After = (string[])task.After.Clone() };

    public static ScheduleGraph Compile(ComponentRegistry registry, IEnumerable<ScheduleTask> tasks,
        IEnumerable<ScheduleResource>? resources = null)
    {
        ArgumentNullException.ThrowIfNull(registry); ArgumentNullException.ThrowIfNull(tasks);
        // Bounded enumeration before any graph allocation. Later caller mutations cannot alias it.
        var supplied = tasks.Take(MaxTasks + 1).ToArray();
        if (supplied.Length > MaxTasks) throw new ArgumentException("Schedule task budget exceeded.");
        var resourceInput = (resources ?? []).Take(MaxResources + 1).ToArray();
        if (resourceInput.Length > MaxResources) throw new ArgumentException("Schedule resource budget exceeded.");
        var knownResources = new Dictionary<string, ScheduleResource>(StringComparer.Ordinal);
        foreach (var resource in resourceInput)
        {
            if (resource is null) throw new ArgumentException("Null schedule resource.");
            Identifier(resource.Id);
            if (!knownResources.TryAdd(resource.Id, resource)) throw new ArgumentException("Duplicate schedule resource: " + resource.Id);
        }
        var components = registry.Describe().ToDictionary(d => d.TypeId, StringComparer.Ordinal);
        var owned = new Dictionary<string, ScheduleTask>(StringComparer.Ordinal);
        foreach (var task in supplied)
        {
            if (task is null) throw new ArgumentException("Null schedule task.");
            Identifier(task.Id);
            if (task.Phase is < 0 or >= MaxPhases || !Enum.IsDefined(task.Affinity) || task.Access is null ||
                task.Before is null || task.After is null || task.Access.Length > MaxAccessPerTask ||
                task.Before.Length > MaxLinksPerTask || task.After.Length > MaxLinksPerTask ||
                task.Before.Length + task.After.Length > MaxLinksPerTask)
                throw new ArgumentException("Invalid schedule task or declaration budget: " + task.Id);
            var copy = Copy(task);
            var seen = new HashSet<(ScheduleAccessKind, string)>();
            foreach (var access in copy.Access)
            {
                if (access is null || !Enum.IsDefined(access.Kind) || !Enum.IsDefined(access.Mode))
                    throw new ArgumentException("Invalid schedule access.");
                if (!seen.Add((access.Kind, access.Id))) throw new ArgumentException("Duplicate access; declare Write once for read/write.");
                switch (access.Kind)
                {
                    case ScheduleAccessKind.Component:
                        if (access.Id is null || !components.TryGetValue(access.Id, out var descriptor) || descriptor.Version != access.Version)
                            throw new ArgumentException("Unknown component or schema version: " + access.Id);
                        break;
                    case ScheduleAccessKind.Resource:
                        if (access.Id is null || access.Version != 0 || !knownResources.TryGetValue(access.Id, out var resource))
                            throw new ArgumentException("Unknown external schedule resource: " + access.Id);
                        if (task.Affinity == ScheduleAffinity.Worker && !resource.WorkerSafe)
                            throw new ArgumentException("Owner-thread resource requested by worker: " + access.Id);
                        break;
                    case ScheduleAccessKind.Structure:
                        if (access.Id != "*" || access.Version != 0 || access.Mode != ScheduleAccessMode.Write || task.Affinity != ScheduleAffinity.OwnerThread)
                            throw new ArgumentException("Structural commands require owner-thread structural Write.");
                        break;
                }
            }
            if (copy.Before.Concat(copy.After).Distinct(StringComparer.Ordinal).Count() != copy.Before.Length + copy.After.Length)
                throw new ArgumentException("Duplicate or contradictory explicit dependency.");
            copy = copy with { Access = copy.Access.OrderBy(a => a.Kind).ThenBy(a => a.Id, StringComparer.Ordinal).ToArray(),
                Before = copy.Before.Order(StringComparer.Ordinal).ToArray(), After = copy.After.Order(StringComparer.Ordinal).ToArray() };
            if (!owned.TryAdd(copy.Id, copy)) throw new ArgumentException("Duplicate schedule task: " + copy.Id);
        }
        var nodes = owned.Values.OrderBy(t => t.Id, StringComparer.Ordinal).ToArray();
        // Typed columns are attempt-start snapshots, not phase-local publications.
        // Write also grants Read, so later Write declarations must not bypass this rule.
        var firstWriters = new Dictionary<string, ScheduleTask>(StringComparer.Ordinal);
        foreach (var task in nodes)
            foreach (var access in task.Access)
                if (access.Kind == ScheduleAccessKind.Component && access.Mode == ScheduleAccessMode.Write &&
                    (!firstWriters.TryGetValue(access.Id, out var first) || task.Phase < first.Phase))
                    firstWriters[access.Id] = task;
        foreach (var task in nodes)
            foreach (var access in task.Access)
                if (access.Kind == ScheduleAccessKind.Component &&
                    firstWriters.TryGetValue(access.Id, out var writer) && writer.Phase < task.Phase)
                    throw new ArgumentException(
                        $"cross_phase_component_read_requires_resource: use an explicit Resource channel + ReadOutput. Component={access.Id}; Writer={writer.Id}@phase{writer.Phase}; Reader={task.Id}@phase{task.Phase} (Write includes Read).");
        var indices = nodes.Select((t, i) => (t.Id, i)).ToDictionary(x => x.Id, x => x.i, StringComparer.Ordinal);
        var phases = nodes.Select(t => t.Phase).Distinct().Order().ToArray();
        var anchors = Enumerable.Range(0, Math.Max(0, phases.Length - 1)).Select(i =>
            new SchedulePhaseAnchor("$phase:" + phases[i].ToString(System.Globalization.CultureInfo.InvariantCulture), phases[i], phases[i + 1])).ToArray();
        var names = nodes.Select(t => t.Id).Concat(anchors.Select(a => a.Id)).ToArray();
        var edges = new Dictionary<(int From, int To), string>();
        void Edge(int from, int to, string reason)
        {
            if (from == to) throw new ArgumentException("Self dependency: " + names[from]);
            if (edges.TryAdd((from, to), reason) && edges.Count > MaxEdges) throw new ArgumentException("Schedule edge budget exceeded.");
        }
        int Target(string id)
        { Identifier(id); return indices.TryGetValue(id, out int index) ? index : throw new ArgumentException("Unknown dependency task: " + id); }
        void ExplicitEdge(int from, int to, string reason)
        {
            if (nodes[from].Phase > nodes[to].Phase) throw new ArgumentException("Explicit dependency contradicts phase order: " + nodes[from].Id + " -> " + nodes[to].Id);
            // Forward cross-phase declarations are retained in CopyTasks/hash, but the barrier
            // already enforces them. Do not recreate a complete bipartite graph here.
            if (nodes[from].Phase == nodes[to].Phase) Edge(from, to, reason);
        }
        for (int i = 0; i < nodes.Length; ++i)
        {
            foreach (string target in nodes[i].Before) ExplicitEdge(i, Target(target), "explicit-before");
            foreach (string target in nodes[i].After) ExplicitEdge(Target(target), i, "explicit-after");
            int phase = Array.BinarySearch(phases, nodes[i].Phase);
            if (phase > 0) Edge(nodes.Length + phase - 1, i, "phase-enter");
            if (phase < anchors.Length) Edge(i, nodes.Length + phase, "phase-complete");
        }
        for (int i = 1; i < anchors.Length; ++i) Edge(nodes.Length + i - 1, nodes.Length + i, "phase-chain");
        // Resolve explicit order first. Conflicts follow this deterministic total order, so
        // an explicit reverse ordering is respected instead of creating an artificial cycle.
        var explicitOrder = Sort(names, edges).Where(i => i < nodes.Length).ToArray();
        for (int i = 0; i < explicitOrder.Length; ++i)
            for (int j = i + 1; j < explicitOrder.Length; ++j)
            {
                int from = explicitOrder[i], to = explicitOrder[j];
                if (nodes[from].Phase != nodes[to].Phase) continue; // Already ordered through anchors.
                if (Conflict(nodes[from], nodes[to]) is { } reason) Edge(from, to, reason);
            }
        var order = Sort(names, edges).Select(i => names[i]).ToArray();
        var resultEdges = edges.OrderBy(e => e.Key.From).ThenBy(e => e.Key.To)
            .Select(e => new ScheduleEdge(names[e.Key.From], names[e.Key.To], e.Value)).ToArray();
        return new(nodes, knownResources.Values.OrderBy(r => r.Id, StringComparer.Ordinal).ToArray(), resultEdges, anchors, order);
    }

    private static string? Conflict(ScheduleTask a, ScheduleTask b)
    {
        // Structural publication can invalidate entity/query membership: conservatively order
        // against every task, including tasks which declared only external state.
        if (a.Access.Any(x => x.Kind == ScheduleAccessKind.Structure) || b.Access.Any(x => x.Kind == ScheduleAccessKind.Structure)) return "structure";
        foreach (var left in a.Access)
            foreach (var right in b.Access)
                if (left.Kind == right.Kind && left.Id == right.Id &&
                    (left.Mode == ScheduleAccessMode.Write || right.Mode == ScheduleAccessMode.Write))
                    return (left.Kind == ScheduleAccessKind.Component ? "component:" : "resource:") + left.Id;
        return null;
    }
    private static int[] Sort(string[] nodes, Dictionary<(int From, int To), string> edges)
    {
        var degree = new int[nodes.Length]; foreach (var edge in edges.Keys) degree[edge.To]++;
        var result = new int[nodes.Length]; var emitted = new bool[nodes.Length];
        for (int n = 0; n < nodes.Length; ++n)
        {
            int next = -1;
            // Tasks are ordinal, anchors are phase ordered; neither depends on registration order.
            for (int i = 0; i < nodes.Length; ++i) if (!emitted[i] && degree[i] == 0) { next = i; break; }
            if (next < 0) throw new ArgumentException("Schedule dependency cycle: " + string.Join(",", nodes.Where((_, i) => !emitted[i])));
            emitted[next] = true; result[n] = next;
            foreach (var edge in edges.Keys) if (edge.From == next) degree[edge.To]--;
        }
        return result;
    }
    private static void Identifier(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 128 || value.Any(c =>
                !(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-')))
            throw new ArgumentException("Schedule identifiers require 1..128 stable ASCII characters.");
    }
}
