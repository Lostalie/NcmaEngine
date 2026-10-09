using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Ncma.Interop;

namespace Ncma.Rendering;

[StructLayout(LayoutKind.Sequential)] internal unsafe struct ShaderCompileRequest
{ public uint Size, Version, Stage, SourceBytes, EntryBytes, MacroCount, Reserved, Reserved2; public byte* Source; public byte* Entry; public ShaderMacroNative* Macros; }
[StructLayout(LayoutKind.Sequential)] internal unsafe struct ShaderMacroNative { public fixed byte Name[64]; public fixed byte Value[64]; }
[StructLayout(LayoutKind.Sequential)] internal unsafe struct ShaderRowNative
{
    public uint Kind, Stage, Scalar, Columns, Rows, Index, Slot, Count, Offset, Bytes, Order, ArrayCount, ArrayStride, ResourceKind, Access, Stride;
    public fixed byte Name[64]; public fixed byte Parent[64];
}
[StructLayout(LayoutKind.Sequential)] internal struct ShaderCompileOutput
{ public uint Size, Version, Stage, Rows, Bytes, CompilerVersion, Flags, Reserved; }
[StructLayout(LayoutKind.Sequential)] internal struct ShaderCompileApi { public uint Size, Version; public ulong Caps; public nint Compile, Validate; }
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal unsafe delegate uint CompileShaderNative(ulong module, ulong renderer, ShaderCompileRequest* request,
    ShaderCompileOutput* output, ShaderRowNative* rows, uint rowCapacity, byte* bytes, uint byteCapacity, PluginError* error);

public sealed record ShaderMacro(string Name, string Value);
public sealed record ShaderReflectionRow(int Kind, ShaderStage Stage, string Name, string Parent, ShaderScalar Scalar,
    int Columns, int Rows, int Index, int Slot, int Count, long ByteOffset, int ByteSize, ShaderMatrixOrder MatrixOrder,
    int ArrayCount, int ArrayStride, ShaderResourceKind ResourceKind, ShaderResourceAccess Access, int ElementStride);
public sealed class ShaderCompileException(string code, int diagnosticBytes, bool truncated) : InvalidOperationException(code)
{
    public string Code { get; } = code;
    public int DiagnosticBytes { get; } = diagnosticBytes;
    public bool DiagnosticTruncated { get; } = truncated;
}

// Copied immutable bytecode/reflection only. Not a GPU shader handle or executable pipeline registration.
public sealed class CompiledShader
{
    private readonly byte[] _bytes; private readonly ShaderReflectionRow[] _rows;
    public ShaderDescriptor Descriptor { get; }
    public string CacheKey { get; }
    public string BytecodeHash { get; }
    public uint CompilerVersion { get; }
    public uint CompilerFlags { get; }
    public int BytecodeBytes => _bytes.Length;
    internal CompiledShader(ShaderDescriptor descriptor, string key, byte[] bytes, ShaderReflectionRow[] rows, uint compiler, uint flags)
    { Descriptor = descriptor; CacheKey = key; _bytes = bytes; _rows = rows; CompilerVersion = compiler; CompilerFlags = flags; BytecodeHash = Convert.ToHexString(SHA256.HashData(bytes)); }
    public byte[] CopyBytecode() => (byte[])_bytes.Clone();
    public ShaderReflectionRow[] CopyReflection() => (ShaderReflectionRow[])_rows.Clone();
    public ShaderReflectionRow[] CopyReflectionPage(int page)
    {
        if (page < 0 || page > (_rows.Length == 0 ? 0 : (_rows.Length - 1) / ShaderCatalog.PageSize)) ShaderContractCodec.Fail("page", "reflection");
        return _rows.Skip(page * ShaderCatalog.PageSize).Take(ShaderCatalog.PageSize).ToArray();
    }
    internal ReadOnlySpan<ShaderReflectionRow> Rows => _rows;
}

