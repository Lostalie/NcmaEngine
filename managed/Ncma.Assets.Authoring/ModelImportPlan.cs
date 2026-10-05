using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Ncma.Editor.Core;

namespace Ncma.Assets.Authoring;

public sealed record ImportIdentityChange(Guid AssetId, AssetKind Kind, string Name, string SourceKey, string Reason);
public sealed record ModelImportPlan(AssetRecord Record, byte[] DerivedBytes, ImportIdentityChange[] Changes,
    AssetDiagnostic[] Diagnostics, bool RequiresConfirmation);

// Conservative content/structure evidence, NOT FBX array indices/renamed labels masquerading as source IDs.
// When any evidence changes, explicit new-identity+tombstone approval is required; no auto retargeting.
public static class ModelImportPlanner
{
    private sealed record Part(AssetKind Kind, string Name, string Key, byte[] Bytes);
    public static ModelImportPlan Build(ImportedModel model, AssetRecord requested, Guid project, bool staticOnly,
        AssetRecord? previous = null, CancellationToken cancellation = default)
    {
        ImportedModelCodec.Validate(model); _ = AssetRecordCodec.Encode(requested);
        if (requested.Kind != (staticOnly ? AssetKind.StaticMesh : AssetKind.Character) || requested.Settings.SampleRate != model.SampleRate || project == Guid.Empty)
            throw new ArgumentException("Model import kind/settings mismatch.");
        if (previous is not null && (previous.AssetId != requested.AssetId || previous.Kind != requested.Kind || previous.SourcePath != requested.SourcePath))
            throw new ArgumentException("Reimport cannot change root identity/kind/source.");
        var parts = new List<Part>(); var diagnostics = new List<AssetDiagnostic>();
        byte[] skeleton = ModelPayloadCodec.Encode(new SkeletonPayload(model.Bones)); string rigEvidence = Hash(skeleton);
        Part? rig = staticOnly ? null : Add(AssetKind.Skeleton, "Skeleton", skeleton, "rig");
        var meshes = new List<(Part Mesh, Part Materials)>();
        Matrix4x4[] bind = BindMatrices(model.Bones);
        foreach (var source in model.Meshes)
        {
            cancellation.ThrowIfCancellationRequested();
            var vertices = (ImportVertex[])source.Vertices.Clone();
            if (staticOnly)
            {
                for (int v = 0; v < vertices.Length; v++)
                {
                    if ((v & 1023) == 0) cancellation.ThrowIfCancellationRequested(); var value = vertices[v];
                    // Static native mode has one rigid geometry binding. Bake geometry + node transform exactly once.
                    if (value.Weights != new Vector4(1, 0, 0, 0) || value.Joints.X >= source.Bindings.Length) throw new ArgumentException("Static import contains non-rigid skinning.");
                    var binding = source.Bindings[value.Joints.X]; var transform = Matrix(binding.GeometryToBone) * bind[binding.Bone];
                    if (!Matrix4x4.Invert(transform, out var inverse) || transform.GetDeterminant() <= 0) throw new ArgumentException("Static transform reflected/singular.");
                    vertices[v] = value with { Position = Vector3.Transform(value.Position, transform), Normal = Vector3.Normalize(Vector3.TransformNormal(value.Normal, Matrix4x4.Transpose(inverse))), Joints = default, Weights = Vector4.Zero };
                }
            }
            bool degenerate = false;
            Vector4[] tangents = requested.Settings.GenerateTangents ? Tangents(vertices, source.Indices, cancellation, out degenerate) : [];
            if (requested.Settings.GenerateTangents && degenerate) diagnostics.Add(new("tangent_uv_fallback", requested.AssetId, requested.SourcePath, "Degenerate UVs use a deterministic orthogonal tangent; not MikkTSpace."));
            var payload = new MeshPayload(!staticOnly, staticOnly ? 0 : model.Bones.Length, vertices, tangents, source.Indices, source.TriangleMaterials,
                staticOnly ? [] : source.Bindings, source.Materials.Length);
            var mesh = Add(staticOnly ? AssetKind.StaticMesh : AssetKind.SkinnedMesh, Name(source.Name), ModelPayloadCodec.Encode(payload), staticOnly ? "static" : rigEvidence);
            var material = Add(AssetKind.MaterialSet, Name(source.Name) + " Materials", ModelPayloadCodec.Encode(new MaterialSlotsPayload(source.Materials)), mesh.Key);
            meshes.Add((mesh, material));
        }
        var clips = new List<Part>();
        if (!staticOnly) foreach (var clip in model.Clips) { cancellation.ThrowIfCancellationRequested(); clips.Add(Add(AssetKind.Clip, Name(clip.Name), ModelPayloadCodec.Encode(new ClipPayload(model.Bones.Length, clip)), rigEvidence)); }
        if (parts.Count >= DerivedAssetCodec.MaxBlocks) throw new ArgumentException("Model subasset block budget exceeded.");
        if (parts.Select(p => p.Key).Distinct(StringComparer.Ordinal).Count() != parts.Count)
            throw new EditCommandRejectedException("asset_identity_ambiguous");
        // Names are scoped evidence and a required uniqueness condition, never the sole match signal.
        if (parts.GroupBy(p => (p.Kind, p.Name)).Any(g => g.Count() != 1)) throw new EditCommandRejectedException("asset_identity_ambiguous");
        var old = previous?.Subassets.ToDictionary(s => s.SourceKey, StringComparer.Ordinal) ?? [];
        var map = new Dictionary<string, SubassetRecord>(StringComparer.Ordinal); var changes = new List<ImportIdentityChange>();
        foreach (var p in parts)
        {
            bool matched = old.TryGetValue(p.Key, out var original) && original.Kind == p.Kind && !original.Tombstone;
            if (!matched && original is not null) throw new EditCommandRejectedException("asset_tombstone_identity_conflict");
            var s = new SubassetRecord(matched ? original!.AssetId : Guid.NewGuid(), p.Kind, p.Key, p.Name, false); map.Add(p.Key, s);
            changes.Add(new(s.AssetId, s.Kind, s.Name, s.SourceKey, matched ? "exact_scoped_structure_and_content" : "new_identity"));
        }
        var missing = old.Values.Where(s => !map.ContainsKey(s.SourceKey)).Select(s => s with { Tombstone = true }).ToArray();
        foreach (var s in missing) changes.Add(new(s.AssetId, s.Kind, s.Name, s.SourceKey, "missing_evidence_tombstone_no_retarget"));
        bool conflict = previous?.Generation is not null && (missing.Any(s => !old[s.SourceKey].Tombstone) || changes.Any(c => c.Reason == "new_identity"));
        var manifest = new ModelAssetManifest(1, requested.AssetId, staticOnly, requested.SourceHash, requested.Settings,
            rig is null ? null : map[rig.Key].AssetId, meshes.Select(p => new ModelMeshReference(map[p.Mesh.Key].AssetId, map[p.Materials.Key].AssetId)).ToArray(), clips.Select(p => map[p.Key].AssetId).ToArray());
        var blocks = parts.Select(p => new DerivedAssetBlock(map[p.Key].AssetId, p.Kind, p.Bytes)).Prepend(new(requested.AssetId, requested.Kind, ModelAssetManifestCodec.Encode(manifest)));
        byte[] derived = DerivedAssetCodec.Encode(blocks); string content = Hash(derived);
        ulong number = checked((previous?.Generation?.Number ?? 0) + 1);
        var record = requested with { Subassets = map.Values.Concat(missing).ToArray(), Dependencies = [], Importer = "ncma.ufbx", ImporterVersion = 1,
            Generation = new(number, content, $"out/assets/{project:N}/{requested.AssetId:N}/{number}-{content}.nca") };
        _ = AssetRecordCodec.Encode(record); ModelAssetManifestCodec.ValidateBundle(derived, record);
        foreach (string warning in model.Warnings) diagnostics.Add(new("fbx_conversion_warning", record.AssetId, record.SourcePath, warning));
        diagnostics.Add(new("material_slots_only", record.AssetId, record.SourcePath, "Material names/slots only. PBR conversion/textures are M3.3; external/embedded files are never followed."));
        return new(record, derived, changes.ToArray(), diagnostics.ToArray(), conflict);

        Part Add(AssetKind kind, string name, byte[] bytes, string scope)
        {
            string key = $"model1:{kind}:{Hash(Encoding.UTF8.GetBytes(name + "\n" + scope + "\n" + Hash(bytes)))}";
            var part = new Part(kind, name, key, bytes); parts.Add(part); return part;
        }
        static string Name(string value) { if (string.IsNullOrWhiteSpace(value) || value.Length > 220 || value.Any(char.IsControl)) throw new ArgumentException("Invalid subasset name."); return value; }
    }
    public static Matrix4x4[] BindMatrices(ImportBone[] bones)
    {
        Matrix4x4[] result = new Matrix4x4[bones.Length];
        for (int i = 0; i < bones.Length; i++) { var b = bones[i]; var t = b.BindLocal; var local = Matrix4x4.CreateScale(t.Scale) * Matrix4x4.CreateFromQuaternion(t.Rotation) * Matrix4x4.CreateTranslation(t.Position); result[i] = b.Parent < 0 ? local : local * result[b.Parent]; }
        return result;
    }
    private static Matrix4x4 Matrix(float[] columns) => new(columns[0], columns[1], columns[2], columns[3], columns[4], columns[5], columns[6], columns[7], columns[8], columns[9], columns[10], columns[11], columns[12], columns[13], columns[14], columns[15]);
    // Deterministic triangle-UV tangent v1, mirrored sign supported. Not MikkTSpace/DCC equivalence.
    private static Vector4[] Tangents(ImportVertex[] vertices, uint[] indices, CancellationToken token, out bool fallback)
    {
        Vector3[] u = new Vector3[vertices.Length], v = new Vector3[vertices.Length]; fallback = false;
        for (int i = 0; i < indices.Length; i += 3)
        {
            if ((i & 1023) == 0) token.ThrowIfCancellationRequested(); uint a = indices[i], b = indices[i + 1], c = indices[i + 2];
            var e1 = vertices[b].Position - vertices[a].Position; var e2 = vertices[c].Position - vertices[a].Position;
            var uv1 = vertices[b].UV - vertices[a].UV; var uv2 = vertices[c].UV - vertices[a].UV; float d = uv1.X * uv2.Y - uv1.Y * uv2.X;
            if (Math.Abs(d) < .0000001f) { fallback = true; continue; }
            Vector3 tangent = (e1 * uv2.Y - e2 * uv1.Y) / d, bitangent = (e2 * uv1.X - e1 * uv2.X) / d;
            u[a] += tangent; u[b] += tangent; u[c] += tangent; v[a] += bitangent; v[b] += bitangent; v[c] += bitangent;
        }
        var output = new Vector4[vertices.Length];
        for (int i = 0; i < output.Length; i++) { if ((i & 1023) == 0) token.ThrowIfCancellationRequested(); Vector3 normal = Vector3.Normalize(vertices[i].Normal), tangent = u[i] - normal * Vector3.Dot(normal, u[i]);
            if (tangent.LengthSquared() < .0000001f) { fallback = true; tangent = Vector3.Cross(Math.Abs(normal.Y) < .9f ? Vector3.UnitY : Vector3.UnitX, normal); }
            tangent = Vector3.Normalize(tangent); output[i] = new(tangent, Vector3.Dot(Vector3.Cross(normal, tangent), v[i]) < 0 ? -1 : 1); }
        return output;
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
