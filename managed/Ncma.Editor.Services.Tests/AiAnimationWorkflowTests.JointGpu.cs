using System.Text.Json;
using Ncma.Animation;
using Ncma.Animation.Native;
using Ncma.Assets.Runtime;
using Ncma.Editor.Services;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;

internal static partial class AiAnimationWorkflowTests
{
    // Registered ONLY in the existing full EditorService entry with its explicit
    // native-plugin path; CPU/focused entries do not silently acquire a GPU gate.
    internal static IEnumerable<(string, Action)> JointGpuCases(string output, string repository, string plugins)
    {
        yield return ("M8.7-C2 approved AI speed binding drives original DX11 graph preview", () => JointAnimationCommit(output, repository, plugins));
    }
    private static unsafe void JointGpuPlayback(RuntimeAssetSnapshot snapshot, AnimationGraphDefinition graph, Guid parameter, string plugins, string output)
    {
        using var loader = new PluginLoader();
        loader.Load(plugins, [new("platform", ModuleKind.Platform, "NcmaPlatform.dll", "NcmaPlatform.dll", 1, 0, []),
            new("renderer", ModuleKind.Renderer, "NcmaRenderer.dll", "NcmaRenderer.dll", 1, 2, ["platform"])]);
        using var window = new PlatformWindow(loader.Modules.Single(m => m.Kind == ModuleKind.Platform), "M8.7 approved animation preview", 320, 180, false);
        using var renderer = new RendererSession(loader.Modules.Single(m => m.Kind == ModuleKind.Renderer), window, 320, 180);
        string kernelPath = Path.Combine(plugins, "NcmaAnimationKernel.dll");
        using var kernel = new PoseKernel(kernelPath, JointHash(File.ReadAllBytes(kernelPath)));
        using var preview = new EditorAnimationGraphPreview(renderer, () => kernel);
        preview.Prepare(snapshot, graph.AssetId); Check(preview.Prepared && preview.Tick == 0 && preview.WorldId != Guid.Empty);
        byte[]? first = null, last = null;
        for (int i = 0; i < 16; i++) {
            _ = window.Poll(); preview.Advance(1.0 / 60);
            Check(preview.Draw((ulong)i + 1, 320, 180) is not null);
            if (i == 0) first = preview.Capture(); if (i == 15) last = preview.Capture();
            renderer.Present();
            var debug = preview.GraphDebug ?? throw new Exception("Missing actual preview graph");
            Check(debug.SnapshotValid && debug.Frame.GraphId == graph.AssetId && debug.Frame.Context.Tick == (ulong)i + 1 && preview.Tick == 0);
            Check(debug.Parameters.Single(p => p.Id == parameter).Value == 2);
            Check(Math.Abs(debug.Instructions.Single(p => p.Operation == AnimationPoseOperation.Clip).Current - (i + 1) * 2.0 / 60) < 1e-9);
        }
        Check(first is not null && last is not null && !first.SequenceEqual(last));
        Check(renderer.Stats.ValidationErrors == 0 && renderer.Stats.ValidationWarnings == 0);
        Guid world = preview.WorldId; preview.Close();
        Check(!preview.Prepared && renderer.SkinStats.Meshes == 0 && kernel.Statistics.Rigs == 0 && kernel.Statistics.Clips == 0);
        string evidence = Path.Combine(output, "m8-7-animation-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(evidence);
        using (var file = new FileStream(Path.Combine(evidence, "first.rgba"), FileMode.CreateNew, FileAccess.Write)) file.Write(first!);
        using (var file = new FileStream(Path.Combine(evidence, "last.rgba"), FileMode.CreateNew, FileAccess.Write)) file.Write(last!);
        Console.WriteLine("M87-JOINT-GPU " + JsonSerializer.Serialize(new { graphId = graph.AssetId, graphSha256 = JointHash(AnimationGraphCodec.Encode(graph)),
            previewWorld = world, frames = 16, worldTick = 0, firstSha256 = JointHash(first!), lastSha256 = JointHash(last!),
            width = 320, height = 180, kernelSha256 = JointHash(File.ReadAllBytes(kernelPath)), resourcesReleased = true, manualAccepted = false, targetPerformanceAccepted = false }));
    }
}
