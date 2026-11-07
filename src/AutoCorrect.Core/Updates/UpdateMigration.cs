using System.Text.Json;

namespace AutoCorrect.Core.Updates;

/// <summary>
/// Runs once per app version after startup / in-app update so layout changes
/// do not require Setup.exe.
/// </summary>
public static class UpdateMigration
{
    private const string StateFileName = "update-state.json";

    public static UpdateMigrationResult Run(string? currentVersionDisplay = null)
    {
        var version = currentVersionDisplay ?? UpdateService.GetCurrentVersionDisplay();
        var statePath = Path.Combine(DataPaths.LocalAppDataDir(), StateFileName);
        var previous = ReadPreviousVersion(statePath);
        var notesPath = Path.Combine(DataPaths.LocalAppDataDir(), "updates", "pending-notes.txt");
        string? pendingNotes = null;
        try
        {
            if (File.Exists(notesPath))
            {
                pendingNotes = File.ReadAllText(notesPath).Trim();
                File.Delete(notesPath);
            }
        }
        catch
        {
            /* ignore */
        }

        EnsureShippedDataPresent();
        CleanupStaleUpdateStaging();
        var health = UpdateHealth.VerifyInstall();

        var justUpdated = !string.IsNullOrWhiteSpace(previous) &&
                          !string.Equals(previous, version, StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(pendingNotes))
            PersistLastWhatsNew(version, pendingNotes, previous);
        else if (justUpdated)
            PersistLastWhatsNew(version, $"What's new in {version}\nYou're on the latest build.", previous);

        WriteState(statePath, version);

        return new UpdateMigrationResult
        {
            PreviousVersion = previous,
            CurrentVersion = version,
            JustUpdated = justUpdated,
            PendingNotes = pendingNotes,
            HealthOk = health.Ok,
            HealthMessage = health.Message
        };
    }

    /// <summary>Last saved What's New payload (survives after pending-notes.txt is deleted).</summary>
    public static (string Version, string Notes, string? PreviousVersion) GetLastWhatsNew()
    {
        try
        {
            var path = Path.Combine(DataPaths.LocalAppDataDir(), "last-whats-new.json");
            if (!File.Exists(path))
                return (UpdateService.GetCurrentVersionDisplay(), "", null);
            var doc = JsonSerializer.Deserialize<LastWhatsNewFile>(File.ReadAllText(path));
            if (doc is null)
                return (UpdateService.GetCurrentVersionDisplay(), "", null);
            return (
                string.IsNullOrWhiteSpace(doc.Version) ? UpdateService.GetCurrentVersionDisplay() : doc.Version!,
                doc.Notes ?? "",
                doc.PreviousVersion);
        }
        catch
        {
            return (UpdateService.GetCurrentVersionDisplay(), "", null);
        }
    }

    private static void PersistLastWhatsNew(string version, string notes, string? previous)
    {
        try
        {
            var path = Path.Combine(DataPaths.LocalAppDataDir(), "last-whats-new.json");
            var json = JsonSerializer.Serialize(new LastWhatsNewFile
            {
                Version = version,
                Notes = notes,
                PreviousVersion = previous,
                SavedUtc = DateTime.UtcNow.ToString("o")
            });
            File.WriteAllText(path, json);
        }
        catch
        {
            /* ignore */
        }
    }

    /// <summary>Copy missing shipped defaults from Data/shipped into Data/.</summary>
    public static void EnsureShippedDataPresent()
    {
        foreach (var root in DataPaths.CandidateRoots())
        {
            var shipped = Path.Combine(root, "Data", "shipped");
            var data = Path.Combine(root, "Data");
            if (!Directory.Exists(shipped) || !Directory.Exists(data))
                continue;

            foreach (var file in Directory.EnumerateFiles(shipped))
            {
                var name = Path.GetFileName(file);
                if (IsUserOwnedDataFile(name))
                    continue;
                var dest = Path.Combine(data, name);
                try
                {
                    // Always refresh curated/imported/wordlist/local-rules templates from shipped.
                    if (IsShippedRefreshFile(name) || !File.Exists(dest))
                        File.Copy(file, dest, overwrite: IsShippedRefreshFile(name));
                }
                catch
                {
                    /* best-effort */
                }
            }

            break;
        }
    }

