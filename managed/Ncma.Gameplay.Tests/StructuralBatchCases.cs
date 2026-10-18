using System.Reflection;
using System.Text.Json;
using Ncma;
using Ncma.Gameplay;
using Ncma.Runtime;
using Ncma.Scene;

internal static class StructuralBatchCases
{
    private readonly record struct TextData(string Text) : IComponent;
    private const string TextSchema = """{"type":"object","required":["text"],"properties":{"text":{"type":"string"}}}""";
    private static void Check(bool value, string message = "Structural batch assertion failed")
    { if (!value) throw new Exception(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or OverflowException or JsonException) { return; }
        throw new Exception("Invalid structural operation accepted.");
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly SceneDocument Doc;
        internal readonly Probe Probe = new();
        internal readonly PlaySession Play;
        internal readonly Entity Entity;
        internal readonly Guid Id, Binding = Guid.NewGuid();
        internal StructuralTarget Target => StructuralTarget.Existing(Entity);
        internal IRuntimeCommands Commands => Play.Commands;
        internal Fixture(ComponentRegistry? registry = null)
        {
            Doc = new("Batch fixture", registry); Entity = Doc.World.CreateEntity("Original"); Id = Doc.World.GetUuid(Entity);
            Doc.World.Set(Entity, TransformData.Identity);
            Doc.SetBindings(Id, [new(Binding, "Test.Probe", true, [])]);
            Play = new(Doc);
        }
        internal void Start(Func<BehaviourBindingData, Behaviour>? factory = null) => Play.Start(factory ?? (b => b.Id == Binding ? Probe : new Probe()));
        internal void Step() => Play.AdvanceFrame(1.0 / 60);
        public void Dispose() => Play.Dispose();
    }
    private static ComponentSnapshot Transform(Fixture f) => new("ncma.transform", 1, f.Doc.World.Components.Encode(TransformData.Identity));
    private static void AtomicReject(Func<Fixture, StructuralCommand[]> make)
    {
        using var f = new Fixture(); bool rejected = false;
        f.Probe.Fixed = _ => { Reject(() => f.Commands.SubmitBatch(make(f))); rejected = true; };
        f.Start(); byte[] before = f.Doc.CaptureBytes(); ulong revision = f.Doc.Revision;
        var query = f.Doc.World.Query<TransformData>(); Check(query.Count == 1);
        f.Step(); Check(rejected && f.Play.State == PlayState.Faulted && f.Play.Tick == 0);
        Check(f.Doc.CaptureBytes().SequenceEqual(before) && f.Doc.Revision == revision && query.Count == 1);
        Check(f.Doc.World.Resolve(f.Entity) == f.Entity);
    }
    internal static void Run()
    {
        var cases = new (string Name, Action Run)[]
        {
            ("create dependencies and committed-only receipts", () => {
                using var f = new Fixture(); var id = Guid.NewGuid(); StructuralSubmission? batch = null;
                var query = f.Doc.World.Query<TransformData>(); Check(query.Count == 1);
                f.Probe.Fixed = _ => {
                    batch = f.Commands.SubmitBatch([
                        new StructuralCommand.Create(id, "New"), new StructuralCommand.Rename(StructuralTarget.CreatedAt(0), "Final"),
                        new StructuralCommand.Add(StructuralTarget.CreatedAt(0), Transform(f)),
                        new StructuralCommand.Attach(StructuralTarget.CreatedAt(0), new(Guid.NewGuid(), "Test.Other", true, []))]);
                    Check(batch.Count == 4 && batch.Created(0).ObjectId == id && query.Count == 1 && f.Doc.World.Count == 1);
                    Reject(() => f.Commands.Resolve(batch.Created(0))); Reject(() => f.Commands.GetReceipt(batch[3]));
                    Reject(() => batch.Created(1)); Reject(() => f.Doc.World.FindEntity(id));
                };
                f.Start(); f.Step(); Check(f.Play.Tick == 1 && f.Play.BehaviourCount == 2 && query.Count == 2);
                var entity = f.Commands.Resolve(batch!.Created(0)); Check(f.Doc.World.GetName(entity) == "Final");
                for (int i = 0; i < batch.Count; i++) {
                    var receipt = f.Commands.GetReceipt(batch[i]);
                    Check(receipt.Entity == entity && receipt.WorldIdentity == f.Doc.World.Identity && receipt.Tick == 1 && receipt.Disposition == "committed");
                }
                Check(f.Doc.World.Resolve(f.Entity) == f.Entity);
            }),
            ("late component failure rejects whole step despite catch", () => {
                using var f = new Fixture(); PendingEntity? earlier = null; bool rejected = false; var id = Guid.NewGuid();
                f.Probe.Fixed = _ => {
                    earlier = f.Commands.SpawnEmpty("Earlier");
                    f.Probe.GameObject.LocalTransform = Ncma.Transform.Identity with { Position = new(7, 0, 0) };
                    Reject(() => f.Commands.SubmitBatch([new StructuralCommand.Create(id, "Prefix"),
                        new StructuralCommand.Add(StructuralTarget.CreatedAt(0), new("missing.type", 1, Transform(f).Data))]));
                    rejected = true;
                    Check(!((RuntimeCommandBuffer)f.Commands).Bindings.ContainsKey(id));
                    Reject(() => f.Commands.GetReceipt(earlier.Token));
                };
                f.Start(); var before = f.Doc.CaptureBytes(); f.Step();
                Check(rejected && f.Play.State == PlayState.Faulted && f.Doc.CaptureBytes().SequenceEqual(before));
                Reject(() => f.Commands.Resolve(earlier!)); Reject(() => f.Commands.GetReceipt(earlier!.Token));
            }),
            ("late binding metadata failure leaves no prefix", () => {
                using var f = new Fixture(); var id = Guid.NewGuid(); bool rejected = false;
                f.Probe.Fixed = _ => {
                    Reject(() => f.Commands.SubmitBatch([new StructuralCommand.Create(id, "Prefix"),
                        new StructuralCommand.Attach(StructuralTarget.CreatedAt(0), new(Guid.NewGuid(), "Test.Other", true, [new("Bad", 999, 1)]))]));
                    Check(!((RuntimeCommandBuffer)f.Commands).Bindings.ContainsKey(id)); rejected = true;
                };
                f.Start(); var before = f.Doc.CaptureBytes(); f.Step();
                Check(rejected && f.Play.State == PlayState.Faulted && f.Doc.CaptureBytes().SequenceEqual(before));
            }),
            ("duplicate rename rejected", () => AtomicReject(f => [new StructuralCommand.Rename(f.Target, "A"), new StructuralCommand.Rename(f.Target, "B")])),
            ("duplicate component mutation rejected", () => AtomicReject(f => [new StructuralCommand.Remove(f.Target, "ncma.transform"), new StructuralCommand.Add(f.Target, Transform(f))])),
            ("duplicate binding mutation rejected", () => AtomicReject(f => [new StructuralCommand.Enable(f.Target, f.Binding, false), new StructuralCommand.Detach(f.Target, f.Binding)])),
            ("destroy cannot mix with another operation", () => {
                AtomicReject(f => [new StructuralCommand.Rename(f.Target, "A"), new StructuralCommand.Destroy(f.Target)]);
                AtomicReject(f => [new StructuralCommand.Destroy(f.Target), new StructuralCommand.Rename(f.Target, "A")]);
            }),
            ("creation dependencies reject forward self and noncreate", () => {
                foreach (int index in new[] { -1, 0, 1, 2 })
                    AtomicReject(f => [new StructuralCommand.Rename(f.Target, "Prefix"), new StructuralCommand.Rename(StructuralTarget.CreatedAt(index), "Bad")]);
            }),
            ("null empty default stale foreign targets rejected", () => {
                AtomicReject(_ => []); AtomicReject(_ => [null!]);
                AtomicReject(_ => [new StructuralCommand.Destroy(default)]);
                AtomicReject(_ => [new StructuralCommand.Destroy(StructuralTarget.Existing(default))]);
                AtomicReject(f => [new StructuralCommand.Destroy(StructuralTarget.Existing(f.Entity with { Generation = f.Entity.Generation + 1 }))]);
                AtomicReject(_ => [new StructuralCommand.Destroy(StructuralTarget.Existing(new World().CreateEntity("Foreign")))]);
                AtomicReject(_ => [new StructuralCommand.Destroy(StructuralTarget.PendingCreation(null!))]);
            }),
            ("duplicate UUID reservation and unknown binding rejected", () => {
                AtomicReject(f => [new StructuralCommand.Create(f.Id, "Duplicate")]);
                AtomicReject(f => [new StructuralCommand.Detach(f.Target, Guid.NewGuid())]);
                AtomicReject(f => [new StructuralCommand.Enable(f.Target, Guid.NewGuid(), true)]);
                var id = Guid.NewGuid(); AtomicReject(_ => [new StructuralCommand.Create(id, "A"), new StructuralCommand.Create(id, "B")]);
            }),
            ("malformed payloads are rejected before publication", () => {
                AtomicReject(f => [new StructuralCommand.Add(f.Target, null!)]);
                AtomicReject(f => [new StructuralCommand.Add(f.Target, Transform(f) with { Version = 99 })]);
                AtomicReject(f => [new StructuralCommand.Attach(f.Target, new(Guid.Empty, "Type", true, []))]);
                AtomicReject(f => [new StructuralCommand.Attach(f.Target, new(Guid.NewGuid(), new string('T', 513), true, []))]);
                AtomicReject(f => [new StructuralCommand.Attach(f.Target, new(Guid.NewGuid(), "Type", true, [new(new string('E', 129), 0, 1)]))]);
                AtomicReject(f => [new StructuralCommand.Attach(f.Target, new(Guid.NewGuid(), "Type", true, new RuntimeExport[1025]))]);
                AtomicReject(f => [new StructuralCommand.Attach(f.Target, new(f.Binding, "Duplicate", true, []))]);
                using var oversized = JsonDocument.Parse(JsonSerializer.Serialize(new { text = new string('X', ComponentRegistry.MaxPayloadBytes + 512) }));
                AtomicReject(f => [new StructuralCommand.Add(f.Target, new("test.unknown", 1, oversized.RootElement))]);
            }),
            ("batched binding lifecycle follows existing safe boundary", () => {
                using var f = new Fixture(); var other = new Probe(); var binding = Guid.NewGuid();
                f.Probe.Fixed = _ => {
                    if (f.Play.Tick == 0) f.Commands.SubmitBatch([new StructuralCommand.Attach(f.Target, new(binding, "Other", false, []))]);
                    if (f.Play.Tick == 1) f.Commands.SubmitBatch([new StructuralCommand.Enable(f.Target, binding, true)]);
                    if (f.Play.Tick == 2) f.Commands.SubmitBatch([new StructuralCommand.Enable(f.Target, binding, false)]);
                    if (f.Play.Tick == 3) f.Commands.SubmitBatch([new StructuralCommand.Detach(f.Target, binding)]);
                };
                f.Start(b => b.Id == f.Binding ? f.Probe : other);
                f.Step(); Check(other.Created == 0 && f.Doc.GetBindings(f.Id).Length == 2);
                f.Step(); Check(other.Created == 1 && other.Enabled == 0);
                f.Step(); Check(other.Enabled == 1 && other.FixedCount == 1);
                f.Step(); Check(other.Disabled == 1 && other.FixedCount == 1 && f.Doc.GetBindings(f.Id).Length == 1);
                f.Step(); Check(other.Destroyed == 1 && f.Play.Tick == 5);
            }),
            ("foreign pending and aborted retry handles cannot revive", () => {
                using var first = new Fixture(); PendingEntity? foreign = null;
                first.Probe.Fixed = _ => foreign = first.Commands.SpawnEmpty("Foreign"); first.Start(); first.Step();
                using var f = new Fixture(); PendingEntity? aborted = null; Guid id = Guid.NewGuid();
                f.Probe.Fixed = _ => {
                    aborted = f.Commands.SubmitBatch([new StructuralCommand.Create(id, "Aborted")]).Created(0);
                    Reject(() => f.Commands.SubmitBatch([new StructuralCommand.Rename(StructuralTarget.PendingCreation(foreign!), "Foreign")]));
                };
                f.Start(); f.Step(); Check(f.Play.State == PlayState.Faulted); var old = aborted!;
                f.Play.Stop(); f.Probe.Fixed = _ => aborted = f.Commands.SubmitBatch([new StructuralCommand.Create(id, "Retry")]).Created(0);
                f.Start(); f.Step(); var retry = aborted ?? throw new Exception("Missing retry submission.");
                Check(f.Play.Tick == 1 && retry.Token != old.Token);
                Reject(() => f.Commands.Resolve(old)); Reject(() => first.Commands.Resolve(retry));
                Check(f.Doc.World.GetName(f.Commands.Resolve(retry)) == "Retry");
            }),
            ("cross batch ordering and same attempt pending dependencies", () => {
                using var f = new Fixture(); PendingEntity? single = null, batched = null;
                f.Probe.Fixed = _ => {
                    single = f.Commands.SpawnEmpty("Single");
                    batched = f.Commands.SubmitBatch([new StructuralCommand.Create(Guid.NewGuid(), "Batched")]).Created(0);
                    f.Commands.SubmitBatch([new StructuralCommand.Rename(f.Target, "First"), new StructuralCommand.Remove(f.Target, "ncma.transform"),
                        new StructuralCommand.Rename(StructuralTarget.PendingCreation(single), "S"), new StructuralCommand.Rename(StructuralTarget.PendingCreation(batched), "B")]);
                    f.Commands.SubmitBatch([new StructuralCommand.Rename(f.Target, "Last"), new StructuralCommand.Add(f.Target, Transform(f))]);
                };
                f.Start(); f.Step(); Check(f.Play.Tick == 1 && f.Doc.World.GetName(f.Entity) == "Last" && f.Doc.World.Has<TransformData>(f.Entity));
                Check(f.Doc.World.GetName(f.Commands.Resolve(single!)) == "S" && f.Doc.World.GetName(f.Commands.Resolve(batched!)) == "B");
            }),
            ("pending removed before commit has no live result", () => {
                using var f = new Fixture(); StructuralSubmission? create = null, destroy = null;
                f.Probe.Fixed = _ => {
                    create = f.Commands.SubmitBatch([new StructuralCommand.Create(Guid.NewGuid(), "Ephemeral")]);
                    destroy = f.Commands.SubmitBatch([new StructuralCommand.Destroy(StructuralTarget.PendingCreation(create.Created(0)))]);
                };
                f.Start(); f.Step(); Check(f.Play.Tick == 1 && f.Doc.World.Count == 1);
                foreach (var token in new[] { create![0], destroy![0] }) {
                    var receipt = f.Commands.GetReceipt(token); Check(receipt.Entity is null && receipt.Disposition == "removed");
                }
                Reject(() => f.Commands.Resolve(create.Created(0)));
            }),
            ("old attempt pending target rejected but resolved entity works", () => {
                using var f = new Fixture(); PendingEntity? pending = null;
                f.Probe.Fixed = _ => {
                    if (f.Play.Tick == 0) pending = f.Commands.SubmitBatch([new StructuralCommand.Create(Guid.NewGuid(), "First")]).Created(0);
                    else if (f.Play.Tick == 1) f.Commands.SubmitBatch([new StructuralCommand.Rename(StructuralTarget.Existing(f.Commands.Resolve(pending!)), "Second")]);
                    else Reject(() => f.Commands.SubmitBatch([new StructuralCommand.Rename(StructuralTarget.PendingCreation(pending!), "Stale")]));
                };
                f.Start(); f.Step(); f.Step(); Check(f.Play.Tick == 2 && f.Doc.World.GetName(f.Commands.Resolve(pending!)) == "Second");
                f.Step(); Check(f.Play.State == PlayState.Faulted && f.Play.Tick == 2);
            }),
            ("resolved pending never retargets after deletion and slot reuse", () => {
                using var f = new Fixture(); PendingEntity? pending = null, replacement = null;
                f.Probe.Fixed = _ => {
                    if (f.Play.Tick == 0) pending = f.Commands.SpawnEmpty("First");
                    else {
                        f.Commands.SubmitBatch([new StructuralCommand.Destroy(StructuralTarget.Existing(f.Commands.Resolve(pending!)))]);
                        replacement = f.Commands.SpawnEmpty("Replacement");
                    }
                };
                f.Start(); f.Step(); var original = f.Commands.Resolve(pending!); f.Step();
                Check(f.Play.Tick == 2); var next = f.Commands.Resolve(replacement!);
                Check(next.Index == original.Index && next.Generation > original.Generation); Reject(() => f.Commands.Resolve(pending!));
            }),
            ("stop restart and world restore invalidate receipts", () => {
                using var f = new Fixture(); PendingEntity? pending = null;
                f.Probe.Fixed = _ => pending = f.Commands.SpawnEmpty("New"); f.Start(); f.Step(); var old = pending!;
                f.Play.Stop(); Reject(() => f.Commands.GetReceipt(old.Token)); Reject(() => f.Commands.Resolve(old));
                f.Start(); f.Step(); Check(pending!.Token != old.Token); Reject(() => f.Commands.Resolve(old));
                f.Play.Stop(); var bytes = f.Doc.CaptureBytes(); f.Doc.RestoreBytes(bytes); Reject(() => f.Commands.Resolve(pending));
            }),
            ("accepted batch aborted by later factory preserves input and receipts", () => {
                using var f = new Fixture(); StructuralSubmission? accepted = null;
                f.Probe.Fixed = _ => accepted = f.Commands.SubmitBatch([new StructuralCommand.Create(Guid.NewGuid(), "Bad factory"),
                    new StructuralCommand.Attach(StructuralTarget.CreatedAt(0), new(Guid.NewGuid(), "Missing.Type", true, []))]);
                f.Start(b => b.TypeName == "Missing.Type" ? throw new ArgumentException("Missing factory") : f.Probe);
                var before = f.Doc.CaptureBytes();
                var pressed = new ulong[8]; pressed[0] = 2;
                f.Play.SubmitInput(new InputFrame(f.Play.SessionId, 1, true, new ulong[8], new ulong[8], new ulong[8]));
                f.Play.SubmitInput(new InputFrame(f.Play.SessionId, 2, true, new ulong[8], pressed, new ulong[8]));
                f.Step(); Check(f.Play.State == PlayState.Faulted && f.Play.Tick == 0 && f.Doc.CaptureBytes().SequenceEqual(before));
                Check(f.Play.Input.Pressed(1)); Reject(() => f.Commands.GetReceipt(accepted![0])); Reject(() => f.Commands.Resolve(accepted!.Created(0)));
            }),
            ("shared command cap accepts 1024 and rejects 1025", () => {
                foreach (bool overflow in new[] { false, true }) {
                    using var f = new Fixture(); StructuralSubmission? batch = null; CommandToken single = default;
                    f.Probe.Fixed = _ => {
                        single = f.Commands.Rename(f.Id, "Single");
                        batch = f.Commands.SubmitBatch(Enumerable.Range(0, 1023).Select(i => (StructuralCommand)new StructuralCommand.Create(Guid.NewGuid(), "C" + i)).ToArray());
                        if (overflow) Reject(() => f.Commands.SubmitBatch([new StructuralCommand.Rename(f.Target, "Overflow")]));
                    };
                    f.Start(); f.Step();
                    if (overflow) { Check(f.Play.State == PlayState.Faulted && f.Doc.World.Count == 1); Reject(() => f.Commands.GetReceipt(single)); }
                    else { Check(f.Play.Tick == 1 && f.Doc.World.Count == 1024 && batch!.Count == 1023); Check(f.Commands.GetReceipt(single).Tick == 1); }
                }
            }),
            ("receipt retention remains bounded across singles and batches", () => {
                using var f = new Fixture(); CommandToken first = default, last = default;
                f.Probe.Fixed = _ => {
                    if (f.Play.Tick == 0) first = f.Commands.SubmitBatch([new StructuralCommand.Rename(f.Target, "First")])[0];
                    else for (int i = 0; i < 1024; ++i) last = f.Commands.Rename(f.Id, "R" + i);
                };
                f.Start(); f.Step(); f.Step(); Check(f.Play.Tick == 2); Reject(() => f.Commands.GetReceipt(first)); Check(f.Commands.GetReceipt(last).Tick == 2);
            }),
            ("component and binding inputs are copied", () => {
                using var f = new Fixture(); PendingEntity? pending = null;
                f.Probe.Fixed = _ => {
                    using var json = JsonDocument.Parse(Transform(f).Data.GetRawText());
                    RuntimeExport[] exports = [new("Value", (uint)ExportKind.Double, 3)];
                    StructuralCommand[] input = [new StructuralCommand.Create(Guid.NewGuid(), "Copied"),
                        new StructuralCommand.Add(StructuralTarget.CreatedAt(0), new("ncma.transform", 1, json.RootElement)),
                        new StructuralCommand.Attach(StructuralTarget.CreatedAt(0), new(Guid.NewGuid(), "Test.Other", false, exports))];
                    var result = f.Commands.SubmitBatch(input); pending = result.Created(0); input[0] = null!;
                    exports[0] = new("Changed", (uint)ExportKind.Double, 99);
                };
                f.Start(); f.Step(); Check(f.Play.Tick == 1 && f.Doc.World.Has<TransformData>(f.Commands.Resolve(pending!)));
                Check(f.Doc.GetBindings(pending!.ObjectId)[0].Exports[0] == new ExportData("Value", ExportKind.Double, 3));
            }),
            ("bounded raw normalized and aggregate payloads", () => {
                foreach (bool normalize in new[] { false, true }) {
                    var registry = ComponentRegistry.CreateDefault(); registry.Register<TextData>("test.text", 1, TextSchema,
                        value => normalize ? new(new string('N', 60000)) : value);
                    using var f = new Fixture(registry); bool rejected = false;
                    f.Probe.Fixed = _ => {
                        for (int i = 0; i < 18; i++) {
                            using var json = JsonDocument.Parse(JsonSerializer.Serialize(new { text = normalize ? "small" : new string('R', 60000) }));
                            try { f.Commands.SubmitBatch([new StructuralCommand.Create(Guid.NewGuid(), "Payload"),
                                new StructuralCommand.Add(StructuralTarget.CreatedAt(0), new("test.text", 1, json.RootElement))]); }
                            catch (ArgumentException) { rejected = true; break; }
                        }
                    };
                    f.Start(); f.Step(); Check(rejected && f.Play.State == PlayState.Faulted && f.Doc.World.Count == 1);
                }
            }),
            ("caught validator reentrancy poisons the enclosing step", () => {
                foreach (bool single in new[] { false, true }) {
                    Fixture? active = null; bool reentered = false;
                    var registry = ComponentRegistry.CreateDefault(); registry.Register<TextData>("test.text", 1, TextSchema, value => {
                        if (active is not null) { reentered = true; Reject(() => {
                            if (single) active.Commands.Rename(active.Id, "Nested");
                            else active.Commands.SubmitBatch([new StructuralCommand.Rename(active.Target, "Nested")]);
                        }); } return value;
                    });
                    using var f = new Fixture(registry); active = f;
                    f.Probe.Fixed = _ => { using var json = JsonDocument.Parse("{\"text\":\"safe\"}");
                        Reject(() => f.Commands.SubmitBatch([new StructuralCommand.Add(f.Target, new("test.text", 1, json.RootElement))])); };
                    f.Start(); var before = f.Doc.CaptureBytes(); f.Step(); active = null;
                    Check(reentered && f.Play.State == PlayState.Faulted && f.Doc.CaptureBytes().SequenceEqual(before));
                }
            }),
            ("owner thread and read only callbacks cannot submit", () => {
                using var f = new Fixture(); Reject(() => f.Commands.SubmitBatch([new StructuralCommand.Rename(f.Target, "Outside")]));
                Check(Task.Run(() => { try { f.Commands.SubmitBatch([new StructuralCommand.Rename(f.Target, "Foreign")]); return false; }
                    catch (InvalidOperationException) { return true; } }).GetAwaiter().GetResult());
                f.Probe.Update = _ => Reject(() => f.Commands.SubmitBatch([new StructuralCommand.Rename(f.Target, "ReadOnly")]));
                f.Start(); f.Step(); Check(f.Play.Tick == 1 && f.Doc.World.GetName(f.Entity) == "Original");
            }),
            ("sequence overflow rejects without wrapping", () => {
                using var f = new Fixture(); f.Probe.Fixed = _ => {
                    typeof(RuntimeCommandBuffer).GetField("_sequence", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(f.Commands, ulong.MaxValue - 1);
                    Reject(() => f.Commands.SubmitBatch([new StructuralCommand.Create(Guid.NewGuid(), "A"), new StructuralCommand.Create(Guid.NewGuid(), "B")]));
                };
                f.Start(); f.Step(); Check(f.Play.State == PlayState.Faulted && f.Doc.World.Count == 1 && f.Play.Tick == 0);
            }),
        };
        foreach (var test in cases) { test.Run(); Console.WriteLine("PASS structural batch " + test.Name); }
        Console.WriteLine($"Structural batch tests: {cases.Length}/{cases.Length} passed.");
    }
}
