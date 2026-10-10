using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using Ncma.Assets;
using Ncma.Assets.Runtime;
using Ncma.Scene.Rendering;

internal static partial class Program
{
    private static byte[] Package(AssetFixture f, params AssetRef[] dynamicRoots) => SceneAssetPreparation.CreateRuntimePackage(f.Root, f.Project, f.Scene().CaptureSnapshot(), dynamicRoots);
    private static byte[] ChangeIndex(byte[] bytes, Action<JsonObject> change)
    {
        int size = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8)); var node = JsonNode.Parse(bytes.AsSpan(16, size))!.AsObject(); change(node);
        byte[] index = Encoding.UTF8.GetBytes(node.ToJsonString()), payload = bytes.AsSpan(16 + size).ToArray(), result = new byte[16 + index.Length + payload.Length];
        bytes.AsSpan(0, 16).CopyTo(result); BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(8), index.Length); index.CopyTo(result, 16); payload.CopyTo(result, 16 + index.Length); return result;
    }
    private static byte[] ChangeMeshSlots(byte[] bytes, int slots)
    {
        byte[]? changed = null; int offset = 0;
        byte[] result = ChangeIndex(bytes, n => {
            var entry = n["assets"]!.AsArray().Single(a => a!["encoding"]!.GetValue<string>() == "mesh")!;
            offset = entry["offset"]!.GetValue<int>(); int length = entry["length"]!.GetValue<int>();
            var payload = ModelPayloadCodec.DecodeMesh(bytes.AsSpan(16 + BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8)) + offset, length).ToArray());
            changed = ModelPayloadCodec.Encode(payload with { MaterialSlots = slots }); Check(changed.Length == length);
            entry["hash"] = Convert.ToHexString(SHA256.HashData(changed));
        });
        changed!.CopyTo(result, 16 + BinaryPrimitives.ReadInt32LittleEndian(result.AsSpan(8)) + offset); return result;
    }
    private static void RegisterPackageTests(List<(string Name, Action Run)> tests)
    {
        tests.AddRange(new (string, Action)[] {
            ("M7.2 packed surface closure, source-free roundtrip and rehashed wrong semantics reject", () => {
                var f=new AssetFixture();Guid color=f.Texture(TextureSemantic.Color),normal=f.Texture(TextureSemantic.Normal),packed=f.Texture(TextureSemantic.Data),id=Guid.NewGuid();
                var definition=MaterialSurfaceContract.WithPackedSurface(MaterialDefinition.Default(id) with{BaseTexture=color,EmissiveTexture=color,NormalTexture=normal},packed,PackedSurfaceLayout.OcclusionRoughnessMetallic);
                File.WriteAllBytes(Path.Combine(f.Root,"assets/Surface.ncmaterial"),MaterialCodec.Encode(definition));
                using(var source=f.Prepare(true,Ref(id,AssetKind.Material)))using(var lease=source.AcquireLease())Check(lease.List().Count==4&&lease.PinnedGenerations==3);
                byte[] bytes=Package(f,Ref(id,AssetKind.Material));string path=Path.Combine(f.Root,"assets/surface.ncpak");File.WriteAllBytes(path,bytes);
                using(var snapshot=RuntimeAssetPackage.Prepare(f.Root,"assets/surface.ncpak",f.Project,[Ref(id,AssetKind.Material)]))using(var lease=snapshot.AcquireLease())
                    Check(((RuntimeMaterialAsset)lease.Require(id,AssetKind.Material)).Definition==definition&&((RuntimeTextureAsset)lease.Require(packed,AssetKind.Texture)).Data.Semantic==TextureSemantic.Data);
                // Valid material UUID/roles and recomputed payload hash cannot disguise a Color texture as Normal.
                var altered=definition with{BaseTexture=Guid.Empty,EmissiveTexture=Guid.Empty,NormalTexture=color};byte[] changed=MaterialCodec.Encode(altered);int offset=0;
                byte[] bad=ChangeIndex(bytes,n=>{var entry=n["assets"]!.AsArray().Single(a=>a!["assetId"]!.GetValue<Guid>()==id)!;
                    offset=entry["offset"]!.GetValue<int>();Check(changed.Length==entry["length"]!.GetValue<int>());entry["hash"]=Convert.ToHexString(SHA256.HashData(changed));});
                changed.CopyTo(bad,16+BinaryPrimitives.ReadInt32LittleEndian(bad.AsSpan(8))+offset);AssetReject(()=>RuntimeAssetPackage.Inspect(bad,f.Project));
                Check(!Locked(path));
            }),
            ("M7.2 missing material texture is explicit Editor diagnostic, strict runtime/package rejection", () => {
                var f=new AssetFixture();var definition=MaterialDefinition.Default(Guid.NewGuid()) with{BaseTexture=Guid.NewGuid()};
                string path=Path.Combine(f.Root,"assets/Missing.ncmaterial");byte[] input=MaterialCodec.Encode(definition);File.WriteAllBytes(path,input);
                using(var preview=f.Prepare(false,Ref(definition.AssetId,AssetKind.Material)))using(var lease=preview.AcquireLease()){
                    Check(lease.Diagnostics.Count==1&&lease.Diagnostics[0].Code=="asset_missing");AssetReject(()=>RuntimeAssetPackage.Encode(lease));
                }
                AssetReject(()=>f.Prepare(true,Ref(definition.AssetId,AssetKind.Material)));Check(File.ReadAllBytes(path).SequenceEqual(input));
            }),
            ("M3.9 deterministic source-free typed runtime package", () => {
                var f = new AssetFixture(); byte[] bytes = Package(f); Check(bytes.SequenceEqual(Package(f)));
                var index = RuntimeAssetPackage.Inspect(bytes, f.Project); Check(index.Assets.Length == 3 && index.Assets.Select(a => a.AssetId).SequenceEqual(index.Assets.Select(a => a.AssetId).Order()));
                string strings = Encoding.UTF8.GetString(bytes); Check(!strings.Contains("assets/Model.fbx") && !strings.Contains("ufbx") && !strings.Contains("sourcePath") && !strings.Contains("runtimeHandle"));
                Check(!index.Assets.Any(a => a.Kind is AssetKind.Prefab or AssetKind.OverrideSet));
            }),
            ("M3.9 runtime package relocated without authoring catalog/out/generation", () => {
                var f = new AssetFixture(); byte[] bytes = Package(f); string moved = Path.Combine(OutputRoot(), "m3-9-moved", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(moved, "assets"));
                string pack = Path.Combine(moved, "assets/game.ncpak"); File.WriteAllBytes(pack, bytes);
                using (var snapshot = RuntimeAssetPackage.Prepare(moved, "assets/game.ncpak", f.Project, [f.Reference])) {
                    using var lease = snapshot.AcquireLease(); Check(lease.RequireMesh(f.Mesh, AssetKind.StaticMesh).BoundsMax == new System.Numerics.Vector3(1, 1, 0) && Locked(pack));
                    using var scene = SceneAssetPreparation.Prepare(moved, f.Project, f.Scene().CaptureSnapshot(), true, "assets/game.ncpak"); Check(SceneRenderValidation.Inspect(f.Scene().CaptureSnapshot(), scene.Metadata, true).Count == 0);
                }
                Check(!Locked(pack) && !Directory.Exists(Path.Combine(moved, "out")) && Directory.GetFiles(Path.Combine(moved, "assets")).Length == 1);
            }),
            ("M3.9 runtime package strict malformed/version/range/hash/duplicate rejection", () => {
                var f = new AssetFixture(); byte[] bytes = Package(f);
                AssetReject(() => RuntimeAssetPackage.Inspect(bytes, Guid.NewGuid()));
                foreach (int offset in new[] { 0, 4, 8, 12, bytes.Length - 1 }) { byte[] bad = (byte[])bytes.Clone(); bad[offset] ^= 0x7F; AssetReject(() => RuntimeAssetPackage.Inspect(bad, f.Project)); }
                AssetReject(() => RuntimeAssetPackage.Inspect(bytes[..^1], f.Project)); AssetReject(() => RuntimeAssetPackage.Inspect([.. bytes, 0], f.Project));
                AssetReject(() => RuntimeAssetPackage.Inspect(ChangeIndex(bytes, n => n["unexpected"] = 1), f.Project));
                AssetReject(() => RuntimeAssetPackage.Inspect(ChangeIndex(bytes, n => n["version"] = 2), f.Project));
                AssetReject(() => RuntimeAssetPackage.Inspect(ChangeIndex(bytes, n => n["assets"]![0]!["offset"] = -1), f.Project));
                AssetReject(() => RuntimeAssetPackage.Inspect(ChangeIndex(bytes, n => n["assets"]![0]!["length"] = int.MaxValue), f.Project));
                AssetReject(() => RuntimeAssetPackage.Inspect(ChangeIndex(bytes, n => n["assets"]![0]!["assetId"] = Guid.Empty), f.Project));
                AssetReject(() => RuntimeAssetPackage.Inspect(ChangeIndex(bytes, n => n["assets"]![0]!["kind"] = "prefab"), f.Project));
                AssetReject(() => RuntimeAssetPackage.Inspect(ChangeIndex(bytes, n => n["assets"]![0]!["kind"] = 1), f.Project));
                AssetReject(() => RuntimeAssetPackage.Inspect(ChangeIndex(bytes, n => n["assets"]![0]!["kind"] = "unregistered"), f.Project));
                AssetReject(() => RuntimeAssetPackage.Inspect(ChangeIndex(bytes, n => n["assets"]![0]!.AsObject().Remove("hash")), f.Project));
                // JsonNode cannot retain duplicate keys; inject one into the encoded table itself.
                int size = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8));
                byte[] duplicate = Encoding.UTF8.GetBytes("{\"version\":1," + Encoding.UTF8.GetString(bytes, 16, size)[1..]);
                byte[] duplicated = new byte[bytes.Length + duplicate.Length - size]; bytes.AsSpan(0, 16).CopyTo(duplicated);
                BinaryPrimitives.WriteInt32LittleEndian(duplicated.AsSpan(8), duplicate.Length); duplicate.CopyTo(duplicated, 16); bytes.AsSpan(16 + size).CopyTo(duplicated.AsSpan(16 + duplicate.Length));
                AssetReject(() => RuntimeAssetPackage.Inspect(duplicated, f.Project));
            }),
            ("M3.9 package model/rig/material identity and closure cannot be forged", () => {
                var f = new AssetFixture(); byte[] bytes = Package(f);
                // Raw zero and one slots have the same effective fallback count, but must not be
                // conflated by closure validation. A recomputed hash is not proof of typed linkage.
                AssetReject(() => RuntimeAssetPackage.Inspect(ChangeMeshSlots(bytes, 0), f.Project));
                foreach (string field in new[] { "modelId", "skeletonId", "generation", "encoding" }) AssetReject(() => RuntimeAssetPackage.Inspect(ChangeIndex(bytes, n => {
                    var mesh = n["assets"]!.AsArray().Single(a => a!["encoding"]!.GetValue<string>() == "mesh")!;
                    mesh[field] = field == "generation" ? JsonValue.Create(2) : JsonValue.Create(field == "encoding" ? "data" : Guid.NewGuid().ToString("D"));
                }), f.Project));
                AssetReject(() => RuntimeAssetPackage.Inspect(ChangeIndex(bytes, n => { var a = n["assets"]!.AsArray(); (a[0], a[1]) = (a[1]!.DeepClone(), a[0]!.DeepClone()); }), f.Project));
            }),
            ("M3.9 package dynamic roots include PBR material/texture but omit unrequested assets", () => {
                var f = new AssetFixture(); Guid texture = f.Texture(TextureSemantic.Color), unused = f.Texture(TextureSemantic.Normal), material = Guid.NewGuid(), set = Guid.NewGuid();
                File.WriteAllBytes(Path.Combine(f.Root, "assets/Runtime.ncmaterial"), MaterialCodec.Encode(MaterialDefinition.Default(material) with { BaseTexture = texture }));
                File.WriteAllBytes(Path.Combine(f.Root, "assets/Runtime.ncmatset"), MaterialCodec.Encode(new MaterialSetDefinition(1, set, [material])));
                byte[] bytes = Package(f, Ref(set, AssetKind.MaterialSet)); var index = RuntimeAssetPackage.Inspect(bytes, f.Project);
                Check(index.Assets.Any(a => a.AssetId == texture) && index.Assets.Any(a => a.AssetId == material) && !index.Assets.Any(a => a.AssetId == unused));
                Check(bytes.SequenceEqual(Package(f, Ref(set, AssetKind.MaterialSet), Ref(set, AssetKind.MaterialSet))));
                AssetReject(() => Package(f, Ref(Guid.NewGuid(), AssetKind.Clip))); AssetReject(() => Package(f, Ref(texture, AssetKind.Clip)));
            }),
            ("M3.9 explicit package never falls back, type/cancel/budget reject and leases release", () => {
                var f = new AssetFixture(); string path = Path.Combine(f.Root, "assets/game.ncpak"); byte[] bytes = Package(f); File.WriteAllBytes(path, bytes);
                AssetReject(() => RuntimeAssetPackage.Prepare(f.Root, "assets/absent.ncpak", f.Project, [f.Reference]));
                AssetReject(() => RuntimeAssetPackage.Prepare(f.Root, "assets/../game.ncpak", f.Project, [f.Reference]));
                AssetReject(() => RuntimeAssetPackage.Prepare(f.Root, "assets/game.ncpak", f.Project, [Ref(f.Mesh, AssetKind.SkinnedMesh)]));
                AssetReject(() => RuntimeAssetPackage.Prepare(f.Root, "assets/game.ncpak", f.Project, Enumerable.Repeat(f.Reference, 4097)));
                using var stop = new CancellationTokenSource(); stop.Cancel(); AssetReject(() => RuntimeAssetPackage.Prepare(f.Root, "assets/game.ncpak", f.Project, [f.Reference], stop.Token));
                bytes[^1] ^= 1; File.WriteAllBytes(path, bytes); AssetReject(() => SceneAssetPreparation.Prepare(f.Root, f.Project, f.Scene().CaptureSnapshot(), true, "assets/game.ncpak")); Check(!Locked(path));
            }),
            ("M3.9 package 32 create/borrow/close cycles have owned data and no file pin leak", () => {
                var f = new AssetFixture(); string path = Path.Combine(f.Root, "assets/game.ncpak"); File.WriteAllBytes(path, Package(f));
                for (int cycle = 0; cycle < 32; cycle++) {
                    var snapshot = RuntimeAssetPackage.Prepare(f.Root, "assets/game.ncpak", f.Project, [f.Reference]); using (var lease = snapshot.AcquireLease()) {
                        snapshot.Dispose(); Check(Locked(path)); var payload = lease.RequireMesh(f.Mesh, AssetKind.StaticMesh).CopyPayload(); payload.Indices[0] = 2;
                        Check(lease.RequireMesh(f.Mesh, AssetKind.StaticMesh).CopyPayload().Indices[0] == 0);
                        Check(Task.Run(() => { try { lease.List(); return false; } catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult());
                    }
                    Check(!Locked(path));
                }
            })
        });
    }
}
