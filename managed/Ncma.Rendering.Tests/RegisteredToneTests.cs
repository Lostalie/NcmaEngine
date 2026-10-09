using System.Numerics;
using System.Text.Json;
using Ncma.Interop;
using Ncma.Platform;
using Ncma.Rendering;

internal static unsafe partial class Program
{
    private static int RegisteredToneTests(string root, string output)
    {
        int cases = 0;
        void Pass(bool value, string message) { Check(value, "M7.1-C1 " + message); cases++; }
        void Bad(Action action) { Reject(action); cases++; }
        using var loader = new PluginLoader(); loader.Load(Path.GetFullPath(root), Specs().Take(2).ToArray());
        var native = loader.Modules.Single(m => m.Kind == ModuleKind.Renderer);
        using var window = new PlatformWindow(loader.Modules.Single(m => m.Kind == ModuleKind.Platform), "Registered Tone actual pixels", 256, 256, false);
        RegisteredSceneTone old;
        int maxError = 0; bool allowed = true;
        using (var renderer = new RendererSession(native, window, 256, 256)) {
            var catalog = DefaultSceneTone.CopyCatalog(renderer); var metadata = catalog.CopyPage(0);
            Pass(catalog.Count == 2 && metadata.All(r => !r.Compiled), "trusted default declarations have no fake GPU claim");
            var v = catalog.Require(DefaultSceneTone.VertexId, metadata.Single(r => r.Stage == ShaderStage.Vertex).ContentHash);
            var p = catalog.Require(DefaultSceneTone.PixelId, metadata.Single(r => r.Stage == ShaderStage.Pixel).ContentHash);
            var sourceV = v.CopyDefinition(); var sourceP = p.CopyDefinition();
            RegisteredSceneTone Prepare(ShaderDefinition vertex, ShaderDefinition pixel) {
                var user = ShaderCatalog.Create(ShaderProfile.Scene3D, [vertex, pixel]);
                return RegisteredSceneTone.Prepare(renderer, user, ShaderDescriptor.Prepare(vertex), ShaderDescriptor.Prepare(pixel), () => allowed);
            }
            var userPixel = sourceP with { AssetId = Guid.NewGuid(), Name = "User BGR Tone", Source = sourceP.Source.Replace("return float4(c,1);", "return float4(c.bgr,1);", StringComparison.Ordinal) };
            userPixel = userPixel with { SourceHash = ShaderContractCodec.HashSource(userPixel.Source) };
            var sharedCatalog = ShaderCatalog.Create(ShaderProfile.Scene3D, [sourceV, sourceP, userPixel]);
            old = RegisteredSceneTone.Prepare(renderer, sharedCatalog, v, p, () => allowed);
            var user = RegisteredSceneTone.Prepare(renderer, sharedCatalog, v, ShaderDescriptor.Prepare(userPixel), () => allowed);
            Pass(old.CopyMetadata().Compiled && user.CopyMetadata().Compiled && old.CopyMetadata().CatalogHash == user.CopyMetadata().CatalogHash && old.CopyMetadata().PixelId != user.CopyMetadata().PixelId, "default/user SAME immutable catalog/compiler/artifact metadata");
            Pass(renderer.PipelineStats.Pipelines == 0 && renderer.Stats.SubmittedFrames == 0 && renderer.SkinStats.Meshes == 0, "preparation installs no pipeline/skin/frame");
            using var target = renderer.CreateViewTarget(256, 256);
            using var scene = new ScenePipelineSession(renderer, new Scene3DPipeline(shadows: false), 256, 256, old);
            Pass(ReferenceEquals(scene.RegisteredTone, old), "new scene uses actual registered bytecode without duplicate Tone source compilation");
            var light = new ResourceLighting(Vector3.Zero, Vector3.UnitZ, Vector4.One);
            Vector4 clear = new(.125f, .25f, .5f, 1);
            var baseline = new byte[256 * 256 * 4]; var changed = new byte[baseline.Length]; ulong frame = 1;
            void Render(byte[] image) { scene.Submit(frame++, [], [], light, Matrix4x4.Identity, new SceneShadowSettings(), target, clear: clear); renderer.CaptureTarget(target, image); renderer.Present(); }
            static byte Encode(float x) { x = Math.Clamp(x * (2.51f * x + .03f) / (x * (2.43f * x + .59f) + .14f), 0, 1); return (byte)MathF.Round(255 * (x <= .0031308f ? 12.92f * x : 1.055f * MathF.Pow(x, 1 / 2.4f) - .055f)); }
            Render(baseline);
            byte[] expected = [Encode(clear.X), Encode(clear.Y), Encode(clear.Z), 255];
            for (int i = 0; i < baseline.Length; i++) maxError = Math.Max(maxError, Math.Abs(baseline[i] - expected[i % 4]));
            Pass(maxError <= 1, "independent scalar ACES/sRGB full-image oracle tolerance1 byte: " + maxError);
            scene.ReplaceTone(user); Render(changed);
            int userError = 0;
            for (int i = 0; i < changed.Length; i++) userError = Math.Max(userError, Math.Abs(changed[i] - expected[i % 4 == 3 ? 3 : 2 - i % 4]));
            maxError = Math.Max(maxError, userError);
            Pass(userError <= 1 && !changed.SequenceEqual(baseline), "actual user BGR GPU stage/full-image oracle");
            scene.ReplaceTone(old); Render(changed); Pass(changed.SequenceEqual(baseline), "default restore exact full image");
            var wrongLink = sourceV with { Source = sourceV.Source.Replace("float2 uv:TEXCOORD0;};", "float2 uv:TEXCOORD1;};", StringComparison.Ordinal) };
            wrongLink = wrongLink with { SourceHash = ShaderContractCodec.HashSource(wrongLink.Source) };
            // Both sources individually compile; B cannot assert a VS-output/PS-input link.
            var badLink = Prepare(wrongLink, sourceP); Bad(() => scene.ReplaceTone(badLink));
            var badOutput = sourceP with { Source = sourceP.Source.Replace("PSTone(Full i):SV_TARGET", "PSTone(Full i):SV_TARGET1", StringComparison.Ordinal) };
            badOutput = badOutput with { SourceHash = ShaderContractCodec.HashSource(badOutput.Source) };
            var outputCandidate = Prepare(sourceV, badOutput); Bad(() => scene.ReplaceTone(outputCandidate));
            Bad(() => new ScenePipelineSession(renderer, new Scene3DPipeline(shadows: false), 256, 256, outputCandidate));
            var wrongResource = sourceP with { Source = sourceP.Source.Replace("BaseTex:register(t0)", "BaseTex:register(t1)", StringComparison.Ordinal) };
            wrongResource = wrongResource with { SourceHash = ShaderContractCodec.HashSource(wrongResource.Source) }; Bad(() => Prepare(sourceV, wrongResource));
            var wrongLast = sourceP with { Constants = [sourceP.Constants[0] with { Members = sourceP.Constants[0].Members.Select(m => m.Name == "ShadowParameters" ? m with { Name = "ForgedLast" } : m).ToArray() }] };
            Bad(() => Prepare(sourceV, wrongLast));
            Bad(() => RegisteredSceneTone.Prepare(renderer, catalog, v, p, () => false));
            allowed = false; Bad(() => scene.ReplaceTone(user)); allowed = true;
            Bad(() => RegisteredSceneTone.Prepare(renderer, sharedCatalog, v, p, () => { RegisteredSceneTone.Prepare(renderer, sharedCatalog, v, p, () => true); return true; }));
            bool recurse = false;
            var reentrant = RegisteredSceneTone.Prepare(renderer, sharedCatalog, v, p, () => { if (recurse) scene.ReplaceTone(user); return true; });
            recurse = true; Bad(() => scene.ReplaceTone(reentrant));
            Pass(ReferenceEquals(scene.RegisteredTone, old) && renderer.PipelineStats.Pipelines == 1, "all failed last-row/stage/link/authority candidates retain old scene/stage");
            Render(changed); Pass(changed.SequenceEqual(baseline), "original pipeline still renders after complete rejection");
            scene.Submit(frame++, [], [], light, Matrix4x4.Identity, new SceneShadowSettings(), target, clear: clear);
            Bad(() => scene.ReplaceTone(user)); renderer.Present();
            Exception? threadError = null; var thread = new Thread(() => { try { scene.ReplaceTone(user); } catch (Exception e) { threadError = e; } }); thread.Start(); thread.Join();
            Pass(threadError is PluginException { Result: PluginResult.WrongThread }, "actual owner-thread install guard");
            ulong creates = renderer.PipelineStats.Creates;
            for (int i = 0; i < 16; i++) { scene.ReplaceTone(i % 2 == 0 ? user : old); Render(changed); }
            Pass(renderer.PipelineStats.Creates == creates && renderer.PipelineStats.Pipelines == 1, "16 exact same-scene stage replacements without new scene/geometry/texture upload");
            Pass(renderer.Stats.ValidationErrors == 0 && renderer.Stats.ValidationWarnings == 0, "actual registered programs API0/0");
            Directory.CreateDirectory(output);
            File.WriteAllBytes(Path.Combine(output, "registered-tone-default.png"), Png(256, 256, baseline));
            scene.ReplaceTone(user); Render(changed); File.WriteAllBytes(Path.Combine(output, "registered-tone-user.png"), Png(256, 256, changed));
        }
        using (var renderer = new RendererSession(native, window, 256, 256, pureUi: true)) {
            Bad(() => new ScenePipelineSession(renderer, new Scene3DPipeline(shadows: false), 256, 256, old));
            Pass(renderer.PipelineStats.Pipelines == 0 && renderer.Stats.LiveGroups == 0 && renderer.SkinStats.Meshes == 0 && renderer.UiStats.ResidentBytes == 0, "stale prepared owner rejected; pure2D no3D/ui/skin allocations");
        }
        File.WriteAllText(Path.Combine(output, "registered-tone-results.json"), JsonSerializer.Serialize(new { schema = 1, cases, query = 9, api = 1, backend = "DX11", defaultAndUserSameCatalogCompiler = true, actualGpuTone = true, independentFullImageMaxError = maxError,
            validationErrors = 0, validationWarnings = 0, geometryShadowSkinRegistration = false, pure2DShaderRegistration = false, formalHostSwitch = false, runtimeShaderPackage = false, inference = false, agentShaderAuthority = false, manualAcceptance = false }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS M7.1-C1 {cases} actual registered Tone/link/output/atomic/copy/lifetime/full-image cases; maxError={maxError}; API0/0; C2-C4 pending"); return 0;
    }
}
