using System.Reflection;
using System.Text.Json;
using Ncma.Runtime;

internal static class PagedStorageTests
{
    private readonly record struct Label(string Text, int Value) : IComponent;
    private static void Check(bool value) { if (!value) throw new Exception("Paged storage assertion failed."); }
    private static void Reject(Action action)
    { try { action(); } catch (ArgumentException) { return; } throw new Exception("Invalid page operation accepted."); }
    private static Array Directory<T>(PagedValues<T> value) where T : struct =>
        (Array)typeof(PagedValues<T>).GetField("_pages", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static PagedValues<T> Values<T>(ComponentPool<T> pool) where T : struct, IComponent =>
        (PagedValues<T>)typeof(ComponentPool<T>).GetField("_values", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pool)!;
    private static object PoolDirectory<T>(ComponentPool<T> pool, string field) where T : struct, IComponent =>
        typeof(ComponentPool<T>).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pool)!;
    private sealed class ThrowingPool(bool fail) : IComponentPool
    {
        internal int Prepared, Removed;
        public int Count => 1;
        public int SlotAt(int index) => 0;
        public bool Has(int slot) => slot == 0;
        public IComponentPool Clone() => this;
        public void PrepareRemove(int slot) { Prepared++; if (fail) throw new InvalidOperationException("Injected page preparation failure"); }
        public void Remove(int slot) => Removed++;
        public void SetDecoded(int slot, object value) => throw new NotSupportedException();
        public JsonElement Encode(int slot) => throw new NotSupportedException();
    }
    internal static int Run()
    {
        var cases = new (string Name, Action Run)[]
        {
            ("lazy fixed capacity and invalid page sizes", () => {
                foreach (int size in new[] { 16, 32, 64, 128, 256 }) {
                    var values = new PagedValues<Label>(size); Check(Directory(values).Length == World.MaxObjects / size);
                    Check(Directory(values).Cast<object?>().All(p => p is null));
                    for (int i = 0; i < World.MaxObjects; ++i) Check(values.Read(i) == default);
                    Reject(() => values.Read(-1)); Reject(() => values.Read(World.MaxObjects));
                    Reject(() => values.Write(-1, default)); Reject(() => values.PrepareWrite(World.MaxObjects));
                }
                foreach (int bad in new[] { 0, -1, 8, 17, 48, 512, int.MaxValue }) Reject(() => new PagedValues<int>(bad));
            }),
            ("forks share directories until the first write and isolate both owners", () => {
                var source = new PagedValues<Label>(); source.Write(0, new("zero", 1)); source.Write(4095, new("last", 2));
                var oldDirectory = Directory(source); var candidate = source.Fork(); Check(ReferenceEquals(oldDirectory, Directory(candidate)));
                source.Write(0, new("parent", 3)); Check(candidate.Read(0).Text == "zero");
                candidate.Write(4095, new("child", 4)); Check(source.Read(4095).Text == "last");
                Check(!ReferenceEquals(Directory(source), Directory(candidate)));
                var nested = candidate.Fork(); candidate.Write(0, new("sibling", 5)); nested.Write(4095, new("nested", 6));
                Check(nested.Read(0).Text == "zero" && candidate.Read(4095).Text == "child");
            }),
            ("only the touched value page is copied", () => {
                var source = new ComponentPool<TransformData>(); for (int i = 0; i < World.MaxObjects; ++i) source.Set(i, TransformData.Identity);
                var candidate = (ComponentPool<TransformData>)source.Clone(); var old = Directory(Values(source));
                candidate.Set(33, TransformData.Identity with { Position = new(3, 0, 0) }); var current = Directory(Values(candidate));
                Check(!ReferenceEquals(old, current)); int different = 0;
                for (int i = 0; i < old.Length; ++i) if (!ReferenceEquals(old.GetValue(i), current.GetValue(i))) different++;
                Check(different == 1 && source.Get(33).Position.X == 0 && candidate.Get(33).Position.X == 3);
                Check(ReferenceEquals(PoolDirectory(source, "_sparse"), PoolDirectory(candidate, "_sparse")));
                Check(ReferenceEquals(PoolDirectory(source, "_slots"), PoolDirectory(candidate, "_slots")));
            }),
            ("dense swap removal crosses page boundaries without touching parent", () => {
                foreach (int size in new[] { 16, 32, 64, 128, 256 }) {
                    var source = new ComponentPool<Label>(size); for (int i = 0; i < World.MaxObjects; ++i) source.Set(i, new("v" + i, i));
                    var copy = (ComponentPool<Label>)source.Clone();
                    foreach (int slot in new[] { 0, size - 1, size, 2 * size - 1, 4095, 2047 }) {
                        copy.Remove(slot); Check(!copy.Has(slot) && source.Has(slot));
                    }
                    for (int i = 0; i < copy.Count; ++i) Check(copy.Get(copy.SlotAt(i)).Value == copy.SlotAt(i));
                    for (int i = copy.Count; i < World.MaxObjects; ++i) Check(Values(copy).Read(i).Text is null);
                    copy.Set(0, new("reused", 999)); Check(copy.Get(0).Value == 999 && source.Get(0).Value == 0);
                    source.Remove(4094); Check(copy.Has(4094));
                }
            }),
            ("prepared multi-page removal allocates nothing while publishing", () => {
                var source = new ComponentPool<Label>(); for (int i = 0; i < World.MaxObjects; ++i) source.Set(i, new("fixed", i));
                for (int warm = 0; warm < 20; ++warm) { var p = (ComponentPool<Label>)source.Clone(); p.PrepareRemove(5); p.Remove(5); }
                var copy = (ComponentPool<Label>)source.Clone(); copy.PrepareRemove(5);
                long before = GC.GetAllocatedBytesForCurrentThread(); copy.Remove(5); long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                Check(bytes == 0 && !copy.Has(5) && source.Has(5));
            }),
            ("all pools prepare before any cross-pool deletion", () => {
                var storage = new ComponentStorage();
                var pools = (Dictionary<Type, IComponentPool>)typeof(ComponentStorage).GetField("_pools", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(storage)!;
                var first = new ThrowingPool(false); var last = new ThrowingPool(true);
                pools.Add(typeof(TransformData), first); pools.Add(typeof(Label), last);
                bool rejected = false; try { storage.RemoveAll(0); } catch (InvalidOperationException) { rejected = true; }
                Check(rejected && first.Prepared == 1 && last.Prepared == 1 && first.Removed == 0 && last.Removed == 0);
            }),
            ("branching randomized histories match independent maps", () => {
                var random = new Random(318103); var pools = new List<ComponentPool<Label>> { new() };
                var expected = new List<Dictionary<int, Label>> { new() };
                for (int round = 0; round < 8000; ++round) {
                    int branch = random.Next(pools.Count); int slot = random.Next(World.MaxObjects);
                    if (round % 19 == 0) {
                        var copy = (ComponentPool<Label>)pools[branch].Clone(); var map = new Dictionary<int, Label>(expected[branch]);
                        if (pools.Count == 12) { int replace = random.Next(pools.Count); pools[replace] = copy; expected[replace] = map; }
                        else { pools.Add(copy); expected.Add(map); }
                    } else if (round % 3 == 0) { pools[branch].Remove(slot); expected[branch].Remove(slot); }
                    else { var value = new Label("r" + round, round); pools[branch].Set(slot, value); expected[branch][slot] = value; }
                    if (round % 131 == 0) for (int b = 0; b < pools.Count; ++b) {
                        Check(pools[b].Count == expected[b].Count);
                        for (int s = 0; s < World.MaxObjects; ++s) { Check(pools[b].Has(s) == expected[b].ContainsKey(s));
                            if (expected[b].TryGetValue(s, out var value)) Check(pools[b].Get(s) == value); }
                    }
                }
            }),
            ("warm typed read and write remain allocation free", () => {
                var source = new ComponentPool<Label>(); source.Set(1, new("value", 1));
                var copy = (ComponentPool<Label>)source.Clone(); copy.Set(1, new("candidate", 2));
                for (int i = 0; i < 20000; ++i) copy.Set(1, copy.Get(1));
                long before = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 20000; ++i) copy.Set(1, copy.Get(1));
                Check(GC.GetAllocatedBytesForCurrentThread() == before && source.Get(1).Value == 1);
            }),
            ("fork allocation does not grow with populated entity count", () => {
                long Measure(int count) {
                    var pool = new ComponentPool<TransformData>(); for (int i = 0; i < count; ++i) pool.Set(i, TransformData.Identity);
                    for (int warm = 0; warm < 30; ++warm) _ = pool.Clone();
                    long before = GC.GetAllocatedBytesForCurrentThread(); var clone = pool.Clone(); long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                    GC.KeepAlive(clone); return bytes;
                }
                long small = Measure(100), large = Measure(4096); Check(small == large && large < 1024);
                Console.WriteLine($"Paged pool fork allocation: 100={small}B, 4096={large}B (storage-only, not whole-frame).");
            }),
            ("unchanged siblings retain removed strings while current values clear", () => {
                var original = new ComponentPool<Label>(); original.Set(0, new("retained by original", 1)); original.Set(200, new("last", 2));
                var candidate = (ComponentPool<Label>)original.Clone(); candidate.Remove(0); candidate.Remove(200);
                Check(candidate.Count == 0 && original.Count == 2);
                for (int i = 0; i < World.MaxObjects; ++i) Check(Values(candidate).Read(i).Text is null);
                Check(original.Get(0).Text == "retained by original" && original.Get(200).Text == "last");
            }),
        };
        int failures = 0;
        foreach (var test in cases) { try { test.Run(); Console.WriteLine("PASS paged storage " + test.Name); }
            catch (Exception error) { failures++; Console.Error.WriteLine("FAIL paged storage " + test.Name + ": " + error); } }
        Console.WriteLine($"Paged storage tests: {cases.Length - failures}/{cases.Length} passed."); return failures;
    }
}
