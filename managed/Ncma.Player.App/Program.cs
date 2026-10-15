namespace Ncma.Player.App;
internal static class Program
{
    // The standalone process retains a failed shutdown until process exit. Embedded
    // hosts own their own PlayerRunner and may explicitly retry; never force unload.
    private static PlayerRunner? _execution;
    public static int Main(string[] args)
    {
        PlayerOptions options;
        try { options = PlayerOptions.Parse(args); }
        catch (Exception e) when (e is ArgumentException or IOException or NotSupportedException)
        { Console.Error.WriteLine("invalid_arguments"); return 2; }
        if (options.Help) { Console.WriteLine("NcmaPlayer --project <file.ncmaproject> [--headless --ticks N] [--renderer d3d11|null|vulkan] [--fixed-delta h] [--max-runtime-seconds N] [--report file.json]"); return 0; }
        if (options.Version) { Console.WriteLine("NcmaPlayer M2.7 / report v1 / scene JSON v1"); return 0; }
        int cancelled = 0;
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; Interlocked.Exchange(ref cancelled, 1); };
        Console.CancelKeyPress += cancel;
        try
        {
            string manifest = Path.Combine(AppContext.BaseDirectory, Ncma.Application.DeploymentManifest.FileName);
            if (File.Exists(manifest))
            {
                try { Ncma.Application.DeploymentManifest.Validate(AppContext.BaseDirectory); }
                catch { Console.Error.WriteLine("deployment_manifest_failed"); return 3; }
            }
            if(_execution is not null){Console.Error.WriteLine("player_shutdown_retained");return 6;}
            _execution=new PlayerRunner();
            // Bounded/headless runs never prompt. Interactive standalone Player uses the
            // same default-No, renderer-independent local confirmation as the Editor.
            var confirmation=new Ncma.Platform.WindowsConfirmation();
            var result = _execution.Run(options, () => Volatile.Read(ref cancelled) != 0,
                confirmGraphicsRecovery:options.Headless||options.Ticks is not null?null:
                    request=>Volatile.Read(ref cancelled)==0&&confirmation.Confirm(request.Message,"NcmaPlayer 图形恢复"));
            Console.WriteLine($"player_result exit={result.ExitCode} tick={result.Tick} state={result.State} reason={result.Reason}");
            if (result.ExitCode != 0) Console.Error.WriteLine(result.Reason);
            if(_execution.Status.Phase==PlayerRunnerPhase.Idle){_execution.Dispose();_execution=null;}
            return result.ExitCode;
        }
        finally { Console.CancelKeyPress -= cancel; }
    }
}
