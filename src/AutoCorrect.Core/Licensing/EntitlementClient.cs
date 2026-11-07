using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoCorrect.Core.Licensing;

/// <summary>Talks to Supabase Edge Functions for online entitlement grants.</summary>
public sealed class EntitlementClient
{
    public const string DefaultBaseUrl =
        "https://arsfrcafaoagrjbnjydx.supabase.co/functions/v1";

    /// <summary>Anon/publishable key — only used as apikey header to invoke functions.</summary>
    public const string DefaultAnonKey =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImFyc2ZyY2FmYW9hZ3JqYm5qeWR4Iiwicm9sZSI6ImFub24iLCJpYXQiOjE3OTM0OTkwODIsImV4cCI6MjEwOTA3NTA4Mn0.nmnF0kQmCCKMl4NEZzh4ZzZ7gSTGzsH9vKzw0lHGyPg";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _anonKey;

    public EntitlementClient(HttpClient? http = null, string? baseUrl = null, string? anonKey = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _baseUrl = (baseUrl ?? DefaultBaseUrl).TrimEnd('/');
        _anonKey = anonKey ?? DefaultAnonKey;
    }

    public async Task<EntitlementGrant?> ActivateAsync(string email, CancellationToken ct = default)
    {
        var resp = await PostAsync(new { action = "activate", email }, ct).ConfigureAwait(false);
        return await ParseGrant(resp).ConfigureAwait(false);
    }

    public async Task<EntitlementGrant?> RefreshAsync(
        string licenseId,
        string refreshToken,
        CancellationToken ct = default)
    {
        var resp = await PostAsync(new { action = "refresh", licenseId, refreshToken }, ct)
            .ConfigureAwait(false);
        return await ParseGrant(resp).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> PostAsync(object body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/license-api");
        req.Headers.TryAddWithoutValidation("apikey", _anonKey);
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _anonKey);
        req.Content = JsonContent.Create(body, options: JsonOpts);
        return await _http.SendAsync(req, ct).ConfigureAwait(false);
    }

    private static async Task<EntitlementGrant?> ParseGrant(HttpResponseMessage resp)
    {
        var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        EntitlementGrant? grant = null;
        try
        {
            grant = JsonSerializer.Deserialize<EntitlementGrant>(text, JsonOpts);
        }
        catch
        {
            /* fall through */
        }

        if (grant is null)
            return new EntitlementGrant { Entitled = false, Error = text };

        if (!resp.IsSuccessStatusCode && grant.Error is null)
            grant.Error = $"HTTP {(int)resp.StatusCode}";

        return grant;
    }
}

public sealed class EntitlementGrant
{
    public bool Entitled { get; set; }
    public Guid? LicenseId { get; set; }
    public string? Email { get; set; }
    public string? Tier { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? PeriodEnd { get; set; }
    public DateTimeOffset? GrantExpiresAt { get; set; }
    public string? RefreshToken { get; set; }
    public string? Error { get; set; }
}

public sealed class OnlineGrantStore
{
    private readonly string _path;

    public OnlineGrantStore(string? path = null)
    {
        _path = path ?? Path.Combine(DataPaths.LocalAppDataDir(), "online-grant.json");
    }

    public EntitlementGrant? Load()
    {
        try
        {
            if (!File.Exists(_path))
                return null;
            return JsonSerializer.Deserialize<EntitlementGrant>(
                File.ReadAllText(_path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    public void Save(EntitlementGrant grant)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(_path, JsonSerializer.Serialize(grant, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        }));
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }
        catch { /* ignore */ }
    }

    /// <summary>True if cached grant still within offline grace window.</summary>
    public bool IsGrantFresh(EntitlementGrant? g)
    {
        if (g is null || !g.Entitled || g.GrantExpiresAt is null)
            return false;
        return g.GrantExpiresAt > DateTimeOffset.UtcNow;
    }
}
