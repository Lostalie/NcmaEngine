namespace Ncma.Testing;

// Test-only routing, not a receipt, capture validator or permission grant.
// Source arguments remain intact; only explicit generated runtime IO is redirected.
internal static class VerificationWorkspace
{
    internal static string Extract(ref string[] args)
    {
        const string flag = "--verification-workspace";
        if (args.Length == 0) throw new ArgumentException("Source root required.");
        int at = Array.IndexOf(args, flag);
        if (at < 0) return Path.GetFullPath(args[0]);
        if (at == 0 || at != args.Length - 2 || Array.LastIndexOf(args, flag) != at)
            throw new ArgumentException("One trailing verification workspace is required.");
        string value = args[at + 1];
        if (!Path.IsPathFullyQualified(value)) throw new ArgumentException("Absolute verification workspace required.");
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Verification workspace does not exist.");
        for (DirectoryInfo? part = new(root); part is not null; part = part.Parent)
            if ((part.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Verification workspace reparse alias rejected.");
        args = args[..at];
        return root;
    }
}
