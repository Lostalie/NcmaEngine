using System.Buffers.Binary;
using System.Numerics;
using Ncma.Assets;

namespace Ncma.Rendering;

public readonly record struct MeshBounds(Vector3 Min, Vector3 Max);
public readonly record struct MeshDrawRange(uint MaterialSlot, uint FirstIndex, uint IndexCount);
public sealed record MeshUploadDiagnostic(string Code, string Message);

// CPU preparation only; RendererSession explicitly uploads this immutable data through scene-render v1.
// Immutable copied upload data; prepare once OFF the frame path, then cache by asset UUID/generation.
// Layout 1 is a GPU input contract, not a new persistent asset format.
public sealed class MeshUploadData
{
    public const int LayoutVersion = 1, VertexStride = 48, MaxUploadBytes = 64 * 1024 * 1024;
    private readonly byte[] _vertices;
    private readonly uint[] _indices;
    private readonly MeshDrawRange[] _ranges;
    private readonly MeshUploadDiagnostic[] _diagnostics;
    public ReadOnlySpan<byte> Vertices => _vertices;
    public ReadOnlySpan<uint> Indices => _indices;
    public ReadOnlySpan<MeshDrawRange> Ranges => _ranges;
    public ReadOnlySpan<MeshUploadDiagnostic> Diagnostics => _diagnostics;
    public int VertexCount => _vertices.Length / VertexStride;
    public int MaterialSlots { get; }
    public MeshBounds Bounds { get; }
    public bool CanUseNormalMap { get; }
    public long UploadBytes => _vertices.LongLength + _indices.LongLength * 4;
    private MeshUploadData(byte[] vertices, uint[] indices, MeshDrawRange[] ranges, MeshUploadDiagnostic[] diagnostics,
        int slots, MeshBounds bounds, bool canUseNormalMap)
    { _vertices = vertices; _indices = indices; _ranges = ranges; _diagnostics = diagnostics; MaterialSlots = slots; Bounds = bounds; CanUseNormalMap = canUseNormalMap; }

    public static MeshUploadData PrepareStatic(MeshPayload source, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(source); cancellation.ThrowIfCancellationRequested();
        if (source.Skinned) throw new ArgumentException("Use explicit BindPoseMeshUploadData.Prepare; never silently strip skinning.");
        // Closed typed codec supplies layout/topology/finite/budget checks and an owned snapshot.
        var mesh = ModelPayloadCodec.DecodeMesh(ModelPayloadCodec.Encode(source)); cancellation.ThrowIfCancellationRequested();
        long uploadBytes = checked((long)mesh.Vertices.Length * VertexStride + (long)mesh.Indices.Length * 4);
        if (uploadBytes > MaxUploadBytes) throw new ArgumentException("Mesh upload byte budget exceeded.");
        var vertices = new byte[checked(mesh.Vertices.Length * VertexStride)];
        var min = new Vector3(float.PositiveInfinity); var max = new Vector3(float.NegativeInfinity);
        bool hasTangents = mesh.Tangents.Length != 0, degenerateUv = false;
        for (int i = 0; i < mesh.Vertices.Length; i++)
        {
            if ((i & 1023) == 0) cancellation.ThrowIfCancellationRequested(); var v = mesh.Vertices[i];
            var normal = Vector3.Normalize(v.Normal); var tangent = hasTangents ? mesh.Tangents[i] : new Vector4(Vector3.Normalize(Vector3.Cross(Math.Abs(normal.Y) < .9f ? Vector3.UnitY : Vector3.UnitX, normal)), 1);
            if (!Finite(normal) || Math.Abs(normal.LengthSquared() - 1) > .002f || !Finite(new(tangent.X, tangent.Y, tangent.Z))) throw new ArgumentException("Invalid normalized vertex basis.");
            if (Math.Abs(Vector3.Dot(normal, new(tangent.X, tangent.Y, tangent.Z))) > .002f) throw new ArgumentException("Tangent must be orthogonal to its normal.");
            min = Vector3.Min(min, v.Position); max = Vector3.Max(max, v.Position);
            Span<byte> row = vertices.AsSpan(i * VertexStride, VertexStride);
            F(row, 0, v.Position.X); F(row, 4, v.Position.Y); F(row, 8, v.Position.Z);
            F(row, 12, normal.X); F(row, 16, normal.Y); F(row, 20, normal.Z);
            F(row, 24, v.UV.X); F(row, 28, v.UV.Y);
            F(row, 32, tangent.X); F(row, 36, tangent.Y); F(row, 40, tangent.Z); F(row, 44, tangent.W);
        }
        int slots = Math.Max(1, mesh.MaterialSlots), triangles = mesh.Indices.Length / 3;
        var counts = new int[slots];
        for (int i = 0; i < triangles; i++)
        {
            if ((i & 1023) == 0) cancellation.ThrowIfCancellationRequested(); counts[mesh.TriangleMaterials[i]]++;
            var a = mesh.Vertices[mesh.Indices[i * 3]].UV; var b = mesh.Vertices[mesh.Indices[i * 3 + 1]].UV - a; var c = mesh.Vertices[mesh.Indices[i * 3 + 2]].UV - a;
            float determinant = b.X * c.Y - b.Y * c.X;
            if (!float.IsFinite(determinant)) throw new ArgumentException("UV determinant overflow.");
            degenerateUv |= Math.Abs(determinant) < .0000001f;
        }
        // Opaque/alpha-mask only: stable buckets by material, preserving order inside every bucket.
        var offsets = new int[slots]; var ranges = new List<MeshDrawRange>(); int offset = 0;
        for (int i = 0; i < slots; i++) { offsets[i] = offset; if (counts[i] != 0) ranges.Add(new((uint)i, (uint)offset, (uint)(counts[i] * 3))); offset += counts[i] * 3; }
        var cursors = (int[])offsets.Clone(); var indices = new uint[mesh.Indices.Length];
        for (int i = 0; i < triangles; i++) { if ((i & 1023) == 0) cancellation.ThrowIfCancellationRequested(); uint slot = mesh.TriangleMaterials[i]; int at = cursors[slot];
            mesh.Indices.AsSpan(i * 3, 3).CopyTo(indices.AsSpan(at, 3)); cursors[slot] += 3; }
        var diagnostics = new List<MeshUploadDiagnostic>();
        if (!hasTangents) diagnostics.Add(new("missing_tangents", "Deterministic orthogonal tangent fallback; normal mapping disabled."));
        if (degenerateUv) diagnostics.Add(new("degenerate_uv", "Degenerate/missing triangle UVs; normal mapping disabled."));
        if (mesh.MaterialSlots == 0) diagnostics.Add(new("missing_material", "Implicit slot zero requires the named default-material fallback."));
        cancellation.ThrowIfCancellationRequested();
        return new(vertices, indices, ranges.ToArray(), diagnostics.ToArray(), slots, new(min, max), hasTangents && !degenerateUv);
    }
    private static void F(Span<byte> row, int offset, float value) => BinaryPrimitives.WriteSingleLittleEndian(row[offset..], value);
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
