namespace AutoCorrect.Core;

/// <summary>Resolves AHK Data/ files while native settings store is still evolving.</summary>
public static class DataPaths
{
    public static string? FindDictationLocalRules(string? fileName = null)
    {
        fileName = string.IsNullOrWhiteSpace(fileName) ? "DictationLocalRules.txt" : fileName.Trim();
        if (Path.IsPathRooted(fileName) && File.Exists(fileName))
            return fileName;

        foreach (var root in CandidateRoots())
        {
            var p = Path.Combine(root, "Data", fileName);
            if (File.Exists(p))
                return p;
        }
        return null;
    }

    /// <summary>
    /// Locate curated as-you-type list (<c>Data/AutocorrectCurated.txt</c>).
    /// </summary>
    public static string? FindAutocorrectCurated(string? fileName = null)
    {
        fileName = string.IsNullOrWhiteSpace(fileName) ? "AutocorrectCurated.txt" : fileName.Trim();
        if (Path.IsPathRooted(fileName) && File.Exists(fileName))
            return fileName;

        foreach (var root in CandidateRoots())
        {
            var p = Path.Combine(root, "Data", fileName);
            if (File.Exists(p))
                return p;
        }
        return null;
    }

    /// <summary>
    /// Prefer repo <c>Data/AutocorrectCurated.txt</c>; otherwise LocalAppData path
    /// (caller may seed defaults).
    /// </summary>
    public static string FindOrCreateAutocorrectCuratedPath(string? fileName = null)
    {
        var existing = FindAutocorrectCurated(fileName);
        if (existing is not null)
            return existing;

        fileName = string.IsNullOrWhiteSpace(fileName) ? "AutocorrectCurated.txt" : fileName.Trim();
        if (Path.IsPathRooted(fileName))
            return fileName;

        foreach (var root in CandidateRoots())
        {
            var dataDir = Path.Combine(root, "Data");
            if (Directory.Exists(dataDir))
                return Path.Combine(dataDir, fileName);
        }

        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutoCorrect");
        Directory.CreateDirectory(appData);
        return Path.Combine(appData, fileName);
    }

    /// <summary>Locate AHK <c>Core/AutoCorrectHotstrings.ahk</c> for import.</summary>
    public static string? FindAhkHotstringsLibrary()
    {
        foreach (var root in CandidateRoots())
        {
            var p = Path.Combine(root, "Core", "AutoCorrectHotstrings.ahk");
            if (File.Exists(p))
                return p;
        }
        return null;
    }

    public static string? FindAutocorrectImported(string? fileName = null)
    {
        fileName = string.IsNullOrWhiteSpace(fileName) ? "AutocorrectImported.json" : fileName.Trim();
        if (Path.IsPathRooted(fileName) && File.Exists(fileName))
            return fileName;

        foreach (var root in CandidateRoots())
        {
            var p = Path.Combine(root, "Data", fileName);
            if (File.Exists(p))
                return p;
        }
        return null;
    }

    /// <summary>Locate dictionary wordlist (default unigram CSV under Data/).</summary>
    public static string? FindWordlist(string? fileName = null)
    {
        fileName = string.IsNullOrWhiteSpace(fileName)
            ? "unigram_freq_list_filtered_89k.csv"
            : fileName.Trim();
        if (Path.IsPathRooted(fileName) && File.Exists(fileName))
            return fileName;

        foreach (var root in CandidateRoots())
        {
            var p = Path.Combine(root, "Data", fileName);
            if (File.Exists(p))
                return p;
        }
        return null;
    }

    public static string FindOrCreateAutocorrectImportedPath(string? fileName = null)
    {
        var existing = FindAutocorrectImported(fileName);
        if (existing is not null)
            return existing;

        fileName = string.IsNullOrWhiteSpace(fileName) ? "AutocorrectImported.json" : fileName.Trim();
        if (Path.IsPathRooted(fileName))
            return fileName;

        foreach (var root in CandidateRoots())
        {
            var dataDir = Path.Combine(root, "Data");
            if (Directory.Exists(dataDir))
                return Path.Combine(dataDir, fileName);
        }

        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutoCorrect");
        Directory.CreateDirectory(appData);
        return Path.Combine(appData, fileName);
    }

    /// <summary>
    /// Prefer AHK <c>Data/PersonalDictionary.txt</c> when the repo Data folder is found;
    /// otherwise use <c>%LocalAppData%\AutoCorrect\PersonalDictionary.txt</c>.
    /// </summary>
    public static string FindOrCreatePersonalDictionary()
    {
        foreach (var root in CandidateRoots())
        {
            var dataDir = Path.Combine(root, "Data");
            if (Directory.Exists(dataDir))
                return Path.Combine(dataDir, "PersonalDictionary.txt");
        }

        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutoCorrect");
        Directory.CreateDirectory(appData);
        return Path.Combine(appData, "PersonalDictionary.txt");
    }

