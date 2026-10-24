using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Scene;

// E6 diagnostic entry retains the explicit input-file closure checks and workload selectors.
// Historical manifests describe input provenance only; no old/new source parity or E5 acceptance.
internal static class EcsPairedEntry
{
    private static readonly string[] Products = ["Ncma.Runtime", "Ncma.Scene", "Ncma.Managed", "Ncma.Gameplay", "Ncma.Movement", "Ncma.Editor.Core"];
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    internal static int Run(bool measured, bool e5 = false, bool diagnostics = false, bool readDiagnostics = false)
    {
        try
        {
            string root = AppContext.BaseDirectory;
            var required = Products.Select(n => n + ".dll").Concat(new[] {
                "Ncma.Gameplay.Tests.dll", "Ncma.Gameplay.Tests.deps.json", "Ncma.Gameplay.Tests.runtimeconfig.json" }).ToHashSet(StringComparer.Ordinal);
            string manifestPath = Path.Combine(root, "paired-inputs.json");
            if (new FileInfo(manifestPath).Length > 65536) throw new InvalidOperationException("Oversized paired manifest.");
            using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath)); var input = manifest.RootElement;
            string? variant = input.GetProperty("variant").GetString();
            if (input.GetProperty("schema").GetInt32() != (e5 ? 2 : 1) ||
                (e5 ? variant is not ("e2b" or "e4e" or "candidate") : variant is not ("e2b" or "candidate")))
                throw new InvalidOperationException("Invalid paired identity.");
            string cohort = e5 ? input.GetProperty("cohort").GetString()! : "common";
            if (cohort == "common") throw new InvalidOperationException("The E3/E5 common contract predates independent runtime scenes. Use archived products for historical replay; E6 requires its own registered paired contract.");
            if (diagnostics && readDiagnostics) throw new InvalidOperationException("Conflicting diagnostic modes.");
            bool diagnostic = diagnostics || readDiagnostics;
            if (diagnostic ? (!e5 || cohort != (readDiagnostics ? "read-diagnostics" : "dense-diagnostics") || variant == "e2b") :
                (cohort is not ("common" or "reads") || (cohort == "reads" && variant == "e2b")))
                throw new InvalidOperationException("Unsupported measurement cohort.");
            string? contractHash = e5 ? input.GetProperty("contractSha256").GetString() : null;
            if (e5 && (contractHash is null || contractHash.Length != 64 || contractHash.Any(c => !char.IsAsciiHexDigit(c))))
                throw new InvalidOperationException("Missing E5 measurement contract identity.");
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in input.GetProperty("files").EnumerateArray())
            {
                string name = file.GetProperty("name").GetString()!;
                if (!required.Contains(name) || !hashes.TryAdd(name, file.GetProperty("sha256").GetString()!)) throw new InvalidOperationException("Unknown/duplicate paired input.");
                string path = Path.Combine(root, name);
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || new FileInfo(path).Length != file.GetProperty("size").GetInt64() || Hash(path) != hashes[name])
                    throw new InvalidOperationException("Paired input hash/size mismatch.");
            }
            if (hashes.Count != required.Count || Directory.GetDirectories(root).Length != 0 ||
                Directory.GetFiles(root).Select(Path.GetFileName).Any(n => n != "paired-inputs.json" && !required.Contains(n!)))
                throw new InvalidOperationException("Incomplete/extra paired file closure.");
            foreach (string product in Products) _ = Assembly.Load(new AssemblyName(product));
            void VerifyLoaded()
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name!.StartsWith("Ncma.", StringComparison.Ordinal)))
                {
                    string name = assembly.GetName().Name + ".dll";
                    if (!hashes.TryGetValue(name, out string? expected) || !string.Equals(Path.GetFullPath(assembly.Location), Path.Combine(root, name), StringComparison.OrdinalIgnoreCase) || Hash(assembly.Location) != expected)
                        throw new InvalidOperationException("Paired execution loaded an unverified assembly.");
                }
            }
            VerifyLoaded();
            Console.WriteLine(JsonSerializer.Serialize(new { kind = "ecs_e6_diagnostic_inputs", schema = 3, historicalPairedContractCompatible = false, sourceManifestSchema = e5 ? 2 : 1, variant,
                cohort, contractSha256 = contractHash,
                measured, files = hashes, tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
                scope = "E6-adapted real-product diagnostic; historical manifest/workload provenance only, not paired or E5/full-engine acceptance" }));
            void Verified(string name, int count, EcsDiagnosticState state) {
                // Cold semantic evidence names the model actually exercised, never a mirrored document.
                Console.WriteLine(JsonSerializer.Serialize(new { kind = "ecs_e6_diagnostic_semantic", schema = 2,
                    workload = name, objects = count, tick = state.Tick, stateFormat = state.Format, stateVersion = 1,
                    sceneBytes = state.Bytes.Length, sha256 = Convert.ToHexString(SHA256.HashData(state.Bytes)),
                    historicalPairedContractCompatible = false }));
            }
            if (readDiagnostics) EcsReadDiagnostics.Run(measured, Verified);
            else if (diagnostics) EcsDenseDiagnostics.Run(measured, Verified);
            else EcsReadBaseline.Run(measured, Verified);
            VerifyLoaded(); Console.WriteLine("E6 diagnostic loaded-file closure verified; no historical paired acceptance."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("E3 paired run failed: " + error); return 1; }
    }
}
