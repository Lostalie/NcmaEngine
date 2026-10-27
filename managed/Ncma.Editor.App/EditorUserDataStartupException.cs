using Ncma.Application;

namespace Ncma.Editor.App;

// Local startup diagnosis only. Never grants access, changes the output root or retries IO.
internal sealed class EditorUserDataStartupException : IOException
{
    internal string Code { get; }
    private EditorUserDataStartupException(string code, Exception cause) : base(code, cause)
    { Code = code; HResult = cause.HResult; }

    internal static EditorUserDataStartupException From(Exception cause) => cause switch {
        UnauthorizedAccessException => new("editor_user_data_access_denied", cause),
        IOException => new("editor_user_data_io_failed", cause),
        _ => throw new ArgumentException("Only storage access or IO failures may be classified.", nameof(cause))
    };

    internal static ApplicationLog OpenLog(string userDataRoot)
    {
        try { return new(Path.Combine(userDataRoot, "logs/editor-candidate.jsonl")); }
        catch (UnauthorizedAccessException error) { throw From(error); }
        catch (IOException error) { throw From(error); }
    }

    private string Diagnostic => $"{Code}; exception={InnerException!.GetType().Name}; hresult=0x{HResult:X8}; " +
        (Code == "editor_user_data_access_denied"
            ? "User-data log initialization was denied. Check process integrity and storage access in a supported environment."
            : "User-data log initialization failed. Check the storage environment.") +
        " No fallback or retry was attempted.";

    internal static string? Format(Exception error)
    {
        if (error is EditorUserDataStartupException storage) return storage.Diagnostic;
        // ApplicationLifetime preserves the startup error first, then any shutdown failures.
        // Do not misclassify a storage error occurring only during cleanup as the start error.
        if (error is AggregateException aggregate && aggregate.InnerExceptions.Count > 0 &&
            aggregate.InnerExceptions[0] is EditorUserDataStartupException startup)
            return startup.Diagnostic + (aggregate.InnerExceptions.Count > 1 ? " startup_cleanup_failed" : "");
        return null;
    }
}
