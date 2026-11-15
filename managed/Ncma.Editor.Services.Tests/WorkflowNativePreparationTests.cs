using System.Text;
using System.Text.Json;
using Ncma.Editor.Services;

// Native recipe/identity tests only. These never run CMake or claim that an
// installed compiler, real native plug-ins, or the new worker were exercised.
internal static class WorkflowNativePreparationTests
{
    private static readonly string H = new('A', 64);
    private static void Check(bool value) { if (!value) throw new Exception("M8.6 native preparation assertion"); }
    private static void Reject(Action action) {
        try { action(); } catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException or JsonException) { return; }
        throw new Exception("Expected native preparation rejection");
    }
    private static WorkflowNativeSelection Selection() => new(@"C:\fixture\VS", "14.44.35207", @"C:\fixture\Kits", "10.0.26100.0",
        new[] { "cmake", "ninja", "cl", "link", "lib", "rc", "mt" }.Select(n => new WorkflowEvidenceFile("native-tools/" + n + ".exe", 1, H)));
    private static WorkflowNativeRequest Request()
    {
        var selection = Selection(); string prefix = "compiled/" + Guid.NewGuid().ToString("N") + "/runtime/";
        var runtime = new[] { "Ncma.Editor.Services.Tests.dll", "Ncma.Editor.Services.Tests.deps.json", "Ncma.Editor.Services.Tests.runtimeconfig.json" }
            .Select(n => new WorkflowEvidenceFile(prefix + n, 1, H));
        var task = new WorkflowVerificationTask(new(1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), H, new(Guid.NewGuid(), 1, 1),
            Guid.NewGuid(), H, H, H, null, "workflow-native-prepare-v1", "Debug", "default", "fixture-sdk", H),
            runtime.Concat(selection.CopyFiles()).Append(selection.Descriptor()), []);
        return new(task, @"F:\NcmaEngineRelease\source", selection);
    }
    private static byte[] Cache(WorkflowNativePlan plan)
    {
        var fields = new Dictionary<string, string> { ["CMAKE_GENERATOR"] = "Ninja", ["CMAKE_HOME_DIRECTORY"] = plan.SourceRoot };
        foreach (string argument in plan.ConfigureArguments().Where(a => a.StartsWith("-D", StringComparison.Ordinal))) {
            int equals = argument.IndexOf('='); fields.Add(argument[2..equals], argument[(equals + 1)..]);
        }
        return Encoding.UTF8.GetBytes(string.Join('\n', fields.Select(p => p.Key + ":STRING=" + p.Value)) + "\n");
    }
    internal static IEnumerable<(string, Action)> Cases()
    {
        yield return ("M8.6 native tool selection exact inventory detached copies and version grammar", () => {
            var selection = Selection(); byte[] bytes = selection.Encode(); using var json = JsonDocument.Parse(bytes);
            Check(WorkflowNativeSelection.Read(json.RootElement).Encode().SequenceEqual(bytes));
            var copy = selection.CopyFiles(); copy[0] = copy[0] with { Sha256 = new string('B', 64) };
            Check(selection.Encode().SequenceEqual(bytes) && selection.ToolPaths().Count == 7);
            Reject(() => new WorkflowNativeSelection(selection.VisualStudioRoot, "14.44;other", selection.WindowsSdkRoot, selection.WindowsSdkVersion, selection.CopyFiles()));
            Reject(() => new WorkflowNativeSelection(selection.VisualStudioRoot, null!, selection.WindowsSdkRoot, selection.WindowsSdkVersion, selection.CopyFiles()));
            Reject(() => new WorkflowNativeSelection(selection.VisualStudioRoot, selection.ToolsetVersion, selection.WindowsSdkRoot, selection.WindowsSdkVersion, copy[..^1]));
        });
        yield return ("M8.6 native recipe builds only captured DX11 host dependencies with explicit tools", () => {
            var request = Request(); request.Validate(); var plan = request.Plan; var args = plan.ConfigureArguments();
            Check(plan.SourceRoot.EndsWith(request.Task.Binding.CaptureId.ToString("N") + "\\source", StringComparison.Ordinal));
            Check(args.Contains("-DNCMA_BUILD_MANAGED=OFF") && args.Contains("-DNCMA_BUILD_TESTS=OFF") && args.Contains("-DCMAKE_C_FLAGS=/WX"));
            Check(plan.BuildArguments().SequenceEqual(new[] { "--build", plan.NativeRoot, "--config", "Debug", "--target", "NcmaPlatform", "NcmaRenderer", "NcmaGui", "--parallel", "2" }));
            args[0] = "--install"; Check(plan.ConfigureArguments()[0] == "-S");
            Reject(() => new WorkflowNativePlan(request.Workspace, Guid.Empty, Guid.NewGuid(), "Debug", request.Selection));
        });
        yield return ("M8.6 native environment is closed local include lib and tool search data", () => {
            var plan = Request().Plan; var environment = plan.EnvironmentOverrides();
            Check(environment.Count == 5 && !environment.ContainsKey("API_KEY") && environment["VSCMD_ARG_TGT_ARCH"] == "x64");
            Check(environment["INCLUDE"] == string.Join(';', plan.Selection.IncludeDirectories()));
            Check(environment["LIB"] == string.Join(';', plan.Selection.LibraryDirectories()));
            environment["PATH"] = "foreign"; Check(plan.EnvironmentOverrides()["PATH"] != "foreign");
        });
        yield return ("M8.6 native CMake cache wrong source tool configuration or duplicate fails closed", () => {
            var plan = Request().Plan; byte[] bytes = Cache(plan); plan.VerifyCache(bytes);
            string text = Encoding.UTF8.GetString(bytes);
            foreach (string changed in new[] { text.Replace("Ninja", "Other"), text.Replace("CMAKE_BUILD_TYPE:STRING=Debug", "CMAKE_BUILD_TYPE:STRING=Release"),
                text.Replace(plan.SourceRoot, @"F:\NcmaEngine\source"), text.Replace("cl.exe", "other.exe"), text + "CMAKE_GENERATOR:STRING=Ninja\n",
                text.Replace("CMAKE_CXX_COMPILER_LAUNCHER:STRING=", "CMAKE_CXX_COMPILER_LAUNCHER:STRING=foreign") })
                Reject(() => plan.VerifyCache(Encoding.UTF8.GetBytes(changed)));
        });
        yield return ("M8.6 native request binds exact tools metadata task and compiled producer", () => {
            var request = Request(); var read = WorkflowNativeRequest.Read(request.Encode()); Check(read.Task.Fingerprint == request.Task.Fingerprint);
            var domain = new WorkflowNativeRequest(new(request.Task.Binding with { Assets = new(Guid.NewGuid(),1,7) },request.Task.CopyInputs(),[]),request.Workspace,request.Selection);
            Check(WorkflowNativeRequest.Read(domain.Encode()).Task.Binding.Assets == domain.Task.Binding.Assets);
            string domainJson = Encoding.UTF8.GetString(domain.Encode());
            Reject(() => WorkflowNativeRequest.Read(Encoding.UTF8.GetBytes(domainJson.Replace("\"ProjectId\":", "\"Unknown\":0,\"ProjectId\":", StringComparison.Ordinal))));
            var altered = new WorkflowVerificationTask(request.Task.Binding, request.Task.CopyInputs().Select(f => f.Path == "native-selection.json" ? f with { Sha256 = H } : f), []);
            Reject(() => new WorkflowNativeRequest(altered, request.Workspace, request.Selection).Validate());
            var missing = new WorkflowVerificationTask(request.Task.Binding, request.Selection.CopyFiles().Append(request.Selection.Descriptor()), []);
            Reject(() => new WorkflowNativeRequest(missing, request.Workspace, request.Selection).Validate());
            string json = Encoding.UTF8.GetString(request.Encode());
            Reject(() => WorkflowNativeRequest.Read(Encoding.UTF8.GetBytes(json.Replace("\"version\":1", "\"version\":1,\"version\":1"))));
        });
        yield return ("M8.6 native result distinguishes skipped build unknown counts and prepared not tested", () => {
            var request = Request(); var task = request.Task;
            var result = new WorkflowNativeResult(1, task.Fingerprint, 0, 0, true, "");
            Check(WorkflowNativeResult.Read(task, result.Encode()).Complete);
            Check(!WorkflowNativeResult.Read(task, (result with { ConfigureExit = 1, BuildExit = null, CacheVerified = false }).Encode()).Complete);
            Reject(() => WorkflowNativeResult.Read(task, (result with { ConfigureExit = 1 }).Encode()));
            Reject(() => WorkflowNativeResult.Read(task, (result with { TaskFingerprint = new string('B', 64) }).Encode()));
            var products = task.Recipe.RequiredOutputs.Select(n => new WorkflowEvidenceFile(n, 1, H));
            var receipt = new WorkflowVerificationReceipt(task, task.Fingerprint, WorkflowProcessOutcome.Exited, 0, null, null,
                task.CopyInputs(), products, [new("stdout.log", 1, H)], []);
            Check(receipt.Conclusion == WorkflowVerificationConclusion.NativeInputsPrepared && receipt.WarningCount is null && receipt.ErrorCount is null && !receipt.InstallationAuthorized);
            Check(new WorkflowVerificationReceipt(task, task.Fingerprint, WorkflowProcessOutcome.Exited, 0, null, null,
                task.CopyInputs(), [], [new("stdout.log", 1, H)], []).Conclusion == WorkflowVerificationConclusion.Incomplete);
        });
        yield return ("M8.6 native review displays independent authority and exact nested command vector", () => {
            var request = Request(); var environment = new WorkflowBuildEnvironment(request.Workspace, @"C:\fixture\dotnet.exe", @"C:\fixture\powershell.exe", H,
                [new("scripts/WorkflowBuildInputs.ps1", 1, H)], TimeSpan.FromMinutes(10));
            var command = new WorkflowBuildCommand(request.Task, environment); Check(command.CopyArguments()[1] == "--workflow-native-build");
            var review = new WorkflowLocalJobReview(new WorkflowBuildReview(Guid.NewGuid(), request.Task, environment, H), "", request.Plan);
            using var json = JsonDocument.Parse(review.Display); var value = json.RootElement;
            Check(value.GetProperty("nativeBuildAuthorized").GetBoolean() && !value.GetProperty("buildAuthorized").GetBoolean() && !value.GetProperty("testAuthorized").GetBoolean());
            Check(!value.GetProperty("networkAuthorized").GetBoolean() && !value.GetProperty("installationAuthorized").GetBoolean());
            Check(value.GetProperty("nativePreparation").GetProperty("build").EnumerateArray().Select(v => v.GetString()).SequenceEqual(request.Plan.BuildArguments()));
        });
    }
}
