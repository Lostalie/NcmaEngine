using System.Collections.ObjectModel;
using System.Text.Json;

namespace Ncma.Extensions;

public sealed record ExtensionSourcePage(int FileIndex, bool Before, string Text);

// Complete escaped text, not an excerpt/diff. No rendering of bidi/control text.
// For each file/side concatenate pages to recover one exact JSON string, then
// decode as UTF-8 for the original bytes (including BOM and line endings).
public sealed class ExtensionSourcePages
{
    public const int PageCharacters = 900;
    public ReadOnlyCollection<ExtensionSourcePage> Pages { get; }
    public ExtensionSourcePages(ExtensionSourcePatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        var pages = new List<ExtensionSourcePage>();
        for (int i = 0; i < patch.Changes.Count; i++) {
            Append(i, true, patch.Changes[i].Before);
            Append(i, false, patch.Changes[i].After);
        }
        Pages = pages.AsReadOnly();
        void Append(int index, bool before, ExtensionSourceFile file)
        {
            if (!file.Exists) { pages.Add(new(index, before, "null")); return; }
            string json = JsonSerializer.Serialize(file.Text); // default safe escaping, no relaxed encoder
            int start = 0, at = 0;
            while (at < json.Length) {
                int token = json[at] == '\\' ? json[at + 1] == 'u' ? 6 : 2 : 1;
                if (at - start + token > PageCharacters) { pages.Add(new(index, before, json[start..at])); start = at; }
                at += token;
            }
            pages.Add(new(index, before, json[start..]));
        }
    }
}
