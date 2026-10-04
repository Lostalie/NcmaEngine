using System.Text;
namespace Ncma.Editor.Services;

public static class InspectionText
{
    public static string[] Split(string source, int maximumBytes = 4*1024*1024)
    {
        if (maximumBytes is < 1 or > 4*1024*1024 || new UTF8Encoding(false,true).GetByteCount(source) > maximumBytes)
            throw new ArgumentException("Inspection text budget exceeded.");
        var chunks = new List<string>(); var current = new StringBuilder(); int bytes = 0;
        foreach (var rune in source.EnumerateRunes()) {
            if (bytes+rune.Utf8SequenceLength > 900) { chunks.Add(current.ToString()); current.Clear(); bytes=0; }
            current.Append(rune.ToString()); bytes += rune.Utf8SequenceLength;
        }
        if (current.Length > 0) chunks.Add(current.ToString());
        return chunks.ToArray();
    }
}
