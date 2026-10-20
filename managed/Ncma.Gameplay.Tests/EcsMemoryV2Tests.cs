using System.Text.Json;

internal static class EcsMemoryV2Tests
{
    internal static void Run()
    {
        foreach (long value in new[] { long.MinValue, -912528L, -1L, 0L, 1L, long.MaxValue })
        {
            var snapshot = EcsMemoryV2.Capture(value);
            string expected = value < 0 ? "invalid-runtime-reading" : "valid-estimate";
            if (snapshot.status != expected) throw new InvalidOperationException("v2 counter classification changed.");
            var json = JsonSerializer.SerializeToElement(new { raw = value, memory = snapshot });
            if (json.GetProperty("raw").GetInt64() != value ||
                json.GetProperty("memory").GetProperty("status").GetString() != expected ||
                json.GetProperty("memory").GetProperty("lastCollection").GetProperty("scope").GetString() != EcsMemoryV2.Scope)
                throw new InvalidOperationException("v2 memory output lost raw value or scope.");
        }
        Console.WriteLine("E5 v2 memory telemetry checks: 6 passed; no memory acceptance.");
    }
}
