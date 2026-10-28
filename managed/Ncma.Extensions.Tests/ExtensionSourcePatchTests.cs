using System.Security.Cryptography;
using System.Text;
using Ncma.Extensions;

internal static class ExtensionSourcePatchTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Source patch contract assertion failed."); }
    private static void Reject(string code, Action action)
    {
        try { action(); } catch (ExtensionRejectedException e) when (e.Code == code) { return; }
        throw new Exception("Expected source patch rejection: " + code);
    }
    private static ExtensionDescriptor Descriptor() => new("test.source", new(1, 2, 3), new(1, 0, 0));
    private static ExtensionSourceStamp Stamp(ulong revision = 7) => new(new("56f1e878-485a-4627-b0d3-4aaa28d28b17"), revision, new string('A', 64));
    private static ExtensionSourceFile File(string path, string text) => ExtensionSourceFile.Present(path, Encoding.UTF8.GetBytes(text));
    private static ExtensionSourceChange Add(string path = "src/Tool.cs", string text = "class Tool {}") => new(ExtensionSourceFile.Absent(path), File(path, text));
    private static ExtensionSourcePatch Patch(params ExtensionSourceChange[] changes) => ExtensionSourcePatch.Create(Descriptor(), Stamp(), changes);

    internal static int Run() => CaseHarness.Run("Exact source patch review model (no file IO or approval)", [
        ("add replace delete and present-empty distinction", () => {
            var add = Add(); var replace = new ExtensionSourceChange(File("B.cs", "before"), File("B.cs", "after"));
            var delete = new ExtensionSourceChange(File("C.cs", ""), ExtensionSourceFile.Absent("C.cs"));
            var patch = Patch(add, replace, delete);
            Check(add.Kind == ExtensionSourceChangeKind.Add && replace.Kind == ExtensionSourceChangeKind.Replace && delete.Kind == ExtensionSourceChangeKind.Delete);
            Check(delete.Before.Sha256 == Convert.ToHexString(SHA256.HashData([])) && delete.After.Sha256 is null);
            patch.RequireCurrent(Stamp(), add.Before, delete.Before, replace.Before);
            Reject("source_content_stale", () => patch.RequireCurrent(Stamp(), File(add.Path, ""), delete.Before, replace.Before));
            Reject("source_change_empty", () => new ExtensionSourceChange(File("A.cs", ""), File("A.cs", "")));
            Reject("source_change_empty", () => new ExtensionSourceChange(ExtensionSourceFile.Absent("A.cs"), ExtensionSourceFile.Absent("A.cs")));
        }),
        ("portable paths reject traversal devices and generated trees", () => {
            foreach (string path in new[] { "", "/A.cs", "C:/A.cs", "\\\\server\\A.cs", "src\\A.cs", "../A.cs", "a/../A.cs", "a//A.cs",
                ".git/A.cs", "a./A.cs", "a..b.cs", "A.cs ", "A.cs:stream", "A.CS", "a?.cs", "a*.cs", "源.cs", "A%2e.cs", "A.cs\n",
                "CON.cs", "a/Nul.x.cs", "COM1.cs", "lPt9/A.cs", "aux/A.cs", new string('a', 238) + ".cs" })
                Reject("source_path_invalid", () => ExtensionSourceFile.Absent(path));
            foreach (string path in new[] { "bin/A.cs", "OBJ/A.cs", "a/Out/A.cs" })
                Reject("source_generated_path", () => ExtensionSourceFile.Absent(path));
            Check(ExtensionSourceFile.Absent("managed/Ncma.Extensions/Tool_1-test.cs").Path.EndsWith(".cs", StringComparison.Ordinal));
            Check(ExtensionSourceFile.Absent(new string('a', 237) + ".cs").Path.Length == 240);
            Reject("source_path_mismatch", () => new ExtensionSourceChange(File("A.cs", "a"), File("a.cs", "b")));
        }),
        ("strict UTF8 and exact BOM Unicode and newline bytes", () => {
            byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("// 中文🙂\r\nclass A { }\n\t// end")];
            var file = ExtensionSourceFile.Present("A.cs", bytes);
            Check(file.CopyBytes().SequenceEqual(bytes) && file.Text[0] == '\uFEFF' && Encoding.UTF8.GetBytes(file.Text).SequenceEqual(bytes));
            Check(file.Sha256 == Convert.ToHexString(SHA256.HashData(bytes)));
            foreach (byte[] malformed in new byte[][] { [0xFF], [0xC0, 0xAF], [0xED, 0xA0, 0x80], [0xE2, 0x82], [0xF4, 0x90, 0x80, 0x80] })
                Reject("source_utf8_invalid", () => ExtensionSourceFile.Present("A.cs", malformed));
            foreach (string bad in new[] { "a\0b", "a\u001Bb", "a\u007Fb" }) Reject("source_text_control", () => File("A.cs", bad));
            var patch = Patch(new ExtensionSourceChange(file, File("A.cs", "new")));
            Reject("source_content_stale", () => patch.RequireCurrent(Stamp(), File("A.cs", file.Text.Replace("\r\n", "\n", StringComparison.Ordinal))));
            // Bidi characters remain bytes, not a claim that raw text is safe to render.
            Check(File("B.cs", "// \u202E").CopyBytes().SequenceEqual(Encoding.UTF8.GetBytes("// \u202E")));
        }),
        ("input and output buffer immutability", () => {
            byte[] bytes = Encoding.UTF8.GetBytes("before"); var before = ExtensionSourceFile.Present("A.cs", bytes); bytes[0] = (byte)'X';
            var change = new ExtensionSourceChange(before, File("A.cs", "after")); var input = new[] { change };
            var patch = Patch(input); string hash = patch.Fingerprint; input[0] = Add("Other.cs");
            byte[] exported = patch.Changes[0].Before.CopyBytes(); exported[0] = (byte)'Y';
            Check(patch.Changes[0].Before.Text == "before" && patch.Changes[0].Before.CopyBytes()[0] == (byte)'b' && patch.Fingerprint == hash);
            bool blocked = false; try { ((IList<ExtensionSourceChange>)patch.Changes)[0] = Add(); } catch (NotSupportedException) { blocked = true; }
            Check(blocked); patch.RequireCurrent(Stamp(), File("A.cs", "before"));
        }),
        ("all exact count and combined byte budgets", () => {
            Reject("source_patch_count", () => Patch());
            var sixteen = Enumerable.Range(0, 16).Select(i => Add($"A{i}.cs", "")).ToArray();
            Check(Patch(sixteen).Changes.Count == 16);
            Reject("source_patch_count", () => Patch([.. sixteen, Add("Extra.cs")]));
            Reject("source_file_budget", () => File("A.cs", new string('a', ExtensionSourceFile.MaxBytes + 1)));
            string block = new('a', ExtensionSourceFile.MaxBytes);
            var first = new ExtensionSourceChange(File("A.cs", block), File("A.cs", new string('b', block.Length)));
            var second = new ExtensionSourceChange(File("B.cs", block), File("B.cs", new string('b', block.Length)));
            Check(Patch(first, second).TotalBytes == ExtensionSourcePatch.MaxTotalBytes);
            Reject("source_patch_budget", () => Patch(first, second, Add("C.cs", "x")));
        }),
        ("duplicates null items and file directory conflicts", () => {
            Reject("source_change_null", () => Patch(new ExtensionSourceChange[] { null! }));
            Reject("source_path_duplicate", () => Patch(Add("A.cs"), Add("A.cs")));
            Reject("source_path_duplicate", () => Patch(Add("A.cs"), Add("a.cs")));
            Reject("source_path_overlap", () => Patch(Add("a.cs/B.cs"), Add("A.cs")));
            Check(Patch(Add("A.cs"), Add("AA.cs"), Add("src/B.cs")).Changes.Count == 3);
        }),
        ("source stamps and complete exact current set", () => {
            foreach (string hash in new[] { "", new string('A', 63), new string('a', 64), new string('G', 64) })
                Reject("source_stamp_invalid", () => new ExtensionSourceStamp(Guid.NewGuid(), 1, hash));
            Reject("source_stamp_invalid", () => new ExtensionSourceStamp(Guid.Empty, 1, new string('A', 64)));
            var a = Add("A.cs"); var b = new ExtensionSourceChange(File("B.cs", "old"), File("B.cs", "new")); var patch = Patch(a, b);
            patch.RequireCurrent(Stamp(), b.Before, a.Before);
            Reject("source_stamp_stale", () => patch.RequireCurrent(Stamp(8), a.Before, b.Before));
            Reject("source_stamp_stale", () => patch.RequireCurrent(new(Guid.NewGuid(), 7, Stamp().InputSha256), a.Before, b.Before));
            Reject("source_stamp_stale", () => patch.RequireCurrent(new(Stamp().WorkspaceSession, 7, new string('B', 64)), a.Before, b.Before));
            Reject("source_capture_scope", () => patch.RequireCurrent(Stamp(), a.Before));
            Reject("source_capture_scope", () => patch.RequireCurrent(Stamp(), a.Before, b.Before, ExtensionSourceFile.Absent("Other.cs")));
            Reject("source_capture_scope", () => patch.RequireCurrent(Stamp(), a.Before, a.Before));
            Reject("source_capture_scope", () => patch.RequireCurrent(Stamp(), a.Before, null!));
            Reject("source_capture_scope", () => patch.RequireCurrent(Stamp(), a.Before, File("b.cs", "old")));
            Reject("source_capture_scope", () => patch.RequireCurrent(Stamp(), a.Before, File("Other.cs", "old")));
            Reject("source_content_stale", () => patch.RequireCurrent(Stamp(), a.Before, b.After));
            Reject("source_content_stale", () => patch.RequireCurrent(Stamp(), a.Before, ExtensionSourceFile.Absent("B.cs")));
            // Failed validation never mutates proposal or earlier captures.
            patch.RequireCurrent(Stamp(), b.Before, a.Before);
        }),
        ("canonical framing independent writer and deterministic order", () => {
            var a = Add("A.cs", "ab"); var b = Add("B.cs", "c");
            var patch = Patch(b, a); Check(patch.Changes[0].Path == "A.cs" && patch.Fingerprint == Patch(a, b).Fingerprint);
            Check(patch.Fingerprint == IndependentDigest(patch));
            Check(patch.Fingerprint != Patch(Add("A.cs", "a"), Add("B.cs", "bc")).Fingerprint);
            Check(Patch(Add("A.cs", "")).Fingerprint != Patch(new ExtensionSourceChange(File("A.cs", ""), ExtensionSourceFile.Absent("A.cs"))).Fingerprint);
        }),
        ("fingerprint binds complete descriptor source and both byte sides", () => {
            var change = new ExtensionSourceChange(File("A.cs", "old"), File("A.cs", "new")); var patch = Patch(change);
            var variants = new[] {
                ExtensionSourcePatch.Create(new("other", new(1,2,3), new(1,0,0)), Stamp(), change),
                ExtensionSourcePatch.Create(new("test.source", new(1,2,4), new(1,0,0)), Stamp(), change),
                ExtensionSourcePatch.Create(new("test.source", new(1,2,3), new(1,1,0)), Stamp(), change),
                ExtensionSourcePatch.Create(new("test.source", new(1,2,3), new(1,0,0), new ExtensionDependency("dep",new(1,0,0),new(1,2,0))), Stamp(), change),
                ExtensionSourcePatch.Create(Descriptor(), Stamp(8), change),
                Patch(new ExtensionSourceChange(File("A.cs", "OLD"), change.After)),
                Patch(new ExtensionSourceChange(change.Before, File("A.cs", "NEW"))),
                Patch(new ExtensionSourceChange(File("B.cs", "old"), File("B.cs", "new"))) };
            foreach (var candidate in variants) Check(candidate.Fingerprint != patch.Fingerprint && candidate.Fingerprint == IndependentDigest(candidate));
        }),
        ("review data exposes no executable authority or model dependency", () => {
            var names = typeof(ExtensionSourcePatch).GetMethods().Select(m => m.Name).ToArray();
            Check(!names.Any(n => n is "Apply" or "Approve" or "Install" or "Execute"));
            Check(!typeof(ExtensionSourcePatch).Assembly.GetReferencedAssemblies().Any(a => a.Name!.StartsWith("Ncma.", StringComparison.Ordinal)));
        })
    ]);

    // Independent BinaryWriter framing (not the production incremental writer).
    private static string IndependentDigest(ExtensionSourcePatch patch)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        void Bytes(byte[] bytes) { writer.Write((ulong)bytes.Length); writer.Write(bytes); }
        void Text(string value) => Bytes(Encoding.UTF8.GetBytes(value));
        void Version(ExtensionVersion v) { writer.Write((ulong)v.Major); writer.Write((ulong)v.Minor); writer.Write((ulong)v.Patch); }
        Text("ncma.extension.source-patch"); writer.Write(1UL);
        Text(patch.Extension.Id); Version(patch.Extension.Version); Version(patch.Extension.MinimumHostApi);
        writer.Write((ulong)patch.Extension.Dependencies.Count);
        foreach (var d in patch.Extension.Dependencies) { Text(d.Id); Version(d.Minimum); Version(d.Maximum); }
        Text(patch.Source.WorkspaceSession.ToString("D")); writer.Write(patch.Source.Revision); Text(patch.Source.InputSha256);
        writer.Write((ulong)patch.Changes.Count);
        foreach (var c in patch.Changes) {
            Text(c.Path); writer.Write(c.Before.Exists ? 1UL : 0UL); Bytes(c.Before.CopyBytes());
            writer.Write(c.After.Exists ? 1UL : 0UL); Bytes(c.After.CopyBytes());
        }
        writer.Flush(); return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }
}
