using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AutoCorrect.Core.Autocorrect;

/// <summary>
/// Parses AHK <c>AutoCorrectHotstrings.ahk</c> into <c>Data/AutocorrectImported.json</c>
/// so startup does not re-scan 7k+ lines every launch.
/// </summary>
public static partial class AhkHotstringImporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>AHK line: <c>:options:trigger::replacement</c> or <c>::f("…")</c>.</summary>
    [GeneratedRegex(
        @"^:(?<opts>[^:]*):(?<trig>.+?)::(?:f\(\s*""(?<frep>(?:\\.|[^""\\])*)""\s*\)|(?<rep>[^\s;]*))?",
        RegexOptions.CultureInvariant)]
    private static partial Regex HotstringLine();

    public static ImportedLibraryFile ParseFile(string ahkPath)
    {
        var file = new ImportedLibraryFile
        {
            SourcePath = ahkPath,
            ImportedUtc = DateTimeOffset.UtcNow
        };

        if (!File.Exists(ahkPath))
            return file;

        var seenEntries = new HashSet<string>(StringComparer.Ordinal);
        var seenNullifiers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in File.ReadLines(ahkPath))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
                continue;
            if (!line.StartsWith(':'))
                continue;

            var m = HotstringLine().Match(line);
            if (!m.Success)
                continue;

            var opts = m.Groups["opts"].Value;
            var trigger = UnescapeAhk(m.Groups["trig"].Value.Trim());
            if (trigger.Length == 0)
                continue;
            // Word-buffer path is single-token only.
            if (trigger.Contains(' ') || trigger.Contains('\t'))
                continue;
            if (trigger.Contains("Ign0re", StringComparison.OrdinalIgnoreCase))
                continue;
            // Snippets own ;triggers — keep them out of the typo library.
            if (trigger.StartsWith(';'))
                continue;

            string replacement;
            if (m.Groups["frep"].Success && m.Groups["frep"].Length > 0)
                replacement = UnescapeQuoted(m.Groups["frep"].Value);
            else if (m.Groups["rep"].Success)
                replacement = UnescapeAhk(m.Groups["rep"].Value.Trim());
            else
                replacement = "";

            var star = opts.Contains('*');
            var question = opts.Contains('?');
            var caseSensitive = opts.Contains('C');
            var key = (caseSensitive ? "C:" : "i:") + trigger;

            // Nullifiers share triggers with real rules (e.g. :B0:kc:: blocks whole-word,
            // while :?:kc::ck still fixes longer tokens). Keep separate dedupe sets.
            if (replacement.Length == 0)
            {
                if (!seenNullifiers.Add(key))
                    continue;
                file.Nullifiers.Add(new ImportedNullifierEntry
                {
                    Trigger = trigger,
                    Options = opts,
                    Question = question,
                    CaseSensitive = caseSensitive
                });
                continue;
            }

            // Multiline / template-like replacements belong in Snippets, not typo inject.
            if (replacement.Contains('\n') || replacement.Contains('\r'))
                continue;

            if (!seenEntries.Add(key))
                continue;

            file.Entries.Add(new ImportedHotstringEntry
            {
                Trigger = trigger,
                Replacement = replacement,
                Options = opts,
                Star = star,
                Question = question,
                CaseSensitive = caseSensitive
            });
        }

        return file;
    }

    /// <summary>
    /// Load cached JSON, or (re)import from AHK when missing/stale/forced.
    /// </summary>
    public static ImportedLibraryFile EnsureImported(bool force = false, string? importedFileName = null)
    {
        var ahk = DataPaths.FindAhkHotstringsLibrary();
        var jsonPath = DataPaths.FindOrCreateAutocorrectImportedPath(importedFileName);

        if (!force && File.Exists(jsonPath))
        {
            try
            {
                var existing = JsonSerializer.Deserialize<ImportedLibraryFile>(
                    File.ReadAllText(jsonPath), JsonOptions);
                if (existing is { Entries.Count: > 0 })
                {
                    // Older caches omitted Nullifiers — rebuild so mid-word "?" stays safe.
                    var needsNullifiers = existing.Nullifiers is null || existing.Nullifiers.Count == 0;
                    if (ahk is null || !File.Exists(ahk))
                        return existing;

                    var ahkWrite = File.GetLastWriteTimeUtc(ahk);
                    var jsonWrite = File.GetLastWriteTimeUtc(jsonPath);
                    if (jsonWrite >= ahkWrite && !needsNullifiers)
                        return existing;
                }
            }
            catch
            {
                // fall through to reimport
            }
        }

        if (ahk is null || !File.Exists(ahk))
        {
            return File.Exists(jsonPath)
                ? JsonSerializer.Deserialize<ImportedLibraryFile>(File.ReadAllText(jsonPath), JsonOptions)
                  ?? new ImportedLibraryFile()
                : new ImportedLibraryFile();
        }

        var parsed = ParseFile(ahk);
        Save(parsed, jsonPath);
        return parsed;
    }

    public static void Save(ImportedLibraryFile file, string? jsonPath = null)
    {
        jsonPath ??= DataPaths.FindOrCreateAutocorrectImportedPath();
        var dir = Path.GetDirectoryName(jsonPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(file, JsonOptions));
    }

    private static string UnescapeAhk(string s)
    {
        if (!s.Contains('`'))
            return s;
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '`' && i + 1 < s.Length)
            {
                sb.Append(s[++i]);
                continue;
            }
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    private static string UnescapeQuoted(string s) =>
        s.Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);
}
