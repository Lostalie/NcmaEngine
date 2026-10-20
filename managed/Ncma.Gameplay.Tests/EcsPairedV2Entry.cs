using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Ncma.Scene;

// Test-only entry. It uses APIs shared by the verified E2-B products and current products.
// Neither product's old/new test suite is substituted for the other measurement driver.
internal static class EcsPairedV2Entry
{
    private static readonly string[] Products = ["Ncma.Runtime", "Ncma.Scene", "Ncma.Managed", "Ncma.Gameplay", "Ncma.Movement", "Ncma.Editor.Core"];
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    internal static int Run(bool measured)
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
            if (input.GetProperty("schema").GetInt32() != 3 ||
                variant is not ("e2b" or "e4e" or "candidate"))
                throw new InvalidOperationException("Invalid paired identity.");
            string cohort = input.GetProperty("cohort").GetString()!;
            if (cohort is not ("common" or "reads") || (cohort == "reads" && variant == "e2b"))
                throw new InvalidOperationException("Unsupported v2 cohort.");
            if (input.GetProperty("contractId").GetString() != "ecs-e5-c-v2") throw new InvalidOperationException("Wrong v2 contract identity.");
            string? contractHash = input.GetProperty("contractSha256").GetString();
            if (contractHash is null || contractHash.Length != 64 || contractHash.Any(c => !char.IsAsciiHexDigit(c)))
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
            Console.WriteLine(JsonSerializer.Serialize(new { kind = "ecs_e5_paired_environment", schema = 3, variant,
                cohort, contractId = "ecs-e5-c-v2", contractSha256 = contractHash,
                measured, files = hashes, tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
                scope = "Same driver, real product assemblies; CPU workload evidence, not E5/full-engine acceptance" }));
            void Verified(string name, int count, SceneDocument document) {
                // Not part of timed/allocation samples. Checks all persistent values with the real codec.
                byte[] bytes = document.CaptureBytes();
                Console.WriteLine(JsonSerializer.Serialize(new { kind = "ecs_e5_semantic", workload = name, objects = count,
                    tick = document.World.Tick, sceneBytes = bytes.Length, sha256 = Convert.ToHexString(SHA256.HashData(bytes)) }));
            }
            EcsBaselineV2.E5Events = true;
            if (cohort == "common") EcsBaselineV2.Run(measured, Verified);
            else EcsReadBaselineV2.Run(measured, Verified);
            VerifyLoaded(); Console.WriteLine("E5 v2 paired closure verified before and after workloads."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("E5 v2 paired run failed: " + error); return 1; }
    }
}
