using System.Globalization;
using Ncma.Editor.Core;
using Ncma.Gui;
using Ncma.Scene;
using Ncma.Scripting;

namespace Ncma.Editor.App;

internal sealed unsafe partial class EditorPresenter
{
    private sealed record ExportTarget(Guid Catalog, Guid Binding, string TypeName, string Name, uint Kind);
    private void BuildDeclaredExports(Guid objectId, BehaviourBindingData binding, ScriptCatalogSnapshot catalog,
        int offset, bool writable, ref ulong control, ref ulong labelId)
    {
        var type = catalog.Types.FirstOrDefault(t => t.TypeName == binding.TypeName);
        var declarations = type?.Exports.ToDictionary(e => e.Name, StringComparer.Ordinal);
        foreach (var export in binding.Exports.Skip(offset).Take(16)) {
            if (declarations is null || !declarations.TryGetValue(export.Name, out var declaration) || declaration.Kind != (uint)export.Kind) {
                Add(GuiItemKind.Label, 3, labelId++, export.Name + " (Export declaration unavailable)"); continue;
            }
            bool boolean = declaration.Kind == (uint)ExportKind.Boolean;
            Add(boolean ? GuiItemKind.Checkbox : GuiItemKind.Text, 2, control++, declaration.Category + " / " + declaration.DisplayName,
                new("export", objectId, Operation: new ExportTarget(catalog.Generation, binding.Id, binding.TypeName, declaration.Name, declaration.Kind)),
                boolean ? "" : export.Value.ToString("R", CultureInfo.InvariantCulture), number: boolean ? export.Value : 0, max: 1, enabled: writable);
        }
    }
    private void RequireExportCatalog(ActionView action)
    {
        var current = workspace.Owner.Catalog.Snapshot;
        // Generation is not part of EditorViewStamp. Reject even before the next Build has
        // noticed a catalog replacement; cancel any old draft through the shared error path.
        if (current.Generation != _lastCatalog || workspace.Owner.Edit!.BehaviourCatalogGeneration != current.Generation)
            throw new EditRejectedException("stale_catalog_view");
        if (action.Kind == "attach") return;
        if (action.Operation is not ExportTarget target || target.Catalog != current.Generation ||
            !current.Types.Any(t => t.TypeName == target.TypeName && t.Exports.Any(e => e.Name == target.Name && e.Kind == target.Kind)))
            throw new EditRejectedException("export_declaration_unavailable");
    }
    private object DeclaredExportOperation(ActionView action, string text, double number)
    {
        RequireExportCatalog(action);
        var target = (ExportTarget)action.Operation!;
        var binding = _page!.Selected!.Behaviours.Single(b => b.Id == target.Binding && b.TypeName == target.TypeName);
        _ = binding.Exports.Single(e => e.Name == target.Name && (uint)e.Kind == target.Kind);
        if (target.Kind != (uint)ExportKind.Boolean && !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            throw new ArgumentException("Expected invariant numeric Export text.");
        number = Ncma.ExportNumbers.Require(number, target.Kind);
        return new { op = "set_export", objectId = action.Object, bindingId = target.Binding, name = target.Name, kind = target.Kind, value = number };
    }
}
