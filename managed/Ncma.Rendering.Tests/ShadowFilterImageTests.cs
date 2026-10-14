using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Assets;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;

internal static unsafe partial class Program
{
    private static int ShadowFilterImageTests(string root, string output)
    {
        output = Path.Combine(Path.GetFullPath(output), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
        int checks = 0, maximumError = 0; ulong frame = 1; bool allowed = true;
        void Pass(bool value, string message) { Check(value, "Shadow filter image: " + message); checks++; }
        using var loader = new PluginLoader(); loader.Load(Path.GetFullPath(root), Specs().Take(2).ToArray());
        using var window = new PlatformWindow(loader.Modules.Single(m => m.Kind == ModuleKind.Platform), "Independent soft shadow reference", 256, 256, false);
        using var renderer = new RendererSession(loader.Modules.Single(m => m.Kind == ModuleKind.Renderer), window, 256, 256);
        var defaults = DefaultSceneShaders.CopyCatalog(renderer); var selection = DefaultSceneShaders.Select(defaults);
        var high = SceneShadowShaders.ComposeHigh(defaults, selection);
        Pass(high.Selection.GeometryPixel.AssetId != selection.GeometryPixel.AssetId &&
            high.Selection.GeometryVertex.ContentHash == selection.GeometryVertex.ContentHash &&
            high.Selection.ShadowVertex!.ContentHash == selection.ShadowVertex!.ContentHash &&
            high.Selection.ShadowPixel!.ContentHash == selection.ShadowPixel!.ContentHash &&
            high.Selection.TonePixel.ContentHash == selection.TonePixel.ContentHash, "only explicit geometry pixel stage replaced");
        Pass(high.Catalog.ContentHash == SceneShadowShaders.ComposeHigh(defaults, selection).Catalog.ContentHash, "deterministic declaration");
        var unshadowed = DefaultSceneShaders.CopyCatalog(renderer, false);
        Reject(() => SceneShadowShaders.ComposeHigh(unshadowed, DefaultSceneShaders.Select(unshadowed, false))); checks++;
        Reject(() => SceneShadowShaders.ComposeHigh(high.Catalog, selection)); checks++;
        Reject(() => SceneShadowShaders.ComposeHigh(defaults, selection with { GeometryVertex = selection.GeometryPixel })); checks++;
        Reject(() => RegisteredSceneShaders.Prepare(renderer, high.Catalog, high.Selection, () => false)); checks++;
        RegisteredSceneShaders prepared;
        // Cook and re-admit exact bytes through the same source-free runtime path as products.
        using (var compiler = new ShaderCompilerService(renderer, () => allowed))
        {
            var s = high.Selection;
            var package = RuntimeShaderPackage.Cook(ShaderProfile.Scene3D, true, false, [
                new(RuntimeShaderRole.GeometryVertex, compiler.Prepare(s.GeometryVertex)), new(RuntimeShaderRole.GeometryPixel, compiler.Prepare(s.GeometryPixel)),
                new(RuntimeShaderRole.ToneVertex, compiler.Prepare(s.ToneVertex)), new(RuntimeShaderRole.TonePixel, compiler.Prepare(s.TonePixel)),
                new(RuntimeShaderRole.ShadowVertex, compiler.Prepare(s.ShadowVertex!)), new(RuntimeShaderRole.ShadowPixel, compiler.Prepare(s.ShadowPixel!))]);
            var bytes = package.CopyBytes(); File.WriteAllBytes(Path.Combine(output, "high.ncs"), bytes);
            var decoded = RuntimeShaderPackage.Preflight(bytes, Convert.ToHexString(SHA256.HashData(bytes)));
            prepared = RuntimeShaderPreparation.Prepare(renderer, decoded, () => allowed).Scene!;
            Pass(decoded.ContentHash == package.ContentHash && !decoded.GpuValidated, "immutable source-free package is not admission credential");
        }
        var environmentSource = DefaultEnvironmentSceneShaders.CopyCatalog(renderer);
        var environment = SceneShadowShaders.ComposeHigh(environmentSource, DefaultEnvironmentSceneShaders.Select(environmentSource));
        var environmentProgram = RegisteredEnvironmentSceneShaders.Prepare(renderer, environment.Catalog, environment.Selection, () => allowed);
        var environmentRuntime = EnvironmentShaderPreparation.Prepare(renderer, environmentProgram.CookPackage(), () => allowed);
        var defaultProgram = RegisteredSceneShaders.Prepare(renderer, defaults, selection, () => allowed);
        var defaultEnvironment = RegisteredEnvironmentSceneShaders.Prepare(renderer, environmentSource, DefaultEnvironmentSceneShaders.Select(environmentSource), () => allowed);
        var highPreset = SceneShadowPresets.Get(SceneShadowQuality.High);
        var beforeInvalid = renderer.PipelineStats;
        Reject(() => new ScenePipelineSession(renderer, highPreset.CreatePipeline(true), 256, 256)); checks++;
        Reject(() => new ScenePipelineSession(renderer, new ShadowForwardPipeline(highPreset.CreatePipeline(true)), 256, 256, shaders: defaultProgram)); checks++;
        Reject(() => new ScenePipelineSession(renderer, highPreset.CreatePipeline(true, profile: ShaderProfile.SceneEnvironment), 256, 256, shaders: prepared)); checks++;
        Pass(renderer.PipelineStats.Creates == beforeInvalid.Creates && renderer.PipelineStats.ResidentBytes == beforeInvalid.ResidentBytes, "required pixel rejection precedes native allocation");
        using var cache = new RenderResourceCache(renderer); var data = Quad(); using var mesh = renderer.CreateStaticMesh(data);
        var definition = MaterialDefinition.Default(Guid.NewGuid()) with { BaseColor = new(.6f, .25f, .1f, 1), Metallic = .1f, Roughness = .6f };
        using var material = cache.AcquireMaterial(Version(definition), definition, _ => null, true, true, out _);
        using var target = renderer.CreateViewTarget(256, 256);
        var camera = Matrix4x4.CreateOrthographic(4, 4, .1f, 10);
        SceneGpuDraw[] geometry = [SceneGpuDraw.Create(mesh, material.Resource, data.Ranges[0], Matrix4x4.CreateScale(4, 4, 1) * Matrix4x4.CreateTranslation(0, 0, -4.5f), camera)];
        var light = new ResourceLighting(Vector3.Zero, Vector3.UnitZ, new(1, 1, 1, 3));
        var settings = SceneShadowPresets.Get(SceneShadowQuality.High).Settings;
        using var dense = new ScenePipelineSession(renderer, new ShadowForwardPipeline(highPreset.CreatePipeline(true)), 256, 256, shaders: prepared);
        using var sparse = new ScenePipelineSession(renderer, new Scene3DPipeline(shadowResolution: 2048), 256, 256);
        using var env = new ScenePipelineSession(renderer, highPreset.CreatePipeline(true, profile: ShaderProfile.SceneEnvironment), 256, 256, environmentShaders: environmentRuntime.Scene);
        var sparsePlan = sparse.Plan;
        Reject(() => sparse.Configure(highPreset.CreatePipeline(true))); checks++;
        Reject(() => dense.ReplaceShaders(defaultProgram)); checks++;
        Reject(() => env.ReplaceEnvironmentShaders(defaultEnvironment)); checks++;
        Pass(ReferenceEquals(sparsePlan, sparse.Plan) && dense.Plan.RequiredSceneGeometryPixelHash == high.Selection.GeometryPixel.ContentHash, "failed Configure/replace preserve old graph and programs");
        byte[] image = new byte[256 * 256 * 4], reference = new byte[image.Length], comparison = new byte[image.Length];
        var cases = new[] { ("self", 0f, 80f, 0f), ("contact", .01f, 80f, 0f), ("near", .3f, 80f, 0f),
            ("far", 3f, 80f, 0f), ("subpixel", 3f, 80f, .137f), ("border", 3f, 4f, 1.9f) };
        var rows = new List<object>();
        foreach (var (name, separation, extent, offset) in cases)
        {
            var lightVp = Matrix4x4.CreateOrthographic(extent, extent, .1f, 10);
            // Keep analytic nearest samples away from exact texel ties: GPU interpolation
            // and double arithmetic may choose opposite sides of a point-sampling boundary.
            // Fixed quarter-texel projection offset, not a tolerance or pixel exclusion.
            lightVp.M41 = lightVp.M42 = .5f / 2048;
            SceneGpuDraw[] casters = [SceneGpuDraw.Create(mesh, material.Resource, data.Ranges[0], Matrix4x4.CreateTranslation(offset, 0, -4.5f + separation), camera)];
            foreach (bool filtered in new[] { false, true })
            {
                var pipeline = filtered ? dense : sparse;
                pipeline.Submit(frame++, geometry, casters, light, lightVp, settings, target); renderer.CaptureTarget(target, image); renderer.Present();
                int error = 0, partial = 0, worstX = 0, worstY = 0; var levels = new HashSet<int>();
                for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++)
                {
                    var point = new Vector3((x + .5f) / 64 - 2, 2 - (y + .5f) / 64, -4);
                    double visibility = ShadowPlaneVisibility(point, lightVp, extent, offset, separation, settings.Bias, filtered);
                    if (visibility is > 0 and < 1) partial++;
                    if (y == 128) levels.Add((int)Math.Round(visibility * 10000));
                    var expected = ScenePbrOracle(new(.6f, .25f, .1f), .1f, .6f, point, Vector3.UnitZ, Vector3.Zero, Vector3.UnitZ, (float)(3 * visibility), .03f, 1);
                    int index = (y * 256 + x) * 4;
                    for (int c = 0; c < 3; c++) { reference[index + c] = (byte)Math.Clamp(MathF.Round(expected[c] * 255), 0, 255); int delta = Math.Abs(image[index + c] - reference[index + c]); if (delta > error) { error = delta; worstX = x; worstY = y; } }
                    reference[index + 3] = 255; error = Math.Max(error, Math.Abs(image[index + 3] - 255));
                }
                string label = name + (filtered ? "-dense" : "-sparse");
                File.WriteAllBytes(Path.Combine(output, label + ".png"), Png(256, 256, image));
                File.WriteAllBytes(Path.Combine(output, label + "-oracle.png"), Png(256, 256, reference));
                maximumError = Math.Max(maximumError, error);
                File.WriteAllText(Path.Combine(output, label + "-comparison.json"), JsonSerializer.Serialize(new { error, worstX, worstY, actual = image[(worstY * 256 + worstX) * 4], expected = reference[(worstY * 256 + worstX) * 4], partial, levels = levels.Count }));
                Pass(error <= 2, label + " independent analytic depth/bilinear comparison/PBR image error=" + error + $" at {worstX},{worstY}");
                if (name == "self") Pass(partial == 0, "bias rejects coplanar self shadow");
                if (name == "far" && filtered) Pass(levels.Count > 10, "dense filter retains more than sparse comparison levels");
                if (filtered)
                {
                    env.Submit(frame++, geometry, casters, light, lightVp, settings, target); renderer.CaptureTarget(target, comparison); renderer.Present();
                    Pass(image.SequenceEqual(comparison), "environment profile Off preserves exact filtered direct lighting");
                }
                rows.Add(new { label, error, partialPixels = partial, centerRowLevels = levels.Count });
            }
        }
        allowed = false; Reject(() => new ScenePipelineSession(renderer, new Scene3DPipeline(shadowResolution: 2048), 256, 256, shaders: prepared)); checks++;
        allowed = true;
        renderer.WaitIdle(); Pass(renderer.Stats.ValidationErrors == 0 && renderer.Stats.ValidationWarnings == 0, "API0/0");
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { schema = 1, checks, maximumError, rows,
            depthSource = "independent analytic orthographic rectangle at texel centers; no GPU depth readback", highMaximumDepthReads = 205,
            sourceFree = true, environmentOffParity = true, animated = false, fullPerformanceAccepted = false }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS M7.5 high filter {checks} cases/12 whole images/independent depth oracle max={maximumError}/API0/0; {output}"); return 0;
    }

