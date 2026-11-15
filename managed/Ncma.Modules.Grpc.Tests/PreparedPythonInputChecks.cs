using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Synthetic receipt/IO contract cases only. These files are never interpreters,
// wheels or a positive real-environment preparation substitute.
internal static class PreparedPythonInputChecks
{
    internal static void Run(string repository) => CaseHarness.RunOrThrow("Prepared Python input contracts", [
        ("required preparation and malformed argument refusal", () => {
            string[] ordinary = [repository, "Debug"], original = ordinary;
            Check(PreparedPythonInputs.Extract(ref ordinary, repository, false) is null && ReferenceEquals(original, ordinary));
            RejectArgs([repository], repository, true);
            RejectArgs([repository, "--python-environment"], repository, false);
            RejectArgs([repository, "--python-environment", "relative", new string('A', 64)], repository, false);
            RejectArgs([repository, "--python-environment", repository, new string('A', 64), "extra"], repository, false);
            RejectArgs([repository, "--python-environment", repository, new string('A', 64), "--python-environment", repository, new string('A', 64)], repository, false);
        }),
        ("exact copied roots and exclusive source locks remain available", () => {
            var fixture = Fixture(repository); string[] args = [fixture.Workspace, "--owned-host", "--python-environment", fixture.Root, fixture.Hash];
            var input = PreparedPythonInputs.Extract(ref args, fixture.Workspace, true)!;
            Check(args.SequenceEqual(new[] { fixture.Workspace, "--owned-host" }) && input.EnvironmentRoot == Path.Combine(fixture.Root, "venv") && input.WorkerRoot == Path.Combine(fixture.Inputs, "worker"));
            using (File.Open(Path.Combine(input.WorkerRoot, "ncma_module_worker.py"), FileMode.Open, FileAccess.Read, FileShare.None))
            using (File.Open(Path.Combine(input.EnvironmentRoot, "Scripts/python.exe"), FileMode.Open, FileAccess.Read, FileShare.None))
            using (File.Open(Path.Combine(fixture.Inputs, "runtime/python.exe"), FileMode.Open, FileAccess.Read, FileShare.None)) { }
            input.VerifyUnchanged();
        }),
        ("changed prepared bytes cannot pass the post-run audit", () => {
            var fixture = Fixture(repository); var input = Open(fixture);
            File.AppendAllText(Path.Combine(fixture.Root, "venv/Scripts/python.exe"), "changed");
            Reject(input.VerifyUnchanged);
        }),
        ("added and changed copied source files cannot pass", () => {
            var added = Fixture(repository); var input = Open(added);
            File.WriteAllText(Path.Combine(added.Inputs, "worker/extra.py"), "not executed"); Reject(input.VerifyUnchanged);
            var changed = Fixture(repository); input = Open(changed);
            File.AppendAllText(Path.Combine(changed.Inputs, "worker/ncma_module_worker.py"), "changed"); Reject(input.VerifyUnchanged);
        }),
        ("receipt hash and duplicate property reject without argv changes", () => {
            var fixture = Fixture(repository);
            RejectArgs([fixture.Workspace, "--python-environment", fixture.Root, new string('F', 64)], fixture.Workspace, true);
            string path = Path.Combine(fixture.Root, "receipt.json"), text = File.ReadAllText(path);
            File.WriteAllText(path, "{\"kind\":\"duplicate\"," + text[1..], new UTF8Encoding(false));
            RejectArgs([fixture.Workspace, "--python-environment", fixture.Root, Hash(File.ReadAllBytes(path))], fixture.Workspace, true);
        }),
        ("foreign base interpreter cannot be substituted by a matching manifest", () => {
            var fixture = Fixture(repository, wrongBase: true);
            RejectArgs([fixture.Workspace, "--python-environment", fixture.Root, fixture.Hash], fixture.Workspace, true);
        })
    ]);