public sealed unsafe partial class RendererSession
{
    private CompileShaderNative? _compileShader;
    private RendererOperation? _validateShaderPreparation;
    private void EnsureShaderCompiler()
    {
        Verify(); if (_compileShader is not null) return;
        if (sizeof(ShaderCompileRequest) != 56 || sizeof(ShaderMacroNative) != 128 || sizeof(ShaderRowNative) != 192 || sizeof(ShaderCompileOutput) != 32 || sizeof(ShaderCompileApi) != 32) throw new PlatformNotSupportedException("Shader compiler x64 ABI.");
        ShaderCompileApi api = default; PluginError error = default;
        PluginModule.Check(Module.Id, "query_shader_compiler", Module.ReadFunction<QuerySceneRender>(144)(Module.Context, 8, &api, 32, &error), error);
        if (api.Size != 32 || api.Version != 1 || api.Caps != 1 || api.Compile == 0 || api.Validate == 0) throw new InvalidOperationException("Shader query8/API1 mismatch.");
        _validateShaderPreparation = Marshal.GetDelegateForFunctionPointer<RendererOperation>(api.Validate);
        _compileShader = Marshal.GetDelegateForFunctionPointer<CompileShaderNative>(api.Compile);
    }
    internal CompiledShader CompileDeclaration(ShaderDescriptor shader, string key, ShaderMacro[] macros)
    {
        EnsureShaderCompiler(); var d = shader.CopyDefinition(); byte[] source = Encoding.UTF8.GetBytes(d.Source), entry = Encoding.ASCII.GetBytes(d.EntryPoint);
        Span<ShaderMacroNative> nativeMacros = stackalloc ShaderMacroNative[16]; nativeMacros.Clear();
        for (int i = 0; i < macros.Length; i++) {
            var m = macros[i]; ref var value = ref nativeMacros[i];
            for (int at = 0; at < m.Name.Length; at++) value.Name[at] = (byte)m.Name[at];
            for (int at = 0; at < m.Value.Length; at++) value.Value[at] = (byte)m.Value[at];
        }
        var rows = new ShaderRowNative[256]; var bytes = new byte[1024 * 1024]; ShaderCompileOutput output = default; PluginError error = default;
        fixed (byte* s = source) fixed (byte* e = entry) fixed (byte* b = bytes) fixed (ShaderRowNative* r = rows) fixed (ShaderMacroNative* m = nativeMacros) {
            ShaderCompileRequest request = new() { Size = 56, Version = 1, Stage = (uint)d.Stage, SourceBytes = (uint)source.Length, EntryBytes = (uint)entry.Length, MacroCount = (uint)macros.Length, Source = s, Entry = e, Macros = m };
            uint result = _compileShader!(Module.Context, Handle, &request, &output, r, 256, b, (uint)bytes.Length, &error);
            if (result == (uint)PluginResult.InvalidArgument || result == (uint)PluginResult.UnsupportedFeature)
                throw new ShaderCompileException(error.Text.Length == 0 ? "shader_compile_rejected" : error.Text, checked((int)Math.Min(error.RequiredBytes, int.MaxValue)), error.Reserved == 1);
            PluginModule.Check(Module.Id, "compile_shader", result, error);
        }
        if (output.Size != 32 || output.Version != 1 || output.Stage != (uint)d.Stage || output.Rows > 256 || output.Bytes is < 4 or > 1048576 || output.CompilerVersion != 47 || output.Flags != 0x48800 || output.Reserved != 0) throw new InvalidOperationException("Shader response contract.");
        var copied = new ShaderReflectionRow[output.Rows];
        for (int i = 0; i < copied.Length; i++) { var r = rows[i];
            copied[i] = new(checked((int)r.Kind), (ShaderStage)r.Stage, ReadShaderName(r.Name), ReadShaderName(r.Parent), (ShaderScalar)r.Scalar,
                checked((int)r.Columns), checked((int)r.Rows), checked((int)r.Index), checked((int)r.Slot), checked((int)r.Count), r.Offset,
                checked((int)r.Bytes), (ShaderMatrixOrder)r.Order, checked((int)r.ArrayCount), checked((int)r.ArrayStride), (ShaderResourceKind)r.ResourceKind, (ShaderResourceAccess)r.Access, checked((int)r.Stride));
        }
        var resultShader = new CompiledShader(shader, key, bytes.AsSpan(0, (int)output.Bytes).ToArray(), copied, output.CompilerVersion, output.Flags);
        ShaderReflectionValidation.Validate(shader, resultShader); return resultShader;
    }
    private static string ReadShaderName(byte* bytes)
    {
        int count = 0; while (count < 64 && bytes[count] != 0) { if (bytes[count] > 127) throw new InvalidOperationException("Shader reflection ASCII."); count++; }
        if (count == 64) throw new InvalidOperationException("Shader reflection name budget.");
        return Encoding.ASCII.GetString(bytes, count);
    }
    internal void VerifyShaderPreparation()
    { EnsureShaderCompiler(); PluginError error = default; PluginModule.Check(Module.Id, "shader_preparation_boundary", _validateShaderPreparation!(Module.Context, Handle, &error), error); }
}

