using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Ncma;
using Ncma.Runtime;
namespace Ncma.ManagedHost;

public static unsafe partial class NativeEntry
{
    private const int SceneLimit = 4 * 1024 * 1024;
    private sealed record PropertyData(string Name, uint Kind, double Value);
    private sealed record BindingData(Guid Id, string TypeName, bool Enabled, PropertyData[] Properties);
    private sealed record ObjectData(Guid Id, string Name, bool HasTransform, Transform Transform, BindingData[] Bindings);
    private sealed record SceneData(string Name, ObjectData[] Objects);
    // Host ownership/lifecycle wrapper only. Document rules live in Ncma.Scene.
    private sealed class SceneSession
    {
        public readonly Ncma.Editor.Services.EditorSessionOwner Owner;
        public Ncma.Scene.SceneDocument Document => Owner.Document;
        public SceneWorld World => Owner.Facade;
        public Ncma.Editor.Core.EditSession? Editor => Owner.Edit;
        public Ncma.Editor.Transport.EditorEndpoint? Endpoint => Owner.Endpoint;
        public SceneSession(string name)
        {
            Owner = new(name, s_catalog, activateEditor: false, components: Ncma.Rendering.RenderConfiguration.CreateRegistry());
        }
        public void Verify() { Document.VerifyAccess(); World.Verify(); }
        public SceneData CaptureView()
        {
            var snapshot = Document.CaptureSnapshot();
            return new(snapshot.Name, snapshot.Objects.Select(o => {
                var item = Document.World.FindObject(o.Id);
                bool spatial = item.Has<TransformData>();
                return new ObjectData(o.Id, o.Name, spatial,
                    SceneWorld.FromData(spatial ? item.Get<TransformData>() : TransformData.Identity),
                    o.Behaviours.Select(b => new BindingData(b.Id, b.TypeName, b.Enabled,
                        b.Exports.Select(p => new PropertyData(p.Name, (uint)p.Kind, p.Value)).ToArray())).ToArray());
            }).ToArray());
        }
        public void SetBindings(GameObject target, BindingData[] values)
        {
            var id = SceneWorld.ToGuid(target.PersistentId.High, target.PersistentId.Low);
            Document.SetBindings(id, values.Select(b => {
                return new Ncma.Scene.BehaviourBindingData(b.Id, b.TypeName, b.Enabled,
                    b.Properties.Select(p => new Ncma.Scene.ExportData(p.Name, (Ncma.Scene.ExportKind)p.Kind, p.Value)).ToArray());
            }).ToArray());
        }
        public BindingData[] GetBindings(GameObject target) => Document.GetBindings(SceneWorld.ToGuid(target.PersistentId.High, target.PersistentId.Low))
            .Select(b => new BindingData(b.Id, b.TypeName, b.Enabled,
                b.Exports.Select(p => new PropertyData(p.Name, (uint)p.Kind, p.Value)).ToArray())).ToArray();
    }
    private static readonly Dictionary<ulong, SceneSession> s_scenes = [];
    private static readonly object s_sceneLock = new();
    private static ulong s_nextScene = 1;
    private static SceneSession Scene(ulong handle)
    {
        lock (s_sceneLock)
        {
            if (!s_scenes.TryGetValue(handle, out var document)) throw new ArgumentException("Released or unknown scene handle.");
            document.Verify(); return document;
        }
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static uint GetSceneBridgeVersion() => 6;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static ulong CreateScene(byte* name, byte* error, int capacity)
    {
        ulong handle = 0;
        _ = Guard(() =>
        {
            var document = new SceneSession(ReadUtf8(name));
            lock (s_sceneLock)
            {
                if (s_scenes.Count >= 64 || s_nextScene == ulong.MaxValue) throw new InvalidOperationException("Scene session capacity exceeded.");
                handle = s_nextScene++; s_scenes.Add(handle, document);
            }
            return 0;
        }, error, capacity);
        return handle;
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    public static int SceneCall(ulong handle, int operation, byte* input, int length, byte* output, int outputCapacity, byte* error, int capacity) => Guard(() =>
    {
        if (length < 0 || length > SceneLimit || (length > 0 && input is null) || output is null || outputCapacity != SceneLimit)
            throw new ArgumentException("Invalid scene ABI buffers (4 MiB output required; mutations never retry).");
        SceneSession doc = Scene(handle);
        if (s_world == doc.World && operation is 2 or 3 or 9 or 13 or 15 or 22 or 25 or 26 or 27)
            throw new InvalidOperationException("Stop the bound play session before editing its document.");
        if (doc.Editor is not null && operation is 2 or 3 or 9 or 13 or 15 or 16 or 17 or 18 or 22 or 25 or 26 or 27)
            throw new InvalidOperationException("Editor documents require the versioned command gateway; runtime writes are Play-only.");
        using var source = new MemoryStream(new ReadOnlySpan<byte>(input, length).ToArray(), false);
        using var reader = new BinaryReader(source, new UTF8Encoding(false, true));
        using var destination = new MemoryStream();
        using var writer = new BinaryWriter(destination, new UTF8Encoding(false, true));
        void End() { if (source.Position != source.Length) throw new ArgumentException("Trailing scene request bytes."); }
        switch (operation)
        {
            case 1:
                End();
                if (s_world == doc.World) throw new InvalidOperationException("End gameplay before releasing its scene.");
                doc.Owner.Dispose(); lock (s_sceneLock) s_scenes.Remove(handle); break;
            case 2:
                string name = ReadText(reader); bool spatial = ReadFlag(reader); End();
                // Explicit editor spatial creation. Public gameplay CreateObject still creates an empty container.
                var created = doc.World.CreateObject(name); if (spatial) created.LocalTransform = Transform.Identity;
                writer.Write(created.Id); break;
            case 3:
                ulong doomed = reader.ReadUInt64(); End();
                var target = doc.World.GetObjects().FirstOrDefault(o => o.Id == doomed);
                if (target is null) { writer.Write((byte)0); break; }
                target.Destroy(); _ = doc.Document.Revision; writer.Write((byte)1); break;
            case 5: End(); var objects = doc.World.GetObjects(); writer.Write(objects.Count); foreach (var o in objects) writer.Write(o.Id); break;
            case 6: ulong contains = reader.ReadUInt64(); End(); writer.Write((byte)(doc.World.GetObjects().Any(o => o.Id == contains) ? 1 : 0)); break;
            case 7: End(); WriteText(writer, doc.World.Runtime.Name); break;
            case 8: ulong named = reader.ReadUInt64(); End(); WriteText(writer, doc.World.FindId(named).Name); break;
            case 9: ulong renamed = reader.ReadUInt64(); string newName = ReadText(reader); End(); doc.World.FindId(renamed).Name = newName; break;
            case 10: ulong identified = reader.ReadUInt64(); End(); var id = doc.World.FindId(identified).PersistentId; writer.Write(id.High); writer.Write(id.Low); break;
            case 11:
                Guid find = ReadUuid(reader); End();
                ulong found;
                try { found = doc.World.FindObject(SceneWorld.ToUuid(find)).Id; } catch (ArgumentException) { found = 0; }
                writer.Write(found); break;
            case 12: ulong transformed = reader.ReadUInt64(); End(); WriteTransform(writer, doc.World.FindId(transformed).LocalTransform); break;
            case 13: ulong edited = reader.ReadUInt64(); Transform value = ReadTransform(reader); End(); doc.World.FindId(edited).LocalTransform = value; break;
            case 15: ulong bound = reader.ReadUInt64(); BindingData[] bindings = ReadBindings(reader); End(); doc.SetBindings(doc.World.FindId(bound), bindings); break;
            // Old external Begin/Commit/Abort operations 16/17/18 are intentionally removed.
            case 19: ulong has = reader.ReadUInt64(); End(); writer.Write((byte)(doc.World.FindId(has).HasTransform ? 1 : 0)); break;
            case 20: ulong attached = reader.ReadUInt64(); End(); WriteBindings(writer, doc.GetBindings(doc.World.FindId(attached))); break;
            case 21: End(); writer.Write(doc.Document.CaptureBytes()); break;
            case 22:
                byte[] full = reader.ReadBytes(length); End(); doc.Document.RestoreBytes(full); doc.World.Restored(); break;
            case 23: End(); WriteView(writer, doc.CaptureView()); break; // Read-only inspection projection.
            case 24: End(); writer.Write(doc.Document.Revision); break;
            case 25: string loadPath = ReadText(reader); End(); Ncma.Scene.SceneDocumentFiles.Load(doc.Document, loadPath); doc.World.Restored(); break;
            case 26: string savePath = ReadText(reader); End(); Ncma.Scene.SceneDocumentFiles.Save(doc.Document, savePath); break;
            case 27:
                string emptyName = ReadText(reader); End();
                doc.Document.RestoreSnapshot(new(1, emptyName, [])); doc.World.Restored(); break;
            default: EditorCall(doc, operation, reader, writer, End); break;
        }
        writer.Flush();
        if (destination.Length > outputCapacity) throw new InvalidOperationException("Scene response exceeds ABI capacity.");
        destination.GetBuffer().AsSpan(0, (int)destination.Length).CopyTo(new Span<byte>(output, outputCapacity));
        return (int)destination.Length;
    }, error, capacity);
    private static int Count(BinaryReader r, int maximum)
    {
        int count = r.ReadInt32(); if (count < 0 || count > maximum) throw new ArgumentException("Invalid scene record count."); return count;
    }
    private static string ReadText(BinaryReader r)
    {
        int length = Count(r, 65536); byte[] bytes = r.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException("Truncated UTF-8 string."); return new UTF8Encoding(false, true).GetString(bytes);
    }
    private static void WriteText(BinaryWriter w, string value) { byte[] bytes = Encoding.UTF8.GetBytes(value); w.Write(bytes.Length); w.Write(bytes); }
    private static Guid ReadUuid(BinaryReader r) => SceneWorld.ToGuid(r.ReadUInt64(), r.ReadUInt64());
    private static void WriteUuid(BinaryWriter w, Guid value) { var id = SceneWorld.ToUuid(value); w.Write(id.High); w.Write(id.Low); }
    private static bool ReadFlag(BinaryReader r) => r.ReadByte() switch { 0 => false, 1 => true, _ => throw new ArgumentException("Invalid flag.") };
    private static Transform ReadTransform(BinaryReader r) => new()
    { Position = new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()), RotationX = r.ReadSingle(), RotationY = r.ReadSingle(), RotationZ = r.ReadSingle(), RotationW = r.ReadSingle(), Scale = new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()) };
    private static void WriteTransform(BinaryWriter w, Transform t)
    { foreach (float value in new[] { t.Position.X, t.Position.Y, t.Position.Z, t.RotationX, t.RotationY, t.RotationZ, t.RotationW, t.Scale.X, t.Scale.Y, t.Scale.Z }) w.Write(value); }
    private static BindingData[] ReadBindings(BinaryReader r)
    {
        var values = new BindingData[Count(r, 1024)];
        for (int i = 0; i < values.Length; i++)
        {
            Guid id = ReadUuid(r); string name = ReadText(r); bool enabled = ReadFlag(r);
            var properties = new PropertyData[Count(r, 1024)];
            for (int p = 0; p < properties.Length; p++) properties[p] = new(ReadText(r), r.ReadUInt32(), r.ReadDouble());
            values[i] = new(id, name, enabled, properties);
        }
        return values;
    }
    private static void WriteBindings(BinaryWriter w, BindingData[] values)
    {
        w.Write(values.Length);
        foreach (var b in values)
        { WriteUuid(w, b.Id); WriteText(w, b.TypeName); w.Write((byte)(b.Enabled ? 1 : 0)); w.Write(b.Properties.Length); foreach (var p in b.Properties) { WriteText(w, p.Name); w.Write(p.Kind); w.Write(p.Value); } }
    }
    private static void WriteView(BinaryWriter w, SceneData scene)
    {
        WriteText(w, scene.Name); w.Write(scene.Objects.Length);
        foreach (var o in scene.Objects)
        { WriteUuid(w, o.Id); WriteText(w, o.Name); w.Write((byte)(o.HasTransform ? 1 : 0)); WriteTransform(w, o.Transform); WriteBindings(w, o.Bindings); }
    }
}
