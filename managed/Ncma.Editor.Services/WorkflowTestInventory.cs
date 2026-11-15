namespace Ncma.Editor.Services;

// One catalogue used by human review AND the actual fixed test registrations.
// This is metadata only, never permission to execute a case or a test assembly.
public static class WorkflowTestInventory
{
    private static readonly string[] contracts = [
        "M8.6 explicit offline restore identity never counts as compilation or tests",
        "M8.6-B fixed recipes distinguish compilation and full declared test coverage",
        "M8.6-B every identity/configuration/tool/capture change invalidates exact task binding",
        "M8.6-B missing duplicate extra and unknown case results are rejected",
        "M8.6-B skipped blocked unrun cancelled and partial results never pass",
        "M8.6-B changed input closure missing products and logs fail closed",
        "M8.6-B portable paths aliases directories hashes and byte counts are checked",
        "M8.6-B bounded enumeration and detached immutable snapshots"
    ];
    private static readonly string[] host = [
        "M8.6-A exact audit chain, copied data, owner thread and stable completed outcome",
        "M8.6-A independent approval waits do not inflate audit; cancellation preserves committed work",
        "M8.6-A full step/repair budget retains all original plans and receipts",
        "M8.6-A expiration, audience revocation and revision conflict record exact stop once",
        "M6.9 post-commit observer fault preserves original receipt and fail-stops subsequent execution",
        "M6.9 real stdio default deny/exact tickets/two approvals/revision receipts/no automatic Undo",
        "M6.9 failed actual isolated assertion blocks successor/repair cannot alter assertion policy",
        "M6.9 two active plans/foreign owner/registry tamper and stale review rejection",
        "M6.9 actual NCA graph propose/approved transaction/validation/isolated assertion workflow",
        "M6.9 cancel queued approved mutation retains tombstone and commits nothing",
        "M6.9 deadline/TTL/revoke-repair/audience/edit/endpoint/foreign ticket fences",
        "M6.9 finite repair/fresh complete approval/original receipts/copied data and closed budgets",
        "M6.9 full UI-page guard/same MCP ledger and actual foreground ImGui",
        "M6.9 owner/monitor observation forbids live writes/nested invocation and preserves editor-only registration"
    ];
    private static string[] Items(string recipe) => recipe switch {
        "workflow-contracts-v1" => contracts,
        "workflow-host-v1" => host,
        _ => throw new IOException("workflow_test_recipe_unknown") };
    public static string[] Copy(string recipe) => (string[])Items(recipe).Clone();
    public static int Count(string recipe) => Items(recipe).Length;
    public static string Name(string recipe, int index) => Items(recipe)[index];
}
