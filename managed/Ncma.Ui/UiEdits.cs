namespace Ncma.Ui;

public enum UiEditKind { Add, Replace, RemoveSubtree, Reparent, SetToken, RemoveToken }
public sealed record UiEdit(UiEditKind Kind, Guid Target, UiElement? Element = null,
    UiToken? Token = null, Guid Parent = default, int Order = 0);

// Pure bounded candidate preparation. No live mutation, history, file IO or Agent permissions.
public static class UiEdits
{
    public const int MaxOperations = 256;
    public static UiDefinition Apply(UiDefinition source, IReadOnlyList<UiEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        if (edits.Count is < 1 or > MaxOperations) throw new ArgumentException("UI operation budget.");
        var copy = UiCodec.Copy(source);
        var elements = copy.Elements.ToDictionary(e => e.Id); var tokens = copy.Tokens.ToDictionary(t => t.Id);
        foreach (var op in edits) {
            if (op is null || op.Target == Guid.Empty || !Enum.IsDefined(op.Kind)) throw new ArgumentException("UI operation identity/kind.");
            switch (op.Kind) {
                case UiEditKind.Add:
                    RequireElement(); if (!elements.TryAdd(op.Target, op.Element!)) throw new ArgumentException("Existing UI element."); break;
                case UiEditKind.Replace:
                    RequireElement(); if (!elements.ContainsKey(op.Target)) throw new ArgumentException("Missing UI element."); elements[op.Target] = op.Element!; break;
                case UiEditKind.RemoveSubtree:
                    if (op.Target == copy.Root || !elements.ContainsKey(op.Target)) throw new ArgumentException("UI root/missing deletion.");
                    var removed = new HashSet<Guid> { op.Target };
                    for (int depth = 0; depth < UiCodec.MaxDepth; depth++) {
                        var children = elements.Values.Where(e => removed.Contains(e.Parent) && !removed.Contains(e.Id)).Select(e => e.Id).ToArray();
                        if (children.Length == 0) break; removed.UnionWith(children);
                    }
                    foreach (Guid id in removed) elements.Remove(id); break;
                case UiEditKind.Reparent:
                    if (op.Target == copy.Root || !elements.TryGetValue(op.Target, out var element)) throw new ArgumentException("UI root/missing reparent.");
                    elements[op.Target] = element with { Parent = op.Parent, Order = op.Order }; break;
                case UiEditKind.SetToken:
                    if (op.Token is null || op.Token.Id != op.Target) throw new ArgumentException("Token identity."); tokens[op.Target] = op.Token; break;
                case UiEditKind.RemoveToken:
                    if (!tokens.Remove(op.Target)) throw new ArgumentException("Missing token."); break;
            }
            void RequireElement() { if (op.Element is null || op.Element.Id != op.Target) throw new ArgumentException("Element identity."); }
            // Bound intermediate work as well as the final document. Final reference validation is
            // atomic, so token replacement/reparent edits may temporarily break references.
            if (elements.Count > UiCodec.MaxElements || tokens.Count > UiCodec.MaxTokens) throw new ArgumentException("UI intermediate budget.");
        }
        return UiCodec.Copy(copy with { Elements = elements.Values.ToArray(), Tokens = tokens.Values.ToArray() });
    }
}
