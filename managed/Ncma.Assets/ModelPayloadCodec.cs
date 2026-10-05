using System.Numerics;
using System.Text;

namespace Ncma.Assets;

public sealed record MeshPayload(bool Skinned, int BoneCount, ImportVertex[] Vertices, Vector4[] Tangents,
    uint[] Indices, uint[] TriangleMaterials, ImportBinding[] Bindings, int MaterialSlots);
public sealed record SkeletonPayload(ImportBone[] Bones);
public sealed record ClipPayload(int BoneCount, ImportClip Clip);
public sealed record MaterialSlotsPayload(string[] Names);

// Typed NCA block payloads, v1. Explicit LE scalars and counts; no ABI dumps/handles/CLR type names.
public static class ModelPayloadCodec
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public static byte[] Encode(MeshPayload mesh)
    {
        Validate(mesh); return Write(0x3148534D, w => {
            w.Write(mesh.Skinned ? 1 : 0); w.Write(mesh.BoneCount); w.Write(mesh.Vertices.Length); w.Write(mesh.Indices.Length);
            w.Write(mesh.Bindings.Length); w.Write(mesh.MaterialSlots); w.Write(mesh.Tangents.Length);
            foreach (var v in mesh.Vertices) { V3(w, v.Position); V3(w, v.Normal); w.Write(v.UV.X); w.Write(v.UV.Y);
                w.Write(v.Joints.X); w.Write(v.Joints.Y); w.Write(v.Joints.Z); w.Write(v.Joints.W); V4(w, v.Weights); }
            foreach (var tangent in mesh.Tangents) V4(w, tangent);
            foreach (uint i in mesh.Indices) w.Write(i); foreach (uint slot in mesh.TriangleMaterials) w.Write(slot);
            foreach (var b in mesh.Bindings) { w.Write(b.Bone); foreach (float v in b.GeometryToBone) w.Write(v); }
        });
    }
    public static byte[] Encode(SkeletonPayload skeleton)
    {
        Validate(skeleton); return Write(0x314C4B53, w => { w.Write(skeleton.Bones.Length); foreach (var b in skeleton.Bones) { Text(w, b.Name); w.Write(b.Parent); TRS(w, b.BindLocal); } });
    }
    public static byte[] Encode(ClipPayload clip)
    {
        Validate(clip); return Write(0x31504C43, w => {
            w.Write(clip.BoneCount); Text(w, clip.Clip.Name); w.Write(clip.Clip.Duration); w.Write(clip.Clip.Tracks.Length);
            foreach (var track in clip.Clip.Tracks) { w.Write(track.Bone); w.Write(track.Keys.Length); foreach (var key in track.Keys) { w.Write(key.Time); TRS(w, key.Value); } }
        });
    }
    public static byte[] Encode(MaterialSlotsPayload materials)
    {
        if (materials.Names is null || materials.Names.Length > 4096) throw new ArgumentException("Material slot budget exceeded.");
        return Write(0x3154414D, w => { w.Write(materials.Names.Length); foreach (string name in materials.Names) Text(w, name); });
    }
    public static MeshPayload DecodeMesh(byte[] bytes) => Read(bytes, 0x3148534D, r => {
        int skinned = r.ReadInt32(); if (skinned is < 0 or > 1) throw new ArgumentException("Invalid mesh type flag.");
        int bones = Count(r, 1024), vc = Count(r, ImportedModelCodec.MaxVertices), ic = Count(r, ImportedModelCodec.MaxIndices),
            bc = Count(r, 4096), mc = Count(r, 4096), tc = Count(r, ImportedModelCodec.MaxVertices);
        if (ic % 3 != 0) throw new ArgumentException("Invalid triangles.");
        Available(r, vc * 56L + tc * 16L + ic * 4L + ic / 3 * 4L + bc * 68L);
        var vs = new ImportVertex[vc]; var ts = new Vector4[tc]; var ix = new uint[ic]; var slots = new uint[ic / 3]; var bindings = new ImportBinding[bc];
        for (int i = 0; i < vc; i++) vs[i] = new(V3(r), V3(r), new(r.ReadSingle(), r.ReadSingle()), new(r.ReadUInt16(), r.ReadUInt16(), r.ReadUInt16(), r.ReadUInt16()), V4(r));
        for (int i = 0; i < tc; i++) ts[i] = V4(r); for (int i = 0; i < ic; i++) ix[i] = r.ReadUInt32();
        for (int i = 0; i < slots.Length; i++) slots[i] = r.ReadUInt32();
        for (int i = 0; i < bc; i++) { uint bone = r.ReadUInt32(); float[] matrix = new float[16]; for (int k = 0; k < 16; k++) matrix[k] = r.ReadSingle(); bindings[i] = new(bone, matrix); }
        var result = new MeshPayload(skinned == 1, bones, vs, ts, ix, slots, bindings, mc); Validate(result); return result;
    });
    public static SkeletonPayload DecodeSkeleton(byte[] bytes) => Read(bytes, 0x314C4B53, r => {
        int count = Count(r, 1024); Available(r, count * 48L); var bones = new ImportBone[count];
        for (int i = 0; i < count; i++) bones[i] = new(Text(r), r.ReadInt32(), TRS(r));
        var result = new SkeletonPayload(bones); Validate(result); return result;
    });
    public static ClipPayload DecodeClip(byte[] bytes) => Read(bytes, 0x31504C43, r => {
        int bones = Count(r, 1024); string name = Text(r); double duration = r.ReadDouble(); int tracks = Count(r, 1024); Available(r, tracks * 8L);
        var ts = new ImportTrack[tracks]; int total = 0;
        for (int t = 0; t < tracks; t++) { uint bone = r.ReadUInt32(); int kc = Count(r, ImportedModelCodec.MaxKeys); total = checked(total + kc);
            if (total > ImportedModelCodec.MaxKeys) throw new ArgumentException("Key budget exceeded."); Available(r, kc * 48L);
            var keys = new ImportKey[kc]; for (int k = 0; k < kc; k++) keys[k] = new(r.ReadDouble(), TRS(r)); ts[t] = new(bone, keys); }
        var result = new ClipPayload(bones, new(name, duration, ts)); Validate(result); return result;
    });
    public static MaterialSlotsPayload DecodeMaterials(byte[] bytes) => Read(bytes, 0x3154414D, r => {
        int count = Count(r, 4096); Available(r, count * 4L); string[] names = new string[count]; for (int i = 0; i < count; i++) names[i] = Text(r); return new MaterialSlotsPayload(names);
    });
    private static byte[] Write(uint magic, Action<BinaryWriter> encode)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Utf8, true); writer.Write(magic); writer.Write(1u); encode(writer);
        if (stream.Length > DerivedAssetCodec.MaxBlockBytes) throw new ArgumentException("Typed block byte budget exceeded."); return stream.ToArray();
    }
    private static T Read<T>(byte[] bytes, uint magic, Func<BinaryReader, T> decode)
    {
        if (bytes.Length is < 8 or > DerivedAssetCodec.MaxBlockBytes) throw new ArgumentException("Typed block byte budget exceeded.");
        using var stream = new MemoryStream(bytes, false); using var reader = new BinaryReader(stream, Utf8);
        try { if (reader.ReadUInt32() != magic || reader.ReadUInt32() != 1) throw new ArgumentException("Wrong typed block/version.");
            var result = decode(reader); if (stream.Position != stream.Length) throw new ArgumentException("Trailing typed block data."); return result; }
        catch (Exception e) when (e is EndOfStreamException or OverflowException or DecoderFallbackException) { throw new ArgumentException("Malformed typed payload.", e); }
    }
    private static int Count(BinaryReader r, int max) { int count = r.ReadInt32(); if (count < 0 || count > max) throw new ArgumentException("Typed count exceeded."); return count; }
    private static void Available(BinaryReader r, long bytes) { if (bytes > r.BaseStream.Length - r.BaseStream.Position) throw new ArgumentException("Truncated typed payload."); }
    private static void Text(BinaryWriter w, string value) { if (value is null || value.Contains('\0')) throw new ArgumentException("Invalid text."); var bytes = Utf8.GetBytes(value); if (bytes.Length > 65536) throw new ArgumentException("Text too large."); w.Write(bytes.Length); w.Write(bytes); }
    private static string Text(BinaryReader r) { int count = Count(r, 65536); Available(r, count); string value = Utf8.GetString(r.ReadBytes(count)); if (value.Contains('\0')) throw new ArgumentException("Invalid text."); return value; }
    private static void V3(BinaryWriter w, Vector3 v) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
    private static void V4(BinaryWriter w, Vector4 v) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); w.Write(v.W); }
    private static Vector3 V3(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    private static Vector4 V4(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    private static void TRS(BinaryWriter w, ImportTransform t) { V3(w, t.Position); V4(w, new(t.Rotation.X, t.Rotation.Y, t.Rotation.Z, t.Rotation.W)); V3(w, t.Scale); }
    private static ImportTransform TRS(BinaryReader r) { Vector3 p = V3(r); Vector4 q = V4(r); return new(p, new(q.X, q.Y, q.Z, q.W), V3(r)); }
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    private static void ValidTRS(ImportTransform t) { if (!Finite(t.Position) || !Finite(t.Scale) || t.Scale.X <= .000001f || t.Scale.Y <= .000001f || t.Scale.Z <= .000001f || !float.IsFinite(t.Rotation.LengthSquared()) || Math.Abs(t.Rotation.LengthSquared() - 1) > .002f) throw new ArgumentException("Invalid TRS."); }
    private static void Validate(SkeletonPayload p)
    {
        if (p.Bones is null || p.Bones.Length is < 1 or > 1024) throw new ArgumentException("Invalid skeleton.");
        for (int i = 0; i < p.Bones.Length; i++) { var b = p.Bones[i]; if (b is null || b.Name is null || b.Parent < -1 || b.Parent >= i) throw new ArgumentException("Invalid bone topology."); ValidTRS(b.BindLocal); }
    }
    private static void Validate(ClipPayload p)
    {
        var c = p.Clip;
        if (p.BoneCount is < 1 or > 1024 || c is null || c.Name is null || !double.IsFinite(c.Duration) || c.Duration is <= 0 or > 600 || c.Tracks is null || c.Tracks.Length > p.BoneCount) throw new ArgumentException("Invalid clip.");
        var seen = new HashSet<uint>(); long total = 0;
        foreach (var t in c.Tracks) { if (t is null || t.Bone >= p.BoneCount || !seen.Add(t.Bone) || t.Keys is null || t.Keys.Length == 0 || (total += t.Keys.Length) > ImportedModelCodec.MaxKeys) throw new ArgumentException("Invalid track.");
            double previous = -1; foreach (var k in t.Keys) { if (!double.IsFinite(k.Time) || k.Time < 0 || k.Time > c.Duration || k.Time <= previous) throw new ArgumentException("Invalid key time."); previous = k.Time; ValidTRS(k.Value); }
            if (t.Keys[0].Time != 0 || t.Keys[^1].Time != c.Duration) throw new ArgumentException("Sampling endpoints missing."); }
    }
    private static void Validate(MeshPayload p)
    {
        if (p.Vertices is null || p.Vertices.Length is < 1 or > ImportedModelCodec.MaxVertices || p.Indices is null || p.Indices.Length is < 3 or > ImportedModelCodec.MaxIndices || p.Indices.Length % 3 != 0 ||
            p.TriangleMaterials is null || p.TriangleMaterials.Length != p.Indices.Length / 3 || p.Tangents is null || (p.Tangents.Length != 0 && p.Tangents.Length != p.Vertices.Length) || p.Bindings is null || p.Bindings.Length > 4096 || p.MaterialSlots is < 0 or > 4096 ||
            (p.Skinned ? p.BoneCount is < 1 or > 1024 || p.Bindings.Length == 0 : p.BoneCount != 0 || p.Bindings.Length != 0)) throw new ArgumentException("Invalid mesh layout.");
        foreach (var v in p.Vertices)
        {
            if (!Finite(v.Position) || !Finite(v.Normal) || v.Normal.LengthSquared() < .000001f || !float.IsFinite(v.UV.X) || !float.IsFinite(v.UV.Y)) throw new ArgumentException("Invalid vertex.");
            if (p.Skinned) { Joint(v.Joints.X, v.Weights.X); Joint(v.Joints.Y, v.Weights.Y); Joint(v.Joints.Z, v.Weights.Z); Joint(v.Joints.W, v.Weights.W); if (Math.Abs(v.Weights.X + v.Weights.Y + v.Weights.Z + v.Weights.W - 1) > .001f) throw new ArgumentException("Invalid normalized weights."); }
            else if (v.Joints != default || v.Weights != Vector4.Zero) throw new ArgumentException("Static mesh cannot contain skin weights.");
        }
        foreach (var t in p.Tangents) if (!Finite(new(t.X, t.Y, t.Z)) || Math.Abs(new Vector3(t.X, t.Y, t.Z).LengthSquared() - 1) > .002f || t.W is not (1 or -1)) throw new ArgumentException("Invalid tangent.");
        foreach (uint index in p.Indices) if (index >= p.Vertices.Length) throw new ArgumentException("Invalid mesh index.");
        foreach (uint slot in p.TriangleMaterials) if (slot >= Math.Max(1, p.MaterialSlots)) throw new ArgumentException("Invalid material slot.");
        foreach (var b in p.Bindings) if (b is null || b.Bone >= p.BoneCount || b.GeometryToBone is null || b.GeometryToBone.Length != 16 || b.GeometryToBone.Any(f => !float.IsFinite(f)) || b.GeometryToBone[3] != 0 || b.GeometryToBone[7] != 0 || b.GeometryToBone[11] != 0 || b.GeometryToBone[15] != 1) throw new ArgumentException("Invalid affine mesh binding.");
        void Joint(ushort j, float w) { if (!float.IsFinite(w) || w < 0 || (w > 0 && j >= p.Bindings.Length)) throw new ArgumentException("Invalid palette weight/index."); }
    }
}
