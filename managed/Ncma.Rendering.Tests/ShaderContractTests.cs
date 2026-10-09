using System.Text;
using System.Text.Json.Nodes;
using Ncma.Rendering;

internal static unsafe partial class Program
{
    private static void TestShaderContracts()
    {
        int cases = 0;
        void Pass(bool ok, string message) { Check(ok, "M7.1-A " + message); cases++; }
        void Bad(Action action, string? code = null) {
            try { action(); } catch (ShaderContractException e) {
                Pass(code is null || e.Code == code, "structured rejection " + code);
                Check(e.Message.Length < 256 && !e.Message.Contains("PRIVATE_SOURCE"), "Source leaked in diagnostic"); return;
            }
            throw new Exception("Expected Shader contract rejection " + code);
        }
        const string source = "// PRIVATE_SOURCE\nfloat4 VSMain(float3 p:POSITION):SV_POSITION{return float4(p,1);}\n";
        ShaderDefinition Vertex(Guid? id = null) => new(1, id ?? Guid.NewGuid(), "fixture", ShaderProfile.Scene3D,
            ShaderStage.Vertex, "VSMain", source, ShaderContractCodec.HashSource(source),
            [new("POSITION", 0, ShaderScalar.Float32, 3, 0), new("TEXCOORD", 0, ShaderScalar.Float32, 2, 12)],
            [new("Scene", 0, 80, [new("MVP", ShaderScalar.Float32, 4, 4, ShaderMatrixOrder.ColumnMajor, 0, 1, 0),
                new("Base", ShaderScalar.Float32, 1, 4, ShaderMatrixOrder.None, 64, 1, 0)])],
            [new("BaseTex", ShaderResourceKind.Texture2D, ShaderResourceAccess.ReadOnly, 0, 1, 0),
                new("S", ShaderResourceKind.Sampler, ShaderResourceAccess.ReadOnly, 0, 1, 0)], []);
        var d = Vertex(); byte[] bytes = ShaderContractCodec.Encode(d);
        Pass(ShaderContractCodec.Encode(ShaderContractCodec.Decode(bytes)).SequenceEqual(bytes), "strict canonical roundtrip");
        var prepared = ShaderDescriptor.Prepare(d);
        var reordered = d with { Source = source.Replace("\n", "\r\n"), Inputs = d.Inputs.Reverse().ToArray(),
            Constants = [d.Constants[0] with { Members = d.Constants[0].Members.Reverse().ToArray() }], Resources = d.Resources.Reverse().ToArray() };
        Pass(ShaderDescriptor.Prepare(reordered).ContentHash == prepared.ContentHash, "canonical row order and source line endings");
        d.Inputs[0] = d.Inputs[0] with { Semantic = "MUTATED" };
        var copy = prepared.CopyDefinition(); copy.Constants[0].Members[0] = copy.Constants[0].Members[0] with { Name = "MUTATED" };
        Pass(prepared.CopyDefinition().Inputs.All(x => x.Semantic != "MUTATED") && prepared.CopyDefinition().Constants[0].Members.All(x => x.Name != "MUTATED"), "nested defensive copies");
        d = prepared.CopyDefinition();
        void JsonBad(Action<JsonObject> edit, string? code = null) {
            var node = JsonNode.Parse(bytes)!.AsObject(); edit(node); Bad(() => ShaderContractCodec.Decode(Encoding.UTF8.GetBytes(node.ToJsonString())), code);
        }
        JsonBad(x => x["unknown"] = 1, "json_fields");
        JsonBad(x => x.Remove("dependencies"), "json_fields");
        JsonBad(x => x["version"] = 0, "version");
        JsonBad(x => x["stage"] = "Geometry", "json_enum");
        JsonBad(x => x["stage"] = 0, "json_enum");
        JsonBad(x => x["stage"] = "vertex", "json_enum");
        JsonBad(x => x["stage"] = "0", "json_enum");
        JsonBad(x => x["resources"]![0]!["access"] = "readonly", "json_enum");
        JsonBad(x => x["constants"]![0]!["members"]![0]!["unknown"] = true, "json_fields");
        JsonBad(x => x["inputs"]![0]!["byteOffset"] = long.MaxValue, "json_format");
        Bad(() => ShaderContractCodec.Decode(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\"version\":1", "\"version\":1,\"version\":1"))), "json_fields");
        Bad(() => ShaderContractCodec.Decode([0x7B, 0xFF, 0x7D]), "source_encoding");
        Bad(() => ShaderContractCodec.Decode(new byte[ShaderContractCodec.MaxBytes + 1]), "document_budget");
        Bad(() => ShaderContractCodec.NormalizeSource("\uD800"), "source_encoding");
        Bad(() => ShaderContractCodec.NormalizeSource("x\0y"), "source_budget");
        Bad(() => ShaderContractCodec.NormalizeSource(new string('界', ShaderContractCodec.MaxSourceBytes / 3 + 1)), "source_budget");
        Pass(ShaderContractCodec.NormalizeSource(new string('x', ShaderContractCodec.MaxSourceBytes)).Length == ShaderContractCodec.MaxSourceBytes, "exact source budget");
        Bad(() => ShaderDescriptor.Prepare(d with { SourceHash = new string('0', 64) }), "source_hash");
        Bad(() => ShaderDescriptor.Prepare(d with { AssetId = Guid.Empty }), "identity");
        Bad(() => ShaderDescriptor.Prepare(d with { Name = "\uD800" }), "name_encoding");
        Bad(() => ShaderDescriptor.Prepare(d with { Stage = (ShaderStage)99 }), "enum");
        Bad(() => ShaderDescriptor.Prepare(d with { Stage = ShaderStage.Pixel }), "input_stage");
        Bad(() => ShaderDescriptor.Prepare(d with { EntryPoint = "../../main" }), "identifier");
        Bad(() => ShaderDescriptor.Prepare(d with { Inputs = [d.Inputs[0], d.Inputs[0]] }), "input_contract");
        Bad(() => ShaderDescriptor.Prepare(d with { Inputs = [d.Inputs[0], d.Inputs[1] with { ByteOffset = 4 }] }), "input_overlap");
        Bad(() => ShaderDescriptor.Prepare(d with { Inputs = [new("SV_UNKNOWN", 0, ShaderScalar.UInt32, 1, -1)] }), "system_input");
        Pass(ShaderDescriptor.Prepare(d with { Inputs = [new("SV_VERTEXID", 0, ShaderScalar.UInt32, 1, -1)] }).AssetId == d.AssetId, "vertex ID input distinct from stream");
        var buffer = d.Constants[0]; var matrix = buffer.Members[0];
        Bad(() => ShaderDescriptor.Prepare(d with { Constants = [buffer with { Members = [matrix, matrix with { Name = "Other" }] }] }), "constant_range");
        Bad(() => ShaderDescriptor.Prepare(d with { Constants = [buffer with { Members = [matrix with { ByteOffset = 4 }] }] }), "constant_alignment");
        Bad(() => ShaderDescriptor.Prepare(d with { Constants = [buffer with { Members = [matrix with { MatrixOrder = ShaderMatrixOrder.None }] }] }), "constant_type");
        Bad(() => ShaderDescriptor.Prepare(d with { Constants = [buffer with { Members = [matrix with { ArrayCount = 4096, ArrayStride = 65536 }] }] }), "constant_range");
        Pass(ShaderDescriptor.Prepare(d with { Constants = [new("Array", 1, 64, [new("Values", ShaderScalar.Float32, 1, 4, ShaderMatrixOrder.None, 0, 4, 16)])] }).BindingCount == 3, "bounded array stride");
        Bad(() => ShaderDescriptor.Prepare(d with { Resources = [d.Resources[0], d.Resources[0] with { Semantic = "Other" }] }), "resource_overlap");
        Bad(() => ShaderDescriptor.Prepare(d with { Resources = [d.Resources[0] with { Count = int.MaxValue }] }), "resource_contract");
        Bad(() => ShaderDescriptor.Prepare(d with { Resources = [d.Resources[0] with { Access = ShaderResourceAccess.ReadWrite }] }), "resource_access");
        Bad(() => ShaderDescriptor.Prepare(d with { Resources = [new("RW", ShaderResourceKind.RWByteAddressBuffer, ShaderResourceAccess.ReadWrite, 0, 1, 0)] }), "resource_access");
        Bad(() => ShaderDescriptor.Prepare(d with { Resources = [new("Buffer", ShaderResourceKind.StructuredBuffer, ShaderResourceAccess.ReadOnly, 0, 1, 0)] }), "resource_stride");
        Bad(() => ShaderDescriptor.Prepare(d with { Dependencies = [new(d.AssetId, prepared.ContentHash)] }), "dependency");
        Bad(() => ShaderDescriptor.Prepare(d with { Resources = Enumerable.Range(0, 65).Select(i => new ShaderResourceBinding("R" + i, ShaderResourceKind.Texture2D, ShaderResourceAccess.ReadOnly, 0, 1, 0)).ToArray() }), "row_budget");
        var rowBudget = Enumerable.Range(0, 255).Select(i => new ShaderConstantMember("V" + i, ShaderScalar.Float32, 1, 1, ShaderMatrixOrder.None, i * 4, 1, 0)).ToArray();
        Pass(ShaderDescriptor.Prepare(d with { Inputs = [], Resources = [], Constants = [new("C", 0, 1024, rowBudget)] }).BindingCount == 1, "exact 256 declaration rows");
        Bad(() => ShaderDescriptor.Prepare(d with { Inputs = [], Resources = [], Constants = [new("C", 0, 1024, rowBudget.Append(new("V255", ShaderScalar.Float32, 1, 1, ShaderMatrixOrder.None, 1020, 1, 0)).ToArray())] }), "row_budget");
        Bad(() => ShaderDescriptor.Prepare(d with { Dependencies = Enumerable.Range(0, 17).Select(_ => new ShaderDependency(Guid.NewGuid(), prepared.ContentHash)).ToArray() }), "row_budget");
        var bindings = new ShaderBindingSet(prepared.ContentHash, d.Stage, d.Inputs, d.Constants, d.Resources);
        ShaderBindingValidation.Validate(prepared, bindings); cases++;
        ShaderBindingValidation.Validate(prepared, bindings with { Resources = d.Resources.Reverse().ToArray() }); cases++;
        Bad(() => ShaderBindingValidation.Validate(prepared, bindings with { ContentHash = new string('F', 64) }), "binding_identity");
        Bad(() => ShaderBindingValidation.Validate(prepared, bindings with { Stage = ShaderStage.Compute }), "binding_identity");
        Bad(() => ShaderBindingValidation.Validate(prepared, bindings with { Resources = [d.Resources[0], d.Resources[1] with { Slot = 1 }] }), "binding_mismatch");
        Bad(() => ShaderBindingValidation.Validate(prepared, bindings with { Constants = [buffer with { Members = [matrix with { MatrixOrder = ShaderMatrixOrder.RowMajor }, buffer.Members[1]] }] }), "binding_mismatch");
        var catalog = ShaderCatalog.Create(ShaderProfile.Scene3D, [d]); var page = catalog.CopyPage(0);
        Pass(catalog.Count == 1 && !page[0].Compiled && catalog.Require(d.AssetId, prepared.ContentHash).ContentHash == prepared.ContentHash, "catalog explicitly uncompiled");
        page[0] = default;
        Pass(catalog.CopyPage(0)[0].AssetId == d.AssetId, "copied read-only metadata");
        Bad(() => catalog.Require(d.AssetId, new string('0', 64)), "catalog_identity");
        Bad(() => catalog.CopyPage(int.MaxValue), "page");
        var flat = ShaderCatalog.Create(ShaderProfile.Flat2D, []);
        Pass(flat.Count == 0 && flat.CopyPage(0).Length == 0, "2D has no automatic 3D/skin registrations");
        Bad(() => ShaderCatalog.Create(ShaderProfile.Flat2D, [d]), "profile");
        Bad(() => ShaderCatalog.Create(ShaderProfile.Scene3D, [d, d]), "duplicate_shader");
        Bad(() => ShaderCatalog.Create(ShaderProfile.Scene3D, [d with { Dependencies = [new(Guid.NewGuid(), prepared.ContentHash)] }]), "dependency_hash");
        var child = d with { AssetId = Guid.NewGuid(), Name = "child", Dependencies = [new(d.AssetId, prepared.ContentHash)] };
        var together = ShaderCatalog.Create(ShaderProfile.Scene3D, [child, d]);
        Pass(together.ContentHash == ShaderCatalog.Create(ShaderProfile.Scene3D, [d, child]).ContentHash, "unordered exact dependency closure canonical");
        Bad(() => ShaderCatalog.Create(ShaderProfile.Scene3D, [child with { Dependencies = [new(d.AssetId, new string('0', 64))] }, d]), "dependency_hash");
        var many = Enumerable.Range(0, 128).Select(i => d with { AssetId = Guid.NewGuid(), Name = "shader" + i }).ToArray();
        var bounded = ShaderCatalog.Create(ShaderProfile.Scene3D, many);
        Pass(bounded.Count == 128 && bounded.CopyPage(15).Length == 8, "128 shaders / 8-row pages");
        Bad(() => ShaderCatalog.Create(ShaderProfile.Scene3D, many.Append(child)), "catalog_budget");
        string bigSource = new('x', ShaderContractCodec.MaxSourceBytes);
        var big = many.Take(33).Select(x => x with { Source = bigSource, SourceHash = ShaderContractCodec.HashSource(bigSource) }).ToArray();
        Bad(() => ShaderCatalog.Create(ShaderProfile.Scene3D, big), "catalog_budget");
        var priorHash = catalog.ContentHash;
        Bad(() => ShaderCatalog.Create(ShaderProfile.Scene3D, [d, child with { Resources = [null!] }]), "row_required");
        Pass(catalog.ContentHash == priorHash && catalog.Count == 1, "last-row failure leaves published catalog intact");
        // Declaration-shape fixtures derived from current native sources, not compiler/reflection evidence.
        var sceneMembers = new List<ShaderConstantMember>();
        for (int i = 0; i < 4; i++) sceneMembers.Add(new("Matrix" + i, ShaderScalar.Float32, 4, 4, ShaderMatrixOrder.ColumnMajor, i * 64, 1, 0));
        for (int i = 0; i < 9; i++) sceneMembers.Add(new("Vector" + i, ShaderScalar.Float32, 1, 4, ShaderMatrixOrder.None, 256 + i * 16, 1, 0));
        var sceneResources = Enumerable.Range(0, 7).Select(i => new ShaderResourceBinding("Texture" + i, ShaderResourceKind.Texture2D, ShaderResourceAccess.ReadOnly, i, 1, 0))
            .Concat([new("S", ShaderResourceKind.Sampler, ShaderResourceAccess.ReadOnly, 0, 1, 0), new("SS", ShaderResourceKind.Sampler, ShaderResourceAccess.ReadOnly, 6, 1, 0)]).ToArray();
        Pass(ShaderDescriptor.Prepare(d with { Stage = ShaderStage.Pixel, Inputs = [], Constants = [new("C", 0, 400, sceneMembers.ToArray())], Resources = sceneResources }).BindingCount == 10, "current scene 400-byte declaration fits budgets");
        var skin = d with { Profile = ShaderProfile.Skinning, Stage = ShaderStage.Compute, Inputs = [],
            Constants = [new("Settings", 0, 16, Enumerable.Range(0, 4).Select(i => new ShaderConstantMember("Value" + i, ShaderScalar.UInt32, 1, 1, ShaderMatrixOrder.None, i * 4, 1, 0)).ToArray())],
            Resources = [new("Source", ShaderResourceKind.StructuredBuffer, ShaderResourceAccess.ReadOnly, 0, 1, 80),
                new("Bones", ShaderResourceKind.StructuredBuffer, ShaderResourceAccess.ReadOnly, 1, 1, 128),
                new("Output", ShaderResourceKind.RWByteAddressBuffer, ShaderResourceAccess.ReadWrite, 0, 1, 0)] };
        Pass(ShaderCatalog.Create(ShaderProfile.Skinning, [skin]).Count == 1, "current skin declaration independent profile");
        Bad(() => ShaderDescriptor.Prepare(skin with { Profile = ShaderProfile.Flat2D }), "profile_stage");
        long allocation = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1024; i++) Check(ReferenceEquals(catalog.Require(d.AssetId, prepared.ContentHash), catalog.Require(d.AssetId, prepared.ContentHash)), "Stable immutable lookup");
        Pass(GC.GetAllocatedBytesForCurrentThread() - allocation == 0, "1024 warmed lookups allocate zero (not GPU performance acceptance)");
        Console.WriteLine($"PASS M7.1-A {cases} pure managed shader contract cases; compiled=false; GPU/reflection/MCP/publish not executed");
    }
}
