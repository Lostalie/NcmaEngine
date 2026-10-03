namespace Ncma.Editor.Core;

public sealed record BehaviourExportDescriptor(string Name, uint Kind, string DisplayName, string Category, double DefaultValue);
public sealed record BehaviourTypeDescriptor(string TypeName, BehaviourExportDescriptor[] Exports);
public sealed record BehaviourCatalog(Guid Generation, BehaviourTypeDescriptor[] Types);

public sealed partial class EditSession
{
    private BehaviourCatalog? _catalog;
    public Guid BehaviourCatalogGeneration { get { _document.VerifyAccess(); return _catalog?.Generation ?? Guid.Empty; } }
    public void SetBehaviourCatalog(BehaviourCatalog? catalog)
    {
        _document.VerifyAccess();
        if (_invoking) throw new InvalidOperationException("Session is executing.");
        _catalog = catalog is null ? null : catalog with { Types = catalog.Types.Select(t => t with { Exports = (BehaviourExportDescriptor[])t.Exports.Clone() }).ToArray() };
    }
}
