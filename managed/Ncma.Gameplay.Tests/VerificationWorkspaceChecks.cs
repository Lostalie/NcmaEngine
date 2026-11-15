using Ncma.Testing;

internal static class VerificationWorkspaceChecks
{
    internal static void Run()
    {
        string parent = Path.Combine(Path.GetTempPath(), "NcmaEngine", "VerificationRouting", Guid.NewGuid().ToString("N"));
        string source = Path.Combine(parent, "captured-source"), runtime = Path.Combine(parent, "runtime");
        Directory.CreateDirectory(source); Directory.CreateDirectory(runtime);
        static void Check(bool value) { if (!value) throw new InvalidOperationException("Verification routing assertion."); }
        static void Reject(string[] input)
        {
            string[] original = (string[])input.Clone(); bool rejected = false;
            try { VerificationWorkspace.Extract(ref input); }
            catch (Exception e) when (e is ArgumentException or IOException) { rejected = true; }
            Check(rejected && input.SequenceEqual(original));
        }
        CaseHarness.RunOrThrow("Verification runtime routing", [
            ("explicit runtime does not replace source, configuration or mode", () => {
                string[] args = [source, "Release", "plugins", "--extension-player-tests", "--verification-workspace", runtime];
                Check(VerificationWorkspace.Extract(ref args) == runtime);
                Check(args.SequenceEqual(new[] { source, "Release", "plugins", "--extension-player-tests" }));
                Check(!Directory.EnumerateFileSystemEntries(runtime).Any());
            }),
            ("ordinary invocation keeps its original source root", () => {
                string[] args = [source, "Debug"]; string[] original = args;
                Check(VerificationWorkspace.Extract(ref args) == source && ReferenceEquals(args, original));
            }),
            ("invalid routing rejects without changing original argv", () => {
                Reject([source, "--verification-workspace"]);
                Reject([source, "--verification-workspace", "relative"]);
                Reject([source, "--verification-workspace", Path.Combine(parent, "absent")]);
                Reject([source, "--verification-workspace", runtime, "extra"]);
                Reject([source, "--verification-workspace", runtime, "--verification-workspace", runtime]);
                Reject(["--verification-workspace", runtime]);
                Reject([]);
            })
        ]);
    }
}
