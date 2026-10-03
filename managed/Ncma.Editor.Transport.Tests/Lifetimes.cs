using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using Ncma.Editor.Core;
using Ncma.Editor.Protocol;
using Ncma.Editor.Transport;
using Ncma.Scene;

internal static class Lifetimes
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Cycle(string root)
    {
        var doc = new SceneDocument("Lifecycle"); var edit = new EditSession(doc); var endpoint = new EditorEndpoint(edit, root);
        string path = endpoint.DescriptorPath; var d = Wire.Decode<EndpointDescriptor>(File.ReadAllBytes(path));
        using (var pipe = new NamedPipeClientStream(".", d.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
        {
            pipe.Connect(3000);
            Wire.WriteAsync(pipe, Wire.Encode(new Hello(1, d.InstanceId, d.ProjectRoot, "Lifetime")), CancellationToken.None).GetAwaiter().GetResult();
            var clock = Stopwatch.StartNew();
            while (endpoint.View.Connections.Length == 0) { if (clock.Elapsed.TotalSeconds > 5) throw new TimeoutException(); Thread.Sleep(1); }
            endpoint.Pair(endpoint.View.Connections[0].ConnectionId, true);
            _ = Wire.ReadAsync(pipe, CancellationToken.None).GetAwaiter().GetResult();
        }
        endpoint.Dispose();
        if (File.Exists(path)) throw new Exception("Owned descriptor leaked");
        return new(endpoint);
    }
    internal static void Run(string root)
    {
        for (int i = 0; i < 8; i++) _ = Cycle(root);
        Thread.Sleep(100); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        using var process = Process.GetCurrentProcess(); process.Refresh(); int initial = process.HandleCount, peak = initial;
        var references = new List<WeakReference>();
        for (int i = 0; i < 32; i++) { references.Add(Cycle(root)); process.Refresh(); peak = Math.Max(peak, process.HandleCount); }
        for (int pass = 0; pass < 20 && references.Any(r => r.IsAlive); pass++)
        { Thread.Sleep(20); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        process.Refresh(); int final = process.HandleCount;
        if (references.Any(r => r.IsAlive) || final > initial + 16) throw new Exception("Bounded IPC lifetime/handle check failed");
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { fixture = "32_connect_pair_disconnect_dispose_after_8_warmup",
            weakEndpointsReleased = references.Count(r => !r.IsAlive), initialHandles = initial, peakHandles = peak, finalHandles = final,
            allowedFinalWarmRuntimeDelta = 16 }));
    }
}