    /// <summary>
    /// Removes leftover download/extract trees under LocalAppData\AutoCorrect\updates\pending.
    /// Staging is only needed while applying; keeping it would leave ~100–200MB per update.
    /// </summary>
    public static void CleanupStaleUpdateStaging()
    {
        try
        {
            var pending = Path.Combine(DataPaths.LocalAppDataDir(), "updates", "pending");
            if (Directory.Exists(pending))
                Directory.Delete(pending, recursive: true);
        }
        catch
        {
            /* locked by a still-running apply script — next launch will retry */
        }
    }

    public static bool IsUserOwnedDataFile(string fileName) =>
        fileName.Equals("AutoLearned.json", StringComparison.OrdinalIgnoreCase) ||
        fileName.Equals("AutoLearnCounts.txt", StringComparison.OrdinalIgnoreCase) ||
        fileName.Equals("AutoLearnLedger.json", StringComparison.OrdinalIgnoreCase) ||
        fileName.Equals("AutoLearnNever.json", StringComparison.OrdinalIgnoreCase) ||
        fileName.Equals("RemovedCorrections.json", StringComparison.OrdinalIgnoreCase) ||
        fileName.Equals("LearnContribution.json", StringComparison.OrdinalIgnoreCase) ||
        fileName.Equals("PersonalApiKey.ini", StringComparison.OrdinalIgnoreCase) ||
        fileName.Equals("PersonalDictionary.txt", StringComparison.OrdinalIgnoreCase) ||
        fileName.Equals("native-settings.json", StringComparison.OrdinalIgnoreCase) ||
        fileName.Equals("RewritePresets.json", StringComparison.OrdinalIgnoreCase) ||
        fileName.StartsWith("user-", StringComparison.OrdinalIgnoreCase);

    public static bool IsShippedRefreshFile(string fileName) =>
        fileName.Equals("AutocorrectCurated.txt", StringComparison.OrdinalIgnoreCase) ||
        fileName.Equals("AutocorrectImported.json", StringComparison.OrdinalIgnoreCase) ||
        fileName.Equals("unigram_freq_list_filtered_89k.csv", StringComparison.OrdinalIgnoreCase) ||
        fileName.Equals("DictationLocalRules.txt", StringComparison.OrdinalIgnoreCase);

    private static string? ReadPreviousVersion(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            var doc = JsonSerializer.Deserialize<UpdateStateFile>(File.ReadAllText(path));
            return doc?.LastVersion;
        }
        catch
        {
            return null;
        }
    }

    private static void WriteState(string path, string version)
    {
        try
        {
            var json = JsonSerializer.Serialize(new UpdateStateFile
            {
                LastVersion = version,
                LastRunUtc = DateTime.UtcNow.ToString("o")
            });
            File.WriteAllText(path, json);
        }
        catch
        {
            /* ignore */
        }
    }

    private sealed class UpdateStateFile
    {
        public string? LastVersion { get; set; }
        public string? LastRunUtc { get; set; }
    }

    private sealed class LastWhatsNewFile
    {
        public string? Version { get; set; }
        public string? Notes { get; set; }
        public string? PreviousVersion { get; set; }
        public string? SavedUtc { get; set; }
    }
}

public sealed class UpdateMigrationResult
{
    public string? PreviousVersion { get; init; }
    public string CurrentVersion { get; init; } = "";
    public bool JustUpdated { get; init; }
    public string? PendingNotes { get; init; }
    public bool HealthOk { get; init; } = true;
    public string HealthMessage { get; init; } = "";
}

public static class UpdateHealth
{
    public static (bool Ok, string Message) VerifyInstall(string? installDir = null)
    {
        installDir ??= AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var exe = Path.Combine(installDir, "AutoCorrect.App.exe");
        if (!File.Exists(exe))
            return (false, "AutoCorrect.App.exe missing after update — use Check for updates to retry.");

        var curated = DataPaths.FindAutocorrectCurated();
        if (curated is null)
            return (false, "Curated word list missing — retry update, or contact support.");

        return (true, "Install OK (" + UpdateService.GetCurrentVersionDisplay() + ").");
    }
}
