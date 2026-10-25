using System.Collections.Frozen;
using Ncma.Runtime;

internal static class ScheduleBudgetTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Schedule budget assertion"); }
    private static void RejectReservation(long reserved)
    {
        try { _ = new ScheduleBudget(reserved); }
        catch (ArgumentOutOfRangeException) { return; }
        throw new Exception("Invalid initial reservation accepted");
    }
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("E6-C failed budget charges leave exact remaining capacity unchanged", () => {
            RejectReservation(-1); RejectReservation(long.MinValue);
            RejectReservation((long)ScheduleExecutor.MaxFrameBytes + 1); RejectReservation(long.MaxValue);
            var budget = new ScheduleBudget(ScheduleExecutor.MaxFrameBytes - 8);
            for (int i = 0; i < 4096; ++i) Check(!budget.Charge(9) && !budget.Charge(int.MaxValue));
            Check(!budget.Charge(-1) && !budget.Charge(int.MinValue) && budget.Charge(0));
            Check(budget.Charge(3) && !budget.Charge(6) && budget.Charge(5));
            Check(!budget.Charge(1) && !budget.Charge(-1) && budget.Charge(0) && !budget.Charge(1));
            var empty = new ScheduleBudget(0);
            Check(empty.Charge(ScheduleExecutor.MaxFrameBytes) && !empty.Charge(1));
        });
        yield return ("E6-C concurrent accepted charges sum to capacity despite repeated rejected charges", () => {
            var budget = new ScheduleBudget(ScheduleExecutor.MaxFrameBytes - 8192);
            int accepted = 0, invalidAccepted = 0;
            Parallel.For(0, 4096, new ParallelOptions { MaxDegreeOfParallelism = 4 }, _ => {
                if (budget.Charge(int.MaxValue)) Interlocked.Increment(ref invalidAccepted);
                if (budget.Charge(-1)) Interlocked.Increment(ref invalidAccepted);
                if (budget.Charge(16)) Interlocked.Increment(ref accepted);
            });
            Check(invalidAccepted == 0 && accepted == 512 && budget.Charge(0) && !budget.Charge(1));
        });
        yield return ("E6-C shared budget rejection preserves other contexts quota but remains sticky for rejected context", () => {
            foreach (bool output in new[] { false, true }) {
                var budget = new ScheduleBudget(ScheduleExecutor.MaxFrameBytes - 8);
                ScheduleContext Context(string id, int size) {
                    var access = ScheduleAccess.Resource("r", output ? ScheduleAccessMode.Write : ScheduleAccessMode.Read);
                    var task = new ScheduleTask(id, 0, ScheduleAffinity.OwnerThread, [access], [], []);
                    var buffer = new ScheduleCompletionBuffer(FrozenDictionary<string, int>.Empty);
                    return new(task, new(), new() { ["r"] = new byte[size] }, buffer.View, 0, new(), 1d / 60, 1,
                        CancellationToken.None, budget, new());
                }
                var failed = Context("failed", 9); bool rejected = false, sticky = false;
                var destination = Enumerable.Repeat((byte)99, 9).ToArray();
                failed.Invoke(c => {
                    try { if (output) c.WriteResource("r", destination); else c.ReadResource("r", destination.AsSpan()); }
                    catch (InvalidOperationException) { rejected = true; }
                    try { c.CheckCancellation(); } catch (InvalidOperationException) { sticky = true; }
                });
                Check(rejected && sticky && failed.Error is InvalidOperationException && failed.ResourceWrites.Count == 0 && destination.All(b => b == 99));
                var valid = Context("valid", 8); bool completed = false;
                valid.Invoke(c => {
                    if (output) c.WriteResource("r", new byte[8]); else Check(c.ReadResource("r", destination.AsSpan()) == 8);
                    completed = true;
                });
                Check(completed && valid.CompletedSuccessfully && valid.Error is null && !budget.Charge(1));
                Check(failed.Error is InvalidOperationException); // No failed-task recovery or partial graph commit.
            }
        });
    }
}
