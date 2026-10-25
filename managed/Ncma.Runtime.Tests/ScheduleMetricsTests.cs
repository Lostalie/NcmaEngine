using System.Runtime.CompilerServices;
using Ncma.Runtime;

internal static class ScheduleMetricsTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Schedule metrics assertion"); }
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("E6-C task reports distinguish logical bytes and actual owner/worker callback allocations", () => {
            foreach (int workers in new[] { 0, 2 }) {
                var world = new World(); world.Set(world.CreateEntity("row"), TransformData.Identity);
                var runner = new WorldRunner(world); byte[] payload = [1, 2, 3, 4]; byte[]? allocated = null;
                var write = ScheduleAccess.Resource("r", ScheduleAccessMode.Write);
                var graph = ScheduleGraph.Compile(world.Components, [
                    new("noop", 0, ScheduleAffinity.Worker, [], [], []),
                    new("allocate", 0, ScheduleAffinity.Worker, [], [], []),
                    new("io", 0, ScheduleAffinity.Worker, [write], [], []),
                    new("consume", 0, ScheduleAffinity.Worker, [write with { Mode = ScheduleAccessMode.Read }], [], ["io"]),
                    new("typed", 0, ScheduleAffinity.Worker, [ScheduleAccess.Write<TransformData>(world.Components)], [], [])
                ], [new ScheduleResource("r", WorkerSafe: true)]);
                using var executor = new ScheduleExecutor(world, graph, [
                    new("noop", _ => { }),
                    new("allocate", _ => allocated = new byte[32768]),
                    new("io", c => {
                        Span<byte> destination = stackalloc byte[4];
                        c.ReadResource("r", destination); GC.KeepAlive(c.ReadResource("r"));
                        c.WriteResource("r", payload); c.WriteResource("r", payload);
                    }),
                    new("consume", c => { Span<byte> destination = stackalloc byte[4]; c.ReadOutput("io", "r", destination); }),
                    new("typed", c => { var value = c.Read<TransformData>(0); c.Write(0, value); c.Write(0, value); })
                ], [ScheduleComponent.Of<TransformData>()], new Dictionary<string, byte[]> { ["r"] = payload }, workers, TimeSpan.FromSeconds(5));
                runner.ConfigureSchedule(executor);
                try {
                    for (int i = 0; i < 8; ++i) runner.Advance(1d / 60);
                    var report = executor.Inspect(); Check(report.State == "committed" && report.Tasks.Length == 5);
                    var rows = report.Tasks.ToDictionary(r => r.TaskId);
                    Check(rows.Values.All(r => r.State == "computed" && r.ElapsedTimestampTicks >= 0 && r.Bytes >= 0 && r.AllocatedBytes >= 0));
                    Check(rows["noop"].Bytes == 0 && rows["noop"].AllocatedBytes == 0);
                    Check(rows["allocate"].Bytes == 0 && rows["allocate"].AllocatedBytes >= 32768 && allocated?.Length == 32768);
                    Check(rows["io"].Bytes == 16 && rows["io"].AllocatedBytes > 0);
                    Check(rows["consume"].Bytes == 4 && rows["consume"].AllocatedBytes == 0);
                    Check(rows["typed"].Bytes == Unsafe.SizeOf<TransformData>() + sizeof(int) && rows["typed"].AllocatedBytes == 0);
                } finally { runner.ClearSchedule(); runner.WaitForScheduleResources(TimeSpan.FromSeconds(4)); }
            }
        });
        yield return ("E6-C failed task keeps measured metrics and skipped tasks are not false zero reports", () => {
            var world = new World(); var runner = new WorldRunner(world); byte[]? allocated = null;
            var write = ScheduleAccess.Resource("r", ScheduleAccessMode.Write);
            var graph = ScheduleGraph.Compile(world.Components, [
                new("fail", 0, ScheduleAffinity.OwnerThread, [write], [], []),
                new("skip", 1, ScheduleAffinity.OwnerThread, [], [], [])
            ], [new("r")]);
            using var executor = new ScheduleExecutor(world, graph, [
                new("fail", c => { allocated = new byte[4096]; c.WriteResource("r", new byte[3]); throw new InvalidOperationException("expected metrics failure"); }),
                new("skip", _ => throw new Exception("Skipped task executed"))
            ], [], new Dictionary<string, byte[]> { ["r"] = [7] });
            runner.ConfigureSchedule(executor);
            try {
                bool rejected = false;
                try { runner.Advance(1d / 60); } catch (InvalidOperationException) { rejected = true; }
                var report = executor.Inspect(); Check(rejected && report.State == "failed" && report.Tasks.Length == 1 && world.Tick == 0);
                var row = report.Tasks[0]; Check(row.TaskId == "fail" && row.State == "failed" && row.Bytes == 3 && row.AllocatedBytes >= 4096 && allocated?.Length == 4096);
                Check(executor.ReadResource("r")[0] == 7);
            } finally { runner.ClearSchedule(); }
        });
    }
}
