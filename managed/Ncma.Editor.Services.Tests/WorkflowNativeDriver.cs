using System.Diagnostics;
using Ncma.Editor.Services;

// Only the original parent job/permit may schedule this fixed child entry.
// No independent approval, package installation, source reread or restore.
internal static class WorkflowNativeDriver
{
    internal static int Run(string requestPath)
    {
        requestPath = WorkflowBuildCommand.CommandPath(requestPath);
        using var pins = new WorkflowPhysicalPins();
        var hash = pins.Observe(requestPath, WorkflowNativeRequest.MaximumBytes);
        var request = WorkflowNativeRequest.Read(pins.ReadFile(requestPath, hash.Sha256, hash.Bytes, WorkflowNativeRequest.MaximumBytes));
        var plan = request.Plan;
        if (!string.Equals(requestPath, Path.Combine(plan.OutputRoot, "native-request.json"), StringComparison.OrdinalIgnoreCase))
            throw new IOException("workflow_native_request_path");
        pins.Directory(plan.NativeRoot); pins.Directory(plan.SourceRoot);
        if (Directory.EnumerateFileSystemEntries(plan.NativeRoot).Any()) throw new IOException("workflow_native_output_not_empty");
        using var tools = new WorkflowNativeToolchain(request.Selection);
        int? configure = null, build = null; bool cache = false; string diagnostic = "";
        try {
            tools.Prepare(); configure = Execute(plan, plan.ConfigureArguments());
            if (configure == 0) {
                VerifyCache(plan); build = Execute(plan, plan.BuildArguments());
                if (build == 0) { VerifyCache(plan); cache = true; }
            }
        } catch (Exception error) when (error is not OutOfMemoryException) { diagnostic = WorkflowObservedEvidence.Code(error); }
        var result = new WorkflowNativeResult(1, request.Task.Fingerprint, configure, build, cache, diagnostic);
        byte[] bytes = result.Encode();
        using (var file = new FileStream(Path.Combine(plan.OutputRoot, "native-result.json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough)) {
            WorkflowPhysicalPins.ValidateHandle(file.SafeFileHandle, false); file.Write(bytes); file.Flush(true);
        }
        return result.Complete ? 0 : 1;
    }
    private static void VerifyCache(WorkflowNativePlan plan)
    {
        // CMake may legitimately rewrite its own cache during regeneration.
        // Observe after each terminal phase; only final producer seal retains it.
        using var pins = new WorkflowPhysicalPins(); string path = Path.Combine(plan.NativeRoot, "CMakeCache.txt");
        var hash = pins.Observe(path, 4194304); plan.VerifyCache(pins.ReadFile(path, hash.Sha256, hash.Bytes, 4194304));
    }
    private static int Execute(WorkflowNativePlan plan, string[] arguments)
    {
        var start = new ProcessStartInfo(plan.CMake) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = plan.OutputRoot,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        start.Environment.Clear();
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        start.Environment["SystemRoot"] = windows; start.Environment["WINDIR"] = windows;
        start.Environment["COMSPEC"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        start.Environment["TEMP"] = Path.Combine(plan.OutputRoot, "temp"); start.Environment["TMP"] = start.Environment["TEMP"];
        foreach (var pair in plan.EnvironmentOverrides()) start.Environment[pair.Key] = pair.Value;
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new IOException("workflow_native_launch_failed");
        process.StandardInput.Close(); int lost = 0;
        Task Pump(Stream input, Stream output) => Task.Run(() => {
            byte[] buffer = new byte[8192]; int count; bool writable = true;
            while ((count = input.Read(buffer, 0, buffer.Length)) != 0) {
                if (!writable) continue;
                try { output.Write(buffer, 0, count); output.Flush(); }
                catch (IOException) { writable = false; Interlocked.Exchange(ref lost, 1); }
            }
        });
        Task stdout = Pump(process.StandardOutput.BaseStream, Console.OpenStandardOutput()), stderr = Pump(process.StandardError.BaseStream, Console.OpenStandardError());
        // Parent Windows Job owns this driver AND descendants. Cancellation is
        // never interpreted as permission to kill, release pins, or ignore drain.
        try { process.WaitForExit(); Task.WaitAll(stdout, stderr); }
        finally { if (!process.HasExited) process.WaitForExit(); }
        if (lost != 0) throw new IOException("workflow_native_log_transport_failed");
        return process.ExitCode;
    }
}
