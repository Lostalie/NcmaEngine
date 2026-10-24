using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Text.Json;
using Ncma.Editor.Core;
using Ncma.Editor.Protocol;
using Ncma.Editor.Transport;
using Ncma.Runtime;
using Ncma.Scene;

internal static class EcsReadTransportCases
{
    internal static void Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new Exception("E4-E IPC: " + label); checks++; }
        var doc = new SceneDocument("ECS IPC");
        for (int i = 0; i < 131; i++)
        {
            var entity = doc.CreateObject("Object " + i, new Guid(500 - i, 0, 0, new byte[8]));
            doc.SetComponent(entity, TransformData.Identity with { Position = new(i, 2, 3) });
        }
        var original = doc.CaptureSnapshot(); var edit = new EditSession(doc);
        using var endpoint = new EditorEndpoint(edit, root);
        var descriptor = Wire.Decode<EndpointDescriptor>(File.ReadAllBytes(endpoint.DescriptorPath));
        using var pipe = new NamedPipeClientStream(".", descriptor.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        pipe.Connect(3000);
        void Until(Func<bool> done) { var timer = Stopwatch.StartNew(); while (!done()) {
            if (timer.Elapsed.TotalSeconds > 10) throw new TimeoutException("E4-E IPC"); endpoint.Pump(); Thread.Sleep(1); } }
        Wire.WriteAsync(pipe, Wire.Encode(new Hello(1, descriptor.InstanceId, root, "ECS joint client")), CancellationToken.None).GetAwaiter().GetResult();
        var helloRead = Wire.ReadAsync(pipe, CancellationToken.None);
        Until(() => endpoint.View.Connections.Length == 1);
        Check(!helloRead.IsCompleted, "pairing remains mandatory");
        endpoint.Pair(endpoint.View.Connections[0].ConnectionId, true); Until(() => helloRead.IsCompleted);
        var hello = Wire.Decode<HelloResult>(helloRead.Result!);
        Check(hello.SessionId == edit.SessionId, "active session identity");
        JsonElement Json(object input) => JsonSerializer.SerializeToElement(input, Wire.Json);
        CapabilityRequest Request(string name, object input) => new(EditSession.ContractVersion, Guid.NewGuid(), edit.SessionId, edit.Revision, name, Json(input));
        IpcResponse Send(CapabilityRequest request, ulong? generation = null)
        {
            Wire.WriteAsync(pipe, Wire.Encode(new IpcRequest("invoke", generation ?? endpoint.View.DocumentGeneration, Json(request), CallId: Guid.NewGuid())), CancellationToken.None).GetAwaiter().GetResult();
            var read = Wire.ReadAsync(pipe, CancellationToken.None); Until(() => read.IsCompleted);
            return Wire.Decode<IpcResponse>(read.Result!);
        }
        JsonElement Invoke(CapabilityRequest request)
        {
            var response = Send(request); Check(response.Code == "ok" && response.Result.HasValue, "bounded invoke response");
            Check(Wire.Encode(response).Length <= EditSession.MaxReadOutputBytes, "bounded response bytes");
            return response.Result!.Value;
        }
        long Captures() => (long)typeof(SceneDocument).GetField("_capturedDocumentCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(doc)!;
        void Pages(SceneDocumentSnapshot expected)
        {
            for (int offset = 0; offset < 131; offset += 128)
            {
                var response = Invoke(Request("ncma.scene.inspect", new { offset, limit = 128 }));
                Check(response.GetProperty("status").GetString() == "ok" && !response.GetProperty("changed").GetBoolean(), "read-only page");
                var data = response.GetProperty("data"); var objects = data.GetProperty("scene").GetProperty("objects").EnumerateArray().ToArray();
                Check(data.GetProperty("totalCount").GetInt32() == 131 && data.GetProperty("documentSessionId").GetGuid() == doc.SessionId && data.GetProperty("documentRevision").GetUInt64() == doc.Revision && data.GetProperty("model").GetString() == "ncma.authoring-scene" && data.GetProperty("modelVersion").GetInt32() == 1, "current authoring model/session/revision/count");
                Check(objects.Length == Math.Min(128, 131 - offset), "page boundary");
                for (int i = 0; i < objects.Length; i++)
                    Check(objects[i].GetProperty("id").GetGuid() == expected.Objects[offset + i].Id && objects[i].GetProperty("name").GetString() == expected.Objects[offset + i].Name, "creation order/current values");
            }
        }
        long before = Captures(); Pages(original); Pages(original);
        Check(Captures() == before, "cold and warm authoring pages never capture the complete document");
        Guid id = original.Objects[129].Id; ulong oldRevision = doc.Revision;
        var stale = Request("ncma.scene.inspect", new { offset = 128, limit = 128 });
        var rename = Request("ncma.scene.transaction", new { operations = new[] { new { op = "rename", objectId = id, name = "Approved" } } });
        Check(Invoke(rename).GetProperty("code").GetString() == "permission_denied" && edit.State.UndoCount == 0, "no implicit write approval");
        endpoint.Approve(endpoint.Proposals.Single(p => p.RequestId == rename.RequestId).Id, true);
        Check(Invoke(rename).GetProperty("changed").GetBoolean() && edit.State.UndoCount == 1, "approved write uses sole history");
        var changed = doc.CaptureSnapshot(); Pages(changed);
        Check(Invoke(stale).GetProperty("code").GetString() == "revision_conflict", "warm proof does not bypass stale revision");
        Check(edit.Invoke(Request("ncma.history.undo", new { }), new(["ncma.scene.transaction", "ncma.history.undo"])).Changed, "user Undo");
        Check(doc.Revision > oldRevision && doc.GetObjectName(id) == original.Objects[129].Name, "Undo invalidates authoring revision and restores the UUID value"); Pages(original);
        Check(Invoke(Request("ncma.history.redo", new { })).GetProperty("changed").GetBoolean(), "remote Redo shares approved history"); Pages(changed);
        var details = Invoke(Request("ncma.scene.object.inspect", new { objectId = id, section = "components", limit = 1 }));
        Check(details.GetProperty("data").GetProperty("items")[0].GetProperty("data").GetRawText() == original.Objects[129].Components[0].Data.GetRawText(), "copied component details after Restore");
        Check(Invoke(Request("ncma.scene.inspect", new { limit = 129 })).GetProperty("code").GetString() == "invalid_input", "page limit cannot be raised remotely");
        ulong oldGeneration = endpoint.View.DocumentGeneration; edit.Resynchronize(); endpoint.Pump();
        Check(Send(Request("ncma.scene.inspect", new { }), oldGeneration).Code == "document_generation_mismatch", "old generation rejected before read");
        Pages(changed); endpoint.Revoke(hello.ConnectionId);
        Check(Send(Request("ncma.scene.inspect", new { })).Code == "connection_revoked", "revoked connection cannot use warm reads");
        Console.WriteLine($"E4-E private pipe ECS reads/history/revocation: {checks} checks passed.");
    }
}
