namespace Ncma.Editor.Core;

// Deterministic participant rejection, not arbitrary status text or an exception crossing a native ABI.
public sealed class EditCommandRejectedException : InvalidOperationException
{
    public EditCommandRejectedException(string code, bool denied = false) : base(code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 128 || !code.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            throw new ArgumentException("Invalid rejection code.");
        Code = code; Denied = denied;
    }
    public string Code { get; }
    public bool Denied { get; }
}
