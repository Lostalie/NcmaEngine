namespace Ncma.AI;

// Explicit trusted host input only. No environment/config-file discovery, persistence, or public key getter.
// A key is bound to the WHOLE profile fingerprint, including endpoint, model and credential generation.
public sealed class AiSessionCredential : IDisposable
{
    private readonly object gate = new();
    private readonly string profileFingerprint;
    private string? token;
    public AiSessionCredential(AiConnectionProfile profile, string bearerToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Credential is null || string.IsNullOrEmpty(bearerToken) || bearerToken.Length > 4096 ||
            bearerToken.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.' or '~' or '+' or '/' or '=')))
            throw new ArgumentException("ai_credential_invalid");
        profileFingerprint = profile.Fingerprint; token = bearerToken;
    }
    internal string Acquire(AiConnectionProfile profile)
    {
        lock (gate) {
            if (token is null || profile.Fingerprint != profileFingerprint) throw new AiProtocolException("credential_unavailable");
            return token;
        }
    }
    internal bool IsCurrent(AiConnectionProfile profile)
    { lock (gate) return token is not null && profile.Fingerprint == profileFingerprint; }
    public void Dispose() { lock (gate) token = null; }
    public override string ToString() => "Session credential (redacted)";
}
