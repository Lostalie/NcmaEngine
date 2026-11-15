namespace Ncma.Testing;

// Test outputs only: exclusive OS-temp leaves, retained after success and failure.
// Never recursively clean these roots (some cases deliberately create links).
internal static class RetainedFixtureDirectory
{
    internal static string Create(string category)
    {
        if (category is not ("assets" or "import" or "prefab"))
            throw new ArgumentException("Unknown fixture category.", nameof(category));
        RequireOutsideApplication(Path.GetTempPath());
        string root = Directory.CreateTempSubdirectory("NcmaFixture-" + category + "-").FullName;
        RequireOutsideApplication(root);
        Console.WriteLine("Retained fixture: " + root);
        return root;
    }

    private static void RequireOutsideApplication(string path)
    {
        string application = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (full.Equals(application, comparison) || full.StartsWith(application + Path.DirectorySeparatorChar, comparison))
            throw new InvalidOperationException("Fixture output overlaps immutable application inputs.");
    }

    internal static void VerifyIsolation()
    {
        string first = Create("assets"), second = Create("assets");
        if (first == second || !Directory.Exists(first) || !Directory.Exists(second))
            throw new InvalidOperationException("Fixture directories must be distinct existing leaves.");
        foreach (string path in new[] { AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "nested") })
        {
            bool rejected = false;
            try { RequireOutsideApplication(path); } catch (InvalidOperationException) { rejected = true; }
            if (!rejected) throw new InvalidOperationException("Immutable input overlap accepted.");
        }
        bool invalid = false;
        try { Create("../invalid"); } catch (ArgumentException) { invalid = true; }
        if (!invalid) throw new InvalidOperationException("Invalid fixture category accepted.");
    }
}
