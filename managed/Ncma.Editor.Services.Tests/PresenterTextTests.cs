using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Ncma.Editor.App;
using Ncma.Editor.Services;
using Ncma.Gui;

internal static class PresenterTextTests
{
    private const int Budget = 2 * 1024 * 1024;
    private static void Check(bool value) { if (!value) throw new Exception("Presenter UTF-8 assertion failed."); }
    // Direct call to the existing private entry; no product test API or reflection allocation in the measured loop.
    private static Action<string, string> Bind(EditorPresenter view)
    {
        var method = typeof(EditorPresenter).GetMethod("Add", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var label = Expression.Parameter(typeof(string)); var value = Expression.Parameter(typeof(string));
        return Expression.Lambda<Action<string, string>>(Expression.Call(Expression.Constant(view), method,
            Expression.Constant(GuiItemKind.Label), Expression.Constant(7UL), Expression.Constant(11UL), label,
            Expression.Constant(null, method.GetParameters()[4].ParameterType), value, Expression.Constant(2.5),
            Expression.Constant(-1.0), Expression.Constant(9.0), Expression.Constant(false)), label, value).Compile();
    }
    private static void Clear(EditorPresenter view)
    {
        ((List<GuiItem>)typeof(EditorPresenter).GetField("_items", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!).Clear();
        ((List<byte>)typeof(EditorPresenter).GetField("_text", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!).Clear();
    }
    private static void RejectUnchanged(EditorPresenter view, Action action)
    {
        var items = view.Items.ToArray(); var text = view.Text.ToArray();
        bool rejected = false; try { action(); } catch (ArgumentException) { rejected = true; }
        Check(rejected && view.Text.SequenceEqual(text) &&
            MemoryMarshal.AsBytes(view.Items).SequenceEqual(MemoryMarshal.AsBytes(items.AsSpan())));
    }
    internal static IEnumerable<(string, Action)> Cases()
    {
        yield return ("E5-D presenter UTF-8 exact old packet oracle", () => {
            using var owner = new EditorSessionOwner("UTF8");
            var view = new EditorPresenter(new EditorWorkspace(owner), null); var add = Bind(view);
            string[] values = ["", "ASCII", "场景对象/动画", "😀e\u0301", "\0中\0", "\ud800", "\udc00", "\ud800\ud800A\udc00",
                new string('界', 341), new string('a', 1023)];
            var expectedItems = new List<GuiItem>(); var expectedText = new List<byte>();
            foreach (string label in values.Append(new string('a', 4096)).Append(new string('中', 1365) + "a"))
                foreach (string value in values) {
                    byte[] a = Encoding.UTF8.GetBytes(label), b = Encoding.UTF8.GetBytes(value);
                    var item = new GuiItem { Kind = (uint)GuiItemKind.Label, Enabled = 0, WidgetHigh = 7, WidgetLow = 11,
                        LabelOffset = (uint)expectedText.Count, LabelLength = (uint)a.Length,
                        Value = 2.5, Minimum = -1, Maximum = 9 };
                    expectedText.AddRange(a); item.TextOffset = (uint)expectedText.Count; item.TextLength = (uint)b.Length;
                    expectedText.AddRange(b); expectedItems.Add(item); add(label, value);
                }
            Check(view.Text.SequenceEqual(CollectionsMarshal.AsSpan(expectedText)));
            Check(MemoryMarshal.AsBytes(view.Items).SequenceEqual(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(expectedItems))));
            Console.WriteLine($"UTF8 oracle: {view.Items.Length} items; {view.Text.Length} bytes");
        });
        yield return ("E5-D presenter UTF-8 byte bounds reject without mutation", () => {
            using var owner = new EditorSessionOwner("Bounds");
            var view = new EditorPresenter(new EditorWorkspace(owner), null); var add = Bind(view);
            add("keep", "保留");
            RejectUnchanged(view, () => add(new string('a', 4097), ""));
            RejectUnchanged(view, () => add(new string('中', 1366), ""));
            RejectUnchanged(view, () => add("", new string('a', 1024)));
            RejectUnchanged(view, () => add("", new string('中', 342)));
            RejectUnchanged(view, () => add(new string('a', 4094) + "\ud800", ""));
            RejectUnchanged(view, () => add("", new string('a', 1021) + "\udc00"));
            RejectUnchanged(view, () => add(new string('x', 100000), ""));
            add(new string('a', 4093) + "\ud800", new string('a', 1020) + "\udc00");
            Check(view.Items[^1].LabelLength == 4096 && view.Items[^1].TextLength == 1023);
        });
        yield return ("E5-D presenter UTF-8 exact aggregate and item limits", () => {
            using var owner = new EditorSessionOwner("Budget");
            var view = new EditorPresenter(new EditorWorkspace(owner), null); var add = Bind(view);
            string label = new('l', 4096), value = new('v', 1023);
            while (view.Text.Length + (view.Items.Length + 1) * 96 + 5119 <= Budget) add(label, value);
            int remaining = Budget - view.Text.Length - (view.Items.Length + 1) * 96;
            Check(remaining is >= 0 and <= 5119);
            string finalLabel = new('z', Math.Min(4096, remaining)), finalValue = new('q', Math.Max(0, remaining - 4096));
            RejectUnchanged(view, () => add(finalLabel, finalValue + "x"));
            add(finalLabel, finalValue); Check(view.Text.Length + view.Items.Length * 96 == Budget);
            RejectUnchanged(view, () => add("", ""));
            Clear(view);
            for (int i = 0; i < 8192; i++) add("", "");
            Check(view.Items.Length == 8192 && view.Text.Length == 0);
            RejectUnchanged(view, () => add("", ""));
        });
        yield return ("E5-D presenter UTF-8 reuse v2 has no transient array allocation", () => {
            using var owner = new EditorSessionOwner("Reuse");
            var view = new EditorPresenter(new EditorWorkspace(owner), null); var add = Bind(view);
            const string label = "场景 UTF8 😀", value = "Value 123";
            for (int i = 0; i < 512; i++) add(label, value);
            Clear(view);
            long allocated;
            // Human-approved v2: exclude background-GC allocation-context retirement from this
            // allocation-only micro-test. Original 6808B v1 failure remains archived, not reclassified.
            using (NoGcAllocationTestScope.Enter()) {
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 512; i++) add(label, value);
                allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            }
            Console.WriteLine($"UTF8 warm append v2: {allocated} allocated bytes / 512 items (checked NoGCRegion; pre-sized lists, no actions/localization)");
            Check(allocated == 0 && view.Items.Length == 512);
            Check(view.Text.Length == 512 * (Encoding.UTF8.GetByteCount(label) + Encoding.UTF8.GetByteCount(value)));
            Clear(view); add("short", "");
            Check(view.Text.SequenceEqual("short"u8) && view.Items[0].TextOffset == 5 && view.Items[0].TextLength == 0);
        });
    }
}
