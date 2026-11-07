using System.IO.Compression;
using System.Text.Json;

namespace AutoCorrect.Core.Backup;

/// <summary>Export/import user settings + learned word data (no license secrets required).</summary>
public static class UserBackupService
{
    private static readonly string[] UserFiles =
    [
        "AutoLearned.json",
        "AutoLearnCounts.txt",
        "AutoLearnLedger.json",
        "AutoLearnNever.json",
        "RemovedCorrections.json",
        "LearnContribution.json",
        "PersonalDictionary.txt",
        "RewritePresets.json",
        "DictationLocalRules.txt"
    ];

    public static string ExportZip(string destinationZipPath)
    {
        var staging = Path.Combine(Path.GetTempPath(), "ac-backup-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(staging);
        try
        {
            var settingsSrc = Path.Combine(DataPaths.LocalAppDataDir(), "native-settings.json");
            if (File.Exists(settingsSrc))
                File.Copy(settingsSrc, Path.Combine(staging, "native-settings.json"), true);

            foreach (var name in UserFiles)
            {
                var found = FindUserFile(name);
                if (found is not null)
                    File.Copy(found, Path.Combine(staging, name), true);
            }

            var meta = JsonSerializer.Serialize(new
            {
                exportedAt = DateTime.UtcNow.ToString("o"),
                appVersion = Updates.UpdateService.GetCurrentVersionDisplay()
            });
            File.WriteAllText(Path.Combine(staging, "backup-meta.json"), meta);

            if (File.Exists(destinationZipPath))
                File.Delete(destinationZipPath);
            ZipFile.CreateFromDirectory(staging, destinationZipPath, CompressionLevel.Optimal, false);
            return destinationZipPath;
        }
        finally
        {
            try { Directory.Delete(staging, true); } catch { /* ignore */ }
        }
    }

    public static int ImportZip(string zipPath)
    {
        var extract = Path.Combine(Path.GetTempPath(), "ac-restore-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(extract);
        var count = 0;
        try
        {
            ZipFile.ExtractToDirectory(zipPath, extract, overwriteFiles: true);
            var settings = Path.Combine(extract, "native-settings.json");
            if (File.Exists(settings))
            {
                File.Copy(settings, Path.Combine(DataPaths.LocalAppDataDir(), "native-settings.json"), true);
                count++;
            }

            var dataDir = PreferDataDir();
            Directory.CreateDirectory(dataDir);
            foreach (var name in UserFiles)
            {
                var src = Path.Combine(extract, name);
                if (!File.Exists(src))
                    continue;
                File.Copy(src, Path.Combine(dataDir, name), true);
                count++;
            }

            return count;
        }
        finally
        {
            try { Directory.Delete(extract, true); } catch { /* ignore */ }
        }
    }

    private static string? FindUserFile(string name)
    {
        foreach (var root in DataPaths.CandidateRoots())
        {
            var p = Path.Combine(root, "Data", name);
            if (File.Exists(p))
                return p;
        }

        var local = Path.Combine(DataPaths.LocalAppDataDir(), name);
        return File.Exists(local) ? local : null;
    }

    private static string PreferDataDir()
    {
        foreach (var root in DataPaths.CandidateRoots())
        {
            var data = Path.Combine(root, "Data");
            if (Directory.Exists(data))
                return data;
        }

        var d = Path.Combine(DataPaths.LocalAppDataDir(), "Data");
        Directory.CreateDirectory(d);
        return d;
    }
}