    private sealed class ShadowForwardPipeline(RenderPipeline inner) : RenderPipeline
    { public override RenderGraph Build(uint width, uint height) => inner.Build(width, height); }

    // Independent scalar reference: analytic projected rectangle, discrete texel coverage,
    // then weighted comparison contributions. No production shader/parser/readback is used.
    private static double ShadowPlaneVisibility(Vector3 point, Matrix4x4 lightVp, double extent, double offset,
        double separation, double bias, bool dense)
    {
        var q = Vector4.Transform(new Vector4(point, 1), lightVp);
        double u = q.X * .5 + .5, v = .5 - q.Y * .5, receiver = q.Z - bias;
        var caster = Vector4.Transform(new Vector4(0, 0, (float)(-4 + separation), 1), lightVp);
        const int resolution = 2048;
        double Depth(int x, int y)
        {
            if (x < 0 || y < 0 || x >= resolution || y >= resolution) return 1;
            double wx = ((x + .5) / resolution - .5 - lightVp.M41 * .5) * extent;
            double wy = (.5 - (y + .5) / resolution - lightVp.M42 * .5) * extent;
            return Math.Abs(wx - offset) < .8 && Math.Abs(wy) < .8 ? caster.Z : 1;
        }
        double nearest(double s, double t) => Depth((int)Math.Floor(s * resolution), (int)Math.Floor(t * resolution));
        int blockers = 0; double depth = 0;
        for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++)
        { double d = nearest(u + i * 8.0 / resolution, v + j * 8.0 / resolution); if (d < receiver) { blockers++; depth += d; } }
        if (blockers == 0) return 1;
        double radius = Math.Clamp((receiver - depth / blockers) * 512, 1, 8), sum = 0;
        int half = dense ? 3 : 1;
        for (int j = -half; j <= half; j++) for (int i = -half; i <= half; i++)
        {
            double s = u + i * radius / half / resolution, t = v + j * radius / half / resolution;
            if (!dense) { sum += receiver <= nearest(s, t) ? 1 : 0; continue; }
            double px = s * resolution - .5, py = t * resolution - .5;
            int left = (int)Math.Floor(px), top = (int)Math.Floor(py); double fx = px - left, fy = py - top;
            for (int yy = 0; yy < 2; yy++) for (int xx = 0; xx < 2; xx++)
                if (receiver <= Depth(left + xx, top + yy)) sum += (xx == 0 ? 1 - fx : fx) * (yy == 0 ? 1 - fy : fy);
        }
        return Math.Clamp(sum / ((half * 2 + 1) * (half * 2 + 1)), 0, 1);
    }
}
