// Keep startup in a BCL-only method. Missing/corrupt product assemblies can fail during JIT
// before a callee's try block; catch that boundary here instead of triggering an OS crash handler.
internal static class TestEntryPoint
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Any(a => a.StartsWith("--ecs-e5-", StringComparison.Ordinal)))
            {
                if (args.Length != 1 || args[0] is not ("--ecs-e5-smoke" or "--ecs-e5-measure" or "--ecs-e5-diagnose" or "--ecs-e5-diagnose-smoke"))
                    throw new ArgumentException("E5 entry requires exactly one known mode.");
                return EcsPairedEntry.Run(args[0] is "--ecs-e5-measure" or "--ecs-e5-diagnose", e5: true,
                    diagnostics: args[0].StartsWith("--ecs-e5-diagnose", StringComparison.Ordinal));
            }
            if (args.Contains("--ecs-paired-smoke") || args.Contains("--ecs-paired-measure"))
            {
                if (args.Length != 1) { Console.Error.WriteLine("Paired entry requires exactly one mode argument."); return 1; }
                return EcsPairedEntry.Run(args[0] == "--ecs-paired-measure");
            }
            return GameplayTestProgram.Run(args);
        }
        catch (Exception error) { Console.Error.WriteLine("Test entry rejected: " + error); return 1; }
    }
}
