global using Ncma.Testing;

namespace Ncma.Testing;

// Linked into test executables only. Catch at independent case boundaries, never at
// assertions or dependent steps within a case. using/finally cleanup still unwinds.
internal sealed class CaseHarness(string name, TextWriter? output = null, TextWriter? errors = null)
{
    private readonly TextWriter _output = output ?? Console.Out, _errors = errors ?? Console.Error;
    private readonly List<(string Name, Exception Error)> _failures = [];
    private int _total, _passed;
    internal int Total => _total;
    internal int Passed => _passed;
    internal int Failed => _failures.Count;

    internal void Case(string caseName, Action run)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseName); ArgumentNullException.ThrowIfNull(run);
        ++_total;
        _output.WriteLine($"BEGIN {name} / {caseName}"); _output.Flush();
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        try { run(); ++_passed; _output.WriteLine($"PASS {name} / {caseName}"); }
        catch (Exception error) {
            _failures.Add((caseName, error));
            _errors.WriteLine($"FAIL {name} / {caseName}: {error}"); _errors.Flush();
        }
        finally {
            _output.WriteLine($"END {name} / {caseName}: {System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds:F2} ms");
            _output.Flush();
        }
    }

    internal void Cases(IEnumerable<(string Name, Action Run)> cases)
    {
        // Registration/enumerator failure is infrastructure failure, not a skipped pass.
        int before = _total;
        try { foreach (var item in cases) Case(item.Name, item.Run); }
        catch (Exception error) { Case("case registration", () => throw new InvalidOperationException("Cannot enumerate remaining cases.", error)); }
        if (_total == before) Case("empty selection", () => throw new InvalidOperationException("No cases selected."));
    }

    internal int Finish()
    {
        _output.WriteLine($"SUMMARY {name}: total={_total}, passed={_passed}, failed={Failed}");
        foreach (var failure in _failures) _errors.WriteLine($"FAILED CASE {name} / {failure.Name} ({failure.Error.GetType().FullName})");
        _output.Flush(); _errors.Flush();
        return Failed == 0 && _total > 0 ? 0 : 1;
    }

    internal static int Run(string name, IEnumerable<(string Name, Action Run)> cases)
    { var harness = new CaseHarness(name); harness.Cases(cases); return harness.Finish(); }

    internal static void RunOrThrow(string name, IEnumerable<(string Name, Action Run)> cases)
    {
        var harness = new CaseHarness(name); harness.Cases(cases);
        if (harness.Finish() != 0) throw new AggregateException(name + " failed after all cases ran.",
            harness._failures.Select(f => new InvalidOperationException(f.Name, f.Error)));
    }

    internal static void VerifyAggregation()
    {
        using var output = new StringWriter(); using var errors = new StringWriter();
        var harness = new CaseHarness("injected failures", output, errors); var reached = new List<int>(); int cleaned = 0;
        harness.Cases([
            ("first fails", () => { try { reached.Add(1); throw new InvalidOperationException("first"); } finally { cleaned++; } }),
            ("middle passes", () => reached.Add(2)),
            ("second fails", () => { reached.Add(3); throw new ArgumentException("second"); }),
            ("last still runs", () => reached.Add(4))
        ]);
        if (harness.Finish() != 1 || harness.Total != 4 || harness.Passed != 2 || harness.Failed != 2 ||
            cleaned != 1 || !reached.SequenceEqual(new[] { 1, 2, 3, 4 }) ||
            !errors.ToString().Contains("first fails") || !errors.ToString().Contains("second fails"))
            throw new Exception("Harness failed to execute every case, preserve cleanup or return failure.");
        var empty = new CaseHarness("empty", output, errors); empty.Cases([]);
        if (empty.Finish() != 1) throw new Exception("Empty suite falsely passed.");
    }
}
