using System.Net;

namespace Ncma.AI;

// Shared production transport policy. Substitution is internal test-only.
internal static class AiHttpPolicy
{
    internal static HttpClient Create(HttpMessageHandler? testHandler = null) =>
        new(testHandler ?? CreateHandler(), disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };

    internal static SocketsHttpHandler CreateHandler() => new() {
        AllowAutoRedirect = false, UseProxy = false, UseCookies = false,
        AutomaticDecompression = DecompressionMethods.None, Credentials = null, PreAuthenticate = false,
        MaxResponseHeadersLength = 16, MaxConnectionsPerServer = 1, ConnectTimeout = TimeSpan.FromSeconds(10),
        // Client-local pool; never shared across adapters/configurations/credentials.
        // Expiration retires connections between requests, not in-flight requests.
        PooledConnectionLifetime = TimeSpan.FromSeconds(30),
        PooledConnectionIdleTimeout = TimeSpan.FromSeconds(15)
    };
}
