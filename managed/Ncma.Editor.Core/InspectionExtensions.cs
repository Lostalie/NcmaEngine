using System.Text.Json;
namespace Ncma.Editor.Core;
public sealed partial class EditSession
{
    private readonly Dictionary<string,Func<JsonElement,object>> _inspections = new(StringComparer.Ordinal);
    // Prevalidate a trusted startup batch before registering any descriptor.
    public void RegisterInspections((CapabilityDescriptor Descriptor, Func<JsonElement,object> Inspect)[] entries)
    {
        _document.VerifyAccess(); ArgumentNullException.ThrowIfNull(entries);
        if (_invoking || _frozen || InteractionBusy || entries.Length == 0 || entries.Length > 16 - _inspections.Count)
            throw new InvalidOperationException("Inspection batch requires idle trusted startup and available capacity.");
        if (entries.Any(e => e.Descriptor is null || e.Inspect is null) || entries.Select(e => e.Descriptor.Name).Distinct(StringComparer.Ordinal).Count() != entries.Length)
            throw new ArgumentException("Invalid inspection batch.");
        foreach (var (descriptor, _) in entries) ValidateInspection(descriptor);
        foreach (var (descriptor, inspect) in entries) RegisterInspection(descriptor, inspect);
    }
    private void ValidateInspection(CapabilityDescriptor descriptor)
    {
        if(descriptor.Risk!=MutationRisk.ReadOnly || string.IsNullOrWhiteSpace(descriptor.Name) || descriptor.Name.Length>128 ||
            string.IsNullOrWhiteSpace(descriptor.Description) || descriptor.Description.Length>512 ||
            descriptor.InputSchema.ValueKind!=JsonValueKind.Object || descriptor.OutputSchema.ValueKind!=JsonValueKind.Object ||
            descriptor.InputSchema.GetRawText().Length>MaxInputBytes || descriptor.OutputSchema.GetRawText().Length>MaxInputBytes ||
            _capabilities.ContainsKey(descriptor.Name))throw new ArgumentException("Invalid/duplicate read-only inspection.");
    }
    // Trusted composition only, never a capability. Extension cannot install mutations or eval tools.
    public void RegisterInspection(CapabilityDescriptor descriptor,Func<JsonElement,object> inspect)
    {
        _document.VerifyAccess(); ArgumentNullException.ThrowIfNull(descriptor);ArgumentNullException.ThrowIfNull(inspect);
        if(_invoking || _frozen || InteractionBusy || _inspections.Count>=16)throw new InvalidOperationException("Inspection registration requires idle trusted startup.");
        ValidateInspection(descriptor);
        var copy=descriptor with{InputSchema=descriptor.InputSchema.Clone(),OutputSchema=descriptor.OutputSchema.Clone()};
        _capabilities.Add(copy.Name,copy);_inspections.Add(copy.Name,inspect);
    }
    private CapabilityResult InspectExtension(CapabilityRequest request)
    {
        object value;
        using(_document.World.ReadOnly()) value=_inspections[request.Capability](request.Input);
        if(JsonSerializer.SerializeToUtf8Bytes(value,OutputJson).Length>256*1024-4096)
            return Result(request,"error","item_too_large",false,new{});
        return Result(request,"ok","ok",false,value);
    }
}
