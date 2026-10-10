using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ncma.Assets;
using Ncma.Assets.Authoring;
using Ncma.Editor.Core;
using Ncma.Scene;

string suiteRoot = Path.Combine(AppContext.BaseDirectory, "asset-test-work", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(suiteRoot); // Generated test inputs stay under ignored bin/. Kept for failure diagnosis.
int serial = 0;
string TestRoot()
{
    string root = Path.Combine(suiteRoot, (++serial).ToString());
    Directory.CreateDirectory(Path.Combine(root, "assets", "角色"));
    File.WriteAllText(Path.Combine(root, "assets", "角色", "Hero.fbx"), "fixture-only, no parser");
    return root;
}
AssetRecord Record(Guid? id = null) => new(1, id ?? Guid.NewGuid(), AssetKind.Character, "assets/角色/Hero.fbx",
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("fixture-only, no parser"))), "ufbx", 1,
    new(1, 60, true), [], [], null);
JsonElement Element(object value) => JsonSerializer.SerializeToElement(value);
JsonElement Raw(string text) { using var doc = JsonDocument.Parse(text); return doc.RootElement.Clone(); }
CapabilityRequest Request(EditSession session, string capability, JsonElement input) =>
    new(2, Guid.NewGuid(), session.SessionId, session.Revision, capability, input);
JsonElement Command(AssetMetadataCommands commands, AssetRecord record, string operation = "create") => Raw(
    $$"""{"operation":"{{operation}}","path":"assets/Hero.ncmeta","expectedAssetRevision":{{commands.AssetRevision}},"record":{{Encoding.UTF8.GetString(AssetRecordCodec.Encode(record))}}} """);
CapabilityPermissions Permissions() => new([AssetMetadataCommands.CapabilityName, AssetFileCommands.CapabilityName, "ncma.history.undo", "ncma.history.redo", "ncma.scene.transaction"]);
(EditSession Session, AssetMetadataCommands Commands) Setup(string root, AssetRecord record, Func<bool>? current = null, Action<string>? fault = null)
{
    var session = new EditSession(new SceneDocument());
    var commands = new AssetMetadataCommands(new(root), new(["assets/Hero.ncmeta"], [record.SourcePath], [record.AssetId], current ?? (() => true)), fault);
    session.RegisterCommandParticipant(AssetMetadataCommands.Descriptor, commands);
    return (session, commands);
}
void Check(bool condition) { if (!condition) throw new InvalidOperationException("Assertion failed."); }
void Reject(Action action)
{
    try { action(); }
    catch (Exception e) when (e is ArgumentException or InvalidOperationException or JsonException or IOException) { return; }
    throw new InvalidOperationException("Expected rejection.");
}
AssetWriteScope FileScope(AssetRecord record, Guid copyId, Func<bool>? current = null) => new(
    ["assets/Hero.ncmeta", "assets/Copy.ncmeta"], [record.SourcePath, "assets/角色/Copy.fbx"], [record.AssetId, copyId], current ?? (() => true));
JsonElement FileCommand(AssetRevisionClock clock, Guid? copy = null, ulong generation = 1) => Element(new {
    operation = copy.HasValue ? "copy" : "move", sourceMetadata = "assets/Hero.ncmeta", destinationMetadata = "assets/Copy.ncmeta",
    destinationSource = "assets/角色/Copy.fbx", newAssetId = copy, expectedAssetRevision = clock.Revision, projectGeneration = generation });
(EditSession Session, AssetFileCommands Commands, AssetRevisionClock Clock) FileSetup(string root, AssetRecord record, Guid copyId,
    Guid projectId, Action<string>? fault = null, Func<bool>? current = null)
{
    var session = new EditSession(new SceneDocument()); var clock = new AssetRevisionClock();
    var commands = new AssetFileCommands(new(root), FileScope(record, copyId, current), projectId, 1, clock, current ?? (() => true), fault);
    session.RegisterCommandParticipant(AssetFileCommands.Descriptor, commands); return (session, commands, clock);
}

