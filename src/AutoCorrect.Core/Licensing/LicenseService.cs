using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoCorrect.Core.Licensing;

/// <summary>
/// Offline license keys + local 14-day trial. Buyers pay via PayPal/Gumroad/etc.;
/// you email/paste a signed key — no Stripe required in the app.
/// </summary>
public sealed class LicenseService
{
    public const int TrialDays = 14;
    private const string KeyPrefix = "AC2.1.";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private readonly string _licensePath;
    private readonly string _trialPath;
    private readonly OnlineGrantStore _onlineStore;
    private readonly EntitlementClient _entitlements;
    private LicenseInfo _current;

    public LicenseService(
        string? licensePath = null,
        string? trialPath = null,
        OnlineGrantStore? onlineStore = null,
        EntitlementClient? entitlements = null)
    {
        _licensePath = licensePath ?? DataPaths.FindOrCreateLicenseKeyPath();
        _trialPath = trialPath ?? DataPaths.FindOrCreateTrialStatePath();
        _onlineStore = onlineStore ?? new OnlineGrantStore();
        _entitlements = entitlements ?? new EntitlementClient();
        _current = Evaluate();
    }

    public LicenseInfo Current => _current;

    public LicenseInfo Refresh()
    {
        _current = Evaluate();
        return _current;
    }

