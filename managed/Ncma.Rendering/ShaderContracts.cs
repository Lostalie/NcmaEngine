using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ncma.Rendering;

// Declarations only. No shader compilation, GPU support registration, file IO or execution authority.
public enum ShaderStage { Vertex, Pixel, Compute }
public enum ShaderProfile { Flat2D, Scene3D, Skinning }
public enum ShaderScalar { Float32, Int32, UInt32 }
public enum ShaderMatrixOrder { None, RowMajor, ColumnMajor }
public enum ShaderResourceKind { Texture2D, TextureCube, StructuredBuffer, ByteAddressBuffer, Sampler, RWStructuredBuffer, RWByteAddressBuffer }
public enum ShaderResourceAccess { ReadOnly, ReadWrite }
public sealed record ShaderVertexInput(string Semantic, int Index, ShaderScalar Scalar, int Components, int ByteOffset);
public sealed record ShaderConstantMember(string Name, ShaderScalar Scalar, int Rows, int Columns, ShaderMatrixOrder MatrixOrder, int ByteOffset, int ArrayCount, int ArrayStride);
public sealed record ShaderConstantBuffer(string Semantic, int Slot, int ByteSize, ShaderConstantMember[] Members);
public sealed record ShaderResourceBinding(string Semantic, ShaderResourceKind Kind, ShaderResourceAccess Access, int Slot, int Count, int ElementStride);
public sealed record ShaderDependency(Guid AssetId, string ContentHash);
public sealed record ShaderDefinition(int Version, Guid AssetId, string Name, ShaderProfile Profile, ShaderStage Stage,
    string EntryPoint, string Source, string SourceHash, ShaderVertexInput[] Inputs, ShaderConstantBuffer[] Constants,
    ShaderResourceBinding[] Resources, ShaderDependency[] Dependencies);
public sealed class ShaderContractException(string code, string path) : ArgumentException($"Shader contract: {code} at {path}.")
{
    public string Code { get; } = code;
    public string FieldPath { get; } = path;
}

