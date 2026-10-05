using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Ncma.Assets;

namespace Ncma.Asset.Import;

public readonly record struct ImportProgress(uint Phase, ulong BytesRead, ulong BytesTotal);

// Trusted tool bootstrap only. Never constructed on the editor/frame thread. No finalizer/unload race.
public sealed unsafe class ImportKernel : IDisposable
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly object _gate = new();
    private ToolCodePin? _libraryPin;
    private nint _library;
    private ulong _context;
    private Api _api;
    public ImportKernel(string libraryPath, string expectedHash)
    {
        AssetRecordCodec.ValidateHash(expectedHash); ValidateAbsolute(libraryPath);
        if (IntPtr.Size != 8 || sizeof(Api) != 104 || sizeof(Error) != 528 || sizeof(Vertex) != 56 || sizeof(Key) != 48 || sizeof(Info) != 72)
            throw new PlatformNotSupportedException("Import ABI requires validated x64 POD layouts.");
        _libraryPin = new(libraryPath);
        try
        {
            if (System.Convert.ToHexString(SHA256.HashData(_libraryPin.Stream)) != expectedHash) throw new ArgumentException("Import kernel hash mismatch.");
            _library = NativeLibrary.Load(libraryPath, typeof(ImportKernel).Assembly,
                DllImportSearchPath.UseDllDirectoryForDependencies | DllImportSearchPath.System32);
            var get = (delegate* unmanaged[Cdecl]<uint, uint, void*, uint, Error*, uint>)NativeLibrary.GetExport(_library, "ncma_import_get_api");
            Error error = default; Api api = default;
            Check(get(1, 1, &api, (uint)sizeof(Api), &error), error);
            if (api.Size != sizeof(Api) || api.Major != 1 || api.Minor != 1 || api.Reserved != 0 || api.LoadMode == null ||
                api.Create == null || api.Close == null || api.Load == null || api.Info == null || api.MeshInfo == null || api.ClipInfo == null ||
                api.Page == null || api.Text == null || api.Cancel == null || api.Status == null) throw new ArgumentException("Invalid import API table.");
            ulong id = 0; Check(api.Create(&id, &error), error); if (id == 0) throw new ArgumentException("Null import context.");
            _api = api; _context = id;
        }
        catch { if (_library != 0) NativeLibrary.Free(_library); _library = 0; _libraryPin.Dispose(); _libraryPin = null; throw; }
    }
    public ImportedModel LoadAndCopy(string source, int sampleRate, CancellationToken cancellation = default, bool staticOnly = false)
    {
        VerifyOwner(); ValidateAbsolute(source); cancellation.ThrowIfCancellationRequested();
        byte[] path = new UTF8Encoding(false, true).GetBytes(source);
        Info info = new() { Size = (uint)sizeof(Info) }; Error error = default;
        fixed (byte* bytes = path) Check(_api.LoadMode(_context, bytes, (uint)path.Length, sampleRate, staticOnly ? 1u : 0u, &info, &error), error);
        cancellation.ThrowIfCancellationRequested();
        if (info.Size != sizeof(Info) || info.Reserved != 0 || info.Binary > 1 || info.Bones is < 1 or > ImportedModelCodec.MaxBones ||
            info.Meshes is < 1 or > ImportedModelCodec.MaxMeshes || info.Clips is < 1 or > ImportedModelCodec.MaxClips || info.Warnings > 4096 ||
            info.Vertices > ImportedModelCodec.MaxVertices || info.Indices > ImportedModelCodec.MaxIndices || info.Keys > ImportedModelCodec.MaxKeys)
            throw new ArgumentException("Invalid native import header/budget.");
        int textBudget = 0;
        var rawBones = Read<Bone>(5, 0, 0, info.Bones);
        var bones = new ImportBone[rawBones.Length];
        for (uint i = 0; i < bones.Length; i++) bones[i] = new(Text(2, i), rawBones[i].Parent, Convert(rawBones[i].Bind));
        var meshes = new ImportMesh[info.Meshes]; long vertexTotal = 0, indexTotal = 0;
        for (uint i = 0; i < meshes.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested(); MeshInfo mi = new() { Size = (uint)sizeof(MeshInfo) };
            Check(_api.MeshInfo(_context, i, &mi, &error), error);
            vertexTotal += mi.Vertices; indexTotal += mi.Indices;
            if (mi.Size != sizeof(MeshInfo) || mi.Flags != 0 || mi.Reserved != 0 || mi.Vertices == 0 || mi.Indices == 0 || mi.Indices % 3 != 0 ||
                mi.Triangles != mi.Indices / 3 || mi.Bindings > 4096 || mi.Materials > 4096 || vertexTotal > (long)info.Vertices || indexTotal > (long)info.Indices)
                throw new ArgumentException("Invalid native mesh info.");
            var raw = Read<Vertex>(1, i, 0, mi.Vertices); var vertices = new ImportVertex[raw.Length];
            for (int v = 0; v < raw.Length; v++)
            {
                Vertex value = raw[v]; vertices[v] = new(V3(value.Position), V3(value.Normal), new(value.UV[0], value.UV[1]),
                    new(value.Joints[0], value.Joints[1], value.Joints[2], value.Joints[3]), new(value.Weights[0], value.Weights[1], value.Weights[2], value.Weights[3]));
            }
            var rb = Read<Binding>(4, i, 0, mi.Bindings); var bindings = new ImportBinding[rb.Length];
            for (int b = 0; b < rb.Length; b++) { Binding value = rb[b]; var matrix = new float[16]; for (int j = 0; j < 16; j++) matrix[j] = value.Matrix[j]; bindings[b] = new(value.Bone, matrix); }
            string[] materials = new string[mi.Materials]; for (uint m = 0; m < materials.Length; m++) materials[m] = Text(4, i, m);
            meshes[i] = new(Text(1, i), vertices, Read<uint>(2, i, 0, mi.Indices), Read<uint>(3, i, 0, mi.Triangles), bindings, materials);
        }
        long keyTotal = 0; var clips = new ImportClip[info.Clips];
        for (uint i = 0; i < clips.Length; i++)
        {
            ClipInfo ci = new() { Size = (uint)sizeof(ClipInfo) }; Check(_api.ClipInfo(_context, i, &ci, &error), error);
            if (ci.Size != sizeof(ClipInfo) || ci.Reserved != 0 || ci.Tracks > info.Bones || ci.Keys > info.Keys) throw new ArgumentException("Invalid native clip info.");
            var rt = Read<Track>(6, i, 0, ci.Tracks); var tracks = new ImportTrack[rt.Length]; long clipKeys = 0;
            for (uint t = 0; t < rt.Length; t++)
            {
                keyTotal += rt[t].Keys; clipKeys += rt[t].Keys;
                if (keyTotal > (long)info.Keys || clipKeys > ci.Keys) throw new ArgumentException("Native key count mismatch.");
                var rk = Read<Key>(7, i, t, rt[t].Keys); var keys = new ImportKey[rk.Length];
                for (int k = 0; k < rk.Length; k++) keys[k] = new(rk[k].Time, Convert(rk[k].Value));
                tracks[t] = new(rt[t].Bone, keys);
            }
            if (clipKeys != ci.Keys) throw new ArgumentException("Native clip key count mismatch.");
            clips[i] = new(Text(3, i), ci.Duration, tracks);
        }
        if (keyTotal != (long)info.Keys || vertexTotal != (long)info.Vertices || indexTotal != (long)info.Indices) throw new ArgumentException("Native aggregate counts mismatch.");
        var warnings = new string[info.Warnings]; for (uint i = 0; i < warnings.Length; i++) warnings[i] = Text(5, i);
        var model = new ImportedModel(info.FbxVersion, info.Binary == 1, info.Unit, info.Rate, meshes, bones, clips, warnings);
        ImportedModelCodec.Validate(model); cancellation.ThrowIfCancellationRequested(); return model;

        T[] Read<T>(uint stream, uint obj, uint sub, uint count) where T : unmanaged
        {
            if ((long)count * sizeof(T) > ImportedModelCodec.MaxBytes) throw new ArgumentException("Native stream byte budget exceeded.");
            T[] output = new T[count];
            for (uint first = 0; first < count;)
            {
                cancellation.ThrowIfCancellationRequested(); uint size = Math.Min(4096, count - first), copied = 0; Error e = default;
                fixed (T* ptr = output) Check(_api.Page(_context, stream, obj, sub, first, size, ptr + first, checked(size * (uint)sizeof(T)), &copied, &e), e);
                if (copied != size) throw new ArgumentException("Native page copy count mismatch."); first += size;
            }
            return output;
        }
        string Text(uint kind, uint obj, uint sub = 0)
        {
            cancellation.ThrowIfCancellationRequested(); byte[] bytes = new byte[65536]; uint length = 0; Error e = default;
            fixed (byte* ptr = bytes) Check(_api.Text(_context, kind, obj, sub, ptr, (uint)bytes.Length, &length, &e), e);
            if (length > bytes.Length || (textBudget = checked(textBudget + (int)length)) > ImportedModelCodec.MaxTextBytes) throw new ArgumentException("Native text budget exceeded.");
            return new UTF8Encoding(false, true).GetString(bytes.AsSpan(0, (int)length));
        }
    }
    // Lock covers only these short native operations versus close; never the synchronous parser.
    public ImportProgress ReadProgress()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_context == 0, this); Status status = new() { Size = (uint)sizeof(Status) }; Error error = default;
            Check(_api.Status(_context, &status, &error), error);
            if (status.Size != sizeof(Status) || status.Phase > 6 || status.Read > status.Total) throw new ArgumentException("Invalid native progress.");
            return new(status.Phase, status.Read, status.Total);
        }
    }
    public void Cancel()
    {
        lock (_gate)
        {
            if (_context == 0) return; Error error = default; uint result = _api.Cancel(_context, &error);
            if (result != 7) Check(result, error); // Ready has won native publication; parent token still rejects the candidate.
        }
    }
    public void Dispose()
    {
        if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("Owner-thread import close required.");
        lock (_gate)
        {
            if (_context == 0) return;
            Error error = default; Check(_api.Close(_context, &error), error); _context = 0;
            NativeLibrary.Free(_library); _library = 0; _libraryPin!.Dispose(); _libraryPin = null;
        }
    }
    private void VerifyOwner()
    {
        if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("Import kernel requires its owner thread.");
        ObjectDisposedException.ThrowIf(_context == 0, this);
    }
    public static void ValidateAbsolute(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.Contains('\0')) throw new ArgumentException("Explicit absolute tool path required.");
        for (FileSystemInfo? entry = new FileInfo(path); entry is not null; entry = entry is FileInfo file ? file.Directory : ((DirectoryInfo)entry).Parent)
            if (entry.Exists && (entry.Attributes & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Tool path traverses a reparse point.");
    }
    private static void Check(uint code, Error error)
    {
        if (code == 256) throw new OperationCanceledException("Native import cancelled.");
        if (code == 0) return;
        throw new InvalidOperationException($"Import ABI returned {code}: " + (error.Length <= 512 ? new UTF8Encoding(false, true).GetString(error.Message, (int)error.Length) : "Invalid diagnostic"));
    }
    private static Vector3 V3(float* values) => new(values[0], values[1], values[2]);
    private static ImportTransform Convert(Transform t) => new(V3(t.Position), new(t.Rotation[0], t.Rotation[1], t.Rotation[2], t.Rotation[3]), V3(t.Scale));
    [StructLayout(LayoutKind.Sequential)] private struct Transform { public fixed float Position[3], Rotation[4], Scale[3]; }
    [StructLayout(LayoutKind.Sequential)] private struct Vertex { public fixed float Position[3], Normal[3], UV[2]; public fixed ushort Joints[4]; public fixed float Weights[4]; }
    [StructLayout(LayoutKind.Sequential)] private struct Binding { public uint Bone; public fixed float Matrix[16]; }
    [StructLayout(LayoutKind.Sequential)] private struct Bone { public int Parent; public Transform Bind; }
    [StructLayout(LayoutKind.Sequential)] private struct Track { public uint Bone, Keys; }
    [StructLayout(LayoutKind.Sequential)] private struct Key { public double Time; public Transform Value; }
    [StructLayout(LayoutKind.Sequential)] private struct Info { public uint Size, FbxVersion, Binary, Bones, Meshes, Clips, Warnings, Reserved; public double Unit, Rate; public ulong Vertices, Indices, Keys; }
    [StructLayout(LayoutKind.Sequential)] private struct MeshInfo { public uint Size, Flags, Vertices, Indices, Triangles, Bindings, Materials, Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct ClipInfo { public uint Size, Tracks, Keys, Reserved; public double Duration; }
    [StructLayout(LayoutKind.Sequential)] private struct Status { public uint Size, Phase, Busy, Cancelled; public ulong Read, Total; }
    [StructLayout(LayoutKind.Sequential)] private struct Error { public uint Code, Reserved, Required, Length; public fixed byte Message[512]; }
    [StructLayout(LayoutKind.Sequential)] private struct Api
    {
        public uint Size, Major, Minor, Reserved;
        public delegate* unmanaged[Cdecl]<ulong*, Error*, uint> Create;
        public delegate* unmanaged[Cdecl]<ulong, Error*, uint> Close;
        public delegate* unmanaged[Cdecl]<ulong, byte*, uint, double, Info*, Error*, uint> Load;
        public delegate* unmanaged[Cdecl]<ulong, Info*, Error*, uint> Info;
        public delegate* unmanaged[Cdecl]<ulong, uint, MeshInfo*, Error*, uint> MeshInfo;
        public delegate* unmanaged[Cdecl]<ulong, uint, ClipInfo*, Error*, uint> ClipInfo;
        public delegate* unmanaged[Cdecl]<ulong, uint, uint, uint, uint, uint, void*, uint, uint*, Error*, uint> Page;
        public delegate* unmanaged[Cdecl]<ulong, uint, uint, uint, byte*, uint, uint*, Error*, uint> Text;
        public delegate* unmanaged[Cdecl]<ulong, Error*, uint> Cancel;
        public delegate* unmanaged[Cdecl]<ulong, Status*, Error*, uint> Status;
        public delegate* unmanaged[Cdecl]<ulong, byte*, uint, double, uint, Info*, Error*, uint> LoadMode;
    }
}
