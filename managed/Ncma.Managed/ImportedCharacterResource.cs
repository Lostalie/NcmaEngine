using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Ncma;

/// <summary>Immutable FBX resource lease. ABI 2 copies reports, indices and numerical samples into caller-owned buffers.</summary>
public sealed unsafe class ImportedCharacterResource : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint VersionCall();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong ImportCall(uint version, byte* source, double rate, byte* asset);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte ReleaseCall(ulong handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte ReportCall(ulong handle, byte* output, uint capacity, uint* required);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte ErrorCall(byte* output, uint capacity, uint* required);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte SampleCall(ulong handle, uint clip, double time, float* output, uint capacity, uint* required);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte IndicesCall(ulong handle, uint mesh, uint* output, uint capacity, uint* required);
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private nint _module;
    private ulong _handle;
    private readonly ReleaseCall _release;
    private readonly ReportCall _report;
    private readonly ErrorCall _error;
    private readonly SampleCall _sample;
    private readonly IndicesCall _indices;
    private readonly JsonElement _metadata;
    private readonly int[] _vertexCounts, _indexCounts;
    private readonly double[] _durations;
    private readonly string[] _clipNames;
    public Guid AssetId { get; }
    public Guid ResourceIdentity { get; } = Guid.NewGuid();
    public int BoneCount { get; }
    public int MeshCount => _vertexCounts.Length;
    public int ClipCount => _durations.Length;
    public int SampleFloatCount { get; }
    public JsonElement Report { get { Verify(); return _metadata.Clone(); } }
    public string ClipName(int clip) { Verify(); return _clipNames[clip]; }
    public double ClipDuration(int clip) { Verify(); return _durations[clip]; }
    public int VertexCount(int mesh) { Verify(); return _vertexCounts[mesh]; }
    public int IndexCount(int mesh) { Verify(); return _indexCounts[mesh]; }
    public string BoneName(int bone) { Verify(); return _metadata.GetProperty("skeleton")[bone].GetProperty("name").GetString()!; }
    public int BoneParent(int bone) { Verify(); return _metadata.GetProperty("skeleton")[bone].GetProperty("parent").GetInt32(); }
    public ImportedCharacterResource(string libraryPath, string source, double sampleRate = 30, Guid? assetId = null)
    {
        if (!Path.IsPathFullyQualified(libraryPath) || !Path.IsPathFullyQualified(source))
            throw new ArgumentException("Explicit absolute native library and FBX source paths required.");
        if (!File.Exists(libraryPath)) throw new FileNotFoundException("Character kernel missing.", libraryPath);
        if ((File.GetAttributes(libraryPath) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Reparse kernel path rejected.");
        if (assetId == Guid.Empty) throw new ArgumentException("Empty persistent character UUID.");
        _module = NativeLibrary.Load(Path.GetFullPath(libraryPath));
        try
        {
            T Bind<T>(string symbol) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_module, symbol));
            if (Bind<VersionCall>("ncma_character_abi_version")() != 2) throw new NotSupportedException("Character ABI 2 required.");
            var import = Bind<ImportCall>("ncma_character_import");
            _release = Bind<ReleaseCall>("ncma_character_release"); _report = Bind<ReportCall>("ncma_character_read_report");
            _error = Bind<ErrorCall>("ncma_character_read_error"); _sample = Bind<SampleCall>("ncma_character_sample");
            _indices = Bind<IndicesCall>("ncma_character_read_indices");
            byte[] path = Encoding.UTF8.GetBytes(Path.GetFullPath(source) + "\0");
            byte[] uuid = Encoding.UTF8.GetBytes((assetId?.ToString() ?? "") + "\0");
            fixed (byte* p = path) fixed (byte* u = uuid) _handle = import(2, p, sampleRate, u);
            if (_handle == 0) throw new InvalidOperationException(ReadError());
            uint count = 0;
            if (_report(_handle, null, 0, &count) != 2 || count is < 2 or > 4194304) throw new InvalidOperationException("Invalid character report size.");
            byte[] json = new byte[count]; uint copied = 0;
            fixed (byte* bytes = json) Check(_report(_handle, bytes, count, &copied));
            if (copied != count || json[^1] != 0) throw new InvalidOperationException("Invalid character report copy.");
            using var document = JsonDocument.Parse(json.AsMemory(0, json.Length - 1));
            _metadata = document.RootElement.Clone();
            if (_metadata.GetProperty("schema_version").GetInt32() != 1) throw new NotSupportedException("Unsupported character report schema.");
            AssetId = _metadata.GetProperty("asset_uuid").GetGuid();
            BoneCount = _metadata.GetProperty("bones").GetInt32();
            if (AssetId == Guid.Empty || BoneCount is < 1 or > 1024) throw new InvalidOperationException("Invalid character identity/skeleton.");
            var skeleton = _metadata.GetProperty("skeleton");
            if (skeleton.GetArrayLength() != BoneCount) throw new InvalidOperationException("Invalid character skeleton metadata.");
            for (int i = 0; i < BoneCount; i++) {
                int parent = skeleton[i].GetProperty("parent").GetInt32();
                if (i == 0 ? parent != -1 : parent < 0 || parent >= i) throw new InvalidOperationException("Invalid bone parent ordering.");
            }
            var meshes = _metadata.GetProperty("meshes").EnumerateArray().ToArray();
            _vertexCounts = meshes.Select(m => m.GetProperty("vertices").GetInt32()).ToArray();
            _indexCounts = meshes.Select(m => checked(m.GetProperty("triangles").GetInt32() * 3)).ToArray();
            var clips = _metadata.GetProperty("clips").EnumerateArray().ToArray();
            _durations = clips.Select(c => c.GetProperty("duration").GetDouble()).ToArray();
            _clipNames = clips.Select(c => c.GetProperty("name").GetString()!).ToArray();
            if (meshes.Length == 0 || clips.Length == 0 || _vertexCounts.Any(n => n <= 0) ||
                _indexCounts.Any(n => n <= 0) || _durations.Any(d => !double.IsFinite(d) || d <= 0))
                throw new InvalidOperationException("Invalid character metadata.");
            SampleFloatCount = checked(BoneCount * 16 + _vertexCounts.Sum(n => checked(n * 3)));
            count = 0;
            if (_sample(_handle, 0, 0, null, 0, &count) != 2 || count != SampleFloatCount)
                throw new InvalidOperationException("Character sample layout mismatch.");
        }
        catch
        {
            if (_handle != 0) { Check((_release ?? throw new InvalidOperationException("Character release function unavailable."))(_handle)); _handle = 0; }
            NativeLibrary.Free(_module); _module = 0; throw;
        }
    }
    private void Verify()
    {
        if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("Character resource owner thread required.");
        ObjectDisposedException.ThrowIf(_handle == 0, this);
    }
    private string ReadError()
    {
        uint required = 0;
        if (_error(null, 0, &required) != 2 || required is < 1 or > 4194304) return "Character kernel failure.";
        byte[] bytes = new byte[required]; uint copied = 0;
        fixed (byte* data = bytes)
            if (_error(data, required, &copied) != 1 || copied != required || bytes[^1] != 0) return "Invalid character error copy.";
        return new UTF8Encoding(false, true).GetString(bytes, 0, bytes.Length - 1);
    }
    private void Check(byte result) { if (result != 1) throw new InvalidOperationException(ReadError()); }
    public void Sample(int clip, double time, Span<float> destination)
    {
        Verify();
        if ((uint)clip >= ClipCount || !double.IsFinite(time) || time is < 0 or > 1e9 || destination.Length != SampleFloatCount)
            throw new ArgumentException("Invalid character sample request.");
        uint required = 0;
        fixed (float* output = destination) Check(_sample(_handle, (uint)clip, time, output, (uint)destination.Length, &required));
        if (required != destination.Length) throw new InvalidOperationException("Invalid character sample count.");
    }
    public void ReadIndices(int mesh, Span<uint> destination)
    {
        Verify();
        if ((uint)mesh >= MeshCount || destination.Length != _indexCounts[mesh]) throw new ArgumentException("Invalid character index buffer.");
        uint required = 0;
        fixed (uint* output = destination) Check(_indices(_handle, (uint)mesh, output, (uint)destination.Length, &required));
        if (required != destination.Length || destination.ContainsAnyExceptInRange(0u, (uint)_vertexCounts[mesh] - 1))
            throw new InvalidOperationException("Invalid character indices.");
    }
    public void Dispose()
    {
        if (Environment.CurrentManagedThreadId != _thread) throw new InvalidOperationException("Character resource owner thread required.");
        if (_handle == 0) return;
        Check(_release(_handle)); _handle = 0;
        NativeLibrary.Free(_module); _module = 0; GC.SuppressFinalize(this);
    }
}