    /// <summary>Activate via Supabase using the email that paid / was granted.</summary>
    public async Task<(bool Ok, string Message)> ActivateOnlineAsync(
        string email,
        CancellationToken ct = default)
    {
        try
        {
            var grant = await _entitlements.ActivateAsync(email, ct).ConfigureAwait(false);
            if (grant is null || !grant.Entitled)
                return (false, grant?.Error ?? "Activate failed.");

            _onlineStore.Save(grant);
            Refresh();
            return (true, Current.StatusLine);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>Refresh grant with server (detects cancel). No-op if no stored grant.</summary>
    public async Task RefreshOnlineAsync(CancellationToken ct = default)
    {
        var cached = _onlineStore.Load();
        if (cached?.LicenseId is null || string.IsNullOrWhiteSpace(cached.RefreshToken))
            return;

        try
        {
            var grant = await _entitlements
                .RefreshAsync(cached.LicenseId.Value.ToString(), cached.RefreshToken!, ct)
                .ConfigureAwait(false);

            if (grant is null)
                return;

            if (grant.Entitled)
                _onlineStore.Save(grant);
            else
            {
                // Cancelled / expired — drop online grant so Evaluate falls through.
                _onlineStore.Clear();
            }
        }
        catch
        {
            // Keep cached grant for offline grace if still fresh.
        }

        Refresh();
    }

    public string? ReadSavedKey()
    {
        try
        {
            if (!File.Exists(_licensePath))
                return null;
            var text = File.ReadAllText(_licensePath).Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Validate and persist a key. Returns null on success, else error message.</summary>
    public string? Activate(string rawKey)
    {
        if (!TryParseAndVerify(rawKey, out var payload, out var error))
            return error;

        try
        {
            var dir = Path.GetDirectoryName(_licensePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(_licensePath, NormalizeKey(rawKey) + Environment.NewLine);
        }
        catch (Exception ex)
        {
            return "Couldn't save license file: " + ex.Message;
        }

        _ = payload;
        Refresh();
        return null;
    }

    public void ClearLicense()
    {
        try
        {
            if (File.Exists(_licensePath))
                File.Delete(_licensePath);
        }
        catch { /* ignore */ }
        _onlineStore.Clear();
        Refresh();
    }

    public static string GenerateKey(
        string email,
        LicenseTier tier,
        DateTime? expiresUtc,
        string? hmacSecret = null)
    {
        if (tier is LicenseTier.None or LicenseTier.Trial)
            throw new ArgumentException("Generate Personal, Pro, Team, or Lifetime keys.", nameof(tier));

        var payload = new KeyPayload
        {
            Email = email.Trim().ToLowerInvariant(),
            Tier = TierToString(tier),
            Exp = expiresUtc?.ToUniversalTime().ToString("yyyy-MM-dd"),
            Iat = DateTime.UtcNow.ToString("yyyy-MM-dd")
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
        var body = Base64Url.Encode(Encoding.UTF8.GetBytes(json));
        var sig = Base64Url.Encode(Hmac(body, hmacSecret ?? LicenseSecrets.HmacUtf8));
        return KeyPrefix + body + "." + sig;
    }

    private LicenseInfo Evaluate()
    {
        // Product is free; optional tips live in Settings → Donate (and the website).
        // Keep key/online paths for existing installs, but never gate features.
        var saved = ReadSavedKey();
        if (!string.IsNullOrWhiteSpace(saved) &&
            TryParseAndVerify(saved, out var payload, out _))
        {
            var tier = ParseTier(payload.Tier);
            DateTime? exp = null;
            if (!string.IsNullOrWhiteSpace(payload.Exp) &&
                DateTime.TryParse(payload.Exp, out var expDt))
                exp = DateTime.SpecifyKind(expDt.Date, DateTimeKind.Utc);

            if (tier == LicenseTier.Lifetime || exp is null || exp >= DateTime.UtcNow.Date)
            {
                return new LicenseInfo
                {
                    Tier = tier == LicenseTier.None ? LicenseTier.Personal : tier,
                    Access = LicenseAccess.Full,
                    Email = payload.Email,
                    ExpiresUtc = tier == LicenseTier.Lifetime ? null : exp,
                    StatusLine = "Free · all features",
                    DetailLine = "AutoCorrect is free. Optional tips: Settings → Donate."
                };
            }
        }

        var online = _onlineStore.Load();
        if (_onlineStore.IsGrantFresh(online) && online is not null)
        {
            return new LicenseInfo
            {
                Tier = LicenseTier.Personal,
                Access = LicenseAccess.Full,
                Email = online.Email,
                ExpiresUtc = online.PeriodEnd?.UtcDateTime,
                StatusLine = "Free · all features",
                DetailLine = "AutoCorrect is free. Optional tips: Settings → Donate."
            };
        }

        return new LicenseInfo
        {
            Tier = LicenseTier.Lifetime,
            Access = LicenseAccess.Full,
            StatusLine = "Free · all features",
            DetailLine = "AutoCorrect is free forever. Optional tips help hosting and updates — Settings → Donate."
        };
    }

    private TrialState EnsureTrial()
    {
        try
        {
            if (File.Exists(_trialPath))
            {
                var json = File.ReadAllText(_trialPath);
                var state = JsonSerializer.Deserialize<TrialState>(json, JsonOpts);
                if (state is not null && state.StartedUtc != default)
                    return state;
            }
        }
        catch { /* recreate */ }

        var fresh = new TrialState { StartedUtc = DateTime.UtcNow };
        try
        {
            var dir = Path.GetDirectoryName(_trialPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(_trialPath, JsonSerializer.Serialize(fresh, JsonOpts));
        }
        catch { /* still use in-memory */ }
        return fresh;
    }

    private static bool TryParseAndVerify(string raw, out KeyPayload payload, out string? error)
    {
        payload = new KeyPayload();
        error = null;
        var key = NormalizeKey(raw);
        if (!key.StartsWith(KeyPrefix, StringComparison.Ordinal))
        {
            error = "Not a valid AutoCorrect license key.";
            return false;
        }

        var rest = key[KeyPrefix.Length..];
        var dot = rest.LastIndexOf('.');
        if (dot <= 0 || dot >= rest.Length - 1)
        {
            error = "License key is incomplete.";
            return false;
        }

        var body = rest[..dot];
        var sig = rest[(dot + 1)..];
        byte[] expected;
        try { expected = Hmac(body, LicenseSecrets.HmacUtf8); }
        catch
        {
            error = "License verification failed.";
            return false;
        }

        byte[] actual;
        try { actual = Base64Url.Decode(sig); }
        catch
        {
            error = "License signature is corrupt.";
            return false;
        }

        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            error = "License signature invalid (wrong or tampered key).";
            return false;
        }

        try
        {
            var json = Encoding.UTF8.GetString(Base64Url.Decode(body));
            var parsed = JsonSerializer.Deserialize<KeyPayload>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.Tier))
            {
                error = "License payload missing tier.";
                return false;
            }
            payload = parsed;
            return true;
        }
        catch
        {
            error = "Couldn't read license payload.";
            return false;
        }
    }

    private static string NormalizeKey(string raw) =>
        raw.Trim().Replace("\r", "").Replace("\n", "").Replace(" ", "");

    private static byte[] Hmac(string body, string secret)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return h.ComputeHash(Encoding.UTF8.GetBytes(body));
    }

    private static LicenseTier ParseTier(string? tier) =>
        (tier ?? "").Trim().ToLowerInvariant() switch
        {
            "personal" => LicenseTier.Personal,
            "pro" => LicenseTier.Pro,
            "team" => LicenseTier.Team,
            "lifetime" => LicenseTier.Lifetime,
            _ => LicenseTier.None
        };

    private static string TierToString(LicenseTier tier) => tier switch
    {
        LicenseTier.Personal => "personal",
        LicenseTier.Pro => "pro",
        LicenseTier.Team => "team",
        LicenseTier.Lifetime => "lifetime",
        _ => "personal"
    };

    private static string StatusForTier(LicenseTier tier) => tier switch
    {
        LicenseTier.Personal => "Licensed · Personal",
        LicenseTier.Pro => "Licensed · Pro",
        LicenseTier.Team => "Licensed · Team",
        LicenseTier.Lifetime => "Licensed · Lifetime",
        _ => "Licensed"
    };

    private static string FormatTrialRemaining(TimeSpan remaining)
    {
        if (remaining.TotalDays >= 2)
            return $"{(int)remaining.TotalDays}d {remaining.Hours}h";
        if (remaining.TotalHours >= 1)
            return $"{(int)remaining.TotalHours}h {remaining.Minutes}m";
        if (remaining.TotalMinutes >= 1)
            return $"{Math.Max(1, (int)remaining.TotalMinutes)}m";
        return "under 1m";
    }

    private static string BuildLicensedDetail(string? email, LicenseTier tier, DateTime? exp)
    {
        var who = string.IsNullOrWhiteSpace(email) ? "activated" : email;
        if (tier == LicenseTier.Lifetime || exp is null)
            return $"{who} · never expires";
        return $"{who} · renews/expires {exp:yyyy-MM-dd} UTC";
    }

    private sealed class KeyPayload
    {
        public string? Email { get; set; }
        public string? Tier { get; set; }
        public string? Exp { get; set; }
        public string? Iat { get; set; }
    }

    private sealed class TrialState
    {
        public DateTime StartedUtc { get; set; }
    }

    private static class Base64Url
    {
        public static string Encode(byte[] data) =>
            Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public static byte[] Decode(string s)
        {
            var padded = s.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2: padded += "=="; break;
                case 3: padded += "="; break;
            }
            return Convert.FromBase64String(padded);
        }
    }
}
