using System.Security.Cryptography;
using Ncma.Application;
using Ncma.Editor.App;

internal static class EditorUserDataTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Editor user-data isolation assertion"); }
    internal static IEnumerable<(string, Action)> Cases(string output, string plugins)
    {
        yield return ("Editor storage errors retain category and HRESULT without exposing raw messages", () => {
            const string privateText = "PRIVATE_PATH_OR_CONTENT_MUST_NOT_BE_PRINTED";
            foreach (Exception cause in new Exception[] { new UnauthorizedAccessException(privateText), new IOException(privateText) }) {
                var failure = EditorUserDataStartupException.From(cause);
                string expected = cause is UnauthorizedAccessException ? "editor_user_data_access_denied" : "editor_user_data_io_failed";
                string text = Ncma.Editor.App.Program.ErrorText(failure, null);
                Check(failure.Code == expected && ReferenceEquals(failure.InnerException, cause) && failure.HResult == cause.HResult);
                Check(text.StartsWith(expected + ";", StringComparison.Ordinal) && text.Contains(cause.GetType().Name) && text.Contains($"0x{cause.HResult:X8}"));
                Check(!text.Contains(privateText) && text.Contains("No fallback or retry was attempted."));
            }
            Check(EditorUserDataStartupException.Format(new InvalidOperationException(privateText)) is null);
            Check(Ncma.Editor.App.Program.ErrorText(new InvalidOperationException("original diagnostic"), null) == "original diagnostic");
            try { EditorUserDataStartupException.From(new InvalidOperationException()); }
            catch (ArgumentException) { return; }
            throw new Exception("Unexpected failure classified as storage IO");
        });
        yield return ("Editor actual log IO failure preserves the obstructing file and does not choose another root", () => {
            string root = Path.Combine(output, "log-parent-is-file"); byte[] bytes = [1, 3, 5, 7]; File.WriteAllBytes(root, bytes);
            string[] before = Directory.GetFileSystemEntries(output).Order(StringComparer.Ordinal).ToArray();
            EditorUserDataStartupException? failure = null;
            try { using var log = EditorUserDataStartupException.OpenLog(root); }
            catch (EditorUserDataStartupException error) { failure = error; }
            Check(failure is not null && failure.InnerException is IOException);
            Check(failure!.Code == "editor_user_data_io_failed" && failure.HResult == failure.InnerException!.HResult);
            Check(!Ncma.Editor.App.Program.ErrorText(failure, null).Contains(root, StringComparison.OrdinalIgnoreCase));
            Check(File.ReadAllBytes(root).SequenceEqual(bytes) && Directory.GetFileSystemEntries(output).Order(StringComparer.Ordinal).SequenceEqual(before));
        });
        yield return ("Editor storage startup diagnosis survives lifecycle failure and reports cleanup failure separately", () => {
            var failure = EditorUserDataStartupException.From(new UnauthorizedAccessException("private storage message"));
            using var lifetime = new ApplicationLifetime(); var service = new FailingStorageService(failure);
            try { lifetime.Start([service]); } catch (EditorUserDataStartupException error) { Check(ReferenceEquals(error, failure)); }
            Check(lifetime.State == ApplicationState.Failed && lifetime.Failure!.Code == "application_initialize_failed");
            Check(service.Starts == 1 && service.Closes == 1 && lifetime.RetainedServiceCount == 0);
            Check(Ncma.Editor.App.Program.ErrorText(failure, lifetime).StartsWith("editor_user_data_access_denied;", StringComparison.Ordinal));
            var combined = new AggregateException(failure, new IOException("private cleanup message"));
            string text = Ncma.Editor.App.Program.ErrorText(combined, lifetime);
            Check(text.EndsWith("startup_cleanup_failed", StringComparison.Ordinal) && !text.Contains("private"));
            Check(EditorUserDataStartupException.Format(new AggregateException(new InvalidOperationException(), failure)) is null);
        });
        yield return ("Editor user data chooses external profile, project state and unique transient runs", () => {
            string app = Path.Combine(output, "immutable-package"), profile = Path.Combine(output, "profile"), temp = Path.Combine(output, "temp"), project = Path.Combine(output, "project");
            Guid first = Guid.NewGuid(), second = Guid.NewGuid();
            Check(EditorUserDataPaths.Resolve(app, null, profile, temp, false, first) == Path.Combine(profile, "NcmaEngine", "Editor"));
            Check(EditorUserDataPaths.Resolve(app, project, profile, temp, false, first) == Path.Combine(project, "out", "user"));
            string a = EditorUserDataPaths.Resolve(app, project, profile, temp, true, first);
            string b = EditorUserDataPaths.Resolve(app, null, profile, temp, true, second);
            Check(a != b && a == Path.Combine(temp, "NcmaEngine", "EditorRuns", first.ToString("N")));
            Check(!Directory.Exists(app) && !Directory.Exists(profile) && !Directory.Exists(temp)); // Resolution does not write.
        });
        yield return ("Editor user data refuses unavailable relative and application-overlapping roots", () => {
            string app = Path.Combine(output, "immutable-package"), profile = Path.Combine(output, "profile"), temp = Path.Combine(output, "temp");
            void Reject(Func<string> action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Unsafe output root accepted"); }
            Reject(() => EditorUserDataPaths.Resolve(app, null, "", temp, false, Guid.NewGuid()));
            Reject(() => EditorUserDataPaths.Resolve(app, null, "relative", temp, false, Guid.NewGuid()));
            Reject(() => EditorUserDataPaths.Resolve(app, null, app, temp, false, Guid.NewGuid()));
            Reject(() => EditorUserDataPaths.Resolve(app, app, app, temp, false, Guid.NewGuid()));
            Reject(() => EditorUserDataPaths.Resolve(app, app, "", temp, false, Guid.NewGuid()));
            Reject(() => EditorUserDataPaths.Resolve(app, app, "relative", temp, false, Guid.NewGuid()));
            Reject(() => EditorUserDataPaths.Resolve(app, "relative", profile, temp, false, Guid.NewGuid()));
            Reject(() => EditorUserDataPaths.Resolve(app, null, profile, temp, false, Guid.Empty));
            Reject(() => EditorUserDataPaths.Resolve(app, null, profile, app, true, Guid.NewGuid()));
        });
        yield return ("Editor bundled projects use stable distinct external state without writing inputs", () => {
            string app = Path.Combine(output, "immutable-package"), profile = Path.Combine(output, "profile"), temp = Path.Combine(output, "temp");
            string Resolve(string project, string application) => EditorUserDataPaths.Resolve(application, project, profile, temp, false, Guid.NewGuid());
            string project = Path.Combine(app, "sample"), first = Resolve(project, app);
            string canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(project));
            string key = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
                OperatingSystem.IsWindows() ? canonical.ToUpperInvariant() : canonical)));
            Check(first == Path.Combine(profile, "NcmaEngine", "Projects", key, "Editor"));
            Check(Resolve(project, app) == first); // Different run IDs do not discard preferences.
            Check(Resolve(Path.Combine(project, ".") + Path.DirectorySeparatorChar, app) == first);
            if (OperatingSystem.IsWindows()) Check(Resolve(project.ToUpperInvariant(), app) == first);
            Check(Resolve(app, app) != first); // Project can be the package root itself.
            Check(Resolve(Path.Combine(app, "sample-two"), app) != first);
            string relocated = Path.Combine(output, "other-package");
            Check(Resolve(Path.Combine(relocated, "sample"), relocated) != first);
            Check(!Directory.Exists(app) && !Directory.Exists(profile) && !Directory.Exists(temp));
        });
        yield return ("Editor storage overlap handles ancestor paths without treating sibling prefixes as packages", () => {
            string app = Path.Combine(output, "immutable-package"), profile = Path.Combine(output, "profile"), temp = Path.Combine(output, "temp");
            string sibling = app + "-project";
            Check(EditorUserDataPaths.Resolve(app, sibling, profile, temp, false, Guid.NewGuid()) == Path.Combine(sibling, "out", "user"));
            string project = Path.Combine(output, "ancestor-project"), nestedApp = Path.Combine(project, "out", "user", "bin");
            string state = EditorUserDataPaths.Resolve(nestedApp, project, profile, temp, false, Guid.NewGuid());
            Check(state.StartsWith(Path.Combine(profile, "NcmaEngine", "Projects") + Path.DirectorySeparatorChar, StringComparison.Ordinal));
            Check(!Directory.Exists(profile));
        });
        foreach (bool cpu in new[] { true, false }) {
            bool cpuOnly = cpu;
            yield return ($"Editor actual {(cpuOnly ? "CPU" : "DX11")} smoke writes external log and preserves application file set", () => {
                // Include pre-existing runtime output in the oracle: do not exclude, delete or
                // bless it. This host must not add to or modify anything in its binary root.
                string[] Inventory() => Directory.GetFiles(AppContext.BaseDirectory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
                var files = Inventory(); var hashes = files.Select(p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))).ToArray();
                using var host = new CandidatePresentation(plugins, null, true, cpuOnly, false);
                host.Start(); string data = host.VerificationUserDataRoot;
                Check(!data.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase));
                for (ulong i = 1; i <= 8; i++) {
                    var frame = new FrameContext(i, 1d / 60); Check(host.PumpPlatform()); host.ApplyEditorIntents(frame);
                    host.PumpAgent(frame); host.BeginPresentation(frame); host.SubmitInput(frame); host.AdvancePlay(frame); host.Present(frame);
                }
                if (!cpuOnly) Check(host.VerificationStats.ValidationErrors == 0 && host.VerificationStats.ValidationWarnings == 0);
                host.Dispose(); host.Dispose();
                string log = Path.Combine(data, "logs/editor-candidate.jsonl");
                Check(File.Exists(log) && new FileInfo(log).Length > 0);
                Check(Inventory().SequenceEqual(files));
                for (int i = 0; i < files.Length; i++) Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(files[i]))) == hashes[i]);
            });
        }
    }
    private sealed class FailingStorageService(Exception failure) : IApplicationService
    {
        internal int Starts, Closes;
        public void Start() { Starts++; throw failure; }
        public void Dispose() { Closes++; }
    }
}
