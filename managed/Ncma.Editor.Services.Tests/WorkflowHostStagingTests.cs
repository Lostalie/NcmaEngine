using System.Security.Cryptography;
using Ncma.Editor.Services;

// Synthetic mapping and real retained-handle IO, NOT actual host job acceptance.
internal static class WorkflowHostStagingTests
{
    private static readonly string H = new('A', 64);
    private const string Root = @"F:\NcmaEngineRelease\source";
    private static void Check(bool value) { if (!value) throw new Exception("M8.6 host staging assertion"); }
    private static void Reject(Action action) {
        try { action(); } catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException) { return; }
        throw new Exception("Expected host staging rejection");
    }
    private static WorkflowVerificationTask Task()
    {
        string compiled = "compiled/" + Guid.NewGuid().ToString("N") + "/", native = "native/" + Guid.NewGuid().ToString("N") + "/plugins/";
        string[] paths = [compiled + "runtime/Ncma.Editor.Services.Tests.dll", compiled + "runtime/Ncma.Editor.Services.Tests.deps.json",
            compiled + "runtime/Ncma.Editor.Services.Tests.runtimeconfig.json", compiled + "mcp/Ncma.Editor.Mcp.dll", compiled + "mcp/Ncma.Editor.Mcp.deps.json",
            compiled + "mcp/Ncma.Editor.Mcp.runtimeconfig.json", compiled + "gameplay/Ncma.Gameplay.Sample.dll", compiled + "gameplay/Ncma.Gameplay.Sample.deps.json",
            native + "NcmaPlatform.dll", native + "NcmaRenderer.dll", native + "NcmaGui.dll"];
        return new(new(1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), H, new(Guid.NewGuid(), 1, 1), Guid.NewGuid(), H, H, H, null,
            "workflow-host-v1", "Debug", "default", "fixture-sdk", H), paths.Select(p => new WorkflowEvidenceFile(p, 1, H)), WorkflowTestInventory.Copy("workflow-host-v1"));
    }
    private static WorkflowVerificationTask With(WorkflowVerificationTask task, IEnumerable<WorkflowEvidenceFile> files) => new(task.Binding, files, task.CopyTestInventory());
    internal static IEnumerable<(string, Action)> Cases(string output)
    {
        yield return ("M8.6 staging maps only approved host inputs to exact isolated paths", () => {
            var task = Task(); var map = WorkflowHostStaging.Map(task, Root);
            Check(map.Length == 8 && map.Count(f => f.Group == "mcp") == 3 && map.Count(f => f.Group == "plugins") == 3);
            Check(map.Any(f => f.Destination == "out/managed/editor-mcp/Ncma.Editor.Mcp.dll"));
            Check(map.Any(f => f.Destination == "out/managed/Ncma.Gameplay.Sample.dll") && map.All(f => f.Group != "runtime"));
            using var stage = new WorkflowHostStaging(task, Root); Reject(stage.RequireCurrent); // construction never creates or authorizes output
        });
        yield return ("M8.6 staging rejects mapped aliases and file directory collisions before IO", () => {
            var task = Task(); var files = task.CopyInputs(); string nativePrefix = files.Single(f => f.Path.EndsWith("/NcmaGui.dll", StringComparison.Ordinal)).Path[..^"NcmaGui.dll".Length];
            foreach (string extra in new[] { "Ncma.Gameplay.Sample.dll", "ncma.gameplay.sample.dll", "editor-mcp" })
                Reject(() => WorkflowHostStaging.Map(With(task, [.. files, new(nativePrefix + extra, 1, H)]), Root));
        });
        yield return ("M8.6 staging rejects missing mixed self native producers and byte overflow", () => {
            var task = Task(); var files = task.CopyInputs(); var native = files.First(f => f.Path.StartsWith("native/", StringComparison.Ordinal));
            Reject(() => WorkflowHostStaging.Map(With(task, files.Where(f => f != native)), Root));
            foreach (string id in new[] { Guid.NewGuid().ToString("N"), task.Binding.JobId.ToString("N"), files.First(f => f.Path.StartsWith("compiled/", StringComparison.Ordinal)).Path.Split('/')[1] })
                Reject(() => WorkflowHostStaging.Map(With(task, files.Select(f => f == native ? f with { Path = f.Path.Replace(f.Path.Split('/')[1], id) } : f)), Root));
            Reject(() => WorkflowHostStaging.Map(With(task, files.Select(f => f == native ? f with { Bytes = 64L * 1024 * 1024 + 1 } : f)), Root));
            Reject(() => WorkflowHostStaging.Map(With(task, files.Select(f => f.Path.Contains("/runtime/", StringComparison.Ordinal) ? f : f with { Bytes = 64L * 1024 * 1024 })
                .Append(new(native.Path[..^"NcmaGui.dll".Length] + "extra.bin", 1, H))), Root));
        });
        yield return ("M8.6 retained handle streaming copies exact large and empty files without repinning", () => {
            string root = Path.Combine(output, "workflow-stream-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            byte[] bytes = Enumerable.Range(0, 150001).Select(i => (byte)(i % 251)).ToArray();
            string path = Path.Combine(root, "input.bin"), empty = Path.Combine(root, "empty.bin"); File.WriteAllBytes(path, bytes); File.WriteAllBytes(empty, []);
            using var pins = new WorkflowPhysicalPins(2); var observed = pins.Observe(path, bytes.Length); var zero = pins.Observe(empty, 1, true);
            Check(observed.Sha256 == Convert.ToHexString(SHA256.HashData(bytes))); byte[] buffer = new byte[65536];
            for (int i = 0; i < 3; i++) { using var target = new MemoryStream(); pins.CopyObserved(path, observed.Sha256, observed.Bytes, target, buffer); Check(target.ToArray().SequenceEqual(bytes)); }
            using var emptyTarget = new MemoryStream(); pins.CopyObserved(empty, zero.Sha256, 0, emptyTarget, buffer); Check(emptyTarget.Length == 0);
            Reject(() => pins.CopyObserved(path, H, observed.Bytes, emptyTarget, buffer));
            Reject(() => pins.CopyObserved(Path.Combine(root, "missing.bin"), H, 1, emptyTarget, buffer));
            Reject(() => File.WriteAllBytes(path, [7]));
        });
        yield return ("M8.6 staging child creation refuses existing paths traversal and unpinned parent", () => {
            string root = Path.Combine(output, "workflow-stage-path-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            using var pins = new WorkflowPhysicalPins(); Reject(() => pins.CreateStageChild(root, "inputs")); pins.Directory(root);
            string child = pins.CreateStageChild(root, "inputs"); Check(Directory.Exists(child));
            Reject(() => pins.CreateStageChild(root, "inputs")); Reject(() => pins.CreateStageChild(root, "../escape"));
            Reject(() => pins.CreateStageChild(root, "a/b")); Reject(() => pins.CreateStageChild(root, "CON"));
        });
    }
}
