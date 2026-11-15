using System.Text.Json;

namespace Ncma.Editor.Services;

internal sealed record WorkflowTestRequest(string TaskFingerprint, string RecipeId, string InventorySha256, string[] Inventory);

internal static class WorkflowTestRequestCodec
{
    internal const int MaximumBytes = 32768;
    internal static byte[] Encode(WorkflowVerificationTask task)
    {
        if (task.Recipe.Kind != WorkflowVerificationKind.Tests ||
            !WorkflowEvidenceRules.Inventory(WorkflowTestInventory.Copy(task.Recipe.Id)).SequenceEqual(task.CopyTestInventory(), StringComparer.Ordinal))
            throw new IOException("workflow_test_inventory_required");
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new { version = 1, taskFingerprint = task.Fingerprint,
            recipeId = task.Recipe.Id, inventorySha256 = task.InventorySha256, inventory = task.CopyTestInventory() });
        if (bytes.Length > MaximumBytes) throw new IOException("workflow_test_request_budget"); return bytes;
    }
    internal static WorkflowTestRequest Read(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length is < 2 or > MaximumBytes) throw new IOException("workflow_test_request_budget");
        using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 }); var value = json.RootElement;
        WorkflowCaptureBridge.Shape(value, "version", "taskFingerprint", "recipeId", "inventorySha256", "inventory");
        if (value.GetProperty("version").GetInt32() != 1) throw new IOException("workflow_test_request_version");
        string fingerprint = value.GetProperty("taskFingerprint").GetString()!, recipe = value.GetProperty("recipeId").GetString()!, hash = value.GetProperty("inventorySha256").GetString()!;
        WorkflowEvidenceRules.Hash(fingerprint); WorkflowEvidenceRules.Hash(hash);
        string[] inventory = WorkflowEvidenceRules.Inventory(value.GetProperty("inventory").EnumerateArray().Select(x => x.GetString()!));
        if (WorkflowEvidenceRules.Digest(inventory) != hash || !inventory.SequenceEqual(WorkflowEvidenceRules.Inventory(WorkflowTestInventory.Copy(recipe)), StringComparer.Ordinal))
            throw new IOException("workflow_test_request_inventory");
        return new(fingerprint, recipe, hash, inventory);
    }
}
