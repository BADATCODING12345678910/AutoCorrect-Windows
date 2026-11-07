using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AutoCorrect.Core.Updates;

/// <summary>
/// Checks a public <c>latest.json</c> feed, downloads a zip update, verifies SHA-256,
/// and launches an apply-on-restart script that overwrites the install directory.
/// </summary>
public sealed class UpdateService : IDisposable
{
    /// <summary>Edge Function feed (also mirrors to Storage <c>app-updates/latest.json</c>).</summary>
    public const string DefaultFeedUrl =
        "https://arsfrcafaoagrjbnjydx.supabase.co/functions/v1/update-feed";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public UpdateService(HttpClient? http = null)
    {
        if (http is null)
        {
            _http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            _ownsHttp = true;
        }
        else
        {
            _http = http;
            _ownsHttp = false;
        }
    }

    public static Version GetCurrentVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var cleaned = info.Split('+', '-')[0].Trim();
            if (Version.TryParse(cleaned, out var fromInfo))
                return fromInfo;
        }

        return asm.GetName().Version ?? new Version(0, 0, 0, 0);
    }

    public static string GetCurrentVersionDisplay()
    {
        var v = GetCurrentVersion();
        return v.Revision > 0 && v.Revision != 0
            ? v.ToString()
            : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    public async Task<UpdateCheckResult> CheckAsync(
        string? feedUrl = null,
        CancellationToken ct = default)
    {
        var current = GetCurrentVersion();
        var url = string.IsNullOrWhiteSpace(feedUrl) ? DefaultFeedUrl : feedUrl.Trim();

        try
        {
            using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                var detail = DescribeHttpFailure((int)resp.StatusCode, body);
                return new UpdateCheckResult
                {
                    Status = UpdateCheckStatus.Error,
                    Message = detail,
                    CurrentVersion = current
                };
            }

            var manifest = JsonSerializer.Deserialize<UpdateManifest>(body, JsonOpts);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version))
            {
                return new UpdateCheckResult
                {
                    Status = UpdateCheckStatus.Error,
                    Message = "Update feed was empty or invalid.",
                    CurrentVersion = current
                };
            }

            if (!TryParseVersion(manifest.Version, out var remote))
            {
                return new UpdateCheckResult
                {
                    Status = UpdateCheckStatus.Error,
                    Message = $"Remote version '{manifest.Version}' is not valid.",
                    CurrentVersion = current,
                    Manifest = manifest
                };
            }

            if (!string.IsNullOrWhiteSpace(manifest.MinVersion) &&
                TryParseVersion(manifest.MinVersion, out var min) &&
                Normalize(current) < Normalize(min))
            {
                return new UpdateCheckResult
                {
                    Status = UpdateCheckStatus.RequiresFullSetup,
                    Message =
                        $"Version {GetCurrentVersionDisplay()} is too old for in-app update " +
                        $"(needs ≥ {manifest.MinVersion}). Reinstall with AutoCorrectSetup.exe.",
                    CurrentVersion = current,
                    RemoteVersion = remote,
                    Manifest = manifest
                };
            }

            if (Normalize(remote) <= Normalize(current))
            {
                return new UpdateCheckResult
                {
                    Status = UpdateCheckStatus.UpToDate,
                    Message = $"You're on the latest version ({GetCurrentVersionDisplay()}).",
                    CurrentVersion = current,
                    RemoteVersion = remote,
                    Manifest = manifest
                };
            }

            var notes = string.IsNullOrWhiteSpace(manifest.Notes) ? "" : $" — {manifest.Notes.Trim()}";
            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.UpdateAvailable,
                Message = $"Update {manifest.Version} available{notes}.",
                CurrentVersion = current,
                RemoteVersion = remote,
                Manifest = manifest
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult
            {
                Status = UpdateCheckStatus.Error,
                Message = "Update check failed: " + ex.Message,
                CurrentVersion = current
            };
        }
    }

    /// <summary>
    /// Download, verify, stage, write apply script. Caller should shut down the app afterward
    /// so the script can overwrite files.
    /// </summary>
    public async Task<(bool Ok, string Message)> DownloadAndStageAsync(
        UpdateManifest manifest,
        string? installDir = null,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(manifest.Url))
            return (false, "Update feed has no download URL.");
        if (string.IsNullOrWhiteSpace(manifest.Sha256))
            return (false, "Update feed is missing sha256.");

        installDir ??= AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var pendingRoot = Path.Combine(DataPaths.LocalAppDataDir(), "updates", "pending");
        try
        {
            if (Directory.Exists(pendingRoot))
                Directory.Delete(pendingRoot, recursive: true);
            Directory.CreateDirectory(pendingRoot);
        }
        catch (Exception ex)
        {
            return (false, "Couldn't prepare update folder: " + ex.Message);
        }

        var zipPath = Path.Combine(pendingRoot, "update.zip");
        const int maxAttempts = 3;
        Exception? lastDownloadError = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (File.Exists(zipPath))
                    File.Delete(zipPath);
                var startMsg = attempt == 1
                    ? "Starting download…"
                    : $"Retrying download ({attempt}/{maxAttempts})…";
                Report(progress, UpdateProgressPhase.Downloading, startMsg, 0, manifest.SizeBytes);
                await DownloadZipWithProgressAsync(manifest, zipPath, progress, ct).ConfigureAwait(false);
                lastDownloadError = null;
                break;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (IsTransientDownloadFailure(ex) && attempt < maxAttempts)
            {
                lastDownloadError = ex;
                Report(
                    progress,
                    UpdateProgressPhase.Downloading,
                    $"Download hiccup — retry {attempt + 1}/{maxAttempts}…",
                    0,
                    manifest.SizeBytes);
                await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt), ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return (false, "Download failed: " + ex.Message);
            }
        }

        if (lastDownloadError is not null)
            return (false, "Download failed after retries: " + lastDownloadError.Message);

        string actualHash;
        try
        {
            Report(progress, UpdateProgressPhase.Verifying, "Verifying download…", 0, null);
            await using var hashStream = File.OpenRead(zipPath);
            var hash = await SHA256.HashDataAsync(hashStream, ct).ConfigureAwait(false);
            actualHash = Convert.ToHexString(hash);
        }
        catch (Exception ex)
        {
            return (false, "Couldn't hash download: " + ex.Message);
        }

        var expected = manifest.Sha256.Trim().Replace("-", "", StringComparison.Ordinal);
        if (!actualHash.Equals(expected, StringComparison.OrdinalIgnoreCase))
            return (false, "Update zip failed integrity check (sha256 mismatch).");

        var extractDir = Path.Combine(pendingRoot, "files");
        try
        {
            Report(progress, UpdateProgressPhase.Extracting, "Extracting update…", 0, null);
            Directory.CreateDirectory(extractDir);
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);
        }
        catch (Exception ex)
        {
            return (false, "Couldn't extract update: " + ex.Message);
        }

        // Drop the zip once extracted — otherwise ~100MB staging sticks around forever.
        try
        {
            if (File.Exists(zipPath))
                File.Delete(zipPath);
        }
        catch
        {
            /* best-effort */
        }

        // Zip may contain a single root folder or flat publish output.
        var payloadDir = ResolvePayloadDir(extractDir);
        if (payloadDir is null)
            return (false, "Update zip did not contain AutoCorrect.App.exe.");

        var applyCmd = Path.Combine(pendingRoot, "apply-update.cmd");
        var exeName = "AutoCorrect.App.exe";
        var targetExe = Path.Combine(installDir, exeName);
        try
        {
            Report(progress, UpdateProgressPhase.Applying, "Preparing install…", 0, null);
            WriteApplyScript(applyCmd, payloadDir, installDir, targetExe, pendingRoot);
            // Persist release notes for What's New toast after relaunch.
            {
                var notesDir = Path.Combine(DataPaths.LocalAppDataDir(), "updates");
                Directory.CreateDirectory(notesDir);
                var body = string.IsNullOrWhiteSpace(manifest.Notes)
                    ? "You're on the latest build."
                    : manifest.Notes.Trim();
                File.WriteAllText(
                    Path.Combine(notesDir, "pending-notes.txt"),
                    $"What's new in {manifest.Version}\r\n{body}");
            }
        }
        catch (Exception ex)
        {
            return (false, "Couldn't write apply script: " + ex.Message);
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = applyCmd,
                UseShellExecute = true,
                WorkingDirectory = pendingRoot,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            return (false, "Couldn't start apply script: " + ex.Message);
        }

        var noteBit = string.IsNullOrWhiteSpace(manifest.Notes)
            ? ""
            : " — " + manifest.Notes.Trim();
        return (true,
            $"Installing {manifest.Version}{noteBit}. Restarting to finish — you do not need Setup.exe.");
    }

    private async Task DownloadZipWithProgressAsync(
        UpdateManifest manifest,
        string zipPath,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken ct)
    {
        using var response = await _http
            .GetAsync(manifest.Url, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength;
        if (total is null or <= 0 && manifest.SizeBytes is > 0)
            total = manifest.SizeBytes;

        await using var remote = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var fs = new FileStream(
            zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        long received = 0;
        var lastReport = DateTime.UtcNow;
        var lastPct = -1;

        Report(progress, UpdateProgressPhase.Downloading, FormatDownloadMessage(0, total), 0, total);

        while (true)
        {
            var n = await remote.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
            if (n <= 0)
                break;

            await fs.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
            received += n;

            var now = DateTime.UtcNow;
            var pct = total is > 0 ? (int)(received * 100 / total.Value) : -1;
            var due = (now - lastReport).TotalMilliseconds >= 80 || pct != lastPct;
            if (due)
            {
                lastReport = now;
                lastPct = pct;
                Report(
                    progress,
                    UpdateProgressPhase.Downloading,
                    FormatDownloadMessage(received, total),
                    received,
                    total);
            }
        }

        Report(
            progress,
            UpdateProgressPhase.Downloading,
            FormatDownloadMessage(received, total ?? received),
            received,
            total ?? received);
    }

    private static string FormatDownloadMessage(long received, long? total)
    {
        if (total is > 0)
            return $"Downloading update… {FormatBytes(received)} / {FormatBytes(total.Value)}";
        return received > 0
            ? $"Downloading update… {FormatBytes(received)}"
            : "Downloading update…";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return bytes + " B";
        double kb = bytes / 1024.0;
        if (kb < 1024)
            return kb.ToString("0.0") + " KB";
        double mb = kb / 1024.0;
        return mb.ToString("0.0") + " MB";
    }

    private static void Report(
        IProgress<UpdateDownloadProgress>? progress,
        UpdateProgressPhase phase,
        string message,
        long received,
        long? total)
    {
        progress?.Report(new UpdateDownloadProgress
        {
            Phase = phase,
            Message = message,
            BytesReceived = received,
            TotalBytes = total
        });
    }

    private static bool IsTransientDownloadFailure(Exception ex)
    {
        if (ex is HttpRequestException or IOException or TimeoutException)
            return true;
        if (ex.InnerException is HttpRequestException or IOException or TimeoutException)
            return true;
        var msg = ex.Message ?? "";
        return msg.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("temporarily", StringComparison.OrdinalIgnoreCase) ||
               msg.Contains("connection", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolvePayloadDir(string extractDir)
    {
        if (File.Exists(Path.Combine(extractDir, "AutoCorrect.App.exe")))
            return extractDir;

        foreach (var sub in Directory.EnumerateDirectories(extractDir))
        {
            if (File.Exists(Path.Combine(sub, "AutoCorrect.App.exe")))
                return sub;
        }

        return null;
    }

    private static void WriteApplyScript(
        string scriptPath,
        string sourceDir,
        string installDir,
        string targetExe,
        string pendingRoot)
    {
        // Escape for cmd: double quotes in paths.
        static string Q(string p) => "\"" + p.Replace("\"", "") + "\"";

        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("setlocal EnableExtensions");
        sb.AppendLine("rem AutoCorrect in-app update apply script (preserves user Data files)");
        sb.AppendLine("set /a TRIES=0");
        sb.AppendLine(":wait");
        sb.AppendLine("set /a TRIES+=1");
        sb.AppendLine("if %TRIES% GEQ 90 goto copy");
        sb.AppendLine("tasklist /FI \"IMAGENAME eq AutoCorrect.App.exe\" 2>nul | find /I \"AutoCorrect.App.exe\" >nul");
        sb.AppendLine("if not errorlevel 1 (");
        sb.AppendLine("  timeout /t 1 /nobreak >nul");
        sb.AppendLine("  goto wait");
        sb.AppendLine(")");
        sb.AppendLine(":copy");
        sb.AppendLine("set /a COPYTRY=0");
        sb.AppendLine(":copyloop");
        sb.AppendLine("set /a COPYTRY+=1");
        // Exclude user-owned data so updates never wipe learned/settings files in install Data\.
        sb.AppendLine(
            $"robocopy {Q(sourceDir)} {Q(installDir)} /E /R:3 /W:2 /NFL /NDL /NJH /NJS /nc /ns /np " +
            "/XF AutoLearned.json AutoLearnCounts.txt AutoLearnLedger.json AutoLearnNever.json " +
            "RemovedCorrections.json LearnContribution.json PersonalApiKey.ini PersonalDictionary.txt " +
            "native-settings.json RewritePresets.json >nul");
        sb.AppendLine("set RC=%ERRORLEVEL%");
        sb.AppendLine("rem robocopy 0-7 = success");
        sb.AppendLine("if %RC% LSS 8 goto launch");
        sb.AppendLine("if %COPYTRY% LSS 5 (");
        sb.AppendLine("  timeout /t 2 /nobreak >nul");
        sb.AppendLine("  goto copyloop");
        sb.AppendLine(")");
        sb.AppendLine("exit /b %RC%");
        sb.AppendLine(":launch");
        sb.AppendLine($"if not exist {Q(targetExe)} exit /b 42");
        sb.AppendLine($"if exist \"%ProgramFiles%\\dotnet\\dotnet.exe\" set \"DOTNET_ROOT=%ProgramFiles%\\dotnet\"");
        sb.AppendLine("set \"DOTNET_MULTILEVEL_LOOKUP=1\"");
        sb.AppendLine($"start \"\" {Q(targetExe)}");
        // Leave staging folder so we can delete it after leaving this working directory.
        sb.AppendLine("cd /d \"%TEMP%\"");
        sb.AppendLine("timeout /t 8 /nobreak >nul");
        sb.AppendLine($"rmdir /s /q {Q(pendingRoot)} 2>nul");
        sb.AppendLine("exit /b 0");

        File.WriteAllText(scriptPath, sb.ToString(), Encoding.ASCII);
    }

    public static bool TryParseVersion(string text, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var cleaned = text.Trim().Split('+', '-')[0].Trim();
        if (Version.TryParse(cleaned, out version!))
            return true;
        // Allow "0.2" → 0.2.0
        if (Version.TryParse(cleaned + ".0", out version!))
            return true;
        return false;
    }

    private static Version Normalize(Version v) =>
        new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    private static string DescribeHttpFailure(int status, string body)
    {
        var trimmed = (body ?? "").Trim();
        if (trimmed.Contains("not_found", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("Object not found", StringComparison.OrdinalIgnoreCase) ||
            status is 400 or 404)
        {
            return "Update feed not published yet (HTTP " + status +
                   "). Run push-update.cmd after adding SupabaseCredentials.ini.";
        }

        if (trimmed.Length > 0 && trimmed.Length < 180)
            return $"Update check failed (HTTP {status}): {trimmed}";

        return $"Update check failed (HTTP {status}).";
    }

    public void Dispose()
    {
        if (_ownsHttp)
            _http.Dispose();
    }
}
