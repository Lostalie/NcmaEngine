using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ncma.Assets;
using Ncma.Editor.Core;
using Ncma.Editor.Protocol;
using Ncma.Editor.Services;

internal static class AssetInspectionTests
{
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("Asset inspection assertion failed."); }
    private static void Reject(Action action) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return; } throw new Exception("Expected rejection."); }
    private sealed class Clock : TimeProvider { public long Ticks; public override long TimestampFrequency => 1000; public override long GetTimestamp() => Ticks; }
    private sealed class Fixture : IDisposable
    {
        public readonly EditorSessionOwner Owner = new("Asset inspection");
        public readonly Guid Root = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"), Sub = Guid.NewGuid(), Dependency = Guid.NewGuid(), Hidden = Guid.NewGuid();
        public readonly Clock Time = new(); public ulong Revision = 1, Snapshot = 1; public bool Current = true, ThrowVersion;
        public readonly AssetInspectionService Service; public readonly AssetCatalog Catalog;
        public Fixture()
        {
            AssetRecord Record(Guid id, AssetKind kind, string path, SubassetRecord[] sub, AssetDependency[] deps) =>
                new(1, id, kind, path, new string('A', 64), "ufbx", 1, new(1, 60, true), sub, deps, null);
            Catalog = new([Record(Root, AssetKind.Character, "assets/PRIVATE_SOURCE.fbx", [new(Sub, AssetKind.Skeleton, "PRIVATE_KEY", "PRIVATE_NAME", false)], [new(Dependency, AssetKind.Texture)]),
                Record(Dependency, AssetKind.Texture, "assets/PRIVATE_TEXTURE.png", [], []), Record(Hidden, AssetKind.Material, "assets/PRIVATE_MATERIAL.bin", [], [])]);
            Service = new(Owner.Edit!, Guid.NewGuid(), 1, () => Current, () => ThrowVersion ? throw new IOException("C:/PRIVATE_USER/internal") : (Revision, Snapshot), Time);
            Service.Publish(Catalog, [new("source_missing", Root, "C:/PRIVATE_USER/source", "PRIVATE_MESSAGE")], 1); Service.Register();
        }
        public CapabilityResult Call(string name, object input, Guid? requestId = null) => Owner.Edit!.Invoke(new(2, requestId ?? Guid.NewGuid(), Owner.Edit.SessionId, Owner.Edit.Revision, name, JsonSerializer.SerializeToElement(input, Wire.Json)));
        public void Dispose() => Owner.Dispose();
    }
    public static IEnumerable<(string, Action)> Cases(string output, string repository)
    {
        yield return ("M3.8 descriptors and actual success/error outputs satisfy closed bounded schemas", () => {
            using var f = new Fixture(); var descriptors = AssetInspectionSchemas.Descriptors();
            Check(descriptors.Select(d => d.Name).SequenceEqual(new[] { "ncma.assets.list", "ncma.assets.inspect", "ncma.assets.validate" }));
            f.Service.ApproveForPairedClients([f.Root, f.Sub, f.Dependency]);
            // Additive AnimationGraph asset kind; schemas remain closed/default-denied.
            string[] golden = ["12A4FA159403258F1B4A881B7402518B6FF8B147358DFAA7402002CFC6F53260", "8B0E26AA24EBBC13DF8E7B3D2811AD471256B7D5481D40568B35B6D179F506AD", "EAB19A6CFDF820675FF7AC023E9D6FE406C2256F9AAAB4F9C28B6EC2B2C01509"];
            var hashes = descriptors.Select(d => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(d, Wire.Json)))).ToArray();
            Console.WriteLine("Asset descriptor golden: " + string.Join(" ", hashes));
            Check(hashes.SequenceEqual(golden));
            for (int index = 0; index < descriptors.Length; index++) { var d = descriptors[index];
                Check(d.Risk == MutationRisk.ReadOnly && d.Description.Length is > 32 and < 512 && !d.InputSchema.GetProperty("additionalProperties").GetBoolean());
                object input = d.Name switch { "ncma.assets.list" => new { limit = 1 }, "ncma.assets.inspect" => new { assetId = f.Root, section = "subassets" }, _ => new { assetId = f.Root } };
                Schema(JsonSerializer.SerializeToElement(input, Wire.Json), d.InputSchema);
                var result = f.Call(d.Name, input); Check(result.Status == "ok" && !result.Changed); Schema(JsonSerializer.SerializeToElement(result, Wire.Json), d.OutputSchema);
                result = f.Call(d.Name, new { path = "C:/PRIVATE_USER/source", approve = true }); Check(result.Status != "ok"); Schema(JsonSerializer.SerializeToElement(result, Wire.Json), d.OutputSchema);
            }
            Check(f.Owner.Edit!.Describe().Count(d => d.Name.StartsWith("ncma.assets.")) == 3);
            Check(!f.Owner.Edit.Describe().Any(d => d.Name.StartsWith("ncma.prefab.") || d.Name.Contains("import.begin")));
        });
        yield return ("M3.8 default empty scope, exact UUID/subasset scope and path redaction", () => {
            using var f = new Fixture(); byte[] before = f.Owner.Document.CaptureBytes(); var state = f.Owner.Edit!.State;
            Check(f.Call("ncma.assets.list", new { }).Data.GetProperty("total").GetInt32() == 0);
            Check(f.Call("ncma.assets.inspect", new { assetId = f.Root, section = "summary" }).Code == "asset_not_visible");
            f.Service.ApproveForPairedClients([f.Root]);
            Check(f.Call("ncma.assets.inspect", new { assetId = f.Root, section = "subassets" }).Data.GetProperty("total").GetInt32() == 0);
            Check(f.Call("ncma.assets.inspect", new { assetId = f.Sub, section = "summary" }).Code == "asset_not_visible");
            f.Service.ApproveForPairedClients([f.Root, f.Sub, f.Dependency]);
            foreach (string section in new[] { "summary", "subassets", "dependencies" }) {
                var result = f.Call("ncma.assets.inspect", new { assetId = f.Root, section });
                Check(result.Status == "ok" && !result.Data.GetRawText().Contains("PRIVATE", StringComparison.Ordinal) && !result.Data.GetRawText().Contains(f.Hidden.ToString("D")));
            }
            var validate = f.Call("ncma.assets.validate", new { assetId = f.Root }); Check(!validate.Data.GetProperty("valid").GetBoolean() && !validate.Data.GetRawText().Contains("PRIVATE"));
            Check(state == f.Owner.Edit.State && before.SequenceEqual(f.Owner.Document.CaptureBytes()));
            f.ThrowVersion = true; var failure = f.Call("ncma.assets.list", new { }); Check(failure.Code == "asset_inspection_failed" && !failure.Data.GetRawText().Contains("PRIVATE"));
        });
        yield return ("M3.8 monotonic snapshot, expiry/revoke/document/project identity and thread", () => {
            using var f = new Fixture(); f.Service.ApproveForPairedClients([f.Root]);
            Reject(() => f.Service.Publish(f.Catalog, [], 1));
            f.Time.Ticks = 60000; Check(f.Call("ncma.assets.inspect", new { assetId = f.Root, section = "summary" }).Code == "asset_not_visible");
            f.Service.ApproveForPairedClients([f.Root]); f.Service.Revoke(); Check(f.Call("ncma.assets.list", new { }).Data.GetProperty("total").GetInt32() == 0);
            f.Service.ApproveForPairedClients([f.Root]); f.Revision = 2;
            Check(f.Call("ncma.assets.list", new { }).Code == "asset_snapshot_stale"); Reject(() => f.Service.Publish(f.Catalog, [], 2));
            f.Snapshot = 2; f.Service.Publish(f.Catalog, [], 2); Check(f.Call("ncma.assets.list", new { }).Data.GetProperty("total").GetInt32() == 0);
            f.Service.ApproveForPairedClients([f.Root]); new EditorWorkspace(f.Owner).New(new EditorWorkspace(f.Owner).Stamp, true);
            Check(f.Call("ncma.assets.inspect", new { assetId = f.Root, section = "summary" }).Code == "asset_not_visible");
            f.Current = false; Check(f.Call("ncma.assets.list", new { }).Code == "asset_snapshot_stale");
            Task.Run(() => Reject(() => f.Service.Revoke())).GetAwaiter().GetResult();
        });
        yield return ("M3.8 malformed pagination/UUID/duplicates and hidden dependency diagnostics", () => {
            using var f = new Fixture(); f.Service.ApproveForPairedClients([f.Root]);
            foreach (object input in new object[] { new { limit = 0 }, new { limit = 65 }, new { offset = -1 }, new { offset = 65537 }, new { limit = 1.5 }, new { kind = "Unknown" }, new { kind = "environment" }, new { kind = 2 }, new { path = "../outside" }, new { projectId = Guid.NewGuid() } })
                Check(f.Call("ncma.assets.list", input).Status != "ok");
            foreach (object input in new object[] { new { assetId = Guid.Empty, section = "summary" }, new { assetId = f.Root.ToString("D").ToUpperInvariant(), section = "summary" }, new { assetId = f.Root }, new { assetId = f.Root, section = "raw" } })
                Check(f.Call("ncma.assets.inspect", input).Status != "ok");
            using var duplicate = JsonDocument.Parse("{\"limit\":1,\"limit\":2}");
            Check(f.Owner.Edit!.Invoke(new(2, Guid.NewGuid(), f.Owner.Edit.SessionId, f.Owner.Edit.Revision, "ncma.assets.list", duplicate.RootElement.Clone())).Status == "error");
            string longKey = new('x', 32000);
            using var largeDuplicate = JsonDocument.Parse("{\"" + longKey + "\":0,\"" + longKey + "\":1}");
            var bad = f.Owner.Edit.Invoke(new(2, Guid.NewGuid(), f.Owner.Edit.SessionId, f.Owner.Edit.Revision, "ncma.assets.list", largeDuplicate.RootElement.Clone()));
            Check(bad.Status == "error"); Schema(JsonSerializer.SerializeToElement(bad, Wire.Json), AssetInspectionSchemas.Descriptors()[0].OutputSchema);
            var result = f.Call("ncma.assets.validate", new { assetId = f.Root });
            Check(result.Data.GetRawText().Contains("dependency_not_visible") && !result.Data.GetRawText().Contains(f.Dependency.ToString("D")));
            Reject(() => f.Service.ApproveForPairedClients([f.Root, f.Root])); Reject(() => f.Service.ApproveForPairedClients([Guid.NewGuid()]));
            var request = new CapabilityRequest(2, Guid.NewGuid(), f.Owner.Edit.SessionId, f.Owner.Edit.Revision, "ncma.assets.list", JsonSerializer.SerializeToElement(new { }));
            Check(f.Owner.Edit.Invoke(request with { SessionId = Guid.NewGuid() }).Code == "session_mismatch");
            Check(f.Owner.Edit.Invoke(request with { ExpectedRevision = f.Owner.Edit.Revision + 1 }).Code == "revision_conflict");
        });
        yield return ("M3.8 inspection registration batch rejects atomically", () => {
            using var owner = new EditorSessionOwner("Batch"); var edit = owner.Edit!; var descriptors = AssetInspectionSchemas.Descriptors(); int count = edit.Describe().Count;
            Reject(() => edit.RegisterInspections([(descriptors[0], _ => new { }), (descriptors[1] with { Risk = MutationRisk.Reversible }, _ => new { })])); Check(edit.Describe().Count == count);
            Reject(() => edit.RegisterInspections([(descriptors[0], _ => new { }), (descriptors[0], _ => new { })])); Check(edit.Describe().Count == count);
            edit.RegisterInspection(descriptors[2], _ => { owner.Document.World.CreateObject("Forbidden"); return new { }; });
            var result = edit.Invoke(new(2, Guid.NewGuid(), edit.SessionId, edit.Revision, descriptors[2].Name, JsonSerializer.SerializeToElement(new { })));
            Check(result.Status == "error" && owner.Document.World.Count == 0);
        });
        yield return ("M3.8 live paired IPC reads approved metadata, revoked scope blocks repeat IDs", () => {
            using var f = new Fixture(); f.Owner.ConfigureEndpoint(true, output); var endpoint = f.Owner.Endpoint!;
            var descriptor = Wire.Decode<EndpointDescriptor>(File.ReadAllBytes(endpoint.DescriptorPath));
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var pipe = new NamedPipeClientStream(".", descriptor.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            pipe.Connect(3000); Wire.WriteAsync(pipe, Wire.Encode(new Hello(1, descriptor.InstanceId, descriptor.ProjectRoot, "M3.8 read-only test")), stop.Token).GetAwaiter().GetResult();
            var handshake = Wire.ReadAsync(pipe, stop.Token);
            void Until(Func<bool> done) { var clock = Stopwatch.StartNew(); while (!done()) { if (clock.ElapsedMilliseconds > 5000) throw new TimeoutException(); endpoint.Pump(); Thread.Sleep(1); } }
            Until(() => endpoint.View.Connections.Length == 1); endpoint.Pair(endpoint.View.Connections.Single().ConnectionId, true); Until(() => handshake.IsCompleted);
            var hello = Wire.Decode<HelloResult>(handshake.Result!);
            CapabilityResult Remote(CapabilityRequest request) {
                Wire.WriteAsync(pipe, Wire.Encode(new IpcRequest("invoke", hello.DocumentGeneration, JsonSerializer.SerializeToElement(request, Wire.Json))), stop.Token).GetAwaiter().GetResult();
                var pending = Wire.ReadAsync(pipe, stop.Token); Until(() => pending.IsCompleted);
                var response = Wire.Decode<IpcResponse>(pending.Result!); Check(response.Code == "ok"); return response.Result!.Value.Deserialize<CapabilityResult>(Wire.Json)!;
            }
            var request = new CapabilityRequest(2, Guid.NewGuid(), f.Owner.Edit!.SessionId, f.Owner.Edit.Revision, "ncma.assets.inspect", JsonSerializer.SerializeToElement(new { assetId = f.Root, section = "summary" }));
            Check(Remote(request).Code == "asset_not_visible"); f.Service.ApproveForPairedClients([f.Root]); Check(Remote(request).Status == "ok");
            f.Service.Revoke(); Check(Remote(request).Code == "asset_not_visible"); Check(f.Owner.Edit.State.UndoCount == 0);
        });
        yield return ("M3.8 real MCP stdio discovery/schema/call and revocation without Python", () => {
            using var f = new Fixture(); f.Owner.ConfigureEndpoint(true, output); var endpoint = f.Owner.Endpoint!;
            string helper = Path.Combine(repository, "out/managed/editor-mcp/Ncma.Editor.Mcp.dll");
            var launch = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            launch.ArgumentList.Add(helper); launch.ArgumentList.Add("--descriptor"); launch.ArgumentList.Add(endpoint.DescriptorPath);
            using var process = Process.Start(launch)!; var errors = process.StandardError.ReadToEndAsync();
            JsonElement Send(object message) {
                process.StandardInput.WriteLine(JsonSerializer.Serialize(message, Wire.Json)); process.StandardInput.Flush();
                var pending = process.StandardOutput.ReadLineAsync(); var clock = Stopwatch.StartNew();
                while (!pending.IsCompleted) {
                    if (clock.ElapsedMilliseconds > 10000) throw new TimeoutException("MCP stdio fixture timeout.");
                    foreach (var c in endpoint.View.Connections.Where(c => !c.Paired && c.Connected)) endpoint.Pair(c.ConnectionId, true);
                    endpoint.Pump(); Thread.Sleep(1);
                }
                using var doc = JsonDocument.Parse(pending.GetAwaiter().GetResult() ?? throw new IOException("MCP output closed.")); return doc.RootElement.Clone();
            }
            try {
                Check(Send(new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = "2025-11-25" } }).GetProperty("result").GetProperty("protocolVersion").GetString() == "2025-11-25");
                process.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}"); process.StandardInput.Flush();
                var tools = Send(new { jsonrpc = "2.0", id = 2, method = "tools/list", @params = new { } }).GetProperty("result").GetProperty("tools");
                foreach (var d in AssetInspectionSchemas.Descriptors()) {
                    var tool = tools.EnumerateArray().Single(t => t.GetProperty("name").GetString() == d.Name);
                    Check(tool.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean() && tool.GetProperty("outputSchema").GetRawText() == d.OutputSchema.GetRawText());
                }
                Guid requestId = Guid.NewGuid();
                object Call(int id) => new { jsonrpc = "2.0", id, method = "tools/call", @params = new { name = "ncma.assets.inspect", arguments = new { contractVersion = 2, requestId, sessionId = f.Owner.Edit!.SessionId, expectedRevision = f.Owner.Edit.Revision, input = new { assetId = f.Root, section = "summary" } } } };
                Check(Send(Call(3)).GetProperty("result").GetProperty("isError").GetBoolean());
                f.Service.ApproveForPairedClients([f.Root]); var result = Send(Call(4)).GetProperty("result"); Check(!result.GetProperty("isError").GetBoolean());
                Schema(result.GetProperty("structuredContent"), AssetInspectionSchemas.Descriptors()[1].OutputSchema);
                f.Service.Revoke(); Check(Send(Call(5)).GetProperty("result").GetProperty("structuredContent").GetProperty("code").GetString() == "asset_not_visible");
            }
            finally { process.StandardInput.Close(); if (!process.WaitForExit(5000)) process.Kill(true); }
            Check(process.ExitCode == 0 && errors.GetAwaiter().GetResult().Length == 0 && f.Owner.Edit!.State.UndoCount == 0);
        });
    }
    // Small independent test oracle for the schema subset used here; not a general JSON Schema engine.
    private static void Schema(JsonElement value, JsonElement schema)
    {
        if (schema.TryGetProperty("oneOf", out var branches)) { int matched = 0; foreach (var branch in branches.EnumerateArray()) { try { Schema(value, branch); matched++; } catch (InvalidOperationException) { } } Check(matched == 1); return; }
        if (schema.TryGetProperty("anyOf", out var choices)) { foreach (var choice in choices.EnumerateArray()) { try { Schema(value, choice); return; } catch (InvalidOperationException) { } } throw new InvalidOperationException("Schema anyOf failed."); }
        if (schema.TryGetProperty("const", out var constant)) Check(value.GetRawText() == constant.GetRawText());
        if (schema.TryGetProperty("enum", out var values)) Check(values.EnumerateArray().Any(v => v.GetRawText() == value.GetRawText()));
        if (!schema.TryGetProperty("type", out var type)) return;
        switch (type.GetString()) {
            case "object": Check(value.ValueKind == JsonValueKind.Object); var properties = schema.GetProperty("properties");
                foreach (var p in value.EnumerateObject()) { Check(properties.TryGetProperty(p.Name, out var field)); Schema(p.Value, field); }
                foreach (var p in schema.GetProperty("required").EnumerateArray()) Check(value.TryGetProperty(p.GetString()!, out _)); break;
            case "array": Check(value.ValueKind == JsonValueKind.Array && value.GetArrayLength() <= schema.GetProperty("maxItems").GetInt32()); foreach (var item in value.EnumerateArray()) Schema(item, schema.GetProperty("items")); break;
            case "integer": Check(value.ValueKind == JsonValueKind.Number && value.TryGetUInt64(out _)); if (schema.TryGetProperty("minimum", out var min)) Check(value.GetDecimal() >= min.GetDecimal()); if (schema.TryGetProperty("maximum", out var max)) Check(value.GetDecimal() <= max.GetDecimal()); break;
            case "string": Check(value.ValueKind == JsonValueKind.String); if (schema.TryGetProperty("maxLength", out var length)) Check(value.GetString()!.Length <= length.GetInt32()); if (schema.TryGetProperty("pattern", out var pattern)) Check(Regex.IsMatch(value.GetString()!, pattern.GetString()!)); break;
            case "boolean": Check(value.ValueKind is JsonValueKind.True or JsonValueKind.False); break;
            case "null": Check(value.ValueKind == JsonValueKind.Null); break;
            default: throw new InvalidOperationException("Unknown test schema type.");
        }
    }
}
