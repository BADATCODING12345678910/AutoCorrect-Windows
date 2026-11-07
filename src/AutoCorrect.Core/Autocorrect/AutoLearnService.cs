using System.Text.Json;
using AutoCorrect.Core.Settings;

namespace AutoCorrect.Core.Autocorrect;

/// <summary>
/// Learns typo→fix from manual backspace corrections (native port of AHK AutoLearner).
/// Modes: manual (count only), confirm (promote toast), silent (auto-add).
/// </summary>
public sealed class AutoLearnService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object _gate = new();
    private readonly Dictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _never = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<LedgerEntry> _ledger = [];
    private readonly Queue<PendingPromotion> _pending = new();
    private AutoLearnSettings _settings = new();
    private LearnedStore? _learned;
    private DictionaryDistanceMatcher? _dictionary;

    public event Action<PendingPromotion>? PromotionReady;

    public AutoLearnSettings Settings
    {
        get { lock (_gate) return CloneSettings(_settings); }
    }

    public int PendingCount
    {
        get { lock (_gate) return _pending.Count; }
    }

    public int LearnedCount => _learned?.Count ?? 0;

    public void Configure(AutoLearnSettings settings, LearnedStore learned, DictionaryDistanceMatcher? dictionary = null)
    {
        lock (_gate)
        {
            _settings = CloneSettings(settings);
            _learned = learned;
            _dictionary = dictionary;
            LoadCounts_NoLock();
            LoadNever_NoLock();
            LoadLedger_NoLock();
        }
    }

    /// <summary>Record a detected manual correction. May enqueue a promotion.</summary>
    public void RecordOccurrence(string trigger, string replacement)
    {
        PendingPromotion? ready = null;
        lock (_gate)
        {
            if (!_settings.Enabled)
                return;

            var trig = Normalize(trigger);
            var repl = Normalize(replacement);
            if (!IsPlausiblePair(trig, repl))
                return;
            if (_never.Contains(PairKey(trig, repl)) || _never.Contains(trig))
                return;
            if (_learned is not null && _learned.ContainsTrigger(trig))
                return;
            if (_ledger.Any(e => e.Status == "active" && e.Trigger == trig))
                return;

            var key = PairKey(trig, repl);
            _counts.TryGetValue(key, out var count);
            count++;
            _counts[key] = count;
            SaveCounts_NoLock();

            var mode = NormalizeMode(_settings.LearnMode);
            if (mode == "manual")
                return;
            if (count < Math.Max(1, _settings.MinOccurrences))
                return;
            if (_pending.Any(p => p.Trigger == trig && p.Replacement == repl))
                return;

            ready = new PendingPromotion(trig, repl, count, mode);
            _pending.Enqueue(ready);
        }

        if (ready is not null)
            PromotionReady?.Invoke(ready);
    }

    public PendingPromotion? PeekPending()
    {
        lock (_gate)
            return _pending.Count > 0 ? _pending.Peek() : null;
    }

    public PendingPromotion? DequeuePending()
    {
        lock (_gate)
            return _pending.Count > 0 ? _pending.Dequeue() : null;
    }

    public bool Accept(PendingPromotion promo)
    {
        lock (_gate)
        {
            if (_learned is null)
                return false;

            _learned.Upsert(promo.Trigger, promo.Replacement);
            _ledger.Add(new LedgerEntry
            {
                Status = "active",
                Date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                Mode = promo.Mode,
                Occurrences = promo.Occurrences,
                Trigger = promo.Trigger,
                Replacement = promo.Replacement
            });
            SaveLedger_NoLock();
            _counts.Remove(PairKey(promo.Trigger, promo.Replacement));
            SaveCounts_NoLock();
            // Drop matching pending if still queued
            TrimPending_NoLock(promo.Trigger, promo.Replacement);
            return true;
        }
    }

    public void Skip(PendingPromotion promo)
    {
        lock (_gate)
        {
            // Keep counts; just drop this pending item.
            TrimPending_NoLock(promo.Trigger, promo.Replacement);
        }
    }

    public void Never(PendingPromotion promo)
    {
        lock (_gate)
        {
            _never.Add(PairKey(promo.Trigger, promo.Replacement));
            _never.Add(promo.Trigger);
            SaveNever_NoLock();
            _counts.Remove(PairKey(promo.Trigger, promo.Replacement));
            SaveCounts_NoLock();
            TrimPending_NoLock(promo.Trigger, promo.Replacement);
        }
    }

    /// <summary>Keys blocked from AutoLearn re-promotion (trigger or trigger|replacement).</summary>
    public IReadOnlyList<string> SnapshotNeverKeys()
    {
        lock (_gate)
            return _never.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Silent mode: add immediately without confirm UI.</summary>
    public bool AcceptSilent(string trigger, string replacement, int occurrences)
    {
        var promo = new PendingPromotion(Normalize(trigger), Normalize(replacement), occurrences, "silent");
        return Accept(promo);
    }

    public (string Trigger, string Replacement)? UndoLast()
    {
        lock (_gate)
        {
            for (var i = _ledger.Count - 1; i >= 0; i--)
            {
                if (_ledger[i].Status != "active")
                    continue;
                var e = _ledger[i];
                e.Status = "removed";
                _ledger[i] = e;
                _learned?.Remove(e.Trigger);
                SaveLedger_NoLock();
                return (e.Trigger, e.Replacement);
            }
            return null;
        }
    }

    public IReadOnlyList<LedgerEntry> ActiveLedger()
    {
        lock (_gate)
            return _ledger.Where(e => e.Status == "active").ToList();
    }

    private bool IsPlausiblePair(string trig, string repl)
    {
        if (trig.Length < 3 || repl.Length < 3 || trig.Length > 32 || repl.Length > 32)
            return false;
        if (trig == repl)
            return false;
        if (!trig.All(IsWordChar) || !repl.All(IsWordChar))
            return false;

        // Prefer: replacement looks like a real word when dictionary is loaded.
        if (_dictionary is { Loaded: true })
        {
            if (!_dictionary.Contains(repl))
                return false;
            // Trigger should not already be a dictionary word (typo).
            if (_dictionary.Contains(trig))
                return false;
        }

        // Light letter-overlap: at least 40% of repl letters appear in trig (AHK Test A-ish).
        var overlap = repl.Count(c => trig.Contains(c));
        if (overlap * 100 < repl.Length * 40)
            return false;

        return true;
    }

    private void TrimPending_NoLock(string trig, string repl)
    {
        if (_pending.Count == 0)
            return;
        var keep = _pending.Where(p => !(p.Trigger == trig && p.Replacement == repl)).ToList();
        _pending.Clear();
        foreach (var p in keep)
            _pending.Enqueue(p);
    }

    private void LoadCounts_NoLock()
    {
        _counts.Clear();
        var path = DataPaths.FindAutoLearnCounts() ?? DataPaths.FindOrCreateAutoLearnCountsPath();
        if (!File.Exists(path))
            return;
        foreach (var line in File.ReadAllLines(path))
        {
            var t = line.Trim();
            if (t.Length == 0 || t.StartsWith(';'))
                continue;
            var parts = t.Split('\t');
            if (parts.Length < 3 || !int.TryParse(parts[2], out var n))
                continue;
            _counts[PairKey(parts[0], parts[1])] = n;
        }
    }

    private void SaveCounts_NoLock()
    {
        var path = DataPaths.FindOrCreateAutoLearnCountsPath();
        var lines = _counts.Select(kv =>
        {
            var parts = kv.Key.Split('\t');
            return parts.Length < 2 ? null : $"{parts[0]}\t{parts[1]}\t{kv.Value}";
        }).Where(l => l is not null);
        File.WriteAllLines(path, lines!);
    }

    private void LoadNever_NoLock()
    {
        _never.Clear();
        var path = DataPaths.FindAutoLearnNever();
        if (path is null || !File.Exists(path))
            return;
        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path), JsonOptions);
            if (list is null)
                return;
            foreach (var s in list)
                if (!string.IsNullOrWhiteSpace(s))
                    _never.Add(s.Trim().ToLowerInvariant());
        }
        catch
        {
            // ignore
        }
    }

    private void SaveNever_NoLock()
    {
        var path = DataPaths.FindOrCreateAutoLearnNeverPath();
        File.WriteAllText(path, JsonSerializer.Serialize(_never.OrderBy(s => s).ToList(), JsonOptions));
    }

    private void LoadLedger_NoLock()
    {
        _ledger.Clear();
        var path = DataPaths.FindAutoLearnLedger();
        if (path is null || !File.Exists(path))
            return;
        try
        {
            var list = JsonSerializer.Deserialize<List<LedgerEntry>>(File.ReadAllText(path), JsonOptions);
            if (list is not null)
                _ledger.AddRange(list);
        }
        catch
        {
            // ignore
        }
    }

    private void SaveLedger_NoLock()
    {
        var path = DataPaths.FindOrCreateAutoLearnLedgerPath();
        File.WriteAllText(path, JsonSerializer.Serialize(_ledger, JsonOptions));
    }

    private static string Normalize(string s) => s.Trim().ToLowerInvariant();

    private static string PairKey(string trig, string repl) => trig + "\t" + repl;

    private static string NormalizeMode(string? mode)
    {
        mode = (mode ?? "confirm").Trim().ToLowerInvariant();
        return mode is "manual" or "confirm" or "silent" ? mode : "confirm";
    }

    private static bool IsWordChar(char c) =>
        char.IsLetter(c) || c is '\'' or '-';

    private static AutoLearnSettings CloneSettings(AutoLearnSettings s) => new()
    {
        Enabled = s.Enabled,
        LearnMode = s.LearnMode,
        MinOccurrences = s.MinOccurrences
    };
}

public sealed record PendingPromotion(string Trigger, string Replacement, int Occurrences, string Mode);

public sealed class LedgerEntry
{
    public string Status { get; set; } = "active";
    public string Date { get; set; } = "";
    public string Mode { get; set; } = "";
    public int Occurrences { get; set; }
    public string Trigger { get; set; } = "";
    public string Replacement { get; set; } = "";
}
