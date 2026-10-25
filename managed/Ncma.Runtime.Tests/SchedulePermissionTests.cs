using System.Runtime.CompilerServices;
using System.Collections.Frozen;
using Ncma.Runtime;

internal static class SchedulePermissionTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Schedule permission assertion"); }
    private static void Reject(Action action)
    { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Undeclared access accepted"); }
    private static World Scene()
    { var world = new World(); world.Set(world.CreateEntity("row"), TransformData.Identity); return world; }
    private static ScheduleContext Context(World world, ScheduleTask task, Dictionary<string, byte[]>? resources = null)
    {
        var capture = ScheduleComponent.Of<TransformData>().Bind(world, new()); capture.Capture(0); var column = capture.Buffer(0);
        var writers = new Dictionary<Type,IScheduleWrites>();
        if (task.Access.Any(a=>a.Kind==ScheduleAccessKind.Component && a.Id==column.Descriptor.TypeId && a.Mode==ScheduleAccessMode.Write)) writers.Add(typeof(TransformData),column.CreateWrites());
        return new(task, new() { [typeof(TransformData)] = column }, resources ?? new(),
            new ScheduleCompletionBuffer(FrozenDictionary<string,int>.Empty).View, 0, new(StringComparer.Ordinal), 1d / 60, 1, CancellationToken.None, new(0), writers);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Reads(ScheduleContext context, int count)
    {
        int checksum = 0;
        for (int i = 0; i < count; i++) {
            checksum ^= context.ObjectAt<TransformData>(0).GetHashCode();
            checksum += context.Count<TransformData>() + (int)context.Read<TransformData>(0).Scale.X;
        }
        return checksum;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ReadWrites(ScheduleContext context, int count)
    {
        for (int i = 0; i < count; i++) {
            var value = context.Read<TransformData>(0);
            context.Write(0, value with { Position = System.Numerics.Vector3.UnitX });
        }
    }
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("E6-C constructor expands copied permissions; Write includes Read", () => {
            var world = Scene(); var access = ScheduleAccess.Write<TransformData>(world.Components);
            var task = new ScheduleTask("task", 0, ScheduleAffinity.OwnerThread, [access], [], []);
            var context = Context(world, task);
            task.Access[0] = ScheduleAccess.Resource("unrelated", ScheduleAccessMode.Read);
            context.Invoke(c => { Check(c.Read<TransformData>(0) == TransformData.Identity); c.Write(0, TransformData.Identity); });
            Check(context.Error is null && context.Writes.ContainsKey(typeof(TransformData)));
            Check(world.Tick == 0 && world.Get<TransformData>(world.GetEntities()[0]) == TransformData.Identity);
        });
        yield return ("E6-C permissions distinguish component/resource IDs and preserve sticky denials", () => {
            foreach (bool componentRead in new[] { false, true }) {
                var world = Scene(); var read = ScheduleAccess.Read<TransformData>(world.Components);
                var accesses = new List<ScheduleAccess> { ScheduleAccess.Resource(read.Id, ScheduleAccessMode.Write) };
                if (componentRead) accesses.Add(read);
                var task = new ScheduleTask("task", 0, ScheduleAffinity.OwnerThread, accesses.ToArray(), [], []);
                var context = Context(world, task, new() { [read.Id] = [1] });
                context.Invoke(c => {
                    c.WriteResource(read.Id, new byte[] { 2 });
                    if (componentRead) { Check(c.Read<TransformData>(0) == TransformData.Identity); Reject(() => c.Write(0, TransformData.Identity)); }
                    else Reject(() => c.Read<TransformData>(0));
                });
                Check(context.Error is not null && !context.Writes.ContainsKey(typeof(TransformData)));
            }
        });
        yield return ("E6-C warmed typed Read/Write permission lookup loops allocate exactly 0B", () => {
            foreach (int workers in new[] { 0, 2 }) {
                var world = Scene(); var runner = new WorldRunner(world); long reads = -1, writes = -1; int sink = 0;
                var graph = ScheduleGraph.Compile(world.Components,
                    [new("task", 0, ScheduleAffinity.Worker, [ScheduleAccess.Write<TransformData>(world.Components)], [], [])]);
                using var executor = new ScheduleExecutor(world, graph, [new("task", c => {
                    // Construction, capture, callback binding, JIT warmup, merge, validation and
                    // commit are deliberately OUTSIDE this narrow hot-access allocation oracle.
                    _ = Reads(c, 1024); ReadWrites(c, 1024);
                    _ = GC.GetAllocatedBytesForCurrentThread();
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    sink = Reads(c, 4096);
                    reads = GC.GetAllocatedBytesForCurrentThread() - before;
                    before = GC.GetAllocatedBytesForCurrentThread();
                    ReadWrites(c, 4096);
                    writes = GC.GetAllocatedBytesForCurrentThread() - before;
                })], [ScheduleComponent.Of<TransformData>()], workerCount: workers, timeout: TimeSpan.FromSeconds(5));
                runner.ConfigureSchedule(executor);
                try {
                    runner.Advance(1d / 60);
                    Check(reads == 0 && writes == 0 && world.Tick == 1);
                    Check(world.Get<TransformData>(world.GetEntities()[0]).Position.X == 1);
                    GC.KeepAlive(sink);
                }
                finally { runner.ClearSchedule(); }
            }
        });
    }
}
