using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoCorrect.Core.LanguagePacks;

/// <summary>
/// On-demand language packs for as-you-type autocorrect (curated pairs + optional wordlist).
/// English (en-US) is bundled with the app; other locales download when selected or auto-detected.
/// </summary>
public sealed class LanguagePackService : IDisposable
{
    /// <summary>Primary catalog (Cloudflare Pages — school / Lightspeed-safe).</summary>
    public const string DefaultManifestUrl =
        "https://autocorrect-windows-site.pages.dev/language-packs/manifest.json";

    /// <summary>GitHub Pages fallback when Cloudflare is unreachable.</summary>
    public const string AlternateManifestUrl =
        "https://badatcoding12345678910.github.io/AutoCorrect-Windows/language-packs/manifest.json";

    public const string PrimaryPackRoot =
        "https://autocorrect-windows-site.pages.dev/language-packs/";

    public const string AlternatePackRoot =
        "https://badatcoding12345678910.github.io/AutoCorrect-Windows/language-packs/";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly object _gate = new();
    private LanguagePackManifest? _manifest;
    private DateTimeOffset _manifestLoadedAt = DateTimeOffset.MinValue;

    public LanguagePackService(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
    }

    public string PacksRoot => Path.Combine(DataPaths.LocalAppDataDir(), "packs");

    public string PackDirectory(string code) =>
        Path.Combine(PacksRoot, NormalizeCode(code));

    public bool IsBundledEnglish(string? code)
    {
        var n = NormalizeCode(code);
        return n is "en" or "en-us" or "auto" or "";
    }

    public bool IsInstalled(string? code)
    {
        if (IsBundledEnglish(code))
            return true;

        var dir = PackDirectory(code!);
        return File.Exists(Path.Combine(dir, "pack.json")) &&
               File.Exists(Path.Combine(dir, "AutocorrectCurated.txt"));
    }

