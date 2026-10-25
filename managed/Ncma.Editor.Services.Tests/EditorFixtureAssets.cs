using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using Ncma.Assets;
using Ncma.Editor.Services;
using Ncma.Scene.Rendering;

// Cold test fixtures use the same NCA/NCE codecs and explicit preparation as real hosts.
// No fake prepared metadata, renderer fallback, or mutation of authoring/history during preparation.
internal sealed class EditorFixtureAssets
{
    internal readonly Guid Project = Guid.NewGuid();
    internal readonly string Root;
    internal EditorFixtureAssets(string output, string label)
    {
        Root = Path.Combine(output, label + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(Root, "assets"));
    }
    internal EnvironmentLightingData Environment()
    {
        Guid id = Guid.NewGuid(); var settings = new EnvironmentCookSettings(2, 2, 2, 64);
        byte[] bytes = new byte[EnvironmentPackage.HeaderBytes + settings.FloatCount * 4];
        uint[] header = [0x3145434E, 1, 1, 160, 4, 2, 2, 2, 2, 64, 2, (uint)settings.FloatCount];
        for (int i = 0; i < header.Length; i++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), header[i]);
        id.TryWriteBytes(bytes.AsSpan(48)); Guid.NewGuid().TryWriteBytes(bytes.AsSpan(64));
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(80), 1);
        SHA256.HashData("trusted decoded test source"u8).CopyTo(bytes, 88);
        for (int i = 0; i < settings.FloatCount; i++) {
            float value = i >= settings.FloatCount - 8 ? (i % 2 == 0 ? .5f : .1f) : i % 4 == 3 ? 1 : i < 96 ? .18f * MathF.PI : .18f;
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(160 + i * 4), value);
        }
        SHA256.HashData(bytes.AsSpan(160)).CopyTo(bytes, 120);
        var package = EnvironmentPackage.Decode(bytes, Convert.ToHexString(SHA256.HashData(bytes)));
        var descriptor = EnvironmentAssetDescriptor.FromPackage(Project, package);
        string path = Path.Combine(Root, descriptor.Path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, package.CopyBytes()); File.WriteAllBytes(Path.Combine(Root, "assets/environment.ncenv"), descriptor.Encode());
        return new(1, id, 1, package.ContentHash, 1, 0, true);
    }
    internal StaticMeshData Geometry()
    {
        Guid model = Guid.NewGuid(), mesh = Guid.NewGuid(), set = Guid.NewGuid();
        string sourceHash = Convert.ToHexString(SHA256.HashData("fixture triangle"u8)); var settings = new ImportSettings(1, 30, true);
        var manifest = new ModelAssetManifest(1, model, true, sourceHash, settings, null, [new(mesh, set)], []);
        ImportVertex V(Vector3 p, Vector2 uv) => new(p, Vector3.UnitZ, uv, default, Vector4.Zero);
        var payload = new MeshPayload(false, 0, [V(Vector3.Zero, Vector2.Zero), V(Vector3.UnitX, Vector2.UnitX), V(Vector3.UnitY, Vector2.UnitY)],
            [new(1, 0, 0, 1), new(1, 0, 0, 1), new(1, 0, 0, 1)], [0, 1, 2], [0], [], 1);
        byte[] bytes = DerivedAssetCodec.Encode([new(model, AssetKind.StaticMesh, ModelAssetManifestCodec.Encode(manifest)),
            new(mesh, AssetKind.StaticMesh, ModelPayloadCodec.Encode(payload)), new(set, AssetKind.MaterialSet, ModelPayloadCodec.Encode(new MaterialSlotsPayload(["Imported"])))]);
        string hash = Convert.ToHexString(SHA256.HashData(bytes)), path = $"out/assets/{Project:N}/{model:N}/1-{hash}.nca";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(Root, path))!); File.WriteAllBytes(Path.Combine(Root, path), bytes);
        var record = new AssetRecord(1, model, AssetKind.StaticMesh, "assets/Model.fbx", sourceHash, "ufbx", 1, settings,
            [new(mesh, AssetKind.StaticMesh, "mesh/0", "Triangle", false), new(set, AssetKind.MaterialSet, "materials/0", "Slots", false)], [], new(1, hash, path));
        File.WriteAllBytes(Path.Combine(Root, "assets/Model.fbx.ncmeta"), AssetRecordCodec.Encode(record));
        return new(mesh, set, true, true, 1);
    }
    internal void Prepare(EditorSessionOwner owner)
    {
        byte[] before = owner.Document.CaptureBytes(); var history = owner.Edit!.State;
        owner.PrepareRenderAssets(Root, Project);
        _ = SceneAssetPreparation.ResolveDependencies(owner.Document.CaptureSnapshot(), owner.RenderAssets);
        if (!before.SequenceEqual(owner.Document.CaptureBytes()) || history != owner.Edit.State)
            throw new Exception("Fixture asset preparation changed authoring bytes/history.");
    }
}