var tests = new (string Name, Action Run)[]
{
    ("M7.2 bounded material roles, packed channels, defaults and diagnostics", () => {
        var material=MaterialDefinition.Default(Guid.NewGuid());Guid color=Guid.NewGuid(),normal=Guid.NewGuid(),data=Guid.NewGuid();
        Check(material.Metallic==0&&material.Roughness==.5f&&material.BaseColor==new LinearColor(1,1,1,1));
        var orm=MaterialSurfaceContract.WithPackedSurface(material,data,PackedSurfaceLayout.OcclusionRoughnessMetallic);
        var mra=MaterialSurfaceContract.WithPackedSurface(material,data,PackedSurfaceLayout.MetallicRoughnessOcclusion);
        Check(orm.MetallicChannel==2&&orm.RoughnessChannel==1&&orm.AOChannel==0&&orm.Metallic==1&&orm.Roughness==1);
        Check(mra.MetallicChannel==0&&mra.RoughnessChannel==1&&mra.AOChannel==2);
        var full=orm with{BaseTexture=color,EmissiveTexture=color,NormalTexture=normal};
        var rows=MaterialSurfaceContract.Bindings(full);Check(rows.Length==6&&rows[5].Semantic==TextureSemantic.Color&&rows[1].Semantic==TextureSemantic.Normal&&rows[2].Channel==2);
        rows[0]=rows[0] with{AssetId=Guid.NewGuid()};Check(MaterialSurfaceContract.Bindings(full)[0].AssetId==color);
        foreach(var preset in Enum.GetValues<MaterialPreset>()) {
            var result=MaterialSurfaceContract.Apply(full,preset);Check(result.AssetId==full.AssetId&&result.TextureIds.SequenceEqual(full.TextureIds)&&result.BaseColor==full.BaseColor&&MaterialCodec.Decode(MaterialCodec.Encode(result))==result);
        }
        Check(MaterialSurfaceContract.Apply(full,MaterialPreset.Cutout).Mode==MaterialMode.AlphaMask);
        Check(MaterialSurfaceContract.Apply(full,MaterialPreset.PolishedMetal).Metallic==1&&MaterialSurfaceContract.Apply(full,MaterialPreset.Matte).Roughness==1);
        Check(MaterialSurfaceContract.Inspect(full,id=>id==color?TextureSemantic.Color:id==normal?TextureSemantic.Normal:TextureSemantic.Data,true).Length==0);
        var missing=MaterialSurfaceContract.Inspect(full,_=>null,false);Check(missing.Length==6&&missing.All(d=>d.Code=="missing_texture"));
        Check(MaterialSurfaceContract.Inspect(material,_=>throw new Exception("Empty slots must not resolve"),true).Length==0);
        Reject(()=>MaterialSurfaceContract.Inspect(full,_=>null,true));Reject(()=>MaterialSurfaceContract.Inspect(full,_=>(TextureSemantic)99,false));
        Reject(()=>MaterialSurfaceContract.Apply(full,(MaterialPreset)99));Reject(()=>MaterialSurfaceContract.Semantic(6));
        Reject(()=>MaterialSurfaceContract.WithPackedSurface(full,Guid.Empty,PackedSurfaceLayout.OcclusionRoughnessMetallic));
        Reject(()=>MaterialSurfaceContract.WithPackedSurface(full,color,PackedSurfaceLayout.OcclusionRoughnessMetallic));
        Reject(()=>MaterialSurfaceContract.WithPackedSurface(full,data,(PackedSurfaceLayout)99));
        Reject(()=>MaterialCodec.Encode(full with{NormalTexture=color}));Reject(()=>MaterialCodec.Encode(full with{BaseTexture=data}));
        Reject(()=>MaterialCodec.Decode(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(MaterialCodec.Encode(full)).Replace(normal.ToString(),color.ToString(),StringComparison.Ordinal))));
    }),
    ("M7.2 presets use original approved material transaction and reversible history", () => {
        string root=TestRoot(),path="assets/Preset.ncmaterial";Guid id=Guid.NewGuid();bool current=true;
        var clock=new AssetRevisionClock();var material=MaterialDefinition.Default(id);
        using var commands=new MaterialCommands(new(root),new(new Dictionary<string,Guid>{{path,id}},(_,_)=>false,()=>current),clock);
        var edit=new EditSession(new SceneDocument());edit.RegisterCommandParticipant(MaterialCommands.Descriptor,commands);
        var permission=new CapabilityPermissions([MaterialCommands.CapabilityName,"ncma.history.undo","ncma.history.redo"]);
        JsonElement Input(MaterialPreset preset)=>MaterialCommands.PresetInput(path,clock.Revision,material,preset);
        var request=Request(edit,MaterialCommands.CapabilityName,Input(MaterialPreset.Default));Check(edit.Invoke(request).Status=="denied");
        Check(edit.Invoke(Request(edit,MaterialCommands.CapabilityName,Input(MaterialPreset.Default)),permission).Changed);
        var stale=Input(MaterialPreset.Matte);
        Check(edit.Invoke(Request(edit,MaterialCommands.CapabilityName,Input(MaterialPreset.PolishedMetal)),permission).Changed);
        string full=Path.Combine(root,path);Check(MaterialCodec.Decode(File.ReadAllBytes(full)).Metallic==1);
        Check(edit.Invoke(Request(edit,MaterialCommands.CapabilityName,stale),permission).Status=="conflict");
        Check(edit.Invoke(Request(edit,"ncma.history.undo",Element(new{})),permission).Changed&&MaterialCodec.Decode(File.ReadAllBytes(full))==material);
        Check(edit.Invoke(Request(edit,"ncma.history.redo",Element(new{})),permission).Changed&&MaterialCodec.Decode(File.ReadAllBytes(full)).Metallic==1);
        current=false;Check(edit.Invoke(Request(edit,MaterialCommands.CapabilityName,Input(MaterialPreset.Cutout)),permission).Status=="denied");
        Check(edit.Invoke(Request(edit,"ncma.history.undo",Element(new{})),permission).Status=="denied");
        Reject(()=>MaterialCommands.PresetInput("assets/../bad.ncmaterial",0,material,MaterialPreset.Default));
        Reject(()=>MaterialCommands.PresetInput("assets/bad.ncmatset",0,material,MaterialPreset.Default));
    }),
    ("Material authoring uses exact path/UUID/dependency grants and shared Undo/Redo", () => {
        string root=TestRoot();Guid id=Guid.NewGuid(),texture=Guid.NewGuid(),setId=Guid.NewGuid();bool current=true;
        var clock=new AssetRevisionClock();var scope=new MaterialWriteScope(new Dictionary<string,Guid>{{"assets/Hero.ncmaterial",id},{"assets/Hero.ncmatset",setId}},(dep,kind)=>dep==texture&&kind==AssetKind.Texture||dep==id&&kind==AssetKind.Material,()=>current);
        using var commands=new MaterialCommands(new(root),scope,clock);var edit=new EditSession(new SceneDocument());edit.RegisterCommandParticipant(MaterialCommands.Descriptor,commands);
        var permission=new CapabilityPermissions([MaterialCommands.CapabilityName,"ncma.history.undo","ncma.history.redo"]);
        var material=MaterialDefinition.Default(id) with{BaseTexture=texture};string path="assets/Hero.ncmaterial";
        JsonElement Input(MaterialDefinition m,ulong? revision=null,string? target=null)=>Element(new{path=target??path,expectedAssetRevision=revision??clock.Revision,document=Encoding.UTF8.GetString(MaterialCodec.Encode(m))});
        Check(edit.Invoke(Request(edit,MaterialCommands.CapabilityName,Input(material))).Status=="denied");
        Check(edit.Invoke(Request(edit,MaterialCommands.CapabilityName,Input(material with{AssetId=Guid.NewGuid()})),permission).Status=="denied");
        Check(edit.Invoke(Request(edit,MaterialCommands.CapabilityName,Input(material with{NormalTexture=Guid.NewGuid()})),permission).Status=="denied");
        Check(edit.Invoke(Request(edit,MaterialCommands.CapabilityName,Input(material)),permission).Changed);
        string full=Path.Combine(root,path);Check(MaterialCodec.Decode(File.ReadAllBytes(full))==material);
        Check(edit.Invoke(Request(edit,MaterialCommands.CapabilityName,Input(material with{Metallic=.7f},0)),permission).Status=="conflict");
        Check(edit.Invoke(Request(edit,MaterialCommands.CapabilityName,Input(material with{Metallic=.7f})),permission).Changed);
        Check(edit.Invoke(Request(edit,"ncma.history.undo",Element(new{})),permission).Changed&&MaterialCodec.Decode(File.ReadAllBytes(full)).Metallic==0);
        Check(edit.Invoke(Request(edit,"ncma.history.redo",Element(new{})),permission).Changed&&MaterialCodec.Decode(File.ReadAllBytes(full)).Metallic==.7f);
        var set=new MaterialSetDefinition(1,setId,[id]);Check(edit.Invoke(Request(edit,MaterialCommands.CapabilityName,Element(new{path="assets/Hero.ncmatset",expectedAssetRevision=clock.Revision,document=Encoding.UTF8.GetString(MaterialCodec.Encode(set))})),permission).Changed);
        current=false;Check(edit.Invoke(Request(edit,"ncma.history.undo",Element(new{})),permission).Status=="denied");current=true;
        var prepared=commands.Prepare(Input(material));File.WriteAllBytes(full,MaterialCodec.Encode(material with{Metallic=.9f}));Reject(()=>commands.Validate(prepared,true));Check(MaterialCodec.Decode(File.ReadAllBytes(full)).Metallic==.9f);
    }),
    ("Material journal crash recovery is permission checked and project close revokes replay", () => {
        foreach(string failure in new[]{"journal_created","backup:0","published:0"}) {
            string root=TestRoot();Guid id=Guid.NewGuid();var definition=MaterialDefinition.Default(id);var scope=new MaterialWriteScope(new Dictionary<string,Guid>{{"assets/Hero.ncmaterial",id}},(_,_)=>false,()=>true);
            var clock=new AssetRevisionClock();ParticipantMemento prepared;
            using(var command=new MaterialCommands(new(root),scope,clock,stage=>{if(stage==failure)throw new IOException("crash");})) {
                prepared=command.Prepare(Element(new{path="assets/Hero.ncmaterial",expectedAssetRevision=clock.Revision,document=Encoding.UTF8.GetString(MaterialCodec.Encode(definition))}));Reject(()=>command.Publish(prepared,true));
            }
            using(var denied=new MaterialCommands(new(root),new MaterialWriteScope([],(_,_)=>false,()=>true),clock))Reject(()=>denied.Recover("assets/Hero.ncmaterial"));
            using(var project=new AssetProjectAuthoring(root,Guid.NewGuid(),1,new EditSession(new SceneDocument()),new AssetWriteScope([],[],[],()=>true),materialScope:scope))Check(!File.Exists(Path.Combine(root,"assets/Hero.ncmaterial.journal")));
        }
        string other=TestRoot();Guid asset=Guid.NewGuid();var edit=new EditSession(new SceneDocument());var grant=new MaterialWriteScope(new Dictionary<string,Guid>{{"assets/Hero.ncmaterial",asset}},(_,_)=>false,()=>true);
        var owner=new AssetProjectAuthoring(other,Guid.NewGuid(),1,edit,new AssetWriteScope([],[],[],()=>true),materialScope:grant);
        var permissions=new CapabilityPermissions([MaterialCommands.CapabilityName,"ncma.history.undo"]);
        Check(edit.Invoke(Request(edit,MaterialCommands.CapabilityName,Element(new{path="assets/Hero.ncmaterial",expectedAssetRevision=owner.Clock.Revision,document=Encoding.UTF8.GetString(MaterialCodec.Encode(MaterialDefinition.Default(asset)))})),permissions).Changed);
        owner.Dispose();Check(edit.Invoke(Request(edit,"ncma.history.undo",Element(new{})),permissions).Status!="ok");
    }),
    ("Strict metadata is canonical, required, bounded, closed and duplicate-free", () => {
        var r = Record(); var bytes = AssetRecordCodec.Encode(r); var loaded = AssetRecordCodec.Decode(bytes);
        Check(loaded.AssetId == r.AssetId && AssetRecordCodec.Encode(loaded).SequenceEqual(bytes));
        string text = Encoding.UTF8.GetString(bytes);
        Reject(() => AssetRecordCodec.Decode(Encoding.UTF8.GetBytes(text.Replace("\"version\":1", "\"version\":2"))));
        Reject(() => AssetRecordCodec.Decode(Encoding.UTF8.GetBytes(text.Replace("\"version\":1", "\"version\":1,\"version\":1"))));
        Reject(() => AssetRecordCodec.Decode(Encoding.UTF8.GetBytes(text.Replace("\"version\":1", "\"unknown\":1,\"version\":1"))));
        Reject(() => AssetRecordCodec.Decode(Encoding.UTF8.GetBytes(text.Replace("\"version\":1,", ""))));
        Reject(() => AssetRecordCodec.Decode(Encoding.UTF8.GetBytes(text.Replace("\"character\"", "0"))));
        Reject(() => AssetRecordCodec.Encode(r with { AssetId = Guid.Empty }));
        Reject(() => AssetRecordCodec.Encode(r with { SourceHash = new string('a', 64) }));
        Reject(() => AssetRecordCodec.Encode(r with { Settings = new(1, 121, true) }));
        Reject(() => AssetRecordCodec.Decode(new byte[AssetRecordCodec.MaxBytes + 1]));
    }),
    ("Path policy rejects traversal, Windows aliases, ADS and invalid segments", () => {
        Check(AssetPaths.Validate("assets/角色/with space.fbx") == "assets/角色/with space.fbx");
        foreach (string path in new[] { "F:/assets/a.fbx", "/assets/a.fbx", "assets/../a", "assets//a", "assets/./a", "assets/a:b", "assets/CON.txt",
            "assets/NUL", "assets/a.", "assets/a ", "assets/a\\b", "assets/a\0b", "assets/*", "Assets/a" }) Reject(() => AssetPaths.Validate(path));
    }),
    ("Catalog owns copies, rejects global UUID collisions and checks typed dependencies", () => {
        var root = Record(); Guid sub = Guid.NewGuid(), missing = Guid.NewGuid();
        var r = root with { Subassets = [new(sub, AssetKind.Clip, "take/1", "Idle", false)], Dependencies = [new(missing, AssetKind.Texture)] };
        var catalog = new AssetCatalog([r]); r.Subassets[0] = r.Subassets[0] with { Tombstone = true };
        Check(catalog.TryResolve(new(new(sub), AssetKind.Clip), out var found, out _) && found!.AssetId == root.AssetId);
        var copy = catalog.List()[0]; copy.Subassets[0] = copy.Subassets[0] with { Tombstone = true };
        Check(catalog.TryResolve(new(new(sub), AssetKind.Clip), out _, out _));
        Check(!catalog.TryResolve(new(new(sub), AssetKind.Skeleton), out _, out string mismatch) && mismatch == "asset_kind_mismatch");
        Check(catalog.Diagnostics.Single().Code == "asset_missing"); Reject(catalog.RequireDependencies);
        Reject(() => new AssetCatalog([root, root]));
        Reject(() => new AssetCatalog([catalog.List()[0], Record(sub) with { SourcePath = "assets/other.fbx" }]));
        Reject(() => new AssetCatalog([root, Record() with { SourcePath = "assets/角色/hero.fbx" }]));
        Reject(() => catalog.TryResolve(default, out _, out _));
    }),
    ("Tombstones retain identity but cannot resolve as live resources", () => {
        Guid id = Guid.NewGuid(); var r = Record() with { Subassets = [new(id, AssetKind.Clip, "take/1", "Removed", true)] };
        var catalog = new AssetCatalog([r]); Check(!catalog.TryResolve(new(new(id), AssetKind.Clip), out _, out string code) && code == "asset_tombstone");
    }),
    ("NCA explicit byte order and sorted deterministic round trip", () => {
        Guid id = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        var a = new DerivedAssetBlock(id, AssetKind.Clip, [1, 2, 3]);
        var b = new DerivedAssetBlock(Guid.Parse("11223344-4455-6677-8899-aabbccddeeff"), AssetKind.Skeleton, [4, 5]);
        byte[] bytes = DerivedAssetCodec.Encode([b, a]);
        Check(Encoding.ASCII.GetString(bytes, 0, 4) == "NCA1" && bytes.AsSpan(16, 16).SequenceEqual(Convert.FromHexString("00112233445566778899AABBCCDDEEFF")));
        Check(BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(40)) == 16 + 2 * 72);
        Check(DerivedAssetCodec.Encode([a, b]).SequenceEqual(bytes));
        var roundTrip = DerivedAssetCodec.Decode(bytes); Check(roundTrip[0].AssetId == id && roundTrip[0].Data.SequenceEqual(a.Data));
        Reject(() => DerivedAssetCodec.Encode([a, a])); Reject(() => DerivedAssetCodec.Encode([]));
    }),
    ("NCA rejects corrupt hashes, alias ranges, unknown version, truncation and overflow", () => {
        byte[] valid = DerivedAssetCodec.Encode([new(Guid.NewGuid(), AssetKind.StaticMesh, [1, 2, 3])]);
        foreach (int offset in new[] { 0, 4, 12, 32, 36, 40, 48, 56, valid.Length - 1 })
        { byte[] bad = (byte[])valid.Clone(); bad[offset] ^= 0xFF; Reject(() => DerivedAssetCodec.Decode(bad)); }
        Reject(() => DerivedAssetCodec.Decode(valid.AsSpan(0, valid.Length - 1))); Reject(() => DerivedAssetCodec.Decode([.. valid, 0]));
        byte[] range = (byte[])valid.Clone(); BinaryPrimitives.WriteUInt64LittleEndian(range.AsSpan(40), ulong.MaxValue); Reject(() => DerivedAssetCodec.Decode(range));
    }),
    ("Nonidentity TRS has one row/column conversion and rejects NaN", () => {
        var matrix = Matrix4x4.CreateScale(2, 3, 4) * Matrix4x4.CreateRotationY(0.4f) * Matrix4x4.CreateTranslation(5, 6, 7);
        var p = new Vector3(1, 2, 3); var encoded = AssetMatrices.EncodeColumnMajor(matrix); var expected = Vector3.Transform(p, matrix);
        var column = new Vector3(encoded[0] * p.X + encoded[4] * p.Y + encoded[8] * p.Z + encoded[12],
            encoded[1] * p.X + encoded[5] * p.Y + encoded[9] * p.Z + encoded[13], encoded[2] * p.X + encoded[6] * p.Y + encoded[10] * p.Z + encoded[14]);
        Check(Vector3.Distance(expected, column) < 1e-5f && AssetCoordinates.Handedness == "right");
        matrix.M12 = float.NaN; Reject(() => AssetMatrices.EncodeColumnMajor(matrix));
    }),
    ("Read-only scan survives restart/cache absence and preserves unknown source files", () => {
        string root = TestRoot(); var r = Record();
        File.WriteAllBytes(Path.Combine(root, "assets", "Hero.ncmeta"), AssetRecordCodec.Encode(r));
        File.WriteAllText(Path.Combine(root, "assets", "unknown.txt"), "keep me");
        var scanner = new AssetCatalogScanner(new(root)); var first = scanner.Scan(); var second = new AssetCatalogScanner(new(root)).Scan();
        Check(first.Valid && first.Catalog.Count == 1 && first.Catalog.List()[0].AssetId == second.Catalog.List()[0].AssetId);
        Check(File.ReadAllText(Path.Combine(root, "assets", "unknown.txt")) == "keep me" && !Directory.Exists(Path.Combine(root, "out")));
        var missing = r with { SourcePath = "assets/missing.fbx" }; File.WriteAllBytes(Path.Combine(root, "assets", "Hero.ncmeta"), AssetRecordCodec.Encode(missing));
        var scan = scanner.Scan(); Check(scan.Diagnostics.Any(d => d.Code == "source_missing") && scan.Catalog.List()[0].AssetId == r.AssetId);
    }),
    ("Scanner rejects bad descriptors, duplicate identities and path casing without writing", () => {
        string root = TestRoot(); var r = Record(); var paths = new AssetProjectPaths(root);
        Reject(() => paths.Resolve("assets/角色/hero.fbx", true));
        File.WriteAllBytes(Path.Combine(root, "assets", "one.ncmeta"), AssetRecordCodec.Encode(r));
        File.WriteAllBytes(Path.Combine(root, "assets", "two.ncmeta"), AssetRecordCodec.Encode(r));
        Reject(() => new AssetCatalogScanner(paths).Scan());
        Check(File.Exists(Path.Combine(root, "assets", "one.ncmeta")) && File.Exists(Path.Combine(root, "assets", "two.ncmeta")));
    }),
    ("Filesystem links are rejected by both resolution and recursive scan", () => {
        string root = TestRoot(); string target = Path.Combine(root, "owned-link-target"); Directory.CreateDirectory(target);
        string link = Path.Combine(root, "assets", "linked");
        if (OperatingSystem.IsWindows())
        {
            var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string arg in new[] { "/d", "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start junction fixture.");
            string stdout = process.StandardOutput.ReadToEnd(), stderr = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(5000) || process.ExitCode != 0) throw new InvalidOperationException(stdout + stderr);
        }
        else Directory.CreateSymbolicLink(link, target);
        var paths = new AssetProjectPaths(root);
        Reject(() => paths.Resolve("assets/linked/file.fbx")); Reject(() => new AssetCatalogScanner(paths).Scan());
        Check(Directory.Exists(target)); // Only links to this generated fixture, never to user data. No recursive cleanup.
    }),
    ("Watcher queues bounded generation-tagged observations without creating metadata", () => {
        string root = TestRoot(); var paths = new AssetProjectPaths(root); Guid projectId = Guid.NewGuid();
        using var queue = new AssetChangeQueue(projectId, 1);
        Parallel.For(0, 1024, i => queue.Observe("assets/change" + i + ".fbx", "changed"));
        var batch = queue.Drain(); Check(batch.Items.Length == AssetChangeQueue.Capacity && batch.RequiresRescan);
        Check(batch.Items.All(i => i.ProjectId == projectId && i.Generation == 1));
        Check(queue.Drain().Items.Length == 0 && !queue.Drain().RequiresRescan);
        Reject(() => queue.Observe("assets/../escape", "changed"));
        queue.Start(paths); string observed = Path.Combine(root, "assets", "observed.txt"); File.WriteAllText(observed, "observe only");
        var seen = new List<AssetChangeIntent>();
        bool received = SpinWait.SpinUntil(() => { seen.AddRange(queue.Drain().Items); return seen.Any(i => i.Path == "assets/observed.txt"); }, 3000);
        Check(received && !Directory.EnumerateFiles(Path.Combine(root, "assets"), "*.ncmeta").Any());
        queue.Dispose(); queue.Observe("assets/late.fbx", "created"); Check(queue.Drain().Items.Length == 0);
    }),
    ("Metadata participant shares sole scene history and restores settings/create", () => {
        string root = TestRoot(); var r = Record(); var (session, commands) = Setup(root, r);
        Check(session.Invoke(Request(session, AssetMetadataCommands.CapabilityName, Command(commands, r)), Permissions()).Changed);
        Check(session.State.UndoCount == 1 && commands.AssetRevision == 1);
        var changed = r with { Settings = new(1, 30, false) };
        Check(session.Invoke(Request(session, AssetMetadataCommands.CapabilityName, Command(commands, changed, "settings")), Permissions()).Changed);
        Check(session.State.UndoCount == 2 && commands.AssetRevision == 2);
        Check(session.Invoke(Request(session, "ncma.history.undo", Raw("{}")), Permissions()).Changed);
        Check(AssetRecordCodec.Decode(File.ReadAllBytes(Path.Combine(root, "assets", "Hero.ncmeta"))).Settings.SampleRate == 60);
        Check(session.Invoke(Request(session, "ncma.history.undo", Raw("{}")), Permissions()).Changed);
        Check(!File.Exists(Path.Combine(root, "assets", "Hero.ncmeta")) && File.Exists(Path.Combine(root, "assets", "角色", "Hero.fbx")));
        Check(session.Invoke(Request(session, "ncma.history.redo", Raw("{}")), Permissions()).Changed);
        Check(session.Invoke(Request(session, "ncma.history.redo", Raw("{}")), Permissions()).Changed);
        Check(new AssetCatalogScanner(new(root)).Scan().Catalog.List()[0].AssetId == r.AssetId);
    }),
    ("Scene and asset commands interleave in one history without stale runtime identities", () => {
        var r = Record(); var (session, commands) = Setup(TestRoot(), r); Guid id = Guid.NewGuid();
        var create = Element(new { operations = new[] { new { op = "create", objectId = id, name = "Object" } } });
        Check(session.Invoke(Request(session, "ncma.scene.transaction", create), Permissions()).Changed);
        Check(session.Invoke(Request(session, AssetMetadataCommands.CapabilityName, Command(commands, r)), Permissions()).Changed);
        Check(session.State.UndoCount == 2 && session.Document.World.GetObjects().Count() == 1);
        Check(session.Invoke(Request(session, "ncma.history.undo", Raw("{}")), Permissions()).Changed);
        Check(session.Document.World.GetObjects().Count() == 1);
        Check(session.Invoke(Request(session, "ncma.history.undo", Raw("{}")), Permissions()).Changed);
        Check(session.Document.World.GetObjects().Count() == 0);
    }),
    ("Default read-only, stale revisions, frozen session, changed UUID and grants reject", () => {
        string root = TestRoot(); var r = Record(); bool current = true; var (session, commands) = Setup(root, r, () => current);
        var request = Request(session, AssetMetadataCommands.CapabilityName, Command(commands, r));
        Check(session.Invoke(request).Status == "denied");
        session.SetFrozen(true); Check(session.Invoke(request, Permissions()).Code == "play_frozen"); session.SetFrozen(false);
        var stale = Raw(Command(commands, r).GetRawText().Replace("\"expectedAssetRevision\":0", "\"expectedAssetRevision\":1"));
        Check(session.Invoke(Request(session, AssetMetadataCommands.CapabilityName, stale), Permissions()).Code == "asset_revision_conflict");
        Check(session.Invoke(request, Permissions()).Changed);
        var different = r with { AssetId = Guid.NewGuid() };
        Check(!session.Invoke(Request(session, AssetMetadataCommands.CapabilityName, Command(commands, different, "settings")), Permissions()).Changed);
        current = false;
        Check(session.Invoke(request, Permissions()).Code == "asset_scope_denied");
        Check(session.Invoke(Request(session, "ncma.history.undo", Raw("{}")), Permissions()).Code == "asset_scope_denied" && session.State.UndoCount == 1);
    }),
    ("External metadata edits conflict with Undo and do not get overwritten", () => {
        string root = TestRoot(); var r = Record(); var (session, commands) = Setup(root, r);
        Check(session.Invoke(Request(session, AssetMetadataCommands.CapabilityName, Command(commands, r)), Permissions()).Changed);
        byte[] external = AssetRecordCodec.Encode(r with { Settings = new(1, 20, false) }); File.WriteAllBytes(Path.Combine(root, "assets", "Hero.ncmeta"), external);
        var result = session.Invoke(Request(session, "ncma.history.undo", Raw("{}")), Permissions());
        Check(result.Status == "conflict" && result.Code == "asset_file_conflict" && !result.Changed && session.State.UndoCount == 1 && File.ReadAllBytes(Path.Combine(root, "assets", "Hero.ncmeta")).SequenceEqual(external));
    }),
    ("Each metadata publish failure compensates without partial World/history", () => {
        foreach (string failure in new[] { "journal_created", "metadata_published" })
        {
            string root = TestRoot(); var r = Record(); var (session, commands) = Setup(root, r, fault: stage => { if (stage == failure) throw new IOException("injected"); });
            ulong revision = session.Revision; byte[] before = session.Document.CaptureBytes();
            var result = session.Invoke(Request(session, AssetMetadataCommands.CapabilityName, Command(commands, r)), Permissions());
            Check(!result.Changed && session.State.UndoCount == 0 && session.Revision == revision && session.Document.CaptureBytes().SequenceEqual(before));
            Check(!File.Exists(Path.Combine(root, "assets", "Hero.ncmeta")) && !File.Exists(Path.Combine(root, "assets", "Hero.ncmeta.journal")));
        }
    }),
    ("Restart recovery restores previous metadata and rejects unknown journal targets", () => {
        string root = TestRoot(); var r = Record(); var (session, commands) = Setup(root, r, fault: stage => { if (stage == "metadata_published") throw new IOException("crash fixture"); });
        var memento = commands.Prepare(Command(commands, r)); Reject(() => commands.Publish(memento, true));
        Check(File.Exists(Path.Combine(root, "assets", "Hero.ncmeta.journal")) && File.Exists(Path.Combine(root, "assets", "Hero.ncmeta")));
        Reject(() => new AssetCatalogScanner(new(root)).Scan());
        Reject(() => commands.Prepare(Command(commands, r)));
        commands.Dispose(); // Process exit closes file/directory leases, but leaves the durable journal.
        var (_, restarted) = Setup(root, r); restarted.Recover("assets/Hero.ncmeta");
        Check(!File.Exists(Path.Combine(root, "assets", "Hero.ncmeta")) && !File.Exists(Path.Combine(root, "assets", "Hero.ncmeta.journal")));
        Reject(() => restarted.Recover("assets/other.ncmeta"));
    }),
    ("Completed request replay is idempotent and revoked grants deny cached history", () => {
        string root = TestRoot(); var r = Record(); bool current = true; var (session, commands) = Setup(root, r, () => current);
        var request = Request(session, AssetMetadataCommands.CapabilityName, Command(commands, r));
        var first = session.Invoke(request, Permissions()); Check(first.Changed);
        var replay = session.Invoke(request, Permissions()); Check(replay.Replayed && commands.AssetRevision == 1 && session.State.UndoCount == 1);
        var schema = AssetMetadataCommands.Descriptor.OutputSchema;
        var allowedData = schema.GetProperty("properties").GetProperty("data").GetProperty("properties");
        foreach (var property in replay.Data.EnumerateObject()) Check(allowedData.TryGetProperty(property.Name, out _));
        Check(replay.Data.GetProperty("execution").GetProperty("history").GetProperty("undoCount").GetInt32() == 1);
        var historySchema = schema.GetProperty("$defs").GetProperty("history");
        foreach (var field in historySchema.GetProperty("required").EnumerateArray()) Check(replay.Data.GetProperty("history").TryGetProperty(field.GetString()!, out _));
        var undoRequest = Request(session, "ncma.history.undo", Raw("{}")); Check(session.Invoke(undoRequest, Permissions()).Changed);
        current = false; Check(session.Invoke(undoRequest, Permissions()).Code == "asset_scope_denied");
        Check(!File.Exists(Path.Combine(root, "assets", "Hero.ncmeta")));
    }),
    ("Revocation at publish compensates without installing World or history", () => {
        string root = TestRoot(); var r = Record(); bool current = true;
        var (session, commands) = Setup(root, r, () => current, stage => { if (stage == "metadata_published") current = false; });
        var permissions = new CapabilityPermissions([AssetMetadataCommands.CapabilityName], isCurrent: () => current);
        ulong revision = session.Revision;
        Check(!session.Invoke(Request(session, AssetMetadataCommands.CapabilityName, Command(commands, r)), permissions).Changed);
        Check(session.Revision == revision && session.State.UndoCount == 0 && !File.Exists(Path.Combine(root, "assets", "Hero.ncmeta")));
    }),
    ("Participant cannot mutate live World from prepare and requires owner-thread calls", () => {
        var session = new EditSession(new SceneDocument());
        var participant = new ProbeParticipant(() => session.Document.World.CreateObject("forbidden"));
        session.RegisterCommandParticipant(AssetMetadataCommands.Descriptor, participant);
        Check(!session.Invoke(Request(session, AssetMetadataCommands.CapabilityName, Raw("{}")), Permissions()).Changed);
        Check(session.Document.World.GetObjects().Count() == 0 && session.State.UndoCount == 0);
        var r = Record(); var (_, commands) = Setup(TestRoot(), r);
        Task.Run(() => Reject(() => commands.Prepare(Command(commands, r)))).GetAwaiter().GetResult();
    }),
    ("Participant mementos share history limits and bounded retry retention", () => {
        var session = new EditSession(new SceneDocument()); var participant = new ProbeParticipant(bytes: 400000);
        session.RegisterCommandParticipant(AssetMetadataCommands.Descriptor, participant);
        CapabilityRequest first = Request(session, AssetMetadataCommands.CapabilityName, Raw("{}"));
        Check(session.Invoke(first, Permissions()).Changed);
        for (int i = 0; i < 64; i++) Check(session.Invoke(Request(session, AssetMetadataCommands.CapabilityName, Raw("{}")), Permissions()).Changed);
        Check(session.State.UndoCount < EditSession.MaxHistoryEntries && session.State.UndoCount > 0 && !session.CachedRequestExists(first.RequestId));
        Check(session.Invoke(first, Permissions()).Code == "revision_conflict");
    }),
    ("Compensation failure freezes session and requires explicit journal recovery", () => {
        string root = TestRoot(); var r = Record(); FileStream? journalLock = null; AssetMetadataCommands? crashed = null;
        var (session, commands) = Setup(root, r, fault: stage => {
            if (stage == "metadata_published")
            {
                crashed!.Dispose(); // Simulate a stopped publisher before a different process locks the journal.
                journalLock = new(Path.Combine(root, "assets", "Hero.ncmeta.journal"), FileMode.Open, FileAccess.Read, FileShare.Read);
                throw new IOException("injected lock");
            }
        });
        crashed = commands;
        try
        {
            Check(!session.Invoke(Request(session, AssetMetadataCommands.CapabilityName, Command(commands, r)), Permissions()).Changed);
            Check(session.State.Frozen && session.State.HistoryInvalidated);
        }
        finally { journalLock?.Dispose(); }
        commands.Recover("assets/Hero.ncmeta"); session.SetFrozen(false); session.Resynchronize();
        Check(!session.State.Frozen && !session.State.HistoryInvalidated && !File.Exists(Path.Combine(root, "assets", "Hero.ncmeta")));
    }),
    ("Move preserves asset/subasset identity and shares Undo/Redo with scene commands", () => {
        string root = TestRoot(); Guid copy = Guid.NewGuid(), project = Guid.NewGuid();
        var r = Record() with { Subassets = [new(Guid.NewGuid(), AssetKind.Clip, "take/0", "Idle", false)],
            Generation = new(1, new string('A', 64), "out/assets/" + project.ToString("N") + "/hero.nca") };
        File.WriteAllBytes(Path.Combine(root, "assets/Hero.ncmeta"), AssetRecordCodec.Encode(r));
        var (session, commands, clock) = FileSetup(root, r, copy, project); using var cleanup = commands;
        Check(session.Invoke(Request(session, AssetFileCommands.CapabilityName, FileCommand(clock)), Permissions()).Changed);
        var moved = new AssetCatalogScanner(new(root)).Scan().Catalog.List()[0];
        Check(moved.AssetId == r.AssetId && moved.Subassets[0].AssetId == r.Subassets[0].AssetId && moved.Generation == r.Generation);
        Check(!File.Exists(Path.Combine(root, r.SourcePath)) && File.Exists(Path.Combine(root, "assets/角色/Copy.fbx")));
        Check(session.Invoke(Request(session, "ncma.history.undo", Raw("{}")), Permissions()).Changed);
        Check(File.Exists(Path.Combine(root, r.SourcePath)) && !File.Exists(Path.Combine(root, "assets/Copy.ncmeta")));
        Check(session.Invoke(Request(session, "ncma.history.redo", Raw("{}")), Permissions()).Changed && clock.Revision == 3);
    }),
    ("Copy remaps subasset/internal dependency UUIDs and does not modify the original source", () => {
        string root = TestRoot(); Guid clip = Guid.NewGuid(), copy = Guid.NewGuid(), project = Guid.NewGuid();
        var r = Record() with { Subassets = [new(clip, AssetKind.Clip, "take/0", "Idle", false)], Dependencies = [new(clip, AssetKind.Clip)] };
        byte[] original = AssetRecordCodec.Encode(r); File.WriteAllBytes(Path.Combine(root, "assets/Hero.ncmeta"), original);
        DateTime stamp = File.GetLastWriteTimeUtc(Path.Combine(root, r.SourcePath));
        var (session, commands, clock) = FileSetup(root, r, copy, project); using var cleanup = commands;
        Check(session.Invoke(Request(session, AssetFileCommands.CapabilityName, FileCommand(clock, copy)), Permissions()).Changed);
        var copied = AssetRecordCodec.Decode(File.ReadAllBytes(Path.Combine(root, "assets/Copy.ncmeta")));
        Check(copied.AssetId == copy && copied.Subassets[0].AssetId != clip && copied.Dependencies[0].AssetId == copied.Subassets[0].AssetId);
        Check(File.ReadAllBytes(Path.Combine(root, "assets/Hero.ncmeta")).SequenceEqual(original) && stamp == File.GetLastWriteTimeUtc(Path.Combine(root, r.SourcePath)));
        Check(session.Invoke(Request(session, "ncma.history.undo", Raw("{}")), Permissions()).Changed);
        Check(!File.Exists(Path.Combine(root, "assets/角色/Copy.fbx")) && File.Exists(Path.Combine(root, r.SourcePath)));
        Check(session.Invoke(Request(session, "ncma.history.redo", Raw("{}")), Permissions()).Changed);
        Check(AssetRecordCodec.Decode(File.ReadAllBytes(Path.Combine(root, "assets/Copy.ncmeta"))).Subassets[0].AssetId == copied.Subassets[0].AssetId);
    }),
    ("Every multi-file publication boundary compensates without partial history", () => {
        foreach (string operation in new[] { "move", "copy" })
        foreach (string failure in new[] { "journal_created", "backup:0", "published:0", "backup:1", "published:1", "backup:2", "published:2", "backup:3", "published:3", "metadata_published" })
        {
            // Copy skips unchanged original images, so those publication hooks intentionally do not run.
            if (operation == "copy" && (failure.EndsWith(":0") || failure.EndsWith(":1"))) continue;
            string root = TestRoot(); var r = Record(); byte[] original = AssetRecordCodec.Encode(r);
            File.WriteAllBytes(Path.Combine(root, "assets/Hero.ncmeta"), original);
            Guid copy = Guid.NewGuid(); var (session, commands, clock) = FileSetup(root, r, copy, Guid.NewGuid(), stage => { if (stage == failure) throw new IOException("injected " + failure); });
            using var cleanup = commands; ulong revision = session.Revision;
            var result = session.Invoke(Request(session, AssetFileCommands.CapabilityName, FileCommand(clock, operation == "copy" ? copy : null)), Permissions());
            Check(!result.Changed && !session.State.Frozen && session.State.UndoCount == 0 && session.Revision == revision);
            Check(File.ReadAllBytes(Path.Combine(root, "assets/Hero.ncmeta")).SequenceEqual(original) && File.Exists(Path.Combine(root, r.SourcePath)));
            Check(!File.Exists(Path.Combine(root, "assets/Copy.ncmeta")) && !File.Exists(Path.Combine(root, "assets/角色/Copy.fbx")));
            Check(!Directory.EnumerateFiles(Path.Combine(root, "assets"), "*.journal", SearchOption.AllDirectories).Any());
        }
    }),
    ("Startup recovery restores interrupted move/copy under exact host grants", () => {
        foreach (string operation in new[] { "move", "copy" })
        foreach (string failure in new[] { "journal_created", "published:2", "published:3" })
        {
            string root = TestRoot(); var r = Record(); Guid copy = Guid.NewGuid(), project = Guid.NewGuid();
            File.WriteAllBytes(Path.Combine(root, "assets/Hero.ncmeta"), AssetRecordCodec.Encode(r));
            var (_, commands, clock) = FileSetup(root, r, copy, project, stage => { if (stage == failure) throw new IOException("crash fixture"); });
            var memento = commands.Prepare(FileCommand(clock, operation == "copy" ? copy : null)); Reject(() => commands.Publish(memento, true)); commands.Dispose();
            using var restarted = new AssetProjectAuthoring(root, project, 2, new(new SceneDocument()), FileScope(r, copy));
            Check(restarted.Snapshot.Catalog.Count == 1 && restarted.Snapshot.Catalog.List()[0].AssetId == r.AssetId && File.Exists(Path.Combine(root, r.SourcePath)));
            Check(!File.Exists(Path.Combine(root, "assets/Copy.ncmeta")) && !File.Exists(Path.Combine(root, "assets/角色/Copy.fbx")));
        }
    }),
    ("Project owner is unique, refresh is generation-checked, closing revokes history", () => {
        string root = TestRoot(); var r = Record(); var session = new EditSession(new SceneDocument()); Guid project = Guid.NewGuid();
        using var owner = new AssetProjectAuthoring(root, project, 7, session, FileScope(r, Guid.NewGuid()));
        Reject(() => new AssetProjectAuthoring(root, Guid.NewGuid(), 8, new(new SceneDocument()), FileScope(r, Guid.NewGuid())));
        Check(owner.Refresh(7, true) && owner.Clock.Revision == 1); Reject(() => owner.Refresh(6, true));
        Check(session.Invoke(Request(session, AssetMetadataCommands.CapabilityName, Command(owner.Metadata, r)), Permissions()).Changed);
        Check(owner.Metadata.AssetRevision == owner.Clock.Revision);
        owner.Dispose(); Check(session.Invoke(Request(session, "ncma.history.undo", Raw("{}")), Permissions()).Code == "asset_scope_denied");
        using var reopened = new AssetProjectAuthoring(root, project, 8, new(new SceneDocument()), FileScope(r, Guid.NewGuid()));
        Check(reopened.Snapshot.Catalog.List()[0].AssetId == r.AssetId);
    }),
    ("External source edits and occupied destinations are never overwritten by history", () => {
        string root = TestRoot(); var r = Record(); Guid copy = Guid.NewGuid();
        File.WriteAllBytes(Path.Combine(root, "assets/Hero.ncmeta"), AssetRecordCodec.Encode(r));
        var (session, commands, clock) = FileSetup(root, r, copy, Guid.NewGuid()); using var cleanup = commands;
        File.WriteAllText(Path.Combine(root, "assets/角色/Copy.fbx"), "foreign");
        Check(!session.Invoke(Request(session, AssetFileCommands.CapabilityName, FileCommand(clock)), Permissions()).Changed);
        Check(File.ReadAllText(Path.Combine(root, "assets/角色/Copy.fbx")) == "foreign");
        File.Delete(Path.Combine(root, "assets/角色/Copy.fbx")); // Exact generated fixture only.
        Check(session.Invoke(Request(session, AssetFileCommands.CapabilityName, FileCommand(clock)), Permissions()).Changed);
        File.WriteAllText(Path.Combine(root, "assets/角色/Copy.fbx"), "external changed source");
        Check(session.Invoke(Request(session, "ncma.history.undo", Raw("{}")), Permissions()).Code == "asset_file_conflict");
        Check(File.ReadAllText(Path.Combine(root, "assets/角色/Copy.fbx")) == "external changed source");
    }),
    ("Publication blocks concurrent writers and parent replacement; foreign occupants require recovery", () => {
        string root = TestRoot(); var r = Record(); var bytes = AssetRecordCodec.Encode(r);
        File.WriteAllBytes(Path.Combine(root, "assets/Hero.ncmeta"), bytes);
        bool writerBlocked = false, parentBlocked = false;
        var (session, commands) = Setup(root, r, fault: stage => {
            if (stage == "journal_created")
            {
                try { using var write = new FileStream(Path.Combine(root, "assets/Hero.ncmeta"), FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete); }
                catch (IOException) { writerBlocked = true; }
                try { Directory.Move(Path.Combine(root, "assets"), Path.Combine(root, "assets-old")); }
                catch (IOException) { parentBlocked = true; }
            }
            if (stage == "backup:0") File.WriteAllBytes(Path.Combine(root, "assets/Hero.ncmeta"), bytes); // New identity with identical bytes.
        });
        var changed = r with { Settings = new(1, 30, true) };
        Check(!session.Invoke(Request(session, AssetMetadataCommands.CapabilityName, Command(commands, changed, "settings")), Permissions()).Changed);
        Check(writerBlocked && parentBlocked && session.State.Frozen && session.State.HistoryInvalidated);
        Reject(() => commands.Recover("assets/Hero.ncmeta"));
        Check(File.ReadAllBytes(Path.Combine(root, "assets/Hero.ncmeta")).SequenceEqual(bytes));
        File.Delete(Path.Combine(root, "assets/Hero.ncmeta")); // Explicitly remove only this test-created foreign occupant.
        commands.Recover("assets/Hero.ncmeta"); Check(File.ReadAllBytes(Path.Combine(root, "assets/Hero.ncmeta")).SequenceEqual(bytes));
    }),
    ("Copy/move reject stale generation, revoked grants and oversized sources", () => {
        string root = TestRoot(); var r = Record(); Guid copy = Guid.NewGuid(); bool current = true;
        File.WriteAllBytes(Path.Combine(root, "assets/Hero.ncmeta"), AssetRecordCodec.Encode(r));
        var (session, commands, clock) = FileSetup(root, r, copy, Guid.NewGuid(), current: () => current); using var cleanup = commands;
        Check(session.Invoke(Request(session, AssetFileCommands.CapabilityName, FileCommand(clock, copy, 2)), Permissions()).Code == "asset_project_stale");
        current = false; Check(session.Invoke(Request(session, AssetFileCommands.CapabilityName, FileCommand(clock, copy)), Permissions()).Status == "denied");
        current = true;
        using (var source = new FileStream(Path.Combine(root, r.SourcePath), FileMode.Open, FileAccess.Write)) source.SetLength(256L * 1024 * 1024 + 1);
        Check(!session.Invoke(Request(session, AssetFileCommands.CapabilityName, FileCommand(clock, copy)), Permissions()).Changed && session.State.UndoCount == 0);
    }),
    ("Post-install completion failure freezes; committed restart only completes cleanup", () => {
        foreach (string stage in new[] { "completion_ready", "commit_decided" })
        {
            string root = TestRoot(); var r = Record(); var (session, commands) = Setup(root, r, fault: value => { if (value == stage) throw new IOException("completion failure"); });
            ulong previous = session.Revision;
            Check(!session.Invoke(Request(session, AssetMetadataCommands.CapabilityName, Command(commands, r)), Permissions()).Changed);
            Check(session.State.Frozen && session.State.HistoryInvalidated && session.Revision > previous);
            commands.Dispose();
            if (stage == "commit_decided")
            {
                var (_, restarted) = Setup(root, r); restarted.Recover("assets/Hero.ncmeta");
                Check(File.Exists(Path.Combine(root, "assets/Hero.ncmeta")) && !File.Exists(Path.Combine(root, "assets/Hero.ncmeta.journal")));
            }
            else Check(!File.Exists(Path.Combine(root, "assets/Hero.ncmeta")));
        }
    }),
    ("Recovery rejects corrupt/duplicate journal fields and preserves the original journal", () => {
        string root = TestRoot(); var r = Record(); var (_, commands) = Setup(root, r, fault: stage => { if (stage == "journal_created") throw new IOException("crash"); });
        var memento = commands.Prepare(Command(commands, r)); Reject(() => commands.Publish(memento, true)); commands.Dispose();
        string path = Path.Combine(root, "assets/Hero.ncmeta.journal"); string journal = File.ReadAllText(path);
        File.WriteAllText(path, journal.Replace("\"Version\":1", "\"Version\":1,\"Version\":1"));
        var (_, recovery) = Setup(root, r); Reject(() => recovery.Recover("assets/Hero.ncmeta"));
        Check(File.ReadAllText(path).Contains("\"Version\":1,\"Version\":1"));
        File.WriteAllText(path, journal); recovery.Recover("assets/Hero.ncmeta"); Check(!File.Exists(path));
    }),
    ("Frozen format schemas agree with encoded metadata and NCA limits", () => {
        var assembly = typeof(AssetRecord).Assembly;
        using var metadataStream = assembly.GetManifestResourceStream("Ncma.Assets.Schemas.ncmeta-v1.schema.json")!;
        using var schema = JsonDocument.Parse(metadataStream); using var record = JsonDocument.Parse(AssetRecordCodec.Encode(Record()));
        Check(schema.RootElement.GetProperty("required").EnumerateArray().Select(e => e.GetString()).Order()
            .SequenceEqual(record.RootElement.EnumerateObject().Select(p => p.Name).Order()));
        // The new Environment kind is an independent ncenv/NCE/NCP contract, not ncmeta/NCA.
        string[] frozenKinds = ["character", "staticMesh", "skinnedMesh", "skeleton", "clip", "texture", "material", "materialSet", "prefab", "overrideSet", "animationGraph"];
        Check(schema.RootElement.GetProperty("$defs").GetProperty("kind").GetProperty("enum").EnumerateArray().Select(e => e.GetString()).SequenceEqual(frozenKinds));
        Check(Enum.GetValues<AssetKind>().Where(AssetRecordCodec.SupportsKind).Select(k => System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(k.ToString())).SequenceEqual(frozenKinds));
        Reject(() => AssetRecordCodec.Encode(Record() with { Kind = AssetKind.Environment }));
        Reject(() => AssetRecordCodec.Encode(Record() with { Subassets = [new(Guid.NewGuid(), AssetKind.Environment, "environment", "Environment", false)] }));
        Reject(() => AssetRecordCodec.Encode(Record() with { Dependencies = [new(Guid.NewGuid(), AssetKind.Environment)] }));
        using var layoutStream = assembly.GetManifestResourceStream("Ncma.Assets.Schemas.nca-v1.layout.json")!;
        using var layout = JsonDocument.Parse(layoutStream);
        Check(layout.RootElement.GetProperty("headerBytes").GetInt32() == DerivedAssetCodec.HeaderBytes && layout.RootElement.GetProperty("entryBytes").GetInt32() == DerivedAssetCodec.EntryBytes);
        Check(layout.RootElement.GetProperty("limits").GetProperty("containerBytes").GetInt32() == DerivedAssetCodec.MaxBytes);
        Reject(() => AssetRecordCodec.Encode(Record() with { Generation = new(1, new string('A', 64), "out/assets/not-a-project/hero.nca") }));
    }),
    ("Total descriptor byte budget rejects a scan atomically", () => {
        string root = TestRoot(); byte[] descriptor = AssetRecordCodec.Encode(Record());
        byte[] padded = new byte[AssetRecordCodec.MaxBytes]; Array.Fill(padded, (byte)' '); descriptor.CopyTo(padded, 0);
        for (int i = 0; i < 17; i++) File.WriteAllBytes(Path.Combine(root, "assets/large-" + i + ".ncmeta"), padded);
        Reject(() => new AssetCatalogScanner(new(root)).Scan());
        Check(Directory.EnumerateFiles(Path.Combine(root, "assets"), "*.ncmeta").Count() == 17);
    }),
    ("Scan entry budget is enforced without deleting unknown files", () => {
        string root = TestRoot(); string directory = Path.Combine(root, "assets/角色");
        for (int i = 0; i < AssetCatalogScanner.MaxEntries; i++) using (File.Create(Path.Combine(directory, "unknown-" + i + ".txt"))) { }
        Reject(() => new AssetCatalogScanner(new(root)).Scan());
        Check(File.Exists(Path.Combine(directory, "unknown-16383.txt")));
    }),
    ("Redo branching drops asset future and participant registration stays trusted startup", () => {
        var r = Record(); var (session, commands) = Setup(TestRoot(), r);
        Check(session.Invoke(Request(session, AssetMetadataCommands.CapabilityName, Command(commands, r)), Permissions()).Changed);
        Reject(() => session.RegisterCommandParticipant(AssetMetadataCommands.Descriptor, commands));
        Check(session.Invoke(Request(session, "ncma.history.undo", Raw("{}")), Permissions()).Changed);
        var op = Element(new { operations = new[] { new { op = "create", objectId = Guid.NewGuid(), name = "New branch" } } });
        Check(session.Invoke(Request(session, "ncma.scene.transaction", op), Permissions()).Changed && session.State.RedoCount == 0);
    })
};
int failed = 0;
var suiteClock = Stopwatch.StartNew();
foreach (var test in tests)
{
    var caseClock = Stopwatch.StartNew();
    try { test.Run(); Console.WriteLine($"PASS {test.Name} ({caseClock.Elapsed.TotalMilliseconds:F1} ms)"); }
    catch (Exception error) { failed++; Console.Error.WriteLine("FAIL " + test.Name + ": " + error); }
}
Console.WriteLine($"Assets: {tests.Length - failed}/{tests.Length} passed in {suiteClock.Elapsed.TotalSeconds:F3}s. Fixtures: {suiteRoot}");
return failed == 0 ? 0 : 1;

sealed class ProbeParticipant(Action? prepare = null, int bytes = 1) : IEditCommandParticipant
{
    public ParticipantMemento Prepare(JsonElement input)
    {
        prepare?.Invoke(); var after = new byte[bytes]; after[0] = 1; return new(new byte[bytes], after);
    }
    public void Authorize(ParticipantMemento memento) { }
    public void Validate(ParticipantMemento memento, bool forward) { }
    public void Publish(ParticipantMemento memento, bool forward) { }
    public void Compensate(ParticipantMemento memento, bool forward) { }
}
