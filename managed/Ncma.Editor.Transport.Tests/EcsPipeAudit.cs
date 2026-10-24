using System.Security.Cryptography;
using System.Text.Json;

// Independent file inspection AFTER timing. Record's actual product IO stays enabled.
internal static class EcsPipeAudit
{
    internal sealed record Proof(bool ProductionAuditIncluded, Guid Instance, Guid Session, Guid Connection, int RequestsVerified, object[] Files);

    internal static Proof Verify(string descriptorPath, Guid instance, Guid session, Guid connection, IReadOnlyList<Guid> requests)
    {
        string path = Path.Combine(Path.GetDirectoryName(descriptorPath)!, "audit-" + instance.ToString("N") + ".jsonl");
        var files = new List<object>(); var actual = new List<Guid>();
        for (int i = 3; i >= 0; i--) {
            string file = path + (i == 0 ? "" : "." + i);
            if (!File.Exists(file)) continue;
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("Reparse audit file");
            foreach (string line in File.ReadLines(file)) {
                using var json = JsonDocument.Parse(line); var row = json.RootElement;
                if (row.GetProperty("instance").GetGuid() != instance || row.GetProperty("session").GetGuid() != session ||
                    row.GetProperty("connection").GetGuid() != connection) throw new Exception("Audit identity differs");
                actual.Add(row.GetProperty("requestId").GetGuid());
            }
            files.Add(new { path = file, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))) });
        }
        if (!actual.SequenceEqual(requests)) throw new Exception("Production audit missing, reordered or duplicated requests");
        return new(true, instance, session, connection, actual.Count, files.ToArray());
    }
}
