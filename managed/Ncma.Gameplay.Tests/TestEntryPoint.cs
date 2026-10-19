// Keep startup in a BCL-only method. Missing/corrupt product assemblies can fail during JIT
// before a callee's try block; catch that boundary here instead of triggering an OS crash handler.
internal static class TestEntryPoint
{
    public static int Main(string[] args)
    {
        try
        {
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
