namespace Ncma.Assets;

// Portable lexical policy. Authoring additionally verifies real filesystem entries.
public static class AssetPaths
{
    public const int MaxLength = 1024;
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
      "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "CONIN$", "CONOUT$" };

    public static string Validate(string path, string requiredRoot = "assets")
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Length is < 1 or > MaxLength || !path.StartsWith(requiredRoot + "/", StringComparison.Ordinal) ||
            path.Contains('\\') || path.Any(c => char.IsControl(c) || c is ':' or '*' or '?' or '"' or '<' or '>' or '|'))
            throw new ArgumentException("Expected a canonical project-relative asset path.");
        foreach (string part in path.Split('/'))
        {
            if (part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' ') ||
                part.Length > 255 || Reserved.Contains(part.Split('.')[0]))
                throw new ArgumentException("Invalid asset path segment.");
        }
        return path;
    }
}
