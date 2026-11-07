using System.Net;
using System.Net.Http.Headers;

namespace AutoCorrect.Core.Net;

/// <summary>
/// Shared <see cref="HttpClient"/> factory tuned for Fix/AI calls:
/// connection pooling, HTTP/2 when available, automatic decompression.
/// </summary>
public static class SharedHttp
{
    public static HttpClient Create(TimeSpan timeout)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 8,
            EnableMultipleHttp2Connections = true,
            ConnectTimeout = TimeSpan.FromSeconds(8)
        };

        var client = new HttpClient(handler)
        {
            Timeout = timeout,
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AutoCorrect/Native");
        return client;
    }
}