public static class ShaderReflectionValidation
{
    public static void Validate(ShaderDescriptor descriptor, CompiledShader compiled)
    {
        ArgumentNullException.ThrowIfNull(descriptor); ArgumentNullException.ThrowIfNull(compiled);
        if (descriptor.ContentHash != compiled.Descriptor.ContentHash) ShaderContractCodec.Fail("reflection_identity", "reflection");
        var d = descriptor.CopyDefinition(); var rows = compiled.Rows;
        int expectedRows = d.Inputs.Length + d.Constants.Sum(b => 1 + b.Members.Length) + d.Resources.Length;
        if (rows.Length != expectedRows) ShaderContractCodec.Fail("reflection_rows", "reflection");
        var seen = new HashSet<(int, string, string, int)>();
        foreach (var r in rows) {
            if (r.Stage != d.Stage || !seen.Add((r.Kind, r.Name, r.Parent, r.Index))) ShaderContractCodec.Fail("reflection_row", "reflection");
            bool match = r.Kind switch {
                1 => d.Inputs.Any(i => i.Semantic == r.Name && i.Index == r.Index && i.Scalar == r.Scalar && i.Components == r.Columns),
                2 => d.Constants.Any(b => b.Semantic == r.Name && b.Slot == r.Slot && b.ByteSize == r.ByteSize),
                3 => d.Constants.Any(b => b.Semantic == r.Parent && b.Slot == r.Slot && b.Members.Any(m => m.Name == r.Name &&
                    m.Scalar == r.Scalar && m.Rows == r.Rows && m.Columns == r.Columns && m.MatrixOrder == r.MatrixOrder && m.ByteOffset == r.ByteOffset &&
                    m.ArrayCount == r.ArrayCount && m.ArrayStride == r.ArrayStride && ReflectedSpan(m) == r.ByteSize)),
                4 => d.Resources.Any(x => x.Semantic == r.Name && x.Kind == r.ResourceKind && x.Access == r.Access && x.Slot == r.Slot && x.Count == r.Count && x.ElementStride == r.ElementStride),
                _ => false
            };
            if (!match) ShaderContractCodec.Fail("reflection_mismatch", "reflection");
        }
    }
    private static long ReflectedSpan(ShaderConstantMember m)
    {
        int element = m.MatrixOrder == ShaderMatrixOrder.RowMajor ? (m.Rows - 1) * 16 + m.Columns * 4 :
            m.MatrixOrder == ShaderMatrixOrder.ColumnMajor ? (m.Columns - 1) * 16 + m.Rows * 4 : m.Columns * 4;
        return (long)(m.ArrayCount - 1) * m.ArrayStride + element;
    }
}

// Trusted off-frame service, explicitly not an Agent capability or tick-safe compiler.
// Immutable artifacts have no native resources. Cache is bounded and tied to exact renderer lifetime.
public sealed class ShaderCompilerService : IDisposable
{
    public const int MaxCached = 16, MaxCachedBytes = 8 * 1024 * 1024;
    private readonly RendererSession _renderer;
    private readonly PluginLease _lease;
    private readonly Func<bool> _preparationAllowed;
    private readonly Dictionary<string, CompiledShader> _cache = new(StringComparer.Ordinal);
    private bool _disposed;
    private bool _preparing;
    public ulong Compilations { get; private set; }
    public ulong CacheHits { get; private set; }
    public int CachedBytes { get; private set; }
    public ShaderCompilerService(RendererSession renderer, Func<bool> preparationAllowed)
    {
        ArgumentNullException.ThrowIfNull(renderer); ArgumentNullException.ThrowIfNull(preparationAllowed);
        _renderer = renderer; _preparationAllowed = preparationAllowed; renderer.VerifyShaderPreparation(); _lease = renderer.Module.AcquireLease();
    }
    public CompiledShader Prepare(ShaderDescriptor shader, IEnumerable<ShaderMacro>? defines = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); _ = _lease.Module;
        if (_preparing) throw new InvalidOperationException("Nonreentrant shader preparation required.");
        _preparing = true;
        try { return PrepareCandidate(shader, defines); } finally { _preparing = false; }
    }
    private CompiledShader PrepareCandidate(ShaderDescriptor shader, IEnumerable<ShaderMacro>? defines)
    {
        if (!_preparationAllowed()) throw new InvalidOperationException("Trusted host off-simulation preparation permission required.");
        _renderer.VerifyShaderPreparation(); ArgumentNullException.ThrowIfNull(shader);
        var macros = (defines ?? []).Take(17).OrderBy(m => m?.Name, StringComparer.Ordinal).ToArray();
        if (macros.Length > 16) ShaderContractCodec.Fail("macro_budget", "macros");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in macros) {
            if (m is null || m.Name is null || m.Name.Length is < 1 or > 63 || !(char.IsAsciiLetter(m.Name[0]) || m.Name[0] == '_') || m.Name.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '_')) || !names.Add(m.Name) || m.Value is null || m.Value.Length is < 1 or > 63 || m.Value.Any(c => c is < ' ' or > '~')) ShaderContractCodec.Fail("macro", "macros");
        }
        // No disk cache/foreign version acceptance. Includes definition/source/entry/stage/macros, compiler+flags+contract.
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("query8/api1/descriptor1/compiler47/flags48800/" + shader.ContentHash + "/" +
            string.Join(";", macros.Select(m => m.Name.Length + ":" + m.Name + m.Value.Length + ":" + m.Value)))));
        if (_cache.TryGetValue(key, out var hit)) { ShaderReflectionValidation.Validate(shader, hit); CacheHits++; return hit; }
        if (_cache.Count == MaxCached) ShaderContractCodec.Fail("shader_cache_budget", "cache");
        var candidate = _renderer.CompileDeclaration(shader, key, macros); Compilations++;
        if (candidate.BytecodeBytes > MaxCachedBytes - CachedBytes) ShaderContractCodec.Fail("shader_cache_budget", "cache");
        _cache.Add(key, candidate); CachedBytes += candidate.BytecodeBytes; return candidate;
    }
    public void Dispose() { if (_disposed) return; _ = _lease.Module; if (_preparing) throw new InvalidOperationException("Preparation in progress."); _cache.Clear(); CachedBytes = 0; _lease.Dispose(); _disposed = true; }
}