public static class ShaderContractCodec
{
    public const int CurrentVersion = 1, MaxSourceBytes = 256 * 1024, MaxBytes = 1024 * 1024,
        MaxBindings = 64, MaxRows = 256, MaxDependencies = 16;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions Json = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 12, Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static string NormalizeSource(string source)
    {
        if (source is null || source.Length is < 1 or > MaxSourceBytes || source.Contains('\0')) Fail("source_budget", "source");
        string normalized = source!.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        try { if (Utf8.GetByteCount(normalized) > MaxSourceBytes) Fail("source_budget", "source"); }
        catch (EncoderFallbackException) { Fail("source_encoding", "source"); }
        return normalized;
    }
    public static string HashSource(string source) => Convert.ToHexString(SHA256.HashData(Utf8.GetBytes(NormalizeSource(source))));
    public static byte[] Encode(ShaderDefinition definition)
    {
        Validate(definition);
        var canonical = definition with {
            Source = NormalizeSource(definition.Source),
            Inputs = definition.Inputs.OrderBy(x => x.Semantic, StringComparer.Ordinal).ThenBy(x => x.Index).ToArray(),
            Constants = definition.Constants.OrderBy(x => x.Slot).Select(x => x with { Members = x.Members.OrderBy(m => m.ByteOffset).ToArray() }).ToArray(),
            Resources = definition.Resources.OrderBy(x => Namespace(x.Kind)).ThenBy(x => x.Slot).ToArray(),
            Dependencies = definition.Dependencies.OrderBy(x => x.AssetId).ToArray()
        };
        byte[] result = JsonSerializer.SerializeToUtf8Bytes(canonical, Json);
        if (result.Length > MaxBytes) Fail("document_budget", "$");
        return result;
    }
    public static ShaderDefinition Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 2 or > MaxBytes) Fail("document_budget", "$");
        try {
            _ = Utf8.GetCharCount(bytes);
            using var document = JsonDocument.Parse(bytes.ToArray(), new() { MaxDepth = 12 });
            var root = document.RootElement;
            Fields(root, "version", "assetId", "name", "profile", "stage", "entryPoint", "source", "sourceHash", "inputs", "constants", "resources", "dependencies");
            EnumField<ShaderProfile>(root, "profile"); EnumField<ShaderStage>(root, "stage");
            Rows(root.GetProperty("inputs"), MaxRows, e => {
                Fields(e, "semantic", "index", "scalar", "components", "byteOffset"); EnumField<ShaderScalar>(e, "scalar");
            });
            Rows(root.GetProperty("constants"), MaxBindings, e => {
                Fields(e, "semantic", "slot", "byteSize", "members");
                Rows(e.GetProperty("members"), MaxRows, m => {
                    Fields(m, "name", "scalar", "rows", "columns", "matrixOrder", "byteOffset", "arrayCount", "arrayStride");
                    EnumField<ShaderScalar>(m, "scalar"); EnumField<ShaderMatrixOrder>(m, "matrixOrder");
                });
            });
            Rows(root.GetProperty("resources"), MaxBindings, e => {
                Fields(e, "semantic", "kind", "access", "slot", "count", "elementStride");
                EnumField<ShaderResourceKind>(e, "kind"); EnumField<ShaderResourceAccess>(e, "access");
            });
            Rows(root.GetProperty("dependencies"), MaxDependencies, e => Fields(e, "assetId", "contentHash"));
            var result = JsonSerializer.Deserialize<ShaderDefinition>(bytes, Json);
            if (result is null) Fail("document_required", "$");
            Validate(result!); return result!;
        } catch (JsonException) { throw new ShaderContractException("json_format", "$"); }
        catch (DecoderFallbackException) { throw new ShaderContractException("source_encoding", "$"); }
    }
    private static void EnumField<T>(JsonElement e, string name) where T : struct, Enum
    {
        var value = e.GetProperty(name);
        if (value.ValueKind != JsonValueKind.String || !Enum.GetNames<T>().Contains(value.GetString(), StringComparer.Ordinal)) Fail("json_enum", name);
    }
    private static void Fields(JsonElement e, params string[] expected)
    {
        if (e.ValueKind != JsonValueKind.Object) Fail("json_object", "$");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in e.EnumerateObject()) if (!seen.Add(p.Name) || !expected.Contains(p.Name, StringComparer.Ordinal)) Fail("json_fields", "$");
        if (seen.Count != expected.Length) Fail("json_fields", "$");
    }
    private static void Rows(JsonElement e, int budget, Action<JsonElement> validate)
    {
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() > budget) Fail("row_budget", "$");
        foreach (var row in e.EnumerateArray()) validate(row);
    }
    internal static bool IsHash(string? hash) => hash is { Length: 64 } && hash.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    internal static void Fail(string code, string path) => throw new ShaderContractException(code, path);
    private static void Identifier(string? name, string path, bool upper = false)
    {
        static bool Letter(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '_';
        if (name is null || name.Length is < 1 or > 64 || !Letter(name[0]) || name.Any(c => !Letter(c) && c is not (>= '0' and <= '9')) || (upper && name != name.ToUpperInvariant())) Fail("identifier", path);
    }
    internal static int Namespace(ShaderResourceKind kind) => kind == ShaderResourceKind.Sampler ? 2 : kind is ShaderResourceKind.RWStructuredBuffer or ShaderResourceKind.RWByteAddressBuffer ? 1 : 0;
    public static void Validate(ShaderDefinition d)
    {
        if (d is null) Fail("document_required", "$");
        if (d!.Version != CurrentVersion) Fail("version", "version");
        if (d.AssetId == Guid.Empty) Fail("identity", "assetId");
        if (d.Name is null || d.Name.Length is < 1 or > 128 || d.Name.Any(char.IsControl) || string.IsNullOrWhiteSpace(d.Name)) Fail("name", "name");
        try { _ = Utf8.GetByteCount(d.Name); } catch (EncoderFallbackException) { Fail("name_encoding", "name"); }
        if (!Enum.IsDefined(d.Profile) || !Enum.IsDefined(d.Stage)) Fail("enum", "stage/profile");
        if (d.Profile == ShaderProfile.Flat2D && d.Stage == ShaderStage.Compute || d.Profile == ShaderProfile.Skinning && d.Stage != ShaderStage.Compute) Fail("profile_stage", "stage/profile");
        Identifier(d.EntryPoint, "entryPoint");
        if (!IsHash(d.SourceHash) || HashSource(d.Source) != d.SourceHash) Fail("source_hash", "sourceHash");
        if (d.Inputs is null || d.Constants is null || d.Resources is null || d.Dependencies is null) Fail("collections", "$");
        if (d.Inputs!.Length > MaxRows || d.Constants!.Length + d.Resources!.Length > MaxBindings || d.Dependencies!.Length > MaxDependencies) Fail("row_budget", "$");
        if (d.Stage != ShaderStage.Vertex && d.Inputs.Length != 0) Fail("input_stage", "inputs");
        var inputs = new HashSet<(string, int)>(); var occupied = new HashSet<int>();
        foreach (var i in d.Inputs) {
            if (i is null) Fail("row_required", "inputs");
            Identifier(i!.Semantic, "inputs.semantic", true);
            if (!inputs.Add((i.Semantic, i.Index)) || i.Index is < 0 or > 31 || !Enum.IsDefined(i.Scalar) || i.Components is < 1 or > 4) Fail("input_contract", "inputs");
            if (i.Semantic.StartsWith("SV_", StringComparison.Ordinal)) {
                if (i.Semantic is not ("SV_VERTEXID" or "SV_INSTANCEID") || i.Index != 0 || i.Scalar != ShaderScalar.UInt32 || i.Components != 1 || i.ByteOffset != -1) Fail("system_input", "inputs");
            } else {
                if (i.ByteOffset is < 0 or > 4096 || i.ByteOffset % 4 != 0 || i.ByteOffset + i.Components * 4 > 4096) Fail("input_layout", "inputs");
                for (int at = i.ByteOffset; at < i.ByteOffset + i.Components * 4; at += 4) if (!occupied.Add(at)) Fail("input_overlap", "inputs");
            }
        }
        var names = new HashSet<string>(StringComparer.Ordinal); var slots = new HashSet<int>(); int rows = d.Inputs.Length + d.Resources.Length;
        foreach (var b in d.Constants) {
            if (b is null) Fail("row_required", "constants");
            Identifier(b!.Semantic, "constants.semantic");
            if (!names.Add(b.Semantic) || !slots.Add(b.Slot) || b.Slot is < 0 or > 63 || b.ByteSize is < 16 or > 65536 || b.ByteSize % 16 != 0 || b.Members is null || b.Members.Length is < 1 or > MaxRows) Fail("constant_buffer", "constants");
            rows += 1 + b.Members!.Length;
            var members = new HashSet<string>(StringComparer.Ordinal); var ranges = new List<(long Start, long End)>();
            foreach (var m in b.Members) {
                if (m is null) Fail("row_required", "constants.members");
                Identifier(m!.Name, "constants.members.name");
                if (!members.Add(m.Name) || !Enum.IsDefined(m.Scalar) || !Enum.IsDefined(m.MatrixOrder) || m.Rows is < 1 or > 4 || m.Columns is < 1 or > 4 || m.ArrayCount is < 1 or > 4096 || m.ByteOffset < 0 || m.ByteOffset % 4 != 0) Fail("constant_member", "constants.members");
                bool matrix = m.Rows > 1;
                if (matrix && (m.Scalar != ShaderScalar.Float32 || m.MatrixOrder == ShaderMatrixOrder.None) || !matrix && m.MatrixOrder != ShaderMatrixOrder.None) Fail("constant_type", "constants.members");
                int elementBytes = matrix ? (m.MatrixOrder == ShaderMatrixOrder.RowMajor ? m.Rows : m.Columns) * 16 : m.Columns * 4;
                if ((matrix || m.ArrayCount > 1) && m.ByteOffset % 16 != 0 || !matrix && m.ArrayCount == 1 && m.ByteOffset % 16 + elementBytes > 16) Fail("constant_alignment", "constants.members");
                if (m.ArrayCount == 1 && m.ArrayStride != 0 || m.ArrayCount > 1 && (m.ArrayStride < elementBytes || m.ArrayStride > 65536 || m.ArrayStride % 16 != 0)) Fail("constant_stride", "constants.members");
                long end = (long)m.ByteOffset + (long)(m.ArrayCount - 1) * m.ArrayStride + elementBytes;
                if (end > b.ByteSize || ranges.Any(r => m.ByteOffset < r.End && end > r.Start)) Fail("constant_range", "constants.members");
                ranges.Add((m.ByteOffset, end));
            }
        }
        var resourceSlots = new HashSet<(int, int)>();
        foreach (var r in d.Resources) {
            if (r is null) Fail("row_required", "resources");
            Identifier(r!.Semantic, "resources.semantic");
            if (!names.Add(r.Semantic) || !Enum.IsDefined(r.Kind) || !Enum.IsDefined(r.Access) || r.Slot is < 0 or > 63 || r.Count is < 1 or > 64 || (long)r.Slot + r.Count > 64) Fail("resource_contract", "resources");
            bool write = Namespace(r.Kind) == 1;
            if (write && (d.Stage != ShaderStage.Compute || r.Access != ShaderResourceAccess.ReadWrite) || !write && r.Access != ShaderResourceAccess.ReadOnly) Fail("resource_access", "resources");
            bool structured = r.Kind is ShaderResourceKind.StructuredBuffer or ShaderResourceKind.RWStructuredBuffer;
            if (structured ? r.ElementStride is < 4 or > 2048 || r.ElementStride % 4 != 0 : r.ElementStride != 0) Fail("resource_stride", "resources");
            for (int slot = r.Slot; slot < r.Slot + r.Count; slot++) if (!resourceSlots.Add((Namespace(r.Kind), slot))) Fail("resource_overlap", "resources");
        }
        if (rows > MaxRows) Fail("row_budget", "$");
        var dependencies = new HashSet<Guid>();
        foreach (var dep in d.Dependencies) if (dep is null || dep.AssetId == Guid.Empty || dep.AssetId == d.AssetId || !dependencies.Add(dep.AssetId) || !IsHash(dep.ContentHash)) Fail("dependency", "dependencies");
    }
}