    public string? FindCuratedPath(string? code)
    {
        if (IsBundledEnglish(code))
            return null; // caller uses shipped Data/

        foreach (var candidate in ResolveCandidates(code))
        {
            var path = Path.Combine(PackDirectory(candidate), "AutocorrectCurated.txt");
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    public string? FindWordlistPath(string? code)
    {
        if (IsBundledEnglish(code))
            return null;

        foreach (var candidate in ResolveCandidates(code))
        {
            var path = Path.Combine(PackDirectory(candidate), "wordlist.txt");
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    /// <summary>
    /// Ensure a pack is local. Bundled English is a no-op.
    /// Returns (ok, message). Downloads from the public manifest when needed.
    /// </summary>
    public async Task<(bool Ok, string Message, string ResolvedCode)> EnsurePackAsync(
        string? languageCode,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var code = NormalizeCode(languageCode);
        if (string.IsNullOrEmpty(code) || code == "auto")
            return (true, "Auto-detect / default — using bundled English until a language is known.", "en-US");

        if (IsBundledEnglish(code))
            return (true, "English (US) is built into the app — no download.", "en-US");

        progress?.Report("Checking language pack catalog…");
        var manifest = await GetManifestAsync(ct).ConfigureAwait(false);
        var entry = FindManifestEntry(manifest, code);
        if (entry is null)
        {
            // Fall back to whatever is already installed even if the catalog is offline.
            foreach (var candidate in ResolveCandidates(code))
            {
                if (IsInstalled(candidate) && !IsBundledEnglish(candidate))
                    return (true, $"Language pack ready ({candidate}).", candidate);
            }

            return (false,
                $"No downloadable pack for “{code}” yet. Fix grammar still works via LanguageTool; as-you-type stays on English until a pack is published.",
                code);
        }

        if (entry.Bundled)
            return (true, $"{entry.Name} is bundled — no download.", entry.Code);

        var installedCode = entry.Code;
        var localVersion = TryReadInstalledPackVersion(installedCode);
        var needsDownload = !IsInstalled(installedCode) || IsRemotePackNewer(entry.PackVersion, localVersion);
        if (!needsDownload)
            return (true, $"Language pack ready ({installedCode}).", installedCode);

        if (string.IsNullOrWhiteSpace(entry.Url))
            return (false, $"Pack {entry.Code} has no download URL.", entry.Code);

        var refreshing = localVersion is not null;
        progress?.Report(refreshing
            ? $"Updating {entry.Name} pack ({localVersion} → {entry.PackVersion})…"
            : $"Downloading {entry.Name} pack…");
        var urls = PackDownloadUrls(entry.Url);
        Exception? lastError = null;
        foreach (var url in urls)
        {
            try
            {
                var bytes = await _http.GetByteArrayAsync(url, ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(entry.Sha256))
                {
                    var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                    if (!string.Equals(hash, entry.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                        return (false, $"Pack checksum mismatch for {entry.Code}.", entry.Code);
                }

                progress?.Report($"Installing {entry.Name}…");
                InstallZip(entry.Code, bytes, entry.PackVersion);
                return (true,
                    refreshing
                        ? $"Updated {entry.Name} language pack to {entry.PackVersion}."
                        : $"Downloaded {entry.Name} language pack.",
                    entry.Code);
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        // Keep the previous install usable if a refresh download failed.
        if (IsInstalled(installedCode))
            return (true, $"Language pack ready ({installedCode}); update skipped ({lastError?.Message ?? "download failed"}).", installedCode);

        return (false, $"Couldn't download {entry.Code} pack: {lastError?.Message ?? "unknown error"}", entry.Code);
    }

    private string? TryReadInstalledPackVersion(string code)
    {
        try
        {
            var path = Path.Combine(PackDirectory(code), "pack.json");
            if (!File.Exists(path))
                return null;

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("packVersion", out var pv))
                return pv.GetString();
            if (doc.RootElement.TryGetProperty("PackVersion", out var pv2))
                return pv2.GetString();
        }
        catch
        {
            // ignore corrupt meta
        }

        return null;
    }

    /// <summary>True when remote semver is greater than local (missing local counts as older).</summary>
    internal static bool IsRemotePackNewer(string? remoteVersion, string? localVersion)
    {
        if (string.IsNullOrWhiteSpace(remoteVersion))
            return false;
        if (string.IsNullOrWhiteSpace(localVersion))
            return true;

        if (Version.TryParse(NormalizeSemVer(remoteVersion), out var remote) &&
            Version.TryParse(NormalizeSemVer(localVersion), out var local))
            return remote > local;

        return !string.Equals(remoteVersion.Trim(), localVersion.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeSemVer(string version)
    {
        var parts = version.Trim().Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return "0.0.0";
        if (parts.Length == 1)
            return parts[0] + ".0.0";
        if (parts.Length == 2)
            return parts[0] + "." + parts[1] + ".0";
        return string.Join('.', parts.Take(4));
    }

    public async Task<LanguagePackManifest> GetManifestAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_manifest is not null && DateTimeOffset.UtcNow - _manifestLoadedAt < TimeSpan.FromHours(6))
                return _manifest;
        }

        foreach (var manifestUrl in new[] { DefaultManifestUrl, AlternateManifestUrl })
        {
            try
            {
                await using var stream = await _http.GetStreamAsync(manifestUrl, ct).ConfigureAwait(false);
                var remote = await JsonSerializer.DeserializeAsync<LanguagePackManifest>(stream, JsonOptions, ct)
                    .ConfigureAwait(false);
                if (remote?.Packs is { Count: > 0 })
                {
                    // Prefer Cloudflare pack hosts; older manifests may still list github.io URLs.
                    RewritePackUrlsToPrimary(remote);

                    lock (_gate)
                    {
                        _manifest = remote;
                        _manifestLoadedAt = DateTimeOffset.UtcNow;
                        return _manifest;
                    }
                }
            }
            catch
            {
                // try next source
            }
        }

        var embedded = BuildEmbeddedManifest();
        lock (_gate)
        {
            _manifest = embedded;
            _manifestLoadedAt = DateTimeOffset.UtcNow;
            return _manifest;
        }
    }

    private static IEnumerable<string> PackDownloadUrls(string? primaryUrl)
    {
        if (string.IsNullOrWhiteSpace(primaryUrl))
            yield break;

        var preferred = ToPrimaryPackUrl(primaryUrl);
        yield return preferred;
        var fallback = ToFallbackPackUrl(preferred);
        if (!string.Equals(fallback, preferred, StringComparison.OrdinalIgnoreCase))
            yield return fallback;
    }

    private static string ToPrimaryPackUrl(string url)
    {
        if (url.StartsWith(PrimaryPackRoot, StringComparison.OrdinalIgnoreCase))
            return url;
        if (url.StartsWith(AlternatePackRoot, StringComparison.OrdinalIgnoreCase))
            return PrimaryPackRoot + url[AlternatePackRoot.Length..];

        var file = url.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (!string.IsNullOrEmpty(file) && file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return PrimaryPackRoot + file;
        return url;
    }

    private static string ToFallbackPackUrl(string url)
    {
        if (url.StartsWith(AlternatePackRoot, StringComparison.OrdinalIgnoreCase))
            return url;
        if (url.StartsWith(PrimaryPackRoot, StringComparison.OrdinalIgnoreCase))
            return AlternatePackRoot + url[PrimaryPackRoot.Length..];

        var file = url.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (!string.IsNullOrEmpty(file) && file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return AlternatePackRoot + file;
        return url;
    }

    private static void RewritePackUrlsToPrimary(LanguagePackManifest manifest)
    {
        foreach (var pack in manifest.Packs)
        {
            if (!string.IsNullOrWhiteSpace(pack.Url))
                pack.Url = ToPrimaryPackUrl(pack.Url);
        }
    }

    public IReadOnlyList<string> ResolveCandidates(string? code)
    {
        var n = NormalizeCode(code);
        if (string.IsNullOrEmpty(n))
            return ["en-US"];

        var list = new List<string> { n };
        // en-GB → also try en (never auto-installs over bundled without explicit GB pack)
        var dash = n.IndexOf('-');
        if (dash > 0)
        {
            var stem = n[..dash];
            if (!list.Contains(stem, StringComparer.OrdinalIgnoreCase))
                list.Add(stem);
        }

        // Common fallbacks
        if (n.StartsWith("es", StringComparison.OrdinalIgnoreCase) &&
            !list.Contains("es-ES", StringComparer.OrdinalIgnoreCase))
            list.Add("es-ES");
        if (n.StartsWith("pt", StringComparison.OrdinalIgnoreCase) &&
            !list.Contains("pt-BR", StringComparer.OrdinalIgnoreCase))
            list.Add("pt-BR");
        if (n.StartsWith("fr", StringComparison.OrdinalIgnoreCase) &&
            !list.Contains("fr-FR", StringComparer.OrdinalIgnoreCase))
            list.Add("fr-FR");
        if (n.StartsWith("de", StringComparison.OrdinalIgnoreCase) &&
            !list.Contains("de-DE", StringComparer.OrdinalIgnoreCase))
            list.Add("de-DE");
        if (n.StartsWith("zh", StringComparison.OrdinalIgnoreCase) &&
            !list.Contains("zh-CN", StringComparer.OrdinalIgnoreCase))
            list.Add("zh-CN");

        return list;
    }

    private LanguagePackEntry? FindManifestEntry(LanguagePackManifest manifest, string code)
    {
        foreach (var candidate in ResolveCandidates(code))
        {
            var hit = manifest.Packs.FirstOrDefault(p =>
                string.Equals(p.Code, candidate, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
                return hit;
        }

        return null;
    }

    private void InstallZip(string code, byte[] zipBytes, string packVersion)
    {
        var dir = PackDirectory(code);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);

        using var ms = new MemoryStream(zipBytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;
            var dest = Path.Combine(dir, entry.Name);
            entry.ExtractToFile(dest, overwrite: true);
        }

        var meta = new InstalledPackMeta
        {
            Code = NormalizeCode(code),
            PackVersion = packVersion,
            InstalledAtUtc = DateTimeOffset.UtcNow
        };
        File.WriteAllText(Path.Combine(dir, "pack.json"), JsonSerializer.Serialize(meta, JsonOptions));
    }

    /// <summary>Offline fallback catalog (URLs match site publish layout).</summary>
    public static LanguagePackManifest BuildEmbeddedManifest()
    {
        const string root =
            "https://autocorrect-windows-site.pages.dev/language-packs/";
        string Url(string code) => root + code + ".zip";

        return new LanguagePackManifest
        {
            Version = 1,
            Packs =
            [
                new() { Code = "en-US", Name = "English (United States)", Bundled = true, PackVersion = "1.0.0" },
                new() { Code = "en-GB", Name = "English (United Kingdom)", Url = Url("en-GB"), PackVersion = "1.1.0", Sha256 = "b3a713e1d526de7239d08788f785109285d2fa89eb1f57cb2b9bdc9882940216", SizeBytes = 2947 },
                new() { Code = "es-ES", Name = "Spanish (Spain)", Url = Url("es-ES"), PackVersion = "1.1.0", Sha256 = "a5da0d10716be3050720fce34af614d54f69e231c064a64aa30fe62bc8a2deba", SizeBytes = 2625 },
                new() { Code = "es-AR", Name = "Spanish (Argentina)", Url = Url("es-AR"), PackVersion = "1.1.0", Sha256 = "08c475149dd144e762fd80d3fcf5bca3e44313049fdd9e052812210a8cfbd88e", SizeBytes = 2377, Fallbacks = ["es-ES"] },
                new() { Code = "fr-FR", Name = "French (France)", Url = Url("fr-FR"), PackVersion = "1.1.0", Sha256 = "418bd47e0dab89cadce7cf9fd43ce681c50b23708153da2706c0e4edfcf617e3", SizeBytes = 2634 },
                new() { Code = "de-DE", Name = "German (Germany)", Url = Url("de-DE"), PackVersion = "1.1.0", Sha256 = "2e5952b500ca241e9a1c665d6380abfe6beadb18cc7f472ac5351ef2aaffdc17", SizeBytes = 2574 },
                new() { Code = "pt-BR", Name = "Portuguese (Brazil)", Url = Url("pt-BR"), PackVersion = "1.1.0", Sha256 = "b9e0c6409bc46cb2cb92bb3575e68063e4b3564b54870011a0fb29ee6cc6ea60", SizeBytes = 2467 },
                new() { Code = "it-IT", Name = "Italian", Url = Url("it-IT"), PackVersion = "1.1.0", Sha256 = "2ca5c9eb8dc3bdfd3842a86697fecd2fa6302c5e7febe672e4297bd790830791", SizeBytes = 2060 },
                new() { Code = "nl-NL", Name = "Dutch (Netherlands)", Url = Url("nl-NL"), PackVersion = "1.1.0", Sha256 = "33b19324d76e3bd75643fb645f57fc9820cf509b01f7be30fe371c42ce98fa3a", SizeBytes = 2004 },
                new() { Code = "pl-PL", Name = "Polish", Url = Url("pl-PL"), PackVersion = "1.1.0", Sha256 = "ef43f157b5d6d659458b85c9b845140771498e8916f22fd2845977306b65dbc3", SizeBytes = 2218 },
                new() { Code = "ru-RU", Name = "Russian", Url = Url("ru-RU"), PackVersion = "1.1.0", Sha256 = "2abcba31e874fff2472e89fe30ee454cb7bc34cf3476a1a2a0b632f2a9910749", SizeBytes = 2504 },
                new() { Code = "ja-JP", Name = "Japanese", Url = Url("ja-JP"), PackVersion = "1.1.0", Sha256 = "3b6e917d5a1822bba6bd03f5142b738340f68e85b3db6260275c3565b221b1c7", SizeBytes = 1677 },
                new() { Code = "zh-CN", Name = "Chinese (Simplified)", Url = Url("zh-CN"), PackVersion = "1.1.0", Sha256 = "d2acc8cd30c8106760b366d9ade0c412ab3549ba3c91d82128bfad16b52b88de", SizeBytes = 1904 },
                new() { Code = "ar", Name = "Arabic", Url = Url("ar"), PackVersion = "1.1.0", Sha256 = "b2e057db26e2b7e8e3115fe1f0843d9cf51bab5c74928f82867aaad5c6b83d4b", SizeBytes = 1811 },
            ]
        };
    }

    public static string NormalizeCode(string? code) =>
        string.IsNullOrWhiteSpace(code) ? "" : code.Trim();

    public void Dispose() => _http.Dispose();
}
