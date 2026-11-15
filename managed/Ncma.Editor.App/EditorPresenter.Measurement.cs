namespace Ncma.Editor.App;

internal sealed unsafe partial class EditorPresenter
{
    // Closed human-UI subset. Actual document composition ALSO rejects scripts/non-Transform values.
    // This is not a sandbox for arbitrary trusted C# code. Original local and AI approvals still apply.
    internal bool MeasurementActionAllowed(string kind) => !transientSceneMeasurement ||
        kind.StartsWith("aiui_", StringComparison.Ordinal) || kind is
        "workspace_menu" or "workspace_close_menu" or "workspace_ai" or "workspace_search" or
        "select" or "previous" or "next" or "create" or "name" or "transform" or "declared_field" or
        "component" or "component_page" or "json_previous" or "json_next" or "components_previous" or
        "components_next" or "delete_text" or "delete" or "transaction" or "undo" or "redo" or
        "log_previous" or "log_next" or "log_latest";
}
