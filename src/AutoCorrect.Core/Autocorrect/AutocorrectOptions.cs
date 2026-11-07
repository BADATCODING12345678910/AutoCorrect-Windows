using AutoCorrect.Core.Settings;

namespace AutoCorrect.Core.Autocorrect;

/// <summary>Runtime options mirrored from <c>AutocorrectSettings</c>.</summary>
public sealed class AutocorrectOptions
{
    public bool Enabled { get; set; } = true;
    public string Strictness { get; set; } = "strict";
    public bool UseCurated { get; set; } = true;
    public bool UseImportedLibrary { get; set; }
    public bool UseDictionaryDistance { get; set; }
    public bool UseLearned { get; set; } = true;
    public bool ProtectZones { get; set; } = true;
    public HashSet<string> ExcludedProcesses { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "AutoCorrect.App"
    };
    public List<AppProfile> AppProfiles { get; set; } = [];

    public bool IsStrict =>
        !string.Equals(Strictness, "balanced", StringComparison.OrdinalIgnoreCase);

    public static AutocorrectOptions FromSettings(AutocorrectSettings s)
    {
        var opts = new AutocorrectOptions
        {
            Enabled = s.Enabled,
            Strictness = string.IsNullOrWhiteSpace(s.Strictness) ? "strict" : s.Strictness.Trim(),
            UseCurated = s.UseCurated,
            UseImportedLibrary = s.UseImportedLibrary,
            UseDictionaryDistance = s.UseDictionaryDistance,
            UseLearned = s.UseLearned,
            ProtectZones = s.ProtectZones,
            ExcludedProcesses = ParseExcluded(s.ExcludedProcesses),
            AppProfiles = s.AppProfiles?.Select(CloneProfile).ToList() ?? []
        };
        return opts;
    }

    public static AppProfile CloneProfile(AppProfile p) => new()
    {
        Process = p.Process,
        Autocorrect = p.Autocorrect,
        Fix = p.Fix,
        Strictness = p.Strictness,
        UseCurated = p.UseCurated,
        UseImportedLibrary = p.UseImportedLibrary,
        UseDictionaryDistance = p.UseDictionaryDistance,
        UseLearned = p.UseLearned
    };

    public static HashSet<string> ParseExcluded(string? csv)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(csv))
        {
            set.Add("AutoCorrect.App");
            return set;
        }

        foreach (var part in csv.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var name = part;
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name = name[..^4];
            if (name.Length > 0)
                set.Add(name);
        }

        if (set.Count == 0)
            set.Add("AutoCorrect.App");
        return set;
    }

    /// <summary>Effective toggles for a focused process (global + profile + exclude CSV).</summary>
    public ResolvedAppAutocorrect ResolveForProcess(string? processName)
    {
        var excluded = false;
        if (!string.IsNullOrWhiteSpace(processName))
        {
            var name = AppProfileStore.NormalizeProcess(processName);
            excluded = ExcludedProcesses.Contains(name);
        }

        var profile = AppProfileStore.Find(AppProfiles, processName);
        var autocorrectOn = Enabled && !excluded && profile?.Autocorrect != false;
        var strictness = !string.IsNullOrWhiteSpace(profile?.Strictness)
            ? profile!.Strictness!.Trim()
            : Strictness;
        return new ResolvedAppAutocorrect
        {
            AutocorrectEnabled = autocorrectOn,
            FixEnabled = profile?.Fix != false,
            Strictness = strictness,
            UseCurated = profile?.UseCurated ?? UseCurated,
            UseImportedLibrary = profile?.UseImportedLibrary ?? UseImportedLibrary,
            UseDictionaryDistance = profile?.UseDictionaryDistance ?? UseDictionaryDistance,
            UseLearned = profile?.UseLearned ?? UseLearned,
            Profile = profile
        };
    }
}

/// <summary>Per-process effective autocorrect / Fix flags after applying profiles.</summary>
public sealed class ResolvedAppAutocorrect
{
    public bool AutocorrectEnabled { get; init; }
    public bool FixEnabled { get; init; } = true;
    public string Strictness { get; init; } = "balanced";
    public bool UseCurated { get; init; }
    public bool UseImportedLibrary { get; init; }
    public bool UseDictionaryDistance { get; init; }
    public bool UseLearned { get; init; }
    public AppProfile? Profile { get; init; }

    public bool IsStrict =>
        !string.Equals(Strictness, "balanced", StringComparison.OrdinalIgnoreCase);
}
