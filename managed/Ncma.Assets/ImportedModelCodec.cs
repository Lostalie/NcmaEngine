using System.Numerics;
using System.Text;

namespace Ncma.Assets;

public readonly record struct ImportTransform(Vector3 Position, Quaternion Rotation, Vector3 Scale);
public readonly record struct ImportJoints(ushort X, ushort Y, ushort Z, ushort W);
public readonly record struct ImportVertex(Vector3 Position, Vector3 Normal, Vector2 UV, ImportJoints Joints, Vector4 Weights);
public sealed record ImportBinding(uint Bone, float[] GeometryToBone);
public sealed record ImportMesh(string Name, ImportVertex[] Vertices, uint[] Indices, uint[] TriangleMaterials,
    ImportBinding[] Bindings, string[] Materials);
public sealed record ImportBone(string Name, int Parent, ImportTransform BindLocal);
public readonly record struct ImportKey(double Time, ImportTransform Value);
public sealed record ImportTrack(uint Bone, ImportKey[] Keys);
public sealed record ImportClip(string Name, double Duration, ImportTrack[] Tracks);
public sealed record ImportedModel(uint FbxVersion, bool Binary, double SourceUnitMetres, double SampleRate,
    ImportMesh[] Meshes, ImportBone[] Bones, ImportClip[] Clips, string[] Warnings);

