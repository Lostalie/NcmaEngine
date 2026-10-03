using System.Text.Json;
using Ncma.Runtime;
namespace Ncma.Editor.Core;
public sealed partial class EditSession
{
    public CapabilityResult InvokeJson(ReadOnlySpan<byte> utf8, CapabilityPermissions? permissions = null)
    {
        _document.VerifyAccess();
        try
        {
            if (utf8.Length is 0 or > MaxInputBytes) throw new ArgumentException("Request size exceeds the envelope budget.");
            using var json = JsonDocument.Parse(utf8.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
            SceneJson.RejectDuplicateFields(json.RootElement);
            var request = json.RootElement.Deserialize<CapabilityRequest>(SceneJson.Options) ?? throw new ArgumentException("Missing request.");
            return Invoke(request, permissions);
        }
        catch (Exception e) when (e is ArgumentException or JsonException or FormatException or InvalidOperationException)
        {
            return Result(new(ContractVersion, Guid.Empty, SessionId, null, "", Json("{}")),
                "error", "invalid_input", false, new { message = e.Message });
        }
    }
    public static string EncodeResult(CapabilityResult result) => JsonSerializer.Serialize(result, OutputJson);
}
