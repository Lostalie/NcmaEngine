internal static class TestProjects
{
    internal static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("UI fixture projects are unique and outside immutable assembly inputs", () => {
            string first = Create("authoring"), second = Create("authoring"), canvas = Create("canvas");
            string application = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)) + Path.DirectorySeparatorChar;
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (first.Equals(second, comparison) || first.Equals(canvas, comparison))
                throw new Exception("UI fixture projects share storage.");
            foreach (string root in new[] { first, second, canvas })
                if (root.StartsWith(application, comparison) || !Directory.Exists(Path.Combine(root, "assets")))
                    throw new Exception("UI fixture project overlaps immutable inputs or lacks assets.");
            try { Create("unknown"); }
            catch (ArgumentException) { return; }
            throw new Exception("UI fixture accepted an unknown category.");
        });
    }

    internal static string Create(string category)
    {
        if (category is not ("authoring" or "canvas"))
            throw new ArgumentException("Unknown UI fixture category.", nameof(category));

        string temporary = Path.GetFullPath(Path.GetTempPath());
        string application = Path.GetFullPath(AppContext.BaseDirectory);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string applicationPrefix = Path.TrimEndingDirectorySeparator(application) + Path.DirectorySeparatorChar;
        if (Path.TrimEndingDirectorySeparator(temporary).Equals(Path.TrimEndingDirectorySeparator(application), comparison)
            || temporary.StartsWith(applicationPrefix, comparison))
            throw new InvalidOperationException("UI fixture temporary root overlaps immutable application inputs.");

        // Exclusive random leaf under the OS temporary root, never in the frozen assembly tree.
        // Keep fixtures after both success and failure for diagnosis; no cleanup or retries.
        string root = Directory.CreateTempSubdirectory("NcmaUi-" + category + "-").FullName;
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        Console.WriteLine("Retained UI fixture: " + root);
        return root;
    }
}
