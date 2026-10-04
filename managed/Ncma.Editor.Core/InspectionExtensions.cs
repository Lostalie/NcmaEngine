using System.Text.Json;
namespace Ncma.Editor.Core;
public sealed partial class EditSession
{
    private readonly Dictionary<string,Func<JsonElement,object>> _inspections = new(StringComparer.Ordinal);
    // Trusted composition only, never a capability. Extension cannot install mutations or eval tools.
    public void RegisterInspection(CapabilityDescriptor descriptor,Func<JsonElement,object> inspect)
    {
        _document.VerifyAccess(); ArgumentNullException.ThrowIfNull(descriptor);ArgumentNullException.ThrowIfNull(inspect);
        if(_invoking || _frozen || _draft is not null || _inspections.Count>=16)throw new InvalidOperationException("Inspection registration requires idle trusted startup.");
        if(descriptor.Risk!=MutationRisk.ReadOnly || string.IsNullOrWhiteSpace(descriptor.Name) || descriptor.Name.Length>128 ||
            string.IsNullOrWhiteSpace(descriptor.Description) || descriptor.Description.Length>512 ||
            descriptor.InputSchema.ValueKind!=JsonValueKind.Object || descriptor.OutputSchema.ValueKind!=JsonValueKind.Object ||
            descriptor.InputSchema.GetRawText().Length>MaxInputBytes || descriptor.OutputSchema.GetRawText().Length>MaxInputBytes ||
            _capabilities.ContainsKey(descriptor.Name))throw new ArgumentException("Invalid/duplicate read-only inspection.");
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
