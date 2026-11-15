using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ncma.Editor.Services;

internal static class WorkflowRestoreTests
{
    private const string Project = "managed/Fixture/Fixture.csproj";
    private static string H => new('A', 64);
    private static void Check(bool ok) { if (!ok) throw new Exception("M8.6 offline restore inspection assertion"); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception e) when (e is IOException or JsonException or InvalidOperationException or ArgumentException or UnauthorizedAccessException) { return; }
        throw new Exception("Expected restore rejection");
    }
    private static string Source => @"F:\NcmaEngineRelease\source\out\captured-build\00000000000000000000000000000001\source";
    private static string Output => @"F:\NcmaEngineRelease\source\out\verification\m8-6\restores\00000000000000000000000000000002\artifacts\obj\Fixture";
    private static JsonObject Sample(string source, string output, string name = "Fixture")
    {
        string project = Path.Combine(source, "managed", name, name + ".csproj");
        return JsonSerializer.SerializeToNode(new {
            version = 3,
            targets = new Dictionary<string, object> { ["net8.0"] = new Dictionary<string, object> {
                ["sample/1.0.0"] = new { type = "package", compile = new Dictionary<string, object> { ["lib/net8.0/sample.dll"] = new { } } } } },
            libraries = new Dictionary<string, object> { ["sample/1.0.0"] = new { type = "package", path = "sample/1.0.0", sha512 = Convert.ToBase64String(new byte[64]), files = new[] { "lib/net8.0/sample.dll", "sample.nuspec" } } },
            packageFolders = new Dictionary<string, object> { [@"C:\offline-cache\packages\"] = new { } },
            project = new { restore = new { projectPath = project, projectUniqueName = project, projectName = name, outputPath = output, projectStyle = "PackageReference",
                packagesPath = @"C:\offline-cache\packages", frameworks = new Dictionary<string, object> { ["net8.0"] = new { projectReferences = new Dictionary<string, object>() } } } }
        })!.AsObject();
    }
    private static byte[] Bytes(JsonObject sample) => JsonSerializer.SerializeToUtf8Bytes(sample);
    private static WorkflowRestoreAssets Parse(JsonObject sample) => WorkflowRestoreAssets.Parse(Bytes(sample), Source, Project, Output);
    internal static IEnumerable<(string, Action)> Cases(string output)
    {
        yield return ("M8.6 package closure pins declared payload and archive bytes without execution authority", () => {
            var f = PackageFixture(output); using var graph = new WorkflowRestoreGraph(f.Task, f.Environment, f.Id); graph.Prepare();
            using var closure = new WorkflowPackageClosure(f.Task, graph, [f.Root]); closure.Prepare();
            Check(!closure.CompilationAuthorized && closure.Fingerprint?.Length == 64 && closure.CopyFiles().Length == 3);
            var copy = closure.CopyFiles(); copy[0] = copy[0] with { PhysicalPath = "changed" }; Check(closure.CopyFiles()[0].PhysicalPath != "changed");
            foreach (string file in f.Files) Reject(() => { using var denied = new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.None); });
            System.Threading.Tasks.Task.Run(() => Reject(() => closure.CopyFiles())).GetAwaiter().GetResult();
            Reject(closure.Prepare); closure.Dispose(); Reject(() => closure.CopyFiles());
            foreach (string file in f.Files) { using var released = new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.None); }
            Check(graph.CopyMappings().Length == 1);
        });
        yield return ("M8.6 package closure refuses missing declaration foreign roots and changed payload", () => {
            var f = PackageFixture(output);
            var incomplete = new WorkflowVerificationTask(f.Task.Binding, f.Task.CopyInputs().Where(v => !v.Path.EndsWith("sample.nuspec", StringComparison.Ordinal)), []);
            using (var graph = new WorkflowRestoreGraph(incomplete, f.Environment, f.Id)) {
                graph.Prepare(); using var closure = new WorkflowPackageClosure(incomplete, graph, [f.Root]); Reject(closure.Prepare); Reject(() => closure.CopyFiles());
            }
            using var goodGraph = new WorkflowRestoreGraph(f.Task, f.Environment, f.Id); goodGraph.Prepare();
            using (var wrong = new WorkflowPackageClosure(f.Task, goodGraph, [f.Root + "-foreign"])) Reject(wrong.Prepare);
            File.AppendAllText(f.Files[0], "changed");
            using var changed = new WorkflowPackageClosure(f.Task, goodGraph, [f.Root]); Reject(changed.Prepare); Check(changed.Fingerprint is null);
        });
        yield return ("M8.6 package archive SHA512 mismatch fails and retains earlier acquired files", () => {
            var f = PackageFixture(output, badArchiveHash: true);
            using var graph = new WorkflowRestoreGraph(f.Task, f.Environment, f.Id); graph.Prepare();
            using var closure = new WorkflowPackageClosure(f.Task, graph, [f.Root]); Reject(closure.Prepare); Reject(() => closure.CopyFiles());
            foreach (string file in f.Files) Reject(() => { using var denied = new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.None); });
            closure.Dispose(); foreach (string file in f.Files) { using var released = new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.None); }
        });
        yield return ("M8.6 package closure refuses closed borrowed graph and explicit root aliases", () => {
            var f = PackageFixture(output); using var graph = new WorkflowRestoreGraph(f.Task, f.Environment, f.Id); graph.Prepare();
            Reject(() => new WorkflowPackageClosure(f.Task, graph, [f.Root, f.Root.ToUpperInvariant()]));
            using var closure = new WorkflowPackageClosure(f.Task, graph, [f.Root]); closure.Prepare(); graph.Dispose(); Reject(() => closure.CopyFiles());
        });
        yield return ("M8.6 restore graph separates pinned inputs from job intermediates without launch authority", () => {
            var fixture = PrepareFiles(output, "Ncma.Editor.Services.Tests");
            using var graph = new WorkflowRestoreGraph(fixture.Task, fixture.Environment, fixture.Id);
            Reject(() => graph.CopyMappings()); graph.Prepare(); Check(!graph.CompilationAuthorized && graph.Fingerprint?.Length == 64);
            var command = new WorkflowBuildCommand(fixture.Task, fixture.Environment);
            var row = graph.CopyMappings().Single(); Check(row.RestoreDirectory == Path.GetDirectoryName(fixture.Files[0]));
            Check(row.IntermediateDirectory == Path.Combine(command.ArtifactsRoot, "obj", "Ncma.Editor.Services.Tests"));
            Check(!Directory.Exists(command.OutputRoot));
            byte[] bytes = graph.CopyMappingProps(); string xml = Encoding.UTF8.GetString(bytes);
            var doc = System.Xml.Linq.XElement.Parse(xml); Check(doc.Name == "Project" && xml.Contains("MSBuildProjectExtensionsPath", StringComparison.Ordinal));
            Check(xml.Contains("workflow_restore_output_mapping_required", StringComparison.Ordinal));
            bytes[0] = 0; Check(graph.CopyMappingProps()[0] != 0);
            var rows = graph.CopyMappings(); rows[0] = row with { Project = "changed" }; Check(graph.CopyMappings()[0] == row);
            foreach (string file in fixture.Files) Reject(() => { using var blocked = new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.None); });
            System.Threading.Tasks.Task.Run(() => Reject(() => graph.CopyMappingProps())).GetAwaiter().GetResult();
            Reject(graph.Prepare); graph.Dispose(); Reject(() => graph.CopyMappings());
            foreach (string file in fixture.Files) { using var released = new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.None); }
        });
        yield return ("M8.6 restore graph rejects missing root and incomplete declared input set", () => {
            var foreign = PrepareFiles(output); Reject(() => new WorkflowRestoreGraph(foreign.Task, foreign.Environment, foreign.Id));
            var fixture = PrepareFiles(output, "Ncma.Editor.Services.Tests");
            var incomplete = new WorkflowVerificationTask(fixture.Task.Binding, fixture.Task.CopyInputs()[..^1], []);
            using var graph = new WorkflowRestoreGraph(incomplete, fixture.Environment, fixture.Id);
            Reject(graph.Prepare); Reject(() => graph.CopyMappings()); Check(graph.Fingerprint is null);
        });
        yield return ("M8.6 restore graph missing dependency retains acquired inputs until explicit close", () => {
            var fixture = PrepareFiles(output, "Ncma.Editor.Services.Tests");
            var json = JsonNode.Parse(File.ReadAllBytes(fixture.Files[0]))!.AsObject();
            json["libraries"]!["Missing/1.0.0"] = new JsonObject { ["type"] = "project", ["path"] = "../Missing/Missing.csproj", ["msbuildProject"] = "../Missing/Missing.csproj" };
            File.WriteAllBytes(fixture.Files[0], Bytes(json));
            var inputs = fixture.Task.CopyInputs().Select(f => f.Path.EndsWith("/project.assets.json", StringComparison.Ordinal)
                ? f with { Bytes = new FileInfo(fixture.Files[0]).Length, Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fixture.Files[0]))) } : f);
            var changed = new WorkflowVerificationTask(fixture.Task.Binding, inputs, []);
            using var graph = new WorkflowRestoreGraph(changed, fixture.Environment, fixture.Id);
            Reject(graph.Prepare); Reject(() => graph.CopyMappingProps()); Check(graph.Fingerprint is null);
            foreach (string file in fixture.Files) Reject(() => { using var blocked = new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.None); });
            graph.Dispose(); foreach (string file in fixture.Files) { using var released = new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.None); }
        });
        yield return ("M8.6 restore inventory copies are isolated and never authorize compilation", () => {
            var result = Parse(Sample(Source, Output)); Check(!result.CompilationAuthorized && result.CopyPackages().Length == 1);
            var packages = result.CopyPackages(); packages[0].Files[0] = "changed"; packages[0] = packages[0] with { RelativePath = "elsewhere" };
            Check(result.CopyPackages()[0].RelativePath == "sample/1.0.0" && result.CopyPackages()[0].Files[0] == "lib/net8.0/sample.dll");
            var folders = result.CopyPackageFolders(); folders[0] = "changed"; Check(result.CopyPackageFolders()[0] == @"C:\offline-cache\packages");
        });
        yield return ("M8.6 restore inventory rejects foreign capture output and project identities", () => {
            foreach (string field in new[] { "projectPath", "projectUniqueName", "outputPath", "projectName", "projectStyle" }) {
                var sample = Sample(Source, Output); sample["project"]!["restore"]![field] = field.StartsWith("project", StringComparison.Ordinal) && field is not ("projectName" or "projectStyle") ? @"F:\foreign\managed\Fixture\Fixture.csproj" : "wrong";
                Reject(() => Parse(sample));
            }
            var foreign = Sample(Source, Output);
            foreign["project"]!["restore"]!["frameworks"]!["net8.0"]!["projectReferences"]!["F:/outside/Other.csproj"] = new JsonObject { ["projectPath"] = "F:/outside/Other.csproj" };
            Reject(() => Parse(foreign)); Reject(() => WorkflowRestoreAssets.ProjectName("managed/../Fixture.csproj"));
        });
        yield return ("M8.6 restore inventory rejects duplicate JSON unsupported version and byte budget", () => {
            string json = Sample(Source, Output).ToJsonString();
            Reject(() => WorkflowRestoreAssets.Parse(Encoding.UTF8.GetBytes("{\"version\":3," + json[1..]), Source, Project, Output));
            var version = Sample(Source, Output); version["version"] = 4; Reject(() => Parse(version));
            var missing = Sample(Source, Output); missing.Remove("targets"); Reject(() => Parse(missing));
            Reject(() => WorkflowRestoreAssets.Parse(new byte[WorkflowRestoreAssets.MaximumBytes + 1], Source, Project, Output));
        });
        yield return ("M8.6 restore inventory rejects package path hash aliases and missing target files", () => {
            foreach (string path in new[] { "../escape", "C:/escape", "lib/%2e.dll", "lib/NUL.dll", "lib/file:stream", "lib/a//b" }) {
                var sample = Sample(Source, Output); sample["libraries"]!["sample/1.0.0"]!["files"] = new JsonArray(path); Reject(() => Parse(sample));
            }
            var hash = Sample(Source, Output); hash["libraries"]!["sample/1.0.0"]!["sha512"] = "AAAA"; Reject(() => Parse(hash));
            var alias = Sample(Source, Output); alias["libraries"]!["sample/1.0.0"]!["files"] = new JsonArray("lib/net8.0/sample.dll", "LIB/net8.0/sample.dll"); Reject(() => Parse(alias));
            var missing = Sample(Source, Output); missing["libraries"]!["sample/1.0.0"]!["files"] = new JsonArray("unrelated.dll"); Reject(() => Parse(missing));
            var collision = Sample(Source, Output); collision["libraries"]!["sample/1.0.0"]!["files"] = new JsonArray("lib", "lib/net8.0/sample.dll"); Reject(() => Parse(collision));
        });
        yield return ("M8.6 restore inventory retains in-capture project references but rejects escaping libraries", () => {
            var sample = Sample(Source, Output); sample["libraries"]!["Other/1.0.0"] = new JsonObject { ["type"] = "project", ["path"] = "../Other/Other.csproj", ["msbuildProject"] = "../Other/Other.csproj" };
            Check(Parse(sample).CopyProjects().Single() == Path.Combine(Source, "managed", "Other", "Other.csproj"));
            sample["libraries"]!["Other/1.0.0"]!["path"] = "../../../../Other/Other.csproj";
            sample["libraries"]!["Other/1.0.0"]!["msbuildProject"] = "../../../../Other/Other.csproj"; Reject(() => Parse(sample));
            var target = Sample(Source, Output); target["targets"]!["net8.0"]!["missing/1.0.0"] = new JsonObject { ["type"] = "package" }; Reject(() => Parse(target));
        });
        yield return ("M8.6 restore inventory enforces cumulative file count and package root aliases", () => {
            var sample = Sample(Source, Output); var files = new JsonArray();
            for (int i = 0; i <= WorkflowRestoreAssets.MaximumFiles; i++) files.Add("files/" + i + ".dll");
            sample["libraries"]!["sample/1.0.0"]!["files"] = files; Reject(() => Parse(sample));
            var roots = Sample(Source, Output); roots["packageFolders"]!["C:/offline-cache/packages"] = new JsonObject(); Reject(() => Parse(roots));
        });
        yield return ("M8.6 physical restore inspection holds exact assets and generated imports until explicit close", () => {
            var fixture = PrepareFiles(output);
            using var inspection = new WorkflowRestoreInspection(fixture.Task, fixture.Environment, fixture.Id, Project);
            inspection.Prepare(); Check(!inspection.CompilationAuthorized && inspection.Read().CopyPackages().Length == 1);
            foreach (string file in fixture.Files) Reject(() => { using var denied = new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.None); });
            System.Threading.Tasks.Task.Run(() => Reject(() => inspection.Read())).GetAwaiter().GetResult();
            Reject(inspection.Prepare); inspection.Dispose(); Reject(() => inspection.Read());
            foreach (string file in fixture.Files) { using var released = new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.None); Check(released.Length > 0); }
        });
        yield return ("M8.6 physical restore missing declarations and changed generated bytes fail without fallback", () => {
            var fixture = PrepareFiles(output);
            var missing = new WorkflowVerificationTask(fixture.Task.Binding, fixture.Task.CopyInputs()[..^1], []);
            using (var inspection = new WorkflowRestoreInspection(missing, fixture.Environment, fixture.Id, Project)) { Reject(inspection.Prepare); Reject(() => inspection.Read()); }
            File.AppendAllText(fixture.Files[1], "<!-- changed -->");
            using (var inspection = new WorkflowRestoreInspection(fixture.Task, fixture.Environment, fixture.Id, Project)) { Reject(inspection.Prepare); Reject(() => inspection.Read()); }
            foreach (string file in fixture.Files) { using var released = new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.None); }
        });
    }
    private static (WorkflowVerificationTask Task, WorkflowBuildEnvironment Environment, Guid Id, string Root, string[] Files) PackageFixture(string output, bool badArchiveHash = false)
    {
        var f = PrepareFiles(output, "Ncma.Editor.Services.Tests");
        string root = Path.Combine(f.Environment.WorkspaceRoot, "packages");
        string package = Path.Combine(root, "sample", "1.0.0"); Directory.CreateDirectory(Path.Combine(package, "lib", "net8.0"));
        string[] paths = [Path.Combine(package, "lib", "net8.0", "sample.dll"), Path.Combine(package, "sample.nuspec"), Path.Combine(package, "sample.1.0.0.nupkg")];
        foreach (string path in paths) File.WriteAllBytes(path, [1, 2, 3]); // synthetic bytes, NOT a valid signed NuGet archive
        var json = JsonNode.Parse(File.ReadAllBytes(f.Files[0]))!.AsObject();
        json["packageFolders"] = new JsonObject { [root] = new JsonObject() }; json["project"]!["restore"]!["packagesPath"] = root;
        json["libraries"]!["sample/1.0.0"]!["sha512"] = Convert.ToBase64String(badArchiveHash ? new byte[64] : SHA512.HashData(new byte[] { 1, 2, 3 }));
        File.WriteAllBytes(f.Files[0], Bytes(json));
        var restoreInputs = f.Task.CopyInputs().Select(v => v.Path.EndsWith("/project.assets.json", StringComparison.Ordinal)
            ? v with { Bytes = new FileInfo(f.Files[0]).Length, Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f.Files[0]))) } : v);
        var packageInputs = paths.Select(path => new WorkflowEvidenceFile("packages/0/sample/1.0.0/" + Path.GetRelativePath(package, path).Replace('\\', '/'),
            new FileInfo(path).Length, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))));
        return (new WorkflowVerificationTask(f.Task.Binding, restoreInputs.Concat(packageInputs), []), f.Environment, f.Id, root, paths);
    }
    private static (WorkflowVerificationTask Task, WorkflowBuildEnvironment Environment, Guid Id, string[] Files) PrepareFiles(string output, string name = "Fixture")
    {
        string root = Path.Combine(output, "restore-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        Guid capture = Guid.NewGuid(), restore = Guid.NewGuid();
        string directory = Path.Combine(root, "out", "verification", "m8-6", "restores", restore.ToString("N"), "artifacts", "obj", name); Directory.CreateDirectory(directory);
        string source = Path.Combine(root, "out", "captured-build", capture.ToString("N"), "source");
        string[] files = [Path.Combine(directory, "project.assets.json"), Path.Combine(directory, name + ".csproj.nuget.g.props"), Path.Combine(directory, name + ".csproj.nuget.g.targets")];
        File.WriteAllBytes(files[0], Bytes(Sample(source, directory, name))); File.WriteAllText(files[1], "<Project />"); File.WriteAllText(files[2], "<Project />");
        var inputs = files.Select(f => new WorkflowEvidenceFile("restore/" + restore.ToString("N") + "/obj/" + name + "/" + Path.GetFileName(f), new FileInfo(f).Length, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))))).ToArray();
        var task = new WorkflowVerificationTask(new(1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), H, new(Guid.NewGuid(), 1, 2), capture, H, H, H, null,
            "editor-services-build-v1", "Debug", "default", "unverified-fixture-sdk", H), inputs, []);
        var environment = new WorkflowBuildEnvironment(root, @"C:\fixture\dotnet.exe", @"C:\fixture\powershell.exe", H, [new("scripts/WorkflowBuildInputs.ps1", 1, H)], TimeSpan.FromSeconds(90));
        return (task, environment, restore, files);
    }
}
