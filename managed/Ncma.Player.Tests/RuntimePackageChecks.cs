using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Application;
using Ncma.Assets.Runtime;
using Ncma.Player.App;
using Ncma.Scene;
using Ncma.Scene.Rendering;

internal static class RuntimePackageChecks
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    // Input is the trusted, completed import fixture. Output is a fresh test-owned runtime package,
    // not a merge into an installation and not a release/public distribution of private user assets.
    internal static void Run(string repository, string source, string output, string configuration, ProjectConfiguration config, SceneDocumentSnapshot scene)
    {
        byte[] bytes = SceneAssetPreparation.CreateRuntimePackage(source, config.ProjectId, scene, []);
        Check(bytes.SequenceEqual(SceneAssetPreparation.CreateRuntimePackage(source, config.ProjectId, scene, [])), "Repeated package changed bytes");
        string package = Path.Combine(repository, "out/package/m3-9", configuration, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(package, "sample/assets"));
        string referenceIndex = Path.Combine(repository, "out/verification/m2-7", configuration, "packages.json");
        using var baseline = JsonDocument.Parse(File.ReadAllBytes(referenceIndex)); string template = baseline.RootElement.GetProperty("playerDx11").GetString()!;
        DeploymentManifest.Validate(template);
        using(var shaderFiles=Ncma.Rendering.RuntimeShaderFileSet.Load(template)) {
            Check(shaderFiles.Count==4,"Standalone DX11 Player requires four scene shader variants");
            string shaders=Path.Combine(package,"assets/shaders");Directory.CreateDirectory(shaders);
            var shaderPaths=Directory.GetFiles(Path.Combine(template,"assets/shaders"));Check(shaderPaths.Length==5,"Exact shader variants and selection index");
            foreach(string file in shaderPaths)File.Copy(file,Path.Combine(shaders,Path.GetFileName(file)));
        }
        foreach (var file in Directory.EnumerateFiles(template).Where(p => Path.GetFileName(p) != DeploymentManifest.FileName && Path.GetExtension(p) is not (".pdb" or ".xml"))) File.Copy(file, Path.Combine(package, Path.GetFileName(file)));
        // Standalone direct tests can follow a source rebuild before canonical publish refreshes the
        // baseline template. Only replace exact files in this newly created, test-owned candidate.
        string managed = Path.Combine(repository, "managed/Ncma.Player.App/bin", configuration, "net8.0");
        foreach (string file in Directory.EnumerateFiles(managed).Where(p => Path.GetExtension(p) is not (".pdb" or ".xml"))) File.Copy(file, Path.Combine(package, Path.GetFileName(file)), true);
        Directory.CreateDirectory(Path.Combine(package, "plugins"));
        foreach (string name in new[] { "NcmaPlatform.dll", "NcmaRenderer.dll", "glfw3.dll", "NcmaAnimationKernel.dll" })
            File.Copy(Path.Combine(template, "plugins", name), Path.Combine(package, "plugins", name));
        File.WriteAllBytes(Path.Combine(package, "sample/assets/game.ncpak"), bytes);
        File.Copy(Path.Combine(source, "start.ncmascene"), Path.Combine(package, "sample/start.ncmascene"));
        foreach (string name in new[] { "Ncma.Gameplay.Sample.dll", "Ncma.Gameplay.Sample.deps.json" }) File.Copy(Path.Combine(repository, "out/managed", name), Path.Combine(package, "sample", name));
        config = config with { GameplayAssembly = "Ncma.Gameplay.Sample.dll", AssetPackage = "assets/game.ncpak" };
        var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        string project = Path.Combine(package, "sample/game.ncmaproject"); File.WriteAllBytes(project, JsonSerializer.SerializeToUtf8Bytes(config, json));
        var files = Directory.EnumerateFiles(package, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(path => new {
            path = Path.GetRelativePath(package, path).Replace('\\', '/'), size = new FileInfo(path).Length, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) }).ToArray();
        File.WriteAllBytes(Path.Combine(package, DeploymentManifest.FileName), JsonSerializer.SerializeToUtf8Bytes(new {
            schemaVersion = 1, product = "NcmaPlayer-m3-9-runtime-package-candidate", configuration, rid = "win-x64", tfm = "net8.0", publishMode = "framework-dependent",
            production = false, manualAcceptance = false, selfContainedVerified = false,
            assetPackage = new { version = 1, path = "sample/assets/game.ncpak", sha256 = Convert.ToHexString(SHA256.HashData(bytes)), assets = RuntimeAssetPackage.Inspect(bytes, config.ProjectId).Assets.Length },
            resourceKernels = new[] { new { id = "ncma.pose", abiVersion = 1, path = "plugins/NcmaAnimationKernel.dll", lazy = true } }, files }, json));
        DeploymentManifest.Validate(package);
        string[] forbidden = [".fbx", ".ncmeta", ".ncmaterial", ".ncmatset", ".nim", ".nca", ".py", ".pdb", "NcmaNative", "NcmaImport", "Ncma.Asset.Import", "Ncma.Assets.Authoring", "Ncma.Editor", "Ncma.Gui"];
        foreach (var file in files) foreach (string marker in forbidden) Check(!file.path.Contains(marker, StringComparison.OrdinalIgnoreCase), "Authoring dependency in runtime package: " + file.path);
        string deps = File.ReadAllText(Path.Combine(package, "NcmaPlayer.deps.json")); foreach (string marker in forbidden.Where(m => m.StartsWith("Ncma"))) Check(!deps.Contains(marker), "Forbidden managed runtime dependency");
        var options = PlayerOptions.Parse(["--project", project, "--ticks", "6", "--max-runtime-seconds", "20", "--report", Path.Combine(output, Guid.NewGuid().ToString("N") + "-packed-player.json")]);
        var report = PlayerRunner.Run(options, pluginRoot: Path.Combine(package, "plugins"), visible: false);
        Check(report.ExitCode == 0 && report.Tick >= 6 && report.RenderedFrames > 0 && report.ValidationErrors == 0 && report.ValidationWarnings == 0 && report.ShutdownErrors.Length == 0,
            "Packed graphical scene failed: " + report.Reason);
        byte[] savedScene = File.ReadAllBytes(Path.Combine(package, "sample/start.ncmascene"));
        for (int cycle = 0; cycle < 32; cycle++) {
            var restart = PlayerOptions.Parse(["--project", project, "--ticks", "2", "--max-runtime-seconds", "20", "--report", Path.Combine(output, Guid.NewGuid().ToString("N") + "-packed-cycle.json")]);
            var result = PlayerRunner.Run(restart, pluginRoot: Path.Combine(package, "plugins"), visible: false);
            Check(result.ExitCode == 0 && result.Tick >= 2 && result.RenderedFrames > 0 && result.ValidationErrors == 0 && result.ValidationWarnings == 0 && result.ShutdownErrors.Length == 0, "Packed create/Play/Stop cycle failed");
            // Successful renderer/pose shutdown rejects outstanding resource leases. Independently
            // verify that the final RuntimeAssetLease released the entire immutable package pin.
            using var unlocked = new FileStream(Path.Combine(package, "sample/assets/game.ncpak"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        Check(savedScene.SequenceEqual(File.ReadAllBytes(Path.Combine(package, "sample/start.ncmascene"))), "Packed Play changed the authored scene");
        // Real moved apphosts from an unrelated cwd, without resident authoring/import assemblies.
        // Null validates the pack without initializing GPU/pose; graphical exercises the actual DLLs.
        foreach (bool headless in new[] { true, false }) {
            var start = new ProcessStartInfo(Path.Combine(package, "NcmaPlayer.exe")) { WorkingDirectory = output, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            string childReport = Path.Combine(output, Guid.NewGuid().ToString("N") + "-packed-apphost.json");
            foreach (string arg in new[] { "--project", project, "--ticks", "4", "--max-runtime-seconds", "20", "--report", childReport }) start.ArgumentList.Add(arg);
            if (headless) start.ArgumentList.Add("--headless");
            using (var process = Process.Start(start)!) {
                var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(30000)) { process.Kill(true); process.WaitForExit(); throw new Exception("Packed apphost watchdog"); }
                Check(process.ExitCode == 0, stdout.Result + stderr.Result);
            }
            using var child = JsonDocument.Parse(File.ReadAllBytes(childReport)); var result = child.RootElement;
            Check(result.GetProperty("tick").GetUInt64() >= 4 && result.GetProperty("shutdownErrors").GetArrayLength() == 0, "Packed apphost did not complete/release");
            Check(result.GetProperty("validationErrors").GetUInt64() == 0 && result.GetProperty("validationWarnings").GetUInt64() == 0, "Packed apphost GPU validation failed");
            Check(headless ? result.GetProperty("modules").GetArrayLength() == 0 && result.GetProperty("tick").GetUInt64() == 4 :
                result.GetProperty("renderedFrames").GetUInt64() > 0 && result.GetProperty("modules").GetArrayLength() == 2, "Packed apphost loaded wrong services");
        }
        string packed = Path.Combine(package, "sample/assets/game.ncpak");
        // Corruption is at an exact fresh fixture target after all readers are closed. Keep the
        // original bytes for evidence; never alter the authored source or a checked installation.
        File.WriteAllBytes(Path.Combine(output, Guid.NewGuid().ToString("N") + "-valid-package.ncpak"), bytes);
        byte[] bad = (byte[])bytes.Clone(); bad[^1] ^= 1; File.WriteAllBytes(packed, bad);
        var rejectedOptions = PlayerOptions.Parse(["--project", project, "--ticks", "6", "--report", Path.Combine(output, Guid.NewGuid().ToString("N") + "-corrupt-packed-player.json")]);
        var rejected = PlayerRunner.Run(rejectedOptions, pluginRoot: Path.Combine(package, "plugins"), visible: false);
        Check(rejected.ExitCode == 3 && rejected.Tick == 0 && rejected.Modules.Length == 0 && rejected.RenderedFrames == 0, "Corrupt package started gameplay/GPU or fell back");
        var rejectedNull = PlayerRunner.Run(PlayerOptions.Parse(["--project", project, "--headless", "--ticks", "2", "--report", Path.Combine(output, Guid.NewGuid().ToString("N") + "-corrupt-packed-null.json")]), visible: false);
        Check(rejectedNull.ExitCode == 3 && rejectedNull.Tick == 0 && rejectedNull.Modules.Length == 0 && rejectedNull.ShutdownErrors.Length == 0, "Corrupt Null package started gameplay or leaked pins");
        bool manifestRejected = false; try { DeploymentManifest.Validate(package); } catch (ArgumentException) { manifestRejected = true; }
        Check(manifestRejected, "Manifest accepted changed asset bytes");
        File.WriteAllBytes(packed, bytes); DeploymentManifest.Validate(package);
        Console.WriteLine("M3.9 32 packed create/Play/Stop cycles: validation 0/0, shutdown guards and final package pins released");
        Console.WriteLine("M3.9 source-free Player candidate: " + package);
    }
}
