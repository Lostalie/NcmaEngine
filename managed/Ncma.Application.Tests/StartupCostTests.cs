using Ncma.Application;

internal static class StartupCostTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool condition) { checks++; if (!condition) throw new Exception("Startup costs assertion " + checks); }
        void Reject(Action action) {
            bool rejected = false;
            try { action(); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected);
        }
        var capture = new StartupCostCapture();
        Check(capture.ActiveCall is null && capture.Capture().Calls.Length == 0);
        Reject(() => capture.End(true)); Reject(() => capture.Phase(StartupStage.Window));
        Reject(() => capture.Begin((StartupCall)99));
        capture.Begin(StartupCall.MainPreparation);
        Reject(() => capture.Begin(StartupCall.PresentationStart)); Reject(() => capture.Capture());
        Reject(() => capture.Phase(StartupStage.GuiCreate)); Reject(() => capture.Phase((StartupStage)99));
        capture.Phase(StartupStage.PackageValidation); capture.Phase(StartupStage.Arguments);
        capture.Phase(StartupStage.Arguments);
        capture.End(true);
        Reject(() => capture.Begin(StartupCall.MainPreparation));
        capture.Begin(StartupCall.PresentationStart);
        Reject(() => capture.Phase(StartupStage.PackageValidation));
        capture.Phase(StartupStage.GuiCreate);
        try { throw new IOException("synthetic startup failure"); }
        catch (IOException) { }
        finally { capture.End(false); }
        Reject(() => capture.Begin(StartupCall.PresentationStart)); Reject(() => capture.End(true));
        var report = capture.Capture();
        Check(report.Version == 1 && report.TimestampFrequency > 0 && !report.PerformanceAccepted && report.Calls.Length == 2);
        Check(report.Calls[0].Completed && !report.Calls[1].Completed);
        Check(report.Calls[0].Stages[(int)StartupStage.Arguments].Visits == 2);
        Check(report.Calls[0].EndedTimestamp <= report.Calls[1].StartedTimestamp);
        foreach (var call in report.Calls) {
            Check(call.Stages.Sum(s => s.WallTicks) == call.EndedTimestamp - call.StartedTimestamp);
            Check(call.Stages.Sum(s => s.OwnerAllocatedBytes) == call.OwnerAllocatedBytes);
            Check(call.Stages.Select(s => (int)s.Stage).SequenceEqual(Enumerable.Range(0, 24)));
            Check(call.Stages.Where(s => s.Visits == 0).All(s => s.WallTicks == 0 && s.OwnerAllocatedBytes == 0));
        }
        report.Calls[0].Stages[0] = default; report.Calls[0] = report.Calls[1];
        Check(capture.Capture().Calls[0].Call == StartupCall.MainPreparation && capture.Capture().Calls[0].Stages[0].Visits == 1);
        var serviceOnly = new StartupCostCapture();
        serviceOnly.Begin(StartupCall.PresentationStart); serviceOnly.End(true);
        Check(serviceOnly.Capture().Calls is [{ Call: StartupCall.PresentationStart, Completed: true }]);
        Reject(() => serviceOnly.Begin(StartupCall.MainPreparation));
        Check(Task.Run(() => { try { capture.Capture(); return false; } catch (InvalidOperationException) { return true; } }).Result);
        Check(Task.Run(() => { try { serviceOnly.Begin(StartupCall.MainPreparation); return false; } catch (InvalidOperationException) { return true; } }).Result);
        Check(Task.Run(() => { try { _ = capture.ActiveCall; return false; } catch (InvalidOperationException) { return true; } }).Result);
        Console.WriteLine($"PASS E5.D startup costs {checks} checks");
    }
}
