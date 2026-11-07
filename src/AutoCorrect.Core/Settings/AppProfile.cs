namespace AutoCorrect.Core.Settings;

/// <summary>
/// Per-process overrides (richer than the exclude CSV).
/// Null / empty fields inherit the global Autocorrect / Fix settings.
/// </summary>
public sealed class AppProfile
{
    /// <summary>Process name without extension, e.g. <c>Code</c> or <c>slack</c>.</summary>
    public string Process { get; set; } = "";

    /// <summary>When false, skip as-you-type autocorrect in this app. Null = inherit.</summary>
    public bool? Autocorrect { get; set; }

    /// <summary>When false, Fix hotkey / tray Fix is blocked. Null = inherit.</summary>
    public bool? Fix { get; set; }

    /// <summary><c>strict</c> | <c>balanced</c> | empty = inherit global Strictness.</summary>
    public string? Strictness { get; set; }

    public bool? UseCurated { get; set; }
    public bool? UseImportedLibrary { get; set; }
    public bool? UseDictionaryDistance { get; set; }
    public bool? UseLearned { get; set; }
}

/// <summary>Parse / format the Settings multiline editor and resolve against a process.</summary>
public static class AppProfileStore
{
    public static string Format(IEnumerable<AppProfile>? profiles)
    {
        if (profiles is null)
            return "";
        var lines = new List<string>();
        foreach (var p in profiles)
        {
            var proc = NormalizeProcess(p.Process);
            if (proc.Length == 0)
                continue;
            var parts = new List<string> { proc };
            if (p.Autocorrect is bool ac)
                parts.Add(ac ? "autocorrect=on" : "autocorrect=off");
            if (p.Fix is bool fix)
                parts.Add(fix ? "fix=on" : "fix=off");
            if (!string.IsNullOrWhiteSpace(p.Strictness))
                parts.Add("strictness=" + p.Strictness.Trim().ToLowerInvariant());
            if (p.UseCurated is bool c)
                parts.Add(c ? "curated=on" : "curated=off");
            if (p.UseImportedLibrary is bool i)
                parts.Add(i ? "imported=on" : "imported=off");
            if (p.UseDictionaryDistance is bool d)
                parts.Add(d ? "dictionary=on" : "dictionary=off");
            if (p.UseLearned is bool l)
                parts.Add(l ? "learned=on" : "learned=off");
            lines.Add(string.Join(", ", parts));
        }
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Lines like <c>Code, autocorrect=off, fix=on</c> or <c>slack: strictness=strict</c>.
    /// </summary>
    public static List<AppProfile> Parse(string? text)
    {
        var list = new List<AppProfile>();
        if (string.IsNullOrWhiteSpace(text))
            return list;

        foreach (var raw in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var line = raw;
            if (line.StartsWith('#') || line.StartsWith(';'))
                continue;

            // Allow "proc: flags" or "proc, flags"
            var colon = line.IndexOf(':');
            if (colon > 0 && line.IndexOf('=', 0, colon) < 0)
                line = string.Concat(line.AsSpan(0, colon), ",", line.AsSpan(colon + 1));
            var bits = line.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (bits.Length == 0)
                continue;

            var profile = new AppProfile { Process = NormalizeProcess(bits[0]) };
            if (profile.Process.Length == 0)
                continue;

            for (var i = 1; i < bits.Length; i++)
            {
                var part = bits[i];
                var eq = part.IndexOf('=');
                if (eq <= 0)
                {
                    // Shorthand: bare "off" means autocorrect off (pause in this app)
                    if (part.Equals("off", StringComparison.OrdinalIgnoreCase) ||
                        part.Equals("pause", StringComparison.OrdinalIgnoreCase))
                        profile.Autocorrect = false;
                    else if (part.Equals("on", StringComparison.OrdinalIgnoreCase))
                        profile.Autocorrect = true;
                    else if (part.Equals("strict", StringComparison.OrdinalIgnoreCase) ||
                             part.Equals("balanced", StringComparison.OrdinalIgnoreCase))
                        profile.Strictness = part.ToLowerInvariant();
                    continue;
                }

                var key = part[..eq].Trim().ToLowerInvariant();
                var val = part[(eq + 1)..].Trim().ToLowerInvariant();
                switch (key)
                {
                    case "autocorrect":
                    case "ac":
                        profile.Autocorrect = ParseOnOff(val);
                        break;
                    case "fix":
                        profile.Fix = ParseOnOff(val);
                        break;
                    case "strictness":
                    case "mode":
                        if (val is "strict" or "balanced")
                            profile.Strictness = val;
                        break;
                    case "curated":
                        profile.UseCurated = ParseOnOff(val);
                        break;
                    case "imported":
                    case "library":
                        profile.UseImportedLibrary = ParseOnOff(val);
                        break;
                    case "dictionary":
                    case "dict":
                        profile.UseDictionaryDistance = ParseOnOff(val);
                        break;
                    case "learned":
                        profile.UseLearned = ParseOnOff(val);
                        break;
                }
            }

            list.Add(profile);
        }

        return list;
    }

    public static AppProfile? Find(IEnumerable<AppProfile>? profiles, string? processName)
    {
        var name = NormalizeProcess(processName);
        if (name.Length == 0 || profiles is null)
            return null;
        foreach (var p in profiles)
        {
            if (NormalizeProcess(p.Process).Equals(name, StringComparison.OrdinalIgnoreCase))
                return p;
        }
        return null;
    }

    public static string NormalizeProcess(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return "";
        var name = processName.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        return name;
    }

    private static bool? ParseOnOff(string val) =>
        val switch
        {
            "on" or "true" or "1" or "yes" => true,
            "off" or "false" or "0" or "no" => false,
            _ => null
        };
}
