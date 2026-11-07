using System.Net;
using AutoCorrect.Core.Llm;

namespace AutoCorrect.Core.Net;

/// <summary>
/// Fire-and-forget DNS + TLS warm-up so the first Fix / Rewrite is not paying cold-start cost.
/// Does not change correction results.
/// </summary>
public static class NetworkWarmup
{
    private static int _started;

    public static void Start(string? llmProvider = null, string? llmBaseUrl = null)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await WarmHostAsync("api.languagetool.org").ConfigureAwait(false);

                var info = LlmProviderCatalog.Get(llmProvider);
                var root = string.IsNullOrWhiteSpace(llmBaseUrl)
                    ? info.DefaultBaseUrl
                    : llmBaseUrl.Trim().TrimEnd('/');
                if (Uri.TryCreate(root, UriKind.Absolute, out var uri) &&
                    !string.IsNullOrWhiteSpace(uri.Host))
                    await WarmHostAsync(uri.Host).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort only.
            }
        });
    }

    private static async Task WarmHostAsync(string host)
    {
        try
        {
            _ = await Dns.GetHostAddressesAsync(host).ConfigureAwait(false);
        }
        catch
        {
            // ignore
        }

        try
        {
            using var client = SharedHttp.Create(TimeSpan.FromSeconds(6));
            using var req = new HttpRequestMessage(HttpMethod.Head, "https://" + host + "/");
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(false);
            _ = resp.StatusCode;
        }
        catch
        {
            // HEAD may 405/404 — connection is still warmed.
        }
    }
}
