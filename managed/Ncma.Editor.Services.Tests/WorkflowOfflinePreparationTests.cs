using System.Security.Cryptography;
using Ncma.Editor.Core;
using Ncma.Editor.Services;

// Registered for the unified M8.6-E run. These filesystem/contract cases do NOT
// stand in for the separate real SDK/IPC/cancellation acceptance entries.
internal static partial class WorkflowOfflinePreparationTests
{
    private static readonly string H = new('A', 64);
    private static void Check(bool value) { if (!value) throw new Exception("M8.6 offline preparation assertion"); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception e) when (e is IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException) { return; }
        throw new Exception("Expected fail-closed offline preparation rejection");
    }
    private static string Root(string parent)
    {
        string path = Path.Combine(parent, "offline-preparation-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path;
    }
    private static WorkflowVerificationTask Task(string recipe = "editor-services-restore-offline-v1")
        => new(new(1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), H, new(Guid.NewGuid(), 1, 1), Guid.NewGuid(), H, H, H,
            null, recipe, "Debug", "default", "9.0.315", H), [new("offline-feed/example/1.0.0/example.1.0.0.nupkg", 3, H)], []);
    internal static IEnumerable<(string, Action)> Cases(string parent)
    {
        foreach (var item in ReceiptCases(parent)) yield return item;
        yield return ("M8.6 offline recipe clears remote/fallback sources and is distinct from no-restore build", () => {
            var task = Task(); var env = new WorkflowBuildEnvironment(@"F:\NcmaEngineRelease\source", @"C:\sdk\dotnet.exe", @"C:\runner\powershell.exe", H,
                [new("scripts/WorkflowBuildInputs.ps1", 1, H)], TimeSpan.FromMinutes(10));
            var command = new WorkflowBuildCommand(task, env); var args = command.CopyArguments();
            Check(args[0] == "restore" && args[Array.IndexOf(args, "--source") + 1] == command.FeedRoot);
            Check(args[Array.IndexOf(args, "--configfile") + 1] == command.NugetConfig);
            Check(args.Contains("--no-http-cache") && args.Contains("--disable-parallel") && args.Contains("-p:NuGetAudit=false"));
            Check(args[Array.IndexOf(args, "--verbosity") + 1] == "normal"); // retain observed restore warning/error counts
            Check(args.Contains("-p:RestoreFallbackFolders=") && args.Contains("-p:RestoreAdditionalProjectSources=") && args.Contains("-p:RestoreIgnoreFailedSources=false"));
            Check(args.All(a => !a.Contains("https://", StringComparison.Ordinal)));
            Check(WorkflowBuildCommand.OfflineNugetConfig.Contains("<packageSources><clear /></packageSources>", StringComparison.Ordinal));
            Check(new WorkflowBuildCommand(Task("editor-services-build-v1"), env).CopyArguments().Contains("--no-restore"));
            args[0] = "publish"; Check(command.CopyArguments()[0] == "restore");
        });
        yield return ("M8.6 whole-tree pins include empty files and reject mutation until explicit close", () => {
            string root = Root(parent); File.WriteAllBytes(Path.Combine(root, "empty.bin"), []);
            File.WriteAllBytes(Path.Combine(root, "value.bin"), [1, 2, 3]);
            using (var tree = new WorkflowPinnedTree(root)) {
                tree.Prepare(); var copy = tree.CopyFiles(); Check(copy.Length == 2);
                Check(copy.Single(f => f.Path == "empty.bin").Bytes == 0);
                Check(copy.Single(f => f.Path == "value.bin").Sha256 == Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3 })));
                copy[0] = new("foreign", 1, H); Check(tree.CopyFiles().All(f => f.Path != "foreign"));
                Reject(() => File.WriteAllBytes(Path.Combine(root, "value.bin"), [4]));
                System.Threading.Tasks.Task.Run(() => Reject(tree.RequireCurrent)).GetAwaiter().GetResult();
            }
            File.WriteAllBytes(Path.Combine(root, "value.bin"), [4]);
        });
        yield return ("M8.6 tree additions invalidate closure without releasing original byte pins", () => {
            string root = Root(parent); string original = Path.Combine(root, "original.bin"); File.WriteAllBytes(original, [1]);
            using var tree = new WorkflowPinnedTree(root); tree.Prepare();
            File.WriteAllBytes(Path.Combine(root, "extra.bin"), [2]); Reject(tree.RequireCurrent); Reject(() => tree.CopyFiles());
            Reject(() => File.WriteAllBytes(original, [3]));
            Reject(tree.Prepare); // no same-owner recapture/rebaseline
        });
        yield return ("M8.6 offline cache is exact declared archives, not a directory-wide execution grant", () => {
            string root = Root(parent); var task = Task();
            using var feed = new WorkflowOfflineFeed(task, root);
            Reject(feed.RequirePrepared); Reject(feed.Prepare); // missing archive is not skipped
            Reject(feed.RequirePrepared); Reject(feed.Prepare);
            var unexpected = new WorkflowVerificationTask(task.Binding, [new("other.bin", 1, H)], []);
            Reject(() => _ = new WorkflowOfflineFeed(unexpected, root));
            Reject(() => _ = new WorkflowOfflineFeed(task, Path.GetPathRoot(root)!));
        });
        yield return ("M8.6 local job owner is passive on editor creation and closes without implicit preparation", () => {
            using var owner = new EditorSessionOwner("offline workflow host"); var jobs = owner.WorkflowJobs!;
            jobs.Pump(); Check(jobs.Capture().Length == 0); Check(!jobs.StartApproved());
            Reject(() => jobs.Review(Guid.NewGuid()));
            Reject(() => jobs.PrepareOffline(parent, Guid.NewGuid(), parent, Guid.NewGuid(), "Debug", "default"));
            Check(jobs.Capture().Length == 0);
            System.Threading.Tasks.Task.Run(() => Reject(() => jobs.Capture())).GetAwaiter().GetResult();
        });
    }
}
