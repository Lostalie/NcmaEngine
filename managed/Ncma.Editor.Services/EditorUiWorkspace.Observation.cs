using System.Text.Json;
using Ncma.Editor.Core;
using Ncma.Ui;

namespace Ncma.Editor.Services;

// A local selection, NOT an export/write grant. Only the original domain send review can
// authorize export to one exact recipient. No capability registration or arbitrary reader.
public sealed class UiDomainObservation
{
    private readonly EditorUiWorkspace workspace;
    internal readonly ulong AssetRevision;
    public DomainProposalSource Source { get; }
    public EditorUiStamp Stamp { get; }
    internal UiDomainObservation(EditorUiWorkspace workspace, DomainProposalSource source,
        EditorUiStamp stamp, ulong assets)
    { this.workspace = workspace; Source = source; Stamp = stamp; AssetRevision = assets; }
    public bool IsCurrent() => workspace.DomainObservationCurrent(this);
    internal bool IdentityCurrent() => workspace.DomainObservationCurrent(this, includeAssets: false);
    internal JsonElement Read() => workspace.ReadDomainObservation(this);
    internal bool BelongsTo(EditorUiWorkspace expected) => ReferenceEquals(workspace, expected);
}

public sealed partial class EditorUiWorkspace
{
    private void RequireDomainSource(DomainProposalSource source)
    {
        Verify(); var stamp = Stamp;
        if (!Current() || HasDraft || DocumentId == Guid.Empty || source.ProjectId != Assets.ProjectId ||
            source.EditSessionId != stamp.Edit.SessionId || source.DocumentGeneration != stamp.Edit.Generation ||
            source.Revision != stamp.Edit.Revision || source.DocumentSessionId != _workspace.Owner.Document.SessionId)
            throw new EditRejectedException("ui_domain_source_mismatch");
    }
    // Explicit exact-document selection. Read-only open is sufficient; it never implies write.
    public UiDomainObservation SelectDomainObservation(DomainProposalSource source, Guid exactDocument)
    {
        RequireDomainSource(source);
        if (exactDocument != DocumentId) throw new EditRejectedException("ui_exact_document_required");
        var result = new UiDomainObservation(this, source, Stamp, Assets.Clock.Revision);
        _ = result.Read(); return result;
    }
    internal bool DomainObservationCurrent(UiDomainObservation observation, bool includeAssets = true)
    {
        try {
            RequireDomainSource(observation.Source);
            return observation.BelongsTo(this) && observation.Stamp == Stamp &&
                (!includeAssets || observation.AssetRevision == Assets.Clock.Revision);
        }
        catch (Exception e) when (e is InvalidOperationException or EditRejectedException) { return false; }
    }
    internal JsonElement ReadDomainObservation(UiDomainObservation observation)
    {
        if (!DomainObservationCurrent(observation)) throw new EditRejectedException("ui_observation_stale");
        try { ValidateFile(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or JsonException) {
            throw new EditRejectedException("ui_observation_source_unavailable");
        }
        using var definition = JsonDocument.Parse(UiCodec.Encode(_definition!));
        var used = UiCodec.Dependencies(_definition!).ToHashSet();
        // Only dependencies of the selected document; no project inventory, source paths,
        // font/image bytes or C# code. Actions are registered semantic identifiers only.
        var data = JsonSerializer.SerializeToElement(new {
            documentId = DocumentId, contentRevision = Revision, resourceRevision = Resources.Revision,
            definition = definition.RootElement,
            resources = Resources.Rows.Where(r => used.Contains(r.Id)).OrderBy(r => r.Id)
                .Select(r => new { id = r.Id, kind = r.Kind.ToString(), hash = r.Hash }).ToArray(),
            registeredActions = RegisteredActions
        });
        if (System.Text.Encoding.UTF8.GetByteCount(data.GetRawText()) > EditorDomainProposals.MaximumBytes)
            throw new EditRejectedException("ui_observation_byte_budget");
        return data;
    }
    // Only releases a host-prepared proposal; no file mutation or history grant.
    public void CancelDomainProposal(Guid proposal) { Verify(); Assets.Ui.CancelProposal(proposal); }
}
