using System.Text;
using System.Text.Json;
using Ncma.Rendering;
using Ncma.Interop;
using Ncma.Platform;

internal static unsafe partial class Program
{
    private static void TestShaderCompilation(RendererSession renderer, string output)
    {
        int cases = 0; bool allowed = true;
        void Pass(bool value, string message) { Check(value, "M7.1-B " + message); cases++; }
        void Bad(Action action, string? code = null) {
            try { action(); }
            catch (ShaderContractException e) { Pass(code is null || e.Code == code, "declaration/reflection rejection " + e.Code); return; }
            catch (ShaderCompileException e) { Pass(code is null || e.Code.Contains(code, StringComparison.Ordinal), "compiler rejection " + e.Code); Check(!e.Message.Contains("PRIVATE"), "Source diagnostic leak"); return; }
            catch (PluginException e) { Pass(code is null || e.Result.ToString() == code, "boundary rejection " + e.Result); return; }
            catch (InvalidOperationException) when (code is null) { cases++; return; }
            throw new Exception("Expected real shader rejection: " + code);
        }
        ShaderDefinition Definition(string name, ShaderStage stage, string source, ShaderProfile profile = ShaderProfile.Scene3D) =>
            new(1, Guid.NewGuid(), name, profile, stage, stage == ShaderStage.Vertex ? "VSMain" : stage == ShaderStage.Pixel ? "PSMain" : "CSMain",
                source, ShaderContractCodec.HashSource(source), [], [], [], []);
        var before = renderer.Stats;
        using var compiler = new ShaderCompilerService(renderer, () => allowed);
        const string vertexSource = "cbuffer C:register(b0){column_major float4x4 MVP;float4 Tint;float2 Values[2];};float4 VSMain(float3 p:POSITION,float2 uv:TEXCOORD0,uint id:SV_VertexID):SV_POSITION{return mul(MVP,float4(p,1))+Tint+float4(uv+Values[0]+Values[1],id,0);}";
        var vertex = Definition("vertex", ShaderStage.Vertex, vertexSource) with {
            Inputs = [new("POSITION", 0, ShaderScalar.Float32, 3, 0), new("TEXCOORD", 0, ShaderScalar.Float32, 2, 12), new("SV_VERTEXID", 0, ShaderScalar.UInt32, 1, -1)],
            Constants = [new("C", 0, 112, [new("MVP", ShaderScalar.Float32, 4, 4, ShaderMatrixOrder.ColumnMajor, 0, 1, 0),
                new("Tint", ShaderScalar.Float32, 1, 4, ShaderMatrixOrder.None, 64, 1, 0), new("Values", ShaderScalar.Float32, 1, 2, ShaderMatrixOrder.None, 80, 2, 16)])]
        };
        var descriptor = ShaderDescriptor.Prepare(vertex); var compiled = compiler.Prepare(descriptor);
        var rows = compiled.CopyReflection();
        Pass(compiled.CopyBytecode().AsSpan(0, 4).SequenceEqual("DXBC"u8) && rows.Length == 7, "actual VS DXBC/signature/buffer/members");
        Pass(rows.Any(r => r.Name == "Values" && r.ArrayCount == 2 && r.ArrayStride == 16 && r.ByteOffset == 80 && r.ByteSize == 24), "real float2 array span excludes trailing padding");
        Pass(compiled.CompilerVersion == 47 && compiled.CompilerFlags == 0x48800, "exact compiler/strict/O3/warnings-as-errors");
        byte[] bytecode = compiled.CopyBytecode(); bytecode[0] = 0; rows[0] = rows[0] with { Name = "MUTATED" };
        Pass(compiled.CopyBytecode()[0] == (byte)'D' && !compiled.CopyReflection().Any(r => r.Name == "MUTATED"), "deep copied reflection/bytecode");
        Pass(compiled.CopyReflectionPage(0).Length == 7, "copied bounded8 metadata page");
        Bad(() => compiled.CopyReflectionPage(1), "page");
        ulong compilationCount = compiler.Compilations;
        Pass(ReferenceEquals(compiled, compiler.Prepare(descriptor)) && compiler.Compilations == compilationCount && compiler.CacheHits == 1, "exact immutable cache hit");
        var alternate = compiler.Prepare(descriptor, [new("UNUSED_OPTION", "1")]);
        Pass(alternate.CacheKey != compiled.CacheKey && compiler.Compilations == compilationCount + 1, "macro-sensitive key even identical bytecode");
        var sorted = compiler.Prepare(descriptor, [new("Z", "2"), new("A", "1")]);
        Pass(ReferenceEquals(sorted, compiler.Prepare(descriptor, [new("A", "1"), new("Z", "2")])), "macro order canonical exact hit");
        const string pixelSource = "Texture2D BaseTex:register(t2);SamplerState S:register(s1);cbuffer C:register(b1){float4 Tint;};float4 PSMain(float2 uv:TEXCOORD0):SV_TARGET{return BaseTex.Sample(S,uv)*Tint;}";
        var pixel = Definition("pixel", ShaderStage.Pixel, pixelSource) with {
            Constants = [new("C", 1, 16, [new("Tint", ShaderScalar.Float32, 1, 4, ShaderMatrixOrder.None, 0, 1, 0)])],
            Resources = [new("BaseTex", ShaderResourceKind.Texture2D, ShaderResourceAccess.ReadOnly, 2, 1, 0), new("S", ShaderResourceKind.Sampler, ShaderResourceAccess.ReadOnly, 1, 1, 0)]
        };
        var ps = compiler.Prepare(ShaderDescriptor.Prepare(pixel));
        Pass(ps.CopyReflection().Any(r => r.Name == "BaseTex" && r.Kind == 4 && r.Slot == 2 && r.ResourceKind == ShaderResourceKind.Texture2D), "actual PS SRV and sampler reflection");
        const string computeSource = "struct Input{float3 p;float3 n;float2 uv;float4 t;uint4 joints;float4 weights;};struct Palette{column_major float4x4 model;column_major float4x4 normal;};StructuredBuffer<Input> Source:register(t0);StructuredBuffer<Palette> Bones:register(t1);RWByteAddressBuffer Output:register(u0);cbuffer Settings:register(b0){uint VertexCount,Offset,Unused,Unused2;};[numthreads(64,1,1)]void CSMain(uint3 id:SV_DispatchThreadID){if(id.x<VertexCount){Input v=Source[id.x];Palette b=Bones[Offset+v.joints.x];Output.Store(id.x*4,asuint(v.p.x+b.model[0][0]+b.normal[0][0]+Unused+Unused2));}}";
        var compute = Definition("skin-shape", ShaderStage.Compute, computeSource, ShaderProfile.Skinning) with {
            Constants = [new("Settings", 0, 16, Enumerable.Range(0, 4).Select(i => new ShaderConstantMember(new[] { "VertexCount", "Offset", "Unused", "Unused2" }[i], ShaderScalar.UInt32, 1, 1, ShaderMatrixOrder.None, i * 4, 1, 0)).ToArray())],
            Resources = [new("Source", ShaderResourceKind.StructuredBuffer, ShaderResourceAccess.ReadOnly, 0, 1, 80),
                new("Bones", ShaderResourceKind.StructuredBuffer, ShaderResourceAccess.ReadOnly, 1, 1, 128), new("Output", ShaderResourceKind.RWByteAddressBuffer, ShaderResourceAccess.ReadWrite, 0, 1, 0)]
        };
        var cs = compiler.Prepare(ShaderDescriptor.Prepare(compute));
        Pass(cs.CopyReflection().Any(r => r.Name == "Source" && r.ElementStride == 80) && cs.CopyReflection().Any(r => r.Name == "Bones" && r.ElementStride == 128) && cs.CopyReflection().Any(r => r.Name == "Output" && r.ResourceKind == ShaderResourceKind.RWByteAddressBuffer), "actual CS structured strides/byteaddress UAV");
        var rw = Definition("rw", ShaderStage.Compute, "RWStructuredBuffer<float4> Output:register(u0);[numthreads(1,1,1)]void CSMain(uint3 id:SV_DispatchThreadID){Output[id.x]=float4(id,1);}", ShaderProfile.Skinning) with {
            Resources = [new("Output", ShaderResourceKind.RWStructuredBuffer, ShaderResourceAccess.ReadWrite, 0, 1, 16)] };
        Pass(compiler.Prepare(ShaderDescriptor.Prepare(rw)).CopyReflection()[0].ElementStride == 16, "actual RW structured stride");
        var matrix3 = Definition("matrix3", ShaderStage.Vertex, "cbuffer C:register(b0){row_major float3x3 M;};float4 VSMain(float3 p:POSITION):SV_POSITION{return float4(mul(p,M),1);}") with {
            Inputs = [new("POSITION", 0, ShaderScalar.Float32, 3, 0)],
            Constants = [new("C", 0, 48, [new("M", ShaderScalar.Float32, 3, 3, ShaderMatrixOrder.RowMajor, 0, 1, 0)])] };
        Pass(compiler.Prepare(ShaderDescriptor.Prepare(matrix3)).CopyReflection().Any(r => r.Kind == 3 && r.ByteSize == 44 && r.MatrixOrder == ShaderMatrixOrder.RowMajor), "actual matrix3 reflected44 / reserved48");
        Bad(() => compiler.Prepare(ShaderDescriptor.Prepare(vertex with { EntryPoint = "Missing" })), "shader_compile_failed");
        Bad(() => compiler.Prepare(ShaderDescriptor.Prepare(vertex with { Inputs = vertex.Inputs.Select(i => i.Semantic == "POSITION" ? i with { Components = 2 } : i).ToArray() })), "reflection_mismatch");
        Bad(() => compiler.Prepare(ShaderDescriptor.Prepare(vertex with { Constants = [vertex.Constants[0] with { ByteSize = 128 }] })), "reflection_mismatch");
        Bad(() => compiler.Prepare(ShaderDescriptor.Prepare(vertex with { Constants = [vertex.Constants[0] with { Members = vertex.Constants[0].Members.Select(m => m.Name == "MVP" ? m with { MatrixOrder = ShaderMatrixOrder.RowMajor } : m).ToArray() }] })), "reflection_mismatch");
        Bad(() => compiler.Prepare(ShaderDescriptor.Prepare(pixel with { Resources = pixel.Resources.Select(r => r.Semantic == "BaseTex" ? r with { Kind = ShaderResourceKind.TextureCube } : r).ToArray() })), "reflection_mismatch");
        Bad(() => compiler.Prepare(ShaderDescriptor.Prepare(pixel with { Resources = pixel.Resources.Select(r => r.Semantic == "S" ? r with { Slot = 3 } : r).ToArray() })), "reflection_mismatch");
        Bad(() => compiler.Prepare(ShaderDescriptor.Prepare(compute with { Resources = compute.Resources.Select(r => r.Semantic == "Bones" ? r with { ElementStride = 64 } : r).ToArray() })), "reflection_mismatch");
        var missingInput = vertex with { Inputs = [] };
        Bad(() => compiler.Prepare(ShaderDescriptor.Prepare(missingInput)), "reflection_rows");
        Bad(() => compiler.Prepare(ShaderDescriptor.Prepare(Definition("wrong-stage", ShaderStage.Compute, vertexSource, ShaderProfile.Skinning))), "shader_compile_failed");
        Bad(() => compiler.Prepare(ShaderDescriptor.Prepare(Definition("warning", ShaderStage.Pixel, "float4 PSMain():SV_TARGET{float4 p=float4(1,2,3,4);float3 q=p;return float4(q,1);}"))), "shader_compile_failed");
        const string includeSource = "#include \"PRIVATE_DO_NOT_READ.h\"\nfloat4 PSMain():SV_TARGET{return 1;}";
        Bad(() => compiler.Prepare(ShaderDescriptor.Prepare(Definition("include", ShaderStage.Pixel, includeSource))), "shader_compile_failed");
        const string diagnosticSource = "#error PRIVATE_SOURCE_DO_NOT_EXPOSE\nfloat4 PSMain():SV_TARGET{return 1;}";
        Bad(() => compiler.Prepare(ShaderDescriptor.Prepare(Definition("diagnostic", ShaderStage.Pixel, diagnosticSource))), "shader_compile_failed");
        var hugeDiagnostic = Definition("large-diagnostic", ShaderStage.Pixel, "#line 1 \"" + new string('x', 20000) + "\"\nfloat4 PSMain():SV_TARGET{return UNKNOWN_PRIVATE;}\n");
        try { compiler.Prepare(ShaderDescriptor.Prepare(hugeDiagnostic)); throw new Exception("Expected large diagnostic"); }
        catch (ShaderCompileException e) { Console.WriteLine($"Actual compiler diagnostic bytes={e.DiagnosticBytes}, overBudget={e.DiagnosticTruncated}; synthetic boundary policy tested separately in native test"); Pass(e.DiagnosticBytes > 0 && e.DiagnosticTruncated == (e.DiagnosticBytes > 16384) && e.Message.Length < 128 && !e.Message.Contains('x'), "actual diagnostic size/redaction flag truth (not proof of large compiler blob)"); }
        const string arrayTexture = "Texture2D Textures[3]:register(t0);SamplerState S:register(s0);float4 PSMain(float2 uv:TEXCOORD):SV_TARGET{return Textures[0].Sample(S,uv)+Textures[1].Sample(S,uv)+Textures[2].Sample(S,uv);}";
        var textureArray = Definition("texture-array", ShaderStage.Pixel, arrayTexture) with { Resources = [new("Textures", ShaderResourceKind.Texture2D, ShaderResourceAccess.ReadOnly, 0, 3, 0), new("S", ShaderResourceKind.Sampler, ShaderResourceAccess.ReadOnly, 0, 1, 0)] };
        Pass(compiler.Prepare(ShaderDescriptor.Prepare(textureArray)).CopyReflection().Any(r => r.Name == "Textures" && r.Count == 3), "actual SRV array binding range");
        const string typedSource = "Texture2D<uint4> BaseTex:register(t2);float4 PSMain():SV_TARGET{return (float4)BaseTex.Load(int3(0,0,0));}";
        var typedTexture = Definition("typed-texture", ShaderStage.Pixel, typedSource) with { Resources = [new("BaseTex", ShaderResourceKind.Texture2D, ShaderResourceAccess.ReadOnly, 2, 1, 0)] };
        Bad(() => compiler.Prepare(ShaderDescriptor.Prepare(typedTexture)), "shader_resource_sample_type");
        Bad(() => compiler.Prepare(descriptor, [new("A", "1"), new("A", "2")]), "macro");
        Bad(() => compiler.Prepare(descriptor, [new("A", "1\n#include")]), "macro");
        Bad(() => compiler.Prepare(descriptor, Enumerable.Range(0, 17).Select(i => new ShaderMacro("M" + i, "1"))), "macro_budget");
        allowed = false; ulong priorCompilations = compiler.Compilations, priorHits = compiler.CacheHits;
        Bad(() => compiler.Prepare(descriptor)); allowed = true;
        Pass(compiler.Compilations == priorCompilations && compiler.CacheHits == priorHits, "host simulation/preparation gate rechecked before cache");
        Exception? wrongThread = null;
        var thread = new Thread(() => { try { compiler.Prepare(descriptor); } catch (Exception e) { wrongThread = e; } }); thread.Start(); thread.Join();
        Pass(wrongThread is PluginException { Result: PluginResult.WrongThread }, "real cached owner-thread guard");
        ShaderCompilerService? nested = null;
        using (nested = new ShaderCompilerService(renderer, () => { nested!.Prepare(descriptor); return true; })) {
            Bad(() => nested.Prepare(descriptor)); Pass(nested.Compilations == 0 && nested.CachedBytes == 0, "reentrant host callback cannot compile or publish");
        }
        Pass(ReferenceEquals(compiled, compiler.Prepare(descriptor)), "all failed last-row candidates retain original compiled artifact");
        using (var active = renderer.CreateReferenceResources()) {
            var plan = new ReferencePreviewPipeline().Build(256, 256).Compile(renderer.Capabilities);
            // This fixture is called before the main rendering frame1. Dedicated route uses regular renderer.
            renderer.Submit(plan, active, RenderFrame.Reference(1, 256, 256));
            Bad(() => compiler.Prepare(descriptor), "Busy"); renderer.Present();
        }
        // Above one reference frame proves failed candidates left the existing rendering path working.
        var after = renderer.Stats;
        Pass(after.ValidationErrors == 0 && after.ValidationWarnings == 0, "existing reference render/API0/0 after failures");
        using (var bounded = new ShaderCompilerService(renderer, () => true)) {
            for (int i = 0; i < 16; i++) bounded.Prepare(ShaderDescriptor.Prepare(Definition("flat" + i, ShaderStage.Pixel, "float4 PSMain():SV_TARGET{return 1;}")));
            Bad(() => bounded.Prepare(ShaderDescriptor.Prepare(Definition("over-budget", ShaderStage.Pixel, "float4 PSMain():SV_TARGET{return 1;}"))), "shader_cache_budget");
            Pass(bounded.CachedBytes > 0 && bounded.Compilations == 16, "16-entry compiler cache bound");
        }
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "shader-compile-results.json"), JsonSerializer.Serialize(new { schema = 1, cases,
            backend = "DX11", query = 8, api = 1, compiler = compiled.CompilerVersion, flags = compiled.CompilerFlags,
            actualBytecode = true, actualReflection = true, structuredStrideFromBytecode = true, declarationsMatched = true,
            compilations = compiler.Compilations, cacheHits = compiler.CacheHits, nativeGpuResourcesFromCompile = false,
            existingReferenceFrames = after.SubmittedFrames - before.SubmittedFrames, validationErrors = after.ValidationErrors, validationWarnings = after.ValidationWarnings,
            defaultPipelineIntegration = false, inference = false, agentCompilationAuthority = false, manualAcceptance = false }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS M7.1-B {cases} actual DX11 compile/reflection/stride/cache/boundary cases; API0/0; default/user GPU integration pending C");
    }
    private static int ShaderCompileTests(string root, string output)
    {
        using var loader = new PluginLoader(); loader.Load(Path.GetFullPath(root), Specs().Take(2).ToArray());
        using var window = new PlatformWindow(loader.Modules.Single(m => m.Kind == ModuleKind.Platform), "Shader compile test", 256, 256, false);
        using var renderer = new RendererSession(loader.Modules.Single(m => m.Kind == ModuleKind.Renderer), window, 256, 256);
        TestShaderCompilation(renderer, Path.GetFullPath(output)); return 0;
    }
}
