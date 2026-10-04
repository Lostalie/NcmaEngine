using System.Text;
namespace Ncma.Editor.Services;

// Immutable Unicode-safe slices. A page edit reconstructs the COMPLETE component JSON;
// parsing/validation/Undo/permissions remain in the existing Core interaction path.
public sealed class JsonTextPages
{
    private readonly string _source;
    private readonly (int Start, int Length)[] _pages;
    private readonly int _bytes;
    public const int PageBytes = 900;
    public const int MaxReplacementBytes = 1023;
    private const int MaxBytes = Ncma.Runtime.ComponentRegistry.MaxPayloadBytes;
    public int Count => _pages.Length;
    public JsonTextPages(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _bytes = new UTF8Encoding(false, true).GetByteCount(source);
        if (_bytes is < 1 or > MaxBytes) throw new ArgumentException("Invalid component text budget.");
        _source = source;
        var pages = new List<(int Start, int Length)>(); int start = 0, position = 0, bytes = 0;
        foreach (var rune in source.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > PageBytes)
            { pages.Add((start, position - start)); start = position; bytes = 0; }
            position += rune.Utf16SequenceLength; bytes += rune.Utf8SequenceLength;
        }
        pages.Add((start, position - start)); _pages = pages.ToArray();
    }
    public string Text(int page)
    {
        if ((uint)page >= _pages.Length) throw new ArgumentOutOfRangeException(nameof(page));
        var slice = _pages[page]; return _source.Substring(slice.Start, slice.Length);
    }
    public string Replace(int page, string replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        var current = Text(page); var slice = _pages[page];
        int replacementBytes = new UTF8Encoding(false, true).GetByteCount(replacement);
        if (replacementBytes > MaxReplacementBytes || _bytes - Encoding.UTF8.GetByteCount(current) + replacementBytes > MaxBytes)
            throw new ArgumentException("Component page edit budget exceeded.");
        return string.Concat(_source.AsSpan(0, slice.Start), replacement.AsSpan(), _source.AsSpan(slice.Start + slice.Length));
    }
}
