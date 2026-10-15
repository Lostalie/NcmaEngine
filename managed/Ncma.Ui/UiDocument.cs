namespace Ncma.Ui;

// Immutable owned runtime document. Editing lives in an Editor.Core participant, not here.
// Runtime widget state is per-instance; no World/GameObject ownership or scene transform inheritance.
public sealed class UiDocument
{
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly UiDefinition _definition;
    public Guid InstanceId { get; } = Guid.NewGuid();
    public UiDocument(UiDefinition definition) => _definition = UiCodec.Copy(definition);
    // This private graph was validated (including canonical wire size) at construction.
    // Copy arrays only; no mutable record state escapes through Capture or runtime metrics.
    public UiDefinition Capture() { VerifyAccess(); return UiCodec.CopyOwned(_definition); }
    public byte[] CaptureBytes() { VerifyAccess(); return UiCodec.Encode(_definition); }
    internal UiDefinition Definition { get { VerifyAccess(); return _definition; } }
    public void VerifyAccess() { if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("UI owner thread required."); }
}