// NIM1 is a private tool candidate, NOT a persistent .nca or a dump of native ABI structs.
// Explicit LE scalar encoding, strict UTF-8, no runtime handles/UUID allocation. Publication is separate.
public static class ImportedModelCodec
{
    public const int MaxBytes = 256 * 1024 * 1024, MaxVertices = 2_000_000, MaxIndices = 6_000_000,
        MaxKeys = 2_000_000, MaxBones = 1024, MaxMeshes = 64, MaxClips = 64, MaxTextBytes = 1024 * 1024;
    private const uint Magic = 0x314D494E;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static byte[] Encode(ImportedModel model)
    {
        Validate(model);
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream, Utf8, leaveOpen: true);
        w.Write(Magic); w.Write(1u); w.Write(model.FbxVersion); w.Write(model.Binary ? 1u : 0u);
        w.Write(model.SourceUnitMetres); w.Write(model.SampleRate);
        w.Write(model.Meshes.Length); w.Write(model.Bones.Length); w.Write(model.Clips.Length); w.Write(model.Warnings.Length);
        foreach (var mesh in model.Meshes)
        {
            Text(mesh.Name); w.Write(mesh.Vertices.Length); w.Write(mesh.Indices.Length); w.Write(mesh.TriangleMaterials.Length);
            w.Write(mesh.Bindings.Length); w.Write(mesh.Materials.Length);
            foreach (var v in mesh.Vertices)
            {
                V3(v.Position); V3(v.Normal); w.Write(v.UV.X); w.Write(v.UV.Y);
                w.Write(v.Joints.X); w.Write(v.Joints.Y); w.Write(v.Joints.Z); w.Write(v.Joints.W);
                w.Write(v.Weights.X); w.Write(v.Weights.Y); w.Write(v.Weights.Z); w.Write(v.Weights.W);
            }
            foreach (uint index in mesh.Indices) w.Write(index);
            foreach (uint slot in mesh.TriangleMaterials) w.Write(slot);
            foreach (var binding in mesh.Bindings) { w.Write(binding.Bone); foreach (float scalar in binding.GeometryToBone) w.Write(scalar); }
            foreach (string material in mesh.Materials) Text(material);
        }
        foreach (var bone in model.Bones) { Text(bone.Name); w.Write(bone.Parent); TRS(bone.BindLocal); }
        foreach (var clip in model.Clips)
        {
            Text(clip.Name); w.Write(clip.Duration); w.Write(clip.Tracks.Length);
            foreach (var track in clip.Tracks)
            { w.Write(track.Bone); w.Write(track.Keys.Length); foreach (var key in track.Keys) { w.Write(key.Time); TRS(key.Value); } }
        }
        foreach (string warning in model.Warnings) Text(warning);
        if (stream.Length > MaxBytes) throw new ArgumentException("Model byte budget exceeded.");
        return stream.ToArray();
        void Text(string value) { byte[] bytes = Utf8.GetBytes(value); w.Write(bytes.Length); w.Write(bytes); }
        void V3(Vector3 v) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
        void TRS(ImportTransform t) { V3(t.Position); w.Write(t.Rotation.X); w.Write(t.Rotation.Y); w.Write(t.Rotation.Z); w.Write(t.Rotation.W); V3(t.Scale); }
    }

    public static ImportedModel Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length is < 48 or > MaxBytes) throw new ArgumentException("Model byte budget exceeded.");
        using var stream = new MemoryStream(bytes, writable: false);
        using var r = new BinaryReader(stream, Utf8);
        try
        {
            if (r.ReadUInt32() != Magic || r.ReadUInt32() != 1) throw new ArgumentException("Invalid model magic/version.");
            uint fbx = r.ReadUInt32(), binary = r.ReadUInt32();
            if (binary > 1) throw new ArgumentException("Invalid binary flag.");
            double unit = r.ReadDouble(), rate = r.ReadDouble();
            int meshCount = Count(MaxMeshes), boneCount = Count(MaxBones), clipCount = Count(MaxClips), warningCount = Count(4096);
            int textBytes = 0, vertices = 0, indices = 0, keys = 0;
            var meshes = new ImportMesh[meshCount];
            for (int m = 0; m < meshCount; m++)
            {
                string name = Text(); int vc = Count(MaxVertices), ic = Count(MaxIndices), tc = Count(MaxIndices / 3), bc = Count(4096), mc = Count(4096);
                vertices = checked(vertices + vc); indices = checked(indices + ic);
                if (vertices > MaxVertices || indices > MaxIndices) throw new ArgumentException("Aggregate model budget exceeded.");
                RequireBytes(checked((long)vc * 56 + (long)ic * 4 + (long)tc * 4 + (long)bc * 68 + (long)mc * 4));
                var vs = new ImportVertex[vc]; var ix = new uint[ic]; var slots = new uint[tc]; var bindings = new ImportBinding[bc]; var materials = new string[mc];
                for (int i = 0; i < vc; i++) vs[i] = new(V3(), V3(), new(r.ReadSingle(), r.ReadSingle()),
                    new(r.ReadUInt16(), r.ReadUInt16(), r.ReadUInt16(), r.ReadUInt16()), new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
                for (int i = 0; i < ic; i++) ix[i] = r.ReadUInt32();
                for (int i = 0; i < tc; i++) slots[i] = r.ReadUInt32();
                for (int i = 0; i < bc; i++) { uint bone = r.ReadUInt32(); float[] matrix = new float[16]; for (int j = 0; j < 16; j++) matrix[j] = r.ReadSingle(); bindings[i] = new(bone, matrix); }
                for (int i = 0; i < mc; i++) materials[i] = Text();
                meshes[m] = new(name, vs, ix, slots, bindings, materials);
            }
            RequireBytes((long)boneCount * 48);
            var bones = new ImportBone[boneCount];
            for (int i = 0; i < boneCount; i++) bones[i] = new(Text(), r.ReadInt32(), TRS());
            var clips = new ImportClip[clipCount];
            for (int i = 0; i < clipCount; i++)
            {
                string name = Text(); double duration = r.ReadDouble(); int tc = Count(MaxBones);
                RequireBytes((long)tc * 8); var tracks = new ImportTrack[tc];
                for (int t = 0; t < tc; t++)
                {
                    uint bone = r.ReadUInt32(); int kc = Count(MaxKeys); keys = checked(keys + kc);
                    if (keys > MaxKeys) throw new ArgumentException("Aggregate key budget exceeded.");
                    RequireBytes((long)kc * 48); var values = new ImportKey[kc];
                    for (int k = 0; k < kc; k++) values[k] = new(r.ReadDouble(), TRS());
                    tracks[t] = new(bone, values);
                }
                clips[i] = new(name, duration, tracks);
            }
            var warnings = new string[warningCount]; for (int i = 0; i < warningCount; i++) warnings[i] = Text();
            if (stream.Position != stream.Length) throw new ArgumentException("Unexpected model trailing bytes.");
            var model = new ImportedModel(fbx, binary == 1, unit, rate, meshes, bones, clips, warnings); Validate(model); return model;

            int Count(int max) { int count = r.ReadInt32(); if (count < 0 || count > max) throw new ArgumentException("Invalid model count."); return count; }
            void RequireBytes(long count) { if (count > stream.Length - stream.Position) throw new ArgumentException("Truncated model payload."); }
            string Text() { int length = Count(65536); textBytes = checked(textBytes + length); if (textBytes > MaxTextBytes) throw new ArgumentException("Text budget exceeded."); RequireBytes(length); return Utf8.GetString(r.ReadBytes(length)); }
            Vector3 V3() => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            ImportTransform TRS() => new(V3(), new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()), V3());
        }
        catch (Exception e) when (e is EndOfStreamException or OverflowException or DecoderFallbackException)
        { throw new ArgumentException("Malformed model payload.", e); }
    }

    public static void Validate(ImportedModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!double.IsFinite(model.SourceUnitMetres) || model.SourceUnitMetres <= 0 || !double.IsFinite(model.SampleRate) || model.SampleRate is < 1 or > 120 ||
            model.Meshes is null || model.Meshes.Length is < 1 or > MaxMeshes || model.Bones is null || model.Bones.Length is < 1 or > MaxBones ||
            model.Clips is null || model.Clips.Length is < 1 or > MaxClips || model.Warnings is null || model.Warnings.Length > 4096)
            throw new ArgumentException("Invalid character model header/budget.");
        int text = 0; long vertices = 0, indices = 0, keys = 0, size = 48;
        for (int i = 0; i < model.Bones.Length; i++)
        {
            var b = model.Bones[i] ?? throw new ArgumentException("Null bone."); Text(b.Name); TRS(b.BindLocal); size += 48;
            if (b.Parent < -1 || b.Parent >= i) throw new ArgumentException("Invalid parent ordering/cycle.");
        }
        foreach (var mesh in model.Meshes)
        {
            if (mesh is null || mesh.Vertices is null || mesh.Indices is null || mesh.TriangleMaterials is null || mesh.Bindings is null || mesh.Materials is null ||
                mesh.Vertices.Length == 0 || mesh.Indices.Length == 0 || mesh.Indices.Length % 3 != 0 || mesh.TriangleMaterials.Length != mesh.Indices.Length / 3 ||
                mesh.Bindings.Length > 4096 || mesh.Materials.Length > 4096) throw new ArgumentException("Invalid mesh topology.");
            Text(mesh.Name); vertices += mesh.Vertices.Length; indices += mesh.Indices.Length;
            if (vertices > MaxVertices || indices > MaxIndices) throw new ArgumentException("Aggregate mesh budget exceeded.");
            size += 24L + mesh.Vertices.Length * 56L + mesh.Indices.Length * 4L + mesh.TriangleMaterials.Length * 4L + mesh.Bindings.Length * 68L;
            foreach (var binding in mesh.Bindings)
            {
                if (binding is null || binding.Bone >= model.Bones.Length || binding.GeometryToBone is null || binding.GeometryToBone.Length != 16 || binding.GeometryToBone.Any(v => !float.IsFinite(v)))
                    throw new ArgumentException("Invalid mesh binding.");
                if (Math.Abs(binding.GeometryToBone[3]) > .00001f || Math.Abs(binding.GeometryToBone[7]) > .00001f ||
                    Math.Abs(binding.GeometryToBone[11]) > .00001f || Math.Abs(binding.GeometryToBone[15] - 1) > .00001f)
                    throw new ArgumentException("Mesh binding must be an affine column-major matrix.");
            }
            foreach (var v in mesh.Vertices)
            {
                if (!Finite(v.Position) || !Finite(v.Normal) || !float.IsFinite(v.UV.X) || !float.IsFinite(v.UV.Y) ||
                    !float.IsFinite(v.Weights.X) || !float.IsFinite(v.Weights.Y) || !float.IsFinite(v.Weights.Z) || !float.IsFinite(v.Weights.W) ||
                    v.Weights.X < 0 || v.Weights.Y < 0 || v.Weights.Z < 0 || v.Weights.W < 0 || Math.Abs(v.Weights.X + v.Weights.Y + v.Weights.Z + v.Weights.W - 1) > .001f)
                    throw new ArgumentException("Invalid vertex/weights.");
                Joint(v.Joints.X, v.Weights.X); Joint(v.Joints.Y, v.Weights.Y); Joint(v.Joints.Z, v.Weights.Z); Joint(v.Joints.W, v.Weights.W);
            }
            foreach (uint index in mesh.Indices) if (index >= mesh.Vertices.Length) throw new ArgumentException("Mesh index out of range.");
            foreach (uint slot in mesh.TriangleMaterials) if (slot >= Math.Max(1, mesh.Materials.Length)) throw new ArgumentException("Material slot out of range.");
            foreach (string material in mesh.Materials) Text(material);
            void Joint(ushort joint, float weight) { if (weight > 0 && joint >= mesh.Bindings.Length) throw new ArgumentException("Vertex joint has no mesh palette binding."); }
        }
        foreach (var clip in model.Clips)
        {
            if (clip is null || !double.IsFinite(clip.Duration) || clip.Duration is <= 0 or > 600 || clip.Tracks is null || clip.Tracks.Length > MaxBones) throw new ArgumentException("Invalid clip.");
            Text(clip.Name); size += 16; var bones = new HashSet<uint>();
            foreach (var track in clip.Tracks)
            {
                if (track is null || track.Bone >= model.Bones.Length || !bones.Add(track.Bone) || track.Keys is null || track.Keys.Length < 1) throw new ArgumentException("Invalid track.");
                keys += track.Keys.Length; if (keys > MaxKeys) throw new ArgumentException("Aggregate key budget exceeded."); size += 8L + track.Keys.Length * 48L;
                double previous = -1;
                foreach (var key in track.Keys) { if (!double.IsFinite(key.Time) || key.Time < 0 || key.Time > clip.Duration || key.Time <= previous) throw new ArgumentException("Invalid key time/order."); previous = key.Time; TRS(key.Value); }
                if (track.Keys[0].Time != 0 || track.Keys[^1].Time != clip.Duration) throw new ArgumentException("Missing exact sampling endpoint.");
            }
        }
        foreach (string warning in model.Warnings) Text(warning);
        if (size > MaxBytes) throw new ArgumentException("Model byte budget exceeded.");
        void Text(string value) { if (value is null || value.Contains('\0')) throw new ArgumentException("Invalid model text."); int count = Utf8.GetByteCount(value); if (count > 65536 || (text += count) > MaxTextBytes) throw new ArgumentException("Text budget exceeded."); size += count + 4; }
        static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
        static void TRS(ImportTransform t) { float length = t.Rotation.LengthSquared(); if (!Finite(t.Position) || !Finite(t.Scale) ||
            t.Scale.X <= .000001f || t.Scale.Y <= .000001f || t.Scale.Z <= .000001f || !float.IsFinite(length) || Math.Abs(length - 1) > .002f)
            throw new ArgumentException("Invalid finite positive TRS/quaternion."); }
    }
}