    /// <summary>
    /// Locate LLM credentials. Prefers <c>%LocalAppData%\AutoCorrect\PersonalApiKey.ini</c>
    /// (survives in-app updates). Falls back to older <c>Data/PersonalApiKey.ini</c> and migrates.
    /// </summary>
    public static string? FindPersonalApiKey()
    {
        var canonical = Path.Combine(LocalAppDataDir(), "PersonalApiKey.ini");
        if (File.Exists(canonical) && HasUsableApiKeyFile(canonical))
            return canonical;

        foreach (var root in CandidateRoots())
        {
            var legacy = Path.Combine(root, "Data", "PersonalApiKey.ini");
            if (!File.Exists(legacy) || !HasUsableApiKeyFile(legacy))
                continue;

            try
            {
                Directory.CreateDirectory(LocalAppDataDir());
                var overwrite = !File.Exists(canonical) || !HasUsableApiKeyFile(canonical);
                File.Copy(legacy, canonical, overwrite);
                return canonical;
            }
            catch
            {
                return legacy;
            }
        }

        return File.Exists(canonical) ? canonical : null;
    }

    /// <summary>Always <c>%LocalAppData%\AutoCorrect\PersonalApiKey.ini</c>.</summary>
    public static string FindOrCreatePersonalApiKeyPath()
    {
        var existing = FindPersonalApiKey();
        var canonical = Path.Combine(LocalAppDataDir(), "PersonalApiKey.ini");
        if (existing is not null &&
            existing.StartsWith(LocalAppDataDir(), StringComparison.OrdinalIgnoreCase))
            return existing;

        if (existing is not null && !File.Exists(canonical))
        {
            try
            {
                File.Copy(existing, canonical, overwrite: false);
            }
            catch
            {
                /* Save will create fresh */
            }
        }

        return canonical;
    }

    private static bool HasUsableApiKeyFile(string path)
    {
        try
        {
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#') || line.StartsWith('['))
                    continue;
                var eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;
                if (!line[..eq].Trim().Equals("ApiKey", StringComparison.OrdinalIgnoreCase))
                    continue;
                var value = line[(eq + 1)..].Trim();
                if (!PersonalApiKeyStore.LooksLikePlaceholder(value))
                    return true;
            }
        }
        catch
        {
            /* ignore */
        }

        return false;
    }

    // —— AutoLearn data files ——
    public static string? FindAutoLearned() => FindDataFile("AutoLearned.json");
    public static string FindOrCreateAutoLearnedPath() => FindOrCreateDataFile("AutoLearned.json");
    public static string? FindAutoLearnCounts() => FindDataFile("AutoLearnCounts.txt");
    public static string FindOrCreateAutoLearnCountsPath() => FindOrCreateDataFile("AutoLearnCounts.txt");
    public static string? FindAutoLearnLedger() => FindDataFile("AutoLearnLedger.json");
    public static string FindOrCreateAutoLearnLedgerPath() => FindOrCreateDataFile("AutoLearnLedger.json");
    public static string? FindAutoLearnNever() => FindDataFile("AutoLearnNever.json");
    public static string FindOrCreateAutoLearnNeverPath() => FindOrCreateDataFile("AutoLearnNever.json");
    public static string? FindRemovedCorrections() => FindDataFile("RemovedCorrections.json");
    public static string FindOrCreateRemovedCorrectionsPath() => FindOrCreateDataFile("RemovedCorrections.json");
    public static string? FindDismissedSuggestions() => FindDataFile("DismissedSuggestions.json");
    public static string FindOrCreateDismissedSuggestionsPath() => FindOrCreateDataFile("DismissedSuggestions.json");
    public static string? FindSnippets() => FindDataFile("Snippets.json");
    public static string FindOrCreateSnippetsPath() => FindOrCreateDataFile("Snippets.json");
    public static string? FindLearnContribution() => FindDataFile("LearnContribution.json");
    public static string FindOrCreateLearnContributionPath() => FindOrCreateDataFile("LearnContribution.json");

    /// <summary>Prefer LocalAppData for license (survives reinstall / portable publish).</summary>
    public static string FindOrCreateLicenseKeyPath() =>
        Path.Combine(LocalAppDataDir(), "license.key");

    public static string FindOrCreateTrialStatePath() =>
        Path.Combine(LocalAppDataDir(), "trial.json");

    public static string LocalAppDataDir()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutoCorrect");
        Directory.CreateDirectory(appData);
        return appData;
    }

    public static string? FindDataFilePublic(string fileName) => FindDataFile(fileName);

    public static string FindOrCreateDataFilePublic(string fileName) => FindOrCreateDataFile(fileName);

    private static string? FindDataFile(string fileName)
    {
        foreach (var root in CandidateRoots())
        {
            var p = Path.Combine(root, "Data", fileName);
            if (File.Exists(p))
                return p;
        }
        return null;
    }

    private static string FindOrCreateDataFile(string fileName)
    {
        var existing = FindDataFile(fileName);
        if (existing is not null)
            return existing;

        foreach (var root in CandidateRoots())
        {
            var dataDir = Path.Combine(root, "Data");
            if (Directory.Exists(dataDir))
                return Path.Combine(dataDir, fileName);
        }

        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutoCorrect");
        Directory.CreateDirectory(appData);
        return Path.Combine(appData, fileName);
    }

    public static IEnumerable<string> CandidateRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in EnumerateCandidates())
        {
            string full;
            try { full = Path.GetFullPath(candidate); }
            catch { continue; }
            if (seen.Add(full))
                yield return full;
        }
    }

    private static IEnumerable<string> EnumerateCandidates()
    {
        yield return AppContext.BaseDirectory;
        yield return Directory.GetCurrentDirectory();

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
        {
            yield return dir.FullName;
            if (dir.Name.Equals("AutoCorrectNative", StringComparison.OrdinalIgnoreCase) &&
                dir.Parent is not null)
                yield return dir.Parent.FullName;
        }
    }
}