    private sealed record TestFixture(string Workspace, string Root, string Inputs, string Hash);
    private sealed record Row(string path, long size, string sha256);
    private static TestFixture Fixture(string repository, bool wrongBase = false)
    {
        string workspace = Path.GetFullPath(Path.Combine(repository, "out/verification/m8-6/python-routing-contracts", Guid.NewGuid().ToString("N")));
        string inputRoot = Path.GetFullPath(Path.Combine(workspace, "out/verification/m8-6/python-inputs", Guid.NewGuid().ToString("N")));
        string inputs = Path.Combine(inputRoot, "inputs");
        string root = Path.GetFullPath(Path.Combine(workspace, "out/verification/m8-6/python-environments", Guid.NewGuid().ToString("N")));
        void Write(string path, string text)
        { Directory.CreateDirectory(Path.GetDirectoryName(path)!); using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write); file.Write(Encoding.UTF8.GetBytes(text)); }
        foreach (string file in new[] { "ncma_module_worker.py", "tests/retained_work_probe.py", "tests/malformed_reply_probe.py", "requirements-win-py312.lock" })
            Write(Path.Combine(inputs, "worker", file), "synthetic data only; never executed");
        Write(Path.Combine(inputs, "runtime/python.exe"), "synthetic data only; never executed");
        var inputFiles = Files(inputs); string inputHash = InventoryHash(inputFiles);
        Write(Path.Combine(inputRoot, "receipt.json"), JsonSerializer.Serialize(new {
            kind = "ncma.python-worker.inputs.v1", root = inputRoot, sourceRole = "copied-inputs", files = inputFiles, inputsSha256 = inputHash,
            networkAuthorized = false, interpreterExecuted = false, environmentCreated = false, testsExecuted = false, installed = false, accepted = false
        }));
        string executable = wrongBase ? Path.Combine(workspace, "foreign/python.exe") : Path.Combine(inputs, "runtime/python.exe");
        Write(Path.Combine(root, "venv/pyvenv.cfg"), "executable = " + executable + "\n");
        Write(Path.Combine(root, "venv/Scripts/python.exe"), "synthetic data only; never executed");
        var files = Files(root);
        Write(Path.Combine(root, "receipt.json"), JsonSerializer.Serialize(new {
            kind = "ncma.python-worker.environment.v1", root, inputsRoot = inputRoot,
            inputsReceiptSha256 = Hash(File.ReadAllBytes(Path.Combine(inputRoot, "receipt.json"))), inputsSha256 = inputHash,
            sourceRole = "copied-inputs", environment = Path.Combine(root, "venv"), worker = Path.Combine(inputs, "worker"),
            files, filesSha256 = InventoryHash(files), networkAuthorized = false, workerExecuted = false, testsExecuted = false,
            canonicalEnvironmentReplaced = false, installed = false, accepted = false
        }));
        return new(workspace, root, inputs, Hash(File.ReadAllBytes(Path.Combine(root, "receipt.json"))));
    }
    private static Row[] Files(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .Select(path => new Row(Path.GetRelativePath(root, path).Replace('\\', '/'), new FileInfo(path).Length, Hash(File.ReadAllBytes(path))))
        .OrderBy(row => row.path, StringComparer.Ordinal).ToArray();
    private static string InventoryHash(Row[] files)
    {
        var text = new StringBuilder("ncma-source-inputs-v1\n");
        foreach (var row in files)
            text.Append(Encoding.UTF8.GetByteCount(row.path).ToString(CultureInfo.InvariantCulture)).Append(':').Append(row.path).Append('\n')
                .Append(row.size.ToString(CultureInfo.InvariantCulture)).Append(':').Append(row.sha256).Append('\n');
        return Hash(Encoding.UTF8.GetBytes(text.ToString()));
    }
    private static PreparedPythonInputs Open(TestFixture fixture)
    { string[] args = [fixture.Workspace, "--python-environment", fixture.Root, fixture.Hash]; return PreparedPythonInputs.Extract(ref args, fixture.Workspace, true)!; }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Prepared Python contract assertion."); }
    private static void Reject(Action action)
    {
        bool rejected = false;
        try { action(); } catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or InvalidOperationException) { rejected = true; }
        Check(rejected);
    }
    private static void RejectArgs(string[] args, string workspace, bool required)
    {
        string[] original = args;
        Reject(() => PreparedPythonInputs.Extract(ref args, workspace, required));
        Check(ReferenceEquals(args, original));
    }
}
