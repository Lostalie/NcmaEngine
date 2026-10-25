using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security;
using System.Security.Cryptography;
using System.Text.Json;

// Explicit whole-E6 test modes, never called by build. All mutations are in a fresh
// private verification directory; no cleanup, staging, application launch or deployment.
internal sealed class BuildContractTests
{
    private readonly string repository, configuration, dotnet, run;
    private readonly string? formalBefore;
    private readonly List<object> commands = [];
    private readonly List<string> passed = [];
    private int revision;
    private BuildContractTests(string root, string config, string host, string mode)
    {
        repository = Path.GetFullPath(root); configuration = config; dotnet = Path.GetFullPath(host);
        Check(config is "Debug" or "Release", "Configuration must be explicit Debug or Release.");
        Check(File.Exists(Path.Combine(repository, "managed/Ncma.CodeGen/Ncma.CodeGen.csproj")) && File.Exists(Path.Combine(repository, "managed", "Directory.Build.targets")), "Not an engine workspace.");
        Check(File.Exists(dotnet) && Path.GetFileName(dotnet).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase), "Expected an explicit Windows dotnet host.");
        run = Path.Combine(repository, "out", "verification", "ecs-e6", "codegen-build-contracts", config, mode + "-" + Guid.NewGuid().ToString("N"));
        PlainPath(run); Check(!Directory.Exists(run) && !File.Exists(run), "Run directory must be new.");
        Directory.CreateDirectory(run);
        string formal = Path.Combine(repository, "out/bin/NcmaEngine.exe");
        formalBefore = File.Exists(formal) ? Hash(formal) : null;
    }

    internal static async Task<int> Run(string[] args)
    {
        if (args.Length != 4 || args[0] is not ("--build-contracts" or "--publish-contracts")) {
            Console.Error.WriteLine("Usage: --build-contracts|--publish-contracts <repository> <Debug|Release> <absolute-dotnet.exe>"); return 1;
        }
        BuildContractTests? suite = null;
        try {
            suite = new(args[1], args[2], args[3], args[0][2..]);
            Console.WriteLine("Retained evidence: " + suite.run);
            suite.Write("source-pins.json", JsonSerializer.Serialize(new[] {
                "managed/Ncma.CodeGen/Ncma.CodeGen.csproj", "managed/Directory.Build.targets", "managed/Ncma.CodeGen/ComponentGenerator.cs",
                "managed/Ncma.CodeGen/ComponentIdentities.cs", "managed/Ncma.CodeGen/ComponentAggregation.cs", "managed/Ncma.CodeGen.Tests/BuildContractTests.cs"
            }.Select(p => new { path = p, sha256 = Hash(Path.Combine(suite.repository, p)) })));
            if (args[0] == "--build-contracts") await suite.BuildCases(); else await suite.PublishCases();
            return suite.Receipt(null) ? 0 : 1;
        } catch (Exception e) { suite?.Receipt(e.ToString()); Console.Error.WriteLine(e); return 1; }
    }

    private async Task BuildCases()
    {
        string sdk = await Command("00-sdk-selected", ["--version"], binaryLog: false);
        Check(Version.TryParse(sdk.Trim().Split('-')[0], out var version) && version.Major >= 8, "Engine C# 12 build requires SDK 8 or newer.");
        passed.Add("SDK selection recorded without patch-version pin");
        string project = Project("Fixture"); Write("Fixture/Component.cs", Component("fixture.before"));
        await Build("01-clean", project);
        PortableGenerator(Dll("Ncma.CodeGen"));
        string dll = Dll("Fixture"); var initial = Contract(dll); var text = Generated("Fixture", "artifacts");
        Check(initial.Single().StartsWith("fixture.before|1|", StringComparison.Ordinal), "Initial manifest missing.");
        string hash = Hash(dll); var stamp = File.GetLastWriteTimeUtc(dll);
        await Build("02-noop", project);
        Check(Hash(dll) == hash && File.GetLastWriteTimeUtc(dll) == stamp, "No-op build rewrote the assembly.");
        Check(text.SequenceEqual(Generated("Fixture", "artifacts")), "No-op changed generated text.");
        passed.Add("clean/no-op assembly and generated text");

        Write("Fixture/Component.cs", Component("fixture.after")); await Build("03-edit", project);
        Check(Contract(dll).Single().StartsWith("fixture.after|1|", StringComparison.Ordinal), "Edited identity retained old manifest.");
        await Build("04-independent-clean", project, artifacts: "clean-artifacts");
        Check(Contract(dll).SequenceEqual(Contract(Dll("Fixture", "clean-artifacts"))), "Clean/incremental manifests differ.");
        Check(Generated("Fixture", "artifacts").SequenceEqual(Generated("Fixture", "clean-artifacts")), "Clean/incremental generated text differs.");
        passed.Add("edit and independent clean generation");

        Write("Fixture/Component.cs", "namespace Fixture; public readonly partial record struct Position(float X) : Ncma.Runtime.IComponent;");
        await Build("05-remove-declaration", project);
        Check(Contract(dll).Length == 0 && !HasGeneratedType(dll), "Removed declaration still has generated metadata/accessors.");
        // EmitCompilerGeneratedFiles may retain diagnostic files. Only the new assembly
        // is authoritative; those files live outside Compile inputs, never re-imported.
        Write("Fixture/Component.cs", Component("fixture.restored")); await Build("06-restore", project);
        Check(Contract(dll).Single().StartsWith("fixture.restored|1|", StringComparison.Ordinal), "Restoration failed.");
        hash = Hash(dll);
        Write("Fixture/Component.cs", Component("ncma.transform"));
        await Build("07-local-conflict", project, expected: "NCMA007");
        Check(Hash(dll) == hash, "Failed compile changed last successful assembly.");
        Write("Fixture/Component.cs", Component("fixture.restored")); await Build("08-recover", project);
        passed.Add("declaration removal/restoration and failed-build recovery");

        string left = Project("Left"), right = Project("Right");
        Write("Left/Component.cs", Component("fixture.shared", "LeftSpace"));
        Write("Right/Component.cs", Component("fixture.other", "RightSpace"));
        string composition = Project("Composition", References(left, right));
        Write("Composition/Component.cs", "public class CompositionMarker {}");
        await Build("09-reference-baseline", composition);
        hash = Hash(Dll("Composition"));
        Write("Right/Component.cs", Component("fixture.shared", "RightSpace"));
        await Build("10-reference-conflict", composition, expected: "NCMA007");
        Check(Hash(Dll("Composition")) == hash, "Conflicting reference replaced valid consumer.");
        Project("Composition", References(left)); await Build("11-reference-remove", composition);
        var removedAggregate = Aggregate("Composition", "artifacts");
        Check(removedAggregate.Contains("global::Left.Generated.ComponentModule.Module", StringComparison.Ordinal) &&
            !removedAggregate.Contains("global::Right.Generated.ComponentModule.Module", StringComparison.Ordinal), "Removed module remains in aggregate.");
        Check(!ReferencesAssembly(Dll("Composition"), "Right"), "Removed module remains in emitted PE references.");
        // A new artifacts root forces compilation without deleting prior output/evidence.
        await Build("11b-reference-remove-clean", composition, artifacts: "reference-removed-clean");
        Check(removedAggregate == Aggregate("Composition", "reference-removed-clean"), "Removed-reference incremental and clean aggregates differ.");
        Check(!ReferencesAssembly(Dll("Composition", "reference-removed-clean"), "Right"), "Clean rebuild retained removed module PE reference.");
        Project("Composition", References(left, right));
        Write("Right/Component.cs", Component("fixture.fixed", "RightSpace"));
        await Build("12-reference-repair", composition);
        Check(Aggregate("Composition", "artifacts").Contains("global::Right.Generated.ComponentModule.Module", StringComparison.Ordinal) &&
            ReferencesAssembly(Dll("Composition"), "Right"), "Restored module did not return to aggregate/PE.");
        passed.Add("composition-only reference edit/removal/repair");

        // Exercise actual reference resolution, not synthetic injection into the guard item.
        Project("Fixture", "<PackageReference Include=\"Microsoft.CodeAnalysis.CSharp\" Version=\"4.8.0\" />");
        await Build("14-runtime-leak", project, expected: "NCMADEP001");
        Project("Fixture", "<PackageReference Include=\"Microsoft.CodeAnalysis.CSharp\" Version=\"4.8.0\" ExcludeAssets=\"all\" PrivateAssets=\"all\" GeneratePathProperty=\"true\" /><Content Include=\"$(PkgMicrosoft_CodeAnalysis_CSharp)/lib/netstandard2.0/Microsoft.CodeAnalysis.CSharp.dll\" CopyToPublishDirectory=\"Always\" TargetPath=\"Microsoft.CodeAnalysis.CSharp.dll\" />");
        await Publish("15-publish-leak", project, "fixture-rejected-publish", "NCMADEP002");
        Project("Fixture"); await Publish("16-clean-publish", project, "fixture-publish");
        AuditAndPin("fixture", Owned("fixture-publish"));
        passed.Add("portable generator / reference leak / publish leak / clean publication");
    }

    private async Task PublishCases()
    {
        var failures = new List<Exception>();
        foreach (string product in new[] { "Ncma.Editor.App", "Ncma.Player.App" }) {
            try {
            string project = Path.Combine(repository, "managed", product, product + ".csproj");
            await Publish(product, project, "published/" + product);
            string directory = Owned("published/" + product);
            string name = product == "Ncma.Editor.App" ? "NcmaEngine" : "NcmaPlayer";
            Check(File.Exists(Path.Combine(directory, name + ".exe")) && File.Exists(Path.Combine(directory, name + ".runtimeconfig.json")), "Missing real apphost/runtime configuration.");
            AuditAndPin(product, directory); passed.Add(product + " framework-dependent win-x64 isolated publish/dependency audit (not launched)");
            } catch (Exception error) {
                failures.Add(new InvalidOperationException(product + " isolated publish failed.", error));
                Console.Error.WriteLine("FAIL publish " + product + ": " + error);
            }
        }
        Console.WriteLine($"SUMMARY isolated publish: total=2, passed={2 - failures.Count}, failed={failures.Count}");
        if (failures.Count != 0) throw new AggregateException("Isolated publish cases failed after both products were attempted.", failures);
    }

    private Task Build(string label, string project, string? expected = null, string artifacts = "artifacts") => Command(label,
        ["build", project, "--artifacts-path", Owned(artifacts), "--disable-build-servers", "--configuration", configuration,
         "--nologo", "-v:minimal", "-m:1", "-p:UseSharedCompilation=false", "-p:EmitCompilerGeneratedFiles=true"], expected);
    private Task Publish(string label, string project, string output, string? expected = null) => Command(label,
        ["publish", project, "--artifacts-path", Owned("artifacts"), "--output", Owned(output), "--disable-build-servers",
         "--configuration", configuration, "--runtime", "win-x64", "--self-contained", "false", "--nologo", "-v:minimal", "-m:1",
         "-p:UseSharedCompilation=false", "-p:UseAppHost=true", "-p:PublishAot=false", "-p:PublishTrimmed=false", "-p:PublishSingleFile=false"], expected);

    private async Task<string> Command(string label, string[] arguments, string? expected = null, bool binaryLog = true, string? cwd = null)
    {
        // Each command retains source history, separate streams, binary log and exact argv.
        var argv = binaryLog ? arguments.Concat(new[] { "-bl:" + Owned("logs/" + label + ".binlog") }).ToArray() : arguments;
        string stdout = Owned("logs/" + label + ".stdout.log"), stderr = Owned("logs/" + label + ".stderr.log");
        Directory.CreateDirectory(Path.GetDirectoryName(stdout)!);
        Write("logs/" + label + ".command.json", JsonSerializer.Serialize(new { dotnet, argv, cwd = cwd ?? repository, expected }));
        var info = new ProcessStartInfo(dotnet) { WorkingDirectory = cwd ?? repository, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in argv) info.ArgumentList.Add(arg);
        info.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        // Desktop hosts may expose both casing variants; normalize only this child.
        string? path = Environment.GetEnvironmentVariable("PATH");
        foreach (string key in info.Environment.Keys.Where(k => k.Equals("PATH", StringComparison.OrdinalIgnoreCase)).ToArray()) info.Environment.Remove(key);
        if (path is not null) info.Environment["PATH"] = path;
        using var process = new Process { StartInfo = info };
        var clock = Stopwatch.StartNew(); Check(process.Start(), "Unable to start compiler.");
        Write("logs/" + label + ".process.json", JsonSerializer.Serialize(new { process.Id, startUtc = process.StartTime.ToUniversalTime(), dotnet }));
        await using var output = new FileStream(stdout, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        await using var error = new FileStream(stderr, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        var drains = Task.WhenAll(process.StandardOutput.BaseStream.CopyToAsync(output), process.StandardError.BaseStream.CopyToAsync(error));
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(180)); }
        catch (TimeoutException) {
            commands.Add(new { label, process.Id, timeout = true, elapsedSeconds = clock.Elapsed.TotalSeconds });
            // Only the exact owned child tree, never name-based process discovery or retries.
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await drains.WaitAsync(TimeSpan.FromSeconds(10));
            throw new TimeoutException("Compiler timed out; retained evidence: " + label);
        }
        await drains.WaitAsync(TimeSpan.FromSeconds(10)); await output.FlushAsync(); await error.FlushAsync();
        await output.DisposeAsync(); await error.DisposeAsync();
        string combined = File.ReadAllText(stdout) + "\n" + File.ReadAllText(stderr);
        commands.Add(new { label, exitCode = process.ExitCode, expected, elapsedSeconds = clock.Elapsed.TotalSeconds });
        Check(expected is null ? process.ExitCode == 0 : process.ExitCode != 0 && combined.Contains(expected, StringComparison.Ordinal),
            $"{label}: unexpected exit/diagnostic; expected {expected ?? "success"}, exit {process.ExitCode}. See retained logs.");
        Console.WriteLine("PASS " + label + (expected is null ? "" : " (expected rejection " + expected + ")"));
        return combined;
    }

    private string Project(string name, string items = "")
    {
        string Escape(string p) => SecurityElement.Escape(p)!;
        Write(name + "/" + name + ".csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><TreatWarningsAsErrors>true</TreatWarningsAsErrors><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
              <ItemGroup><Compile Include="Component.cs" /><ProjectReference Include="{{Escape(Path.Combine(repository, "managed/Ncma.Runtime/Ncma.Runtime.csproj"))}}" />{{items}}</ItemGroup>
              <Import Project="{{Escape(Path.Combine(repository, "managed/Directory.Build.targets"))}}" />
            </Project>
            """);
        return Owned(name + "/" + name + ".csproj");
    }
    private static string References(params string[] paths) => string.Concat(paths.Select(p => "<ProjectReference Include=\"" + SecurityElement.Escape(p) + "\" />"));
    private string Dll(string name, string artifacts = "artifacts") => Owned($"{artifacts}/bin/{name}/{configuration.ToLowerInvariant()}/{name}.dll");
    private string Aggregate(string name, string artifacts) => File.ReadAllText(Files(Owned($"{artifacts}/obj/{name}/{configuration.ToLowerInvariant()}/generated"))
        .Single(p => Path.GetFileName(p) == "Assembly.ComponentModule.g.cs"));
    private static bool ReferencesAssembly(string path, string name)
    {
        using var file = File.OpenRead(path); using var pe = new PEReader(file); var reader = pe.GetMetadataReader();
        return reader.AssemblyReferences.Any(h => reader.GetString(reader.GetAssemblyReference(h).Name) == name);
    }
    private string[] Generated(string name, string artifacts) => Files(Owned($"{artifacts}/obj/{name}/{configuration.ToLowerInvariant()}/generated")).Where(p => p.EndsWith(".Component.g.cs", StringComparison.Ordinal))
        .OrderBy(Path.GetFileName, StringComparer.Ordinal).Select(p => Path.GetFileName(p) + "\n" + File.ReadAllText(p)).ToArray();
    private static string[] Contract(string path)
    {
        using var file = File.OpenRead(path); using var pe = new PEReader(file); var reader = pe.GetMetadataReader();
        var values = new List<string>();
        foreach (var handle in reader.GetAssemblyDefinition().GetCustomAttributes()) {
            var a = reader.GetCustomAttribute(handle);
            if (a.Constructor.Kind != HandleKind.MemberReference) continue;
            var parent = reader.GetMemberReference((MemberReferenceHandle)a.Constructor).Parent;
            if (parent.Kind != HandleKind.TypeReference) continue;
            var type = reader.GetTypeReference((TypeReferenceHandle)parent);
            if (reader.GetString(type.Namespace) != "Ncma.Runtime" || reader.GetString(type.Name) != "GeneratedComponentIdentityAttribute") continue;
            var blob = reader.GetBlobReader(a.Value); Check(blob.ReadUInt16() == 1, "Bad custom attribute prolog.");
            values.Add(blob.ReadSerializedString() + "|" + blob.ReadInt32() + "|" + blob.ReadSerializedString());
        }
        return values.Order(StringComparer.Ordinal).ToArray();
    }
    private static bool HasGeneratedType(string path)
    {
        using var file = File.OpenRead(path); using var pe = new PEReader(file); var reader = pe.GetMetadataReader();
        return reader.TypeDefinitions.Any(h => reader.GetString(reader.GetTypeDefinition(h).Name) == "Generated");
    }
    private static void PortableGenerator(string path)
    {
        using var file = File.OpenRead(path); using var pe = new PEReader(file); var reader = pe.GetMetadataReader();
        bool standard = false;
        foreach (var handle in reader.GetAssemblyDefinition().GetCustomAttributes()) {
            var attribute = reader.GetCustomAttribute(handle);
            if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
            var parent = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent;
            if (parent.Kind != HandleKind.TypeReference) continue;
            var type = reader.GetTypeReference((TypeReferenceHandle)parent);
            if (reader.GetString(type.Namespace) != "System.Runtime.Versioning" || reader.GetString(type.Name) != "TargetFrameworkAttribute") continue;
            var blob = reader.GetBlobReader(attribute.Value); Check(blob.ReadUInt16() == 1, "Invalid framework attribute.");
            standard = blob.ReadSerializedString() == ".NETStandard,Version=v2.0";
        }
        Check(standard, "Generator must target netstandard2.0, not a Core runtime.");
        var roslyn = reader.AssemblyReferences.Select(reader.GetAssemblyReference)
            .Where(r => reader.GetString(r.Name).StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal)).ToArray();
        Check(roslyn.Length == 2 && roslyn.All(r => r.Version == new Version(4, 8, 0, 0)), "Compiler API baseline drifted above Roslyn 4.8.");
    }
    private void AuditAndPin(string name, string directory)
    {
        var files = Files(directory).ToArray(); Check(files.Length > 0, "Empty product.");
        var deps = files.Where(p => p.EndsWith(".deps.json", StringComparison.Ordinal)).ToArray(); Check(deps.Length > 0, "Missing dependency manifest.");
        static bool Compiler(string name) => name.StartsWith("Ncma.CodeGen", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase);
        foreach (string file in files) {
            PlainPath(file); Check(!Compiler(Path.GetFileName(file)), "Compiler file in product: " + file);
            if (!file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
            using var stream = File.OpenRead(file); using var pe = new PEReader(stream);
            if (!pe.HasMetadata) continue;
            var metadata = pe.GetMetadataReader();
            Check(metadata.AssemblyReferences.All(r => !Compiler(metadata.GetString(metadata.GetAssemblyReference(r).Name))), "Compiler reference in product assembly: " + file);
        }
        foreach (string file in deps) {
            using var json = JsonDocument.Parse(File.ReadAllText(file));
            Check(json.RootElement.GetProperty("libraries").EnumerateObject().All(p => !Compiler(p.Name)), "Compiler dependency in deps: " + file);
        }
        Write("audits/" + name + ".json", JsonSerializer.Serialize(files.Order(StringComparer.Ordinal).Select(p =>
            new { path = Path.GetRelativePath(directory, p), bytes = new FileInfo(p).Length, sha256 = Hash(p) })));
    }
    private bool Receipt(string? failure)
    {
        string formal = Path.Combine(repository, "out/bin/NcmaEngine.exe");
        string? formalAfter = File.Exists(formal) ? Hash(formal) : null;
        if (formalBefore != formalAfter) failure ??= "Formal EXE changed during isolated validation.";
        Write("result.json", JsonSerializer.Serialize(new { schemaVersion = 1, configuration, run, passed, commands, failure, formalBefore, formalAfter,
            success = failure is null, applicationLaunched = false, deployed = false, performanceAcceptance = false, sourcePinsAreFullFreeze = false }, new JsonSerializerOptions { WriteIndented = true }));
        return failure is null;
    }
    private static IEnumerable<string> Files(string directory)
    {
        var pending = new Stack<string>(); pending.Push(directory);
        while (pending.TryPop(out string? current)) {
            PlainPath(current);
            foreach (string file in Directory.EnumerateFiles(current)) { PlainPath(file); yield return file; }
            foreach (string child in Directory.EnumerateDirectories(current)) { PlainPath(child); pending.Push(child); }
        }
    }
    private string Owned(string relative)
    {
        string path = Path.GetFullPath(Path.Combine(run, relative));
        Check(path.StartsWith(run + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Path outside private run."); PlainPath(path); return path;
    }
    private void Write(string relative, string text)
    {
        string path = Owned(relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path)) { string backup = Owned($"history/{++revision:D4}-{Path.GetFileName(path)}"); Directory.CreateDirectory(Path.GetDirectoryName(backup)!); File.Copy(path, backup); }
        File.WriteAllText(path, text);
    }
    private static void PlainPath(string path)
    {
        for (string? part = Path.GetFullPath(path); part is not null; part = Path.GetDirectoryName(part))
            if (Path.Exists(part)) Check((File.GetAttributes(part) & FileAttributes.ReparsePoint) == 0, "Reparse path rejected: " + part);
    }
    private static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static string Component(string id, string ns = "Fixture") => $$$$"""
        namespace {{{{ns}}}};
        [Ncma.Runtime.GenerateComponent("{{{{id}}}}", 1, "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"x\"],\"properties\":{\"x\":{\"type\":\"number\"}}}", nameof(Validate), nameof(Default))]
        [Ncma.Runtime.ComponentField("value.x", "X", "x")]
        public readonly partial record struct Position(float X) : Ncma.Runtime.IComponent
        {
            public static Position Validate(Position value) => value;
            public static Position Default => new(0);
        }
        """;
}
