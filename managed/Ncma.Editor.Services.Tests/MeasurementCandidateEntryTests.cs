using CandidateProgram = Ncma.Editor.App.Program;

internal static partial class MeasurementCandidateEntryTests
{
    private static void Check(bool value){if(!value)throw new Exception("Candidate entry assertion");}
    internal static IEnumerable<(string,Action)> Cases()
    {
        foreach (var test in SceneCases()) yield return test;
        yield return("M8.7-E2E3 candidate command closed argument shape no implicit project flags",()=>{
            Check(CandidateProgram.CpuMeasurementArguments(["--measure-cpu-idle","request.json",new('A',64),"run.ncobserve"]));
            foreach(string[] args in new[]{Array.Empty<string>(),new[]{"--measure-cpu-idle"},new[]{"--measure-cpu-idle","r",new string('a',64),"o"},new[]{"--measure-cpu-idle","r",new string('A',64),"o","--project"}})Check(!CandidateProgram.CpuMeasurementArguments(args));
        });
        yield return("M8.7-E2E3 actual main malformed measurement branch rejects before IO startup confirmation",()=>{
            Check(CandidateProgram.Main(["--measure-cpu-idle"])==2);
            Check(CandidateProgram.Main(["--measure-cpu-idle","MISSING","not-a-hash","output"])==2);
        });
    }
}
