namespace Ncma.Scene;

// The only scene asset format. No legacy sniffing, conversion or fallback reader.
public static class SceneDocumentFiles
{
    public const string Extension = ".ncmascene";

    public static string ResolvePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string full = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(full), Extension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Scene assets must use the .ncmascene extension.");
        return full;
    }

    private static byte[] Read(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length is <= 0 or > SceneDocumentCodec.MaxBytes)
            throw new ArgumentException("Scene asset exceeds the document size limit or is empty.");
        var bytes = new byte[(int)file.Length];
        file.ReadExactly(bytes);
        if (file.ReadByte() != -1) throw new ArgumentException("Scene asset changed while reading.");
        return bytes;
    }

    // Read a bounded, strictly versioned candidate without installing it into any live document.
    public static byte[] ReadBytes(string path)
    {
        byte[] bytes = Read(ResolvePath(path));
        _ = SceneDocumentCodec.Decode(bytes);
        return bytes;
    }

    public static void Load(SceneDocument document, string path)
    {
        ArgumentNullException.ThrowIfNull(document);
        _ = document.CaptureSnapshot(); // Check owner thread and committed boundary before IO.
        document.RestoreBytes(Read(ResolvePath(path))); // Validation and installation are atomic.
    }

    public static void Save(SceneDocument document, string path)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.ValidateAuthoring(); // Shared composition policy, before creating or replacing a file.
        byte[] bytes = document.CaptureBytes(); // Validate the complete payload before touching disk.
        string full = ResolvePath(path);
        if (File.Exists(full)) _ = SceneDocumentCodec.Decode(Read(full)); // Never overwrite an unsupported format.
        string directory = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, "." + Path.GetFileName(full) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(bytes);
                file.Flush(true);
            }
            if (File.Exists(full)) File.Replace(temporary, full, null);
            else File.Move(temporary, full);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary); // Only this operation's own temporary file.
        }
    }
}
