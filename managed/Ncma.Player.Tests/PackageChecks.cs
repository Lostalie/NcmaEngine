using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Application;
using Ncma.Gameplay;
using Ncma.Player.App;

internal static class PackageChecks
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Expected package rejection."); }
    private static void CopyPackage(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(destination, Path.GetRelativePath(source, file)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
        }
    }
    private static int Run(string exe, string cwd, params string[] args)
    {
        var info = new ProcessStartInfo(exe) { WorkingDirectory = cwd, UseShellExecute = false, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true };
        foreach (string arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(25000)) { process.Kill(true); throw new Exception("Package watchdog"); }
        string diagnostics = stdout.Result + stderr.Result; Console.WriteLine(diagnostics.Trim()); return process.ExitCode;
    }
    public static string[] Run(string root, string configuration, string output, string plugins)
    {
        string indexPath = Path.Combine(root, "out/verification/m2-7", configuration, "packages.json");
        using var index = JsonDocument.Parse(File.ReadAllText(indexPath));
        string sourceNull = index.RootElement.GetProperty("playerNull").GetString()!;
        string sourceDx11 = index.RootElement.GetProperty("playerDx11").GetString()!;
        string editor = index.RootElement.GetProperty("editor").GetString()!;
        DeploymentManifest.Validate(sourceNull); DeploymentManifest.Validate(sourceDx11); DeploymentManifest.Validate(editor);
        string movedNull = Path.Combine(output, "搬迁 包 Null"); CopyPackage(sourceNull, movedNull); DeploymentManifest.Validate(movedNull);
        Check(!Directory.Exists(Path.Combine(movedNull, "plugins")), "Null bundle must not contain native plugins");
        Check(!File.Exists(Path.Combine(sourceDx11, "plugins/NcmaNative.dll")), "Player must not deploy Editor FBX kernel");
        var forbidden = Directory.GetFiles(movedNull, "*", SearchOption.AllDirectories).Where(p => Path.GetFileName(p).StartsWith("Ncma.Editor", StringComparison.Ordinal) ||
            Path.GetFileName(p).StartsWith("Ncma.Gui", StringComparison.Ordinal) || Path.GetFileName(p).StartsWith("Ncma.Managed.Host", StringComparison.Ordinal)).ToArray();
        Check(forbidden.Length == 0, "Player bundle leaked Editor/Gui/old bridge");
        string scene = Path.Combine(movedNull, "sample/start.ncmascene"), project = Path.Combine(movedNull, "sample/sample.ncmaproject");
        byte[] original = File.ReadAllBytes(scene); File.SetAttributes(scene, FileAttributes.ReadOnly);
        try
        {
            Check(Run(Path.Combine(movedNull, "NcmaPlayer.exe"), output, "--project", project, "--headless", "--ticks", "120", "--report", Path.Combine(output, "package-null.json")) == 0,
                "Moved Null apphost failed");
            Check(original.SequenceEqual(File.ReadAllBytes(scene)), "Player modified scene asset");
        }
        finally { File.SetAttributes(scene, FileAttributes.Normal); }
        string movedDx11 = Path.Combine(output, "搬迁 包 DX11"); CopyPackage(sourceDx11, movedDx11); DeploymentManifest.Validate(movedDx11);
        Check(!File.Exists(Path.Combine(movedDx11, "plugins/NcmaGui.dll")), "DX11 Player accidentally needs ImGui");
        var options = PlayerOptions.Parse(["--project", Path.Combine(movedDx11, "sample/sample.ncmaproject"), "--ticks", "2", "--max-runtime-seconds", "20", "--report", Path.Combine(output, "package-dx11.json")]);
        var rendered = PlayerRunner.Run(options, pluginRoot: Path.Combine(movedDx11, "plugins"), visible: false);
        Check(rendered.ExitCode == 0 && rendered.Tick >= 2 && rendered.RenderedFrames > 0 && rendered.ValidationErrors == 0 && rendered.ValidationWarnings == 0,
            "Real DX11 Player did not render/validate/release: " + rendered.Reason + " / " + string.Join(',', rendered.ShutdownErrors));
        Check(Run(Path.Combine(editor, "NcmaEngine.exe"), output, "--smoke-test") == 0, "Packaged Editor apphost failed");
        string movedEditor = Path.Combine(output, "搬迁 FBX Editor"); CopyPackage(editor, movedEditor); DeploymentManifest.Validate(movedEditor);
        using (var character = new Ncma.ImportedCharacterResource(Path.Combine(movedEditor, "plugins/NcmaNative.dll"),
            Path.Combine(root, "tests/assets/fbx/blender_279_sausage_7400_binary.fbx"))) {
            float[] pose = new float[character.SampleFloatCount]; character.Sample(1, .25, pose);
            Check(pose.All(float.IsFinite) && character.Report.GetProperty("format").GetString() == "FBX", "Moved Editor ABI 2 resource did not sample");
        }
        var resource = new FileInfo(Path.Combine(editor, "NcmaEngine.exe"));
        Check(resource.Length > 0 && File.Exists(Path.Combine(editor, "NcmaEngine.runtimeconfig.json")), "Editor apphost/deps absent");
        string sourcePlugin = Path.Combine(movedDx11, "plugins/NcmaRenderer.dll");
        byte[] plugin = File.ReadAllBytes(sourcePlugin); plugin[0] ^= 0x1; File.WriteAllBytes(sourcePlugin, plugin);
        Reject(() => DeploymentManifest.Validate(movedDx11));
        Check(Run(Path.Combine(movedDx11, "NcmaPlayer.exe"), output, "--project", options.Project!, "--headless", "--ticks", "1", "--report", Path.Combine(output, "tampered.json")) == 3,
            "Tampered deployment did not fail closed");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(movedNull, DeploymentManifest.FileName)));
        var rewritten = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(manifest.RootElement.GetRawText())!;
        rewritten["files"] = JsonSerializer.SerializeToElement(new[] { new { path = "../escape.dll", size = 0, sha256 = new string('0', 64) } });
        File.WriteAllText(Path.Combine(movedNull, DeploymentManifest.FileName), JsonSerializer.Serialize(rewritten)); Reject(() => DeploymentManifest.Validate(movedNull));
        // Explicit Physics is a separate optional service; no scene-body/World scheduling claim.
        var physicsOptions = PlayerOptions.Parse(["--project", project, "--headless", "--ticks", "1", "--report", Path.Combine(output, "physics-on.json")]);
        var configurationValue = JsonSerializer.Deserialize<ProjectConfiguration>(File.ReadAllText(project), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;
        File.WriteAllText(project, JsonSerializer.Serialize(configurationValue with { PhysicsEnabled = true }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        var physics = PlayerRunner.Run(physicsOptions, pluginRoot: plugins);
        Check(physics.ExitCode == 0 && physics.Modules.Length == 1 && physics.Modules[0].Id == "ncma.physics", "Optional physics pulled graphics or failed cleanup");
        return ["Independent Null package/moved cwd/Chinese path/read-only scene/apphost", "Real DX11 Player without ImGui and validation 0/0",
            "Packaged C# Editor WinExe/apphost hidden smoke", "Manifest hashes/tamper/path traversal fail closed", "Headless explicit Physics loads only independent solver"];
    }
}