// Immutable, privately copied declaration. CopyDefinition is a TRUSTED source-bearing API, not an Agent tool.
public sealed class ShaderDescriptor
{
    private readonly byte[] _canonical;
    public Guid AssetId { get; }
    public string Name { get; }
    public ShaderProfile Profile { get; }
    public ShaderStage Stage { get; }
    public string ContentHash { get; }
    public string SourceHash { get; }
    public int SourceBytes { get; }
    public int BindingCount { get; }
    private ShaderDescriptor(byte[] canonical, ShaderDefinition value)
    {
        _canonical = canonical; AssetId = value.AssetId; Name = value.Name; Profile = value.Profile; Stage = value.Stage;
        SourceHash = value.SourceHash; ContentHash = Convert.ToHexString(SHA256.HashData(canonical));
        SourceBytes = Encoding.UTF8.GetByteCount(value.Source); BindingCount = value.Constants.Length + value.Resources.Length;
    }
    public static ShaderDescriptor Prepare(ShaderDefinition value)
    {
        byte[] canonical = ShaderContractCodec.Encode(value);
        return new(canonical, ShaderContractCodec.Decode(canonical));
    }
    public ShaderDefinition CopyDefinition() => ShaderContractCodec.Decode(_canonical);
}

public readonly record struct ShaderCatalogRow(Guid AssetId, string Name, ShaderProfile Profile, ShaderStage Stage,
    string ContentHash, string SourceHash, int SourceBytes, int BindingCount, bool Compiled);

// Trusted off-frame immutable publication, not live GPU installation. Explicitly empty until supplied.
public sealed class ShaderCatalog
{
    public const int MaxShaders = 128, MaxSourceBytes = 8 * 1024 * 1024, PageSize = 8;
    private readonly Dictionary<Guid, ShaderDescriptor> _byId;
    private readonly ShaderDescriptor[] _ordered;
    public ShaderProfile Profile { get; }
    public int Count => _ordered.Length;
    public string ContentHash { get; }
    private ShaderCatalog(ShaderProfile profile, Dictionary<Guid, ShaderDescriptor> byId)
    {
        Profile = profile; _byId = byId; _ordered = byId.Values.OrderBy(x => x.AssetId).ToArray();
        string key = ShaderContractCodec.CurrentVersion + ":" + profile + ":" + string.Join(";", _ordered.Select(x => x.AssetId.ToString("D") + ":" + x.ContentHash));
        ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }
    public static ShaderCatalog Create(ShaderProfile profile, IEnumerable<ShaderDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        if (!Enum.IsDefined(profile)) ShaderContractCodec.Fail("enum", "profile");
        var byId = new Dictionary<Guid, ShaderDescriptor>(); var names = new HashSet<(string, ShaderStage)>(); long sourceBytes = 0;
        foreach (var definition in definitions) {
            if (byId.Count == MaxShaders) ShaderContractCodec.Fail("catalog_budget", "catalog");
            var descriptor = ShaderDescriptor.Prepare(definition);
            sourceBytes += descriptor.SourceBytes;
            if (sourceBytes > MaxSourceBytes) ShaderContractCodec.Fail("catalog_budget", "catalog");
            if (descriptor.Profile != profile) ShaderContractCodec.Fail("profile", "catalog");
            if (!byId.TryAdd(descriptor.AssetId, descriptor) || !names.Add((descriptor.Name, descriptor.Stage))) ShaderContractCodec.Fail("duplicate_shader", "catalog");
        }
        var states = new Dictionary<Guid, int>();
        void Visit(Guid id) {
            if (states.TryGetValue(id, out int state)) { if (state == 1) ShaderContractCodec.Fail("dependency_cycle", "catalog"); return; }
            states[id] = 1;
            foreach (var dep in byId[id].CopyDefinition().Dependencies) {
                if (!byId.TryGetValue(dep.AssetId, out var target) || dep.ContentHash != target.ContentHash) ShaderContractCodec.Fail("dependency_hash", "catalog");
                Visit(dep.AssetId);
            }
            states[id] = 2;
        }
        foreach (Guid id in byId.Keys) Visit(id);
        return new(profile, byId);
    }
    public ShaderDescriptor Require(Guid id, string exactContentHash)
    {
        if (!_byId.TryGetValue(id, out var descriptor) || descriptor.ContentHash != exactContentHash) ShaderContractCodec.Fail("catalog_identity", "catalog");
        return descriptor!;
    }
    // Copies metadata only. Compiled=false is deliberate; publication never grants compilation or GPU use.
    public ShaderCatalogRow[] CopyPage(int page)
    {
        if (page < 0 || page > (_ordered.Length == 0 ? 0 : (_ordered.Length - 1) / PageSize)) ShaderContractCodec.Fail("page", "catalog");
        return _ordered.Skip(page * PageSize).Take(PageSize).Select(x => new ShaderCatalogRow(x.AssetId, x.Name, x.Profile, x.Stage,
            x.ContentHash, x.SourceHash, x.SourceBytes, x.BindingCount, false)).ToArray();
    }
}

public sealed record ShaderBindingSet(string ContentHash, ShaderStage Stage, ShaderVertexInput[] Inputs,
    ShaderConstantBuffer[] Constants, ShaderResourceBinding[] Resources);
public static class ShaderBindingValidation
{
    // Checks caller declarations against an exact immutable declaration, NOT reflected bytecode or actual GPU bindings.
    public static void Validate(ShaderDescriptor shader, ShaderBindingSet bindings)
    {
        ArgumentNullException.ThrowIfNull(shader); ArgumentNullException.ThrowIfNull(bindings);
        if (bindings.ContentHash != shader.ContentHash || bindings.Stage != shader.Stage) ShaderContractCodec.Fail("binding_identity", "bindings");
        var declared = shader.CopyDefinition();
        byte[] expected = ShaderContractCodec.Encode(declared);
        byte[] supplied = ShaderContractCodec.Encode(declared with { Inputs = bindings.Inputs, Constants = bindings.Constants, Resources = bindings.Resources });
        if (!expected.AsSpan().SequenceEqual(supplied)) ShaderContractCodec.Fail("binding_mismatch", "bindings");
    }
}
