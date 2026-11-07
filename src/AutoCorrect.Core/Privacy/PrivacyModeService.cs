using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoCorrect.Core.Privacy;

/// <summary>
/// Privacy mode: local-only Fix (no LanguageTool / ChatGPT / Rewrite) for N minutes or until turned off.
/// Active session persists across restarts via %LocalAppData%\AutoCorrect\privacy-session.json.
/// </summary>
public sealed class PrivacyModeService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _gate = new();
    private DateTimeOffset? _endsAtUtc;
    private bool _untilManualOff;

    public event Action? Changed;

    public bool IsActive
    {
        get
        {
            lock (_gate)
            {
                TickExpiredUnlocked();
                return _untilManualOff || _endsAtUtc is not null;
            }
        }
    }

    /// <summary>Remaining time, or null when inactive / until-manual.</summary>
    public TimeSpan? Remaining
    {
        get
        {
            lock (_gate)
            {
                TickExpiredUnlocked();
                if (_untilManualOff)
                    return null;
                if (_endsAtUtc is null)
                    return null;
                var left = _endsAtUtc.Value - DateTimeOffset.UtcNow;
                return left > TimeSpan.Zero ? left : TimeSpan.Zero;
            }
        }
    }

    public bool IsUntilManualOff
    {
        get
        {
            lock (_gate)
            {
                TickExpiredUnlocked();
                return _untilManualOff;
            }
        }
    }

    /// <param name="minutes">Positive = timed; 0 or less = until turned off.</param>
    public void Enable(int minutes)
    {
        lock (_gate)
        {
            if (minutes <= 0)
            {
                _untilManualOff = true;
                _endsAtUtc = null;
            }
            else
            {
                _untilManualOff = false;
                _endsAtUtc = DateTimeOffset.UtcNow.AddMinutes(minutes);
            }

            PersistUnlocked();
        }

        Changed?.Invoke();
    }

    public void Disable()
    {
        lock (_gate)
        {
            if (!_untilManualOff && _endsAtUtc is null)
            {
                ClearPersistUnlocked();
                return;
            }

            _untilManualOff = false;
            _endsAtUtc = null;
            ClearPersistUnlocked();
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Restore a previously persisted session. Returns true when privacy is active after restore.
    /// Expired or corrupt files are cleared; returns false with privacy off.
    /// </summary>
    public bool TryRestore()
    {
        bool restored;
        lock (_gate)
        {
            restored = TryRestoreUnlocked();
        }

        if (restored)
            Changed?.Invoke();
        return restored;
    }

    /// <summary>Call periodically so expiry fires <see cref="Changed"/>.</summary>
    public bool Tick()
    {
        lock (_gate)
        {
            var was = _untilManualOff || _endsAtUtc is not null;
            TickExpiredUnlocked();
            var now = _untilManualOff || _endsAtUtc is not null;
            if (was && !now)
            {
                // expired — notify outside lock (file already cleared in TickExpiredUnlocked)
            }
            else
                return false;
        }

        Changed?.Invoke();
        return true;
    }

    public string StatusSuffix()
    {
        lock (_gate)
        {
            TickExpiredUnlocked();
            if (_untilManualOff)
                return " · PRIVACY";
            if (_endsAtUtc is null)
                return "";
            var left = _endsAtUtc.Value - DateTimeOffset.UtcNow;
            if (left <= TimeSpan.Zero)
                return "";
            if (left.TotalHours >= 1)
                return $" · PRIVACY {(int)left.TotalHours}h{left.Minutes:D2}m";
            if (left.TotalMinutes >= 1)
                return $" · PRIVACY {(int)left.TotalMinutes}m";
            return " · PRIVACY <1m";
        }
    }

    public string StatusToast()
    {
        lock (_gate)
        {
            TickExpiredUnlocked();
            if (_untilManualOff)
                return "Privacy mode on — local-only until you turn it off (tray → Privacy).";
            if (_endsAtUtc is null)
                return "Privacy mode off — cloud Fix (LanguageTool / ChatGPT) allowed.";
            var left = _endsAtUtc.Value - DateTimeOffset.UtcNow;
            if (left <= TimeSpan.Zero)
                return "Privacy mode off — cloud Fix (LanguageTool / ChatGPT) allowed.";
            var mins = Math.Max(1, (int)Math.Ceiling(left.TotalMinutes));
            return $"Privacy mode on — local-only for ~{mins} min (no LanguageTool / ChatGPT / Rewrite).";
        }
    }

    /// <summary>Quiet toast after a successful startup restore.</summary>
    public string RestoredToast()
    {
        lock (_gate)
        {
            TickExpiredUnlocked();
            if (_untilManualOff)
                return "Privacy mode restored — local-only until you turn it off.";
            if (_endsAtUtc is null)
                return "Privacy mode restored.";
            var left = _endsAtUtc.Value - DateTimeOffset.UtcNow;
            if (left <= TimeSpan.Zero)
                return "Privacy mode restored.";
            var mins = Math.Max(1, (int)Math.Ceiling(left.TotalMinutes));
            return $"Privacy mode restored — local-only for ~{mins} min.";
        }
    }

    private bool TryRestoreUnlocked()
    {
        var path = SessionPath();
        try
        {
            if (!File.Exists(path))
                return false;

            var json = File.ReadAllText(path);
            var doc = JsonSerializer.Deserialize<PrivacySessionFile>(json, JsonOptions);
            if (doc is null)
            {
                ClearPersistUnlocked();
                return false;
            }

            if (doc.UntilManualOff)
            {
                _untilManualOff = true;
                _endsAtUtc = null;
                PersistUnlocked();
                return true;
            }

            if (doc.EndsAtUtc is { } end && end > DateTimeOffset.UtcNow)
            {
                _untilManualOff = false;
                _endsAtUtc = end;
                PersistUnlocked();
                return true;
            }

            // Expired or missing end time
            _untilManualOff = false;
            _endsAtUtc = null;
            ClearPersistUnlocked();
            return false;
        }
        catch
        {
            _untilManualOff = false;
            _endsAtUtc = null;
            ClearPersistUnlocked();
            return false;
        }
    }

    private void TickExpiredUnlocked()
    {
        if (_untilManualOff)
            return;
        if (_endsAtUtc is { } end && end <= DateTimeOffset.UtcNow)
        {
            _endsAtUtc = null;
            ClearPersistUnlocked();
        }
    }

    private void PersistUnlocked()
    {
        try
        {
            var dir = DataPaths.LocalAppDataDir();
            Directory.CreateDirectory(dir);
            var path = SessionPath();
            if (!_untilManualOff && _endsAtUtc is null)
            {
                ClearPersistUnlocked();
                return;
            }

            var doc = new PrivacySessionFile
            {
                UntilManualOff = _untilManualOff,
                EndsAtUtc = _untilManualOff ? null : _endsAtUtc
            };
            File.WriteAllText(path, JsonSerializer.Serialize(doc, JsonOptions));
        }
        catch
        {
            // Never break privacy UX on disk errors
        }
    }

    private void ClearPersistUnlocked()
    {
        try
        {
            var path = SessionPath();
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // ignore
        }
    }

    private static string SessionPath() =>
        Path.Combine(DataPaths.LocalAppDataDir(), "privacy-session.json");

    private sealed class PrivacySessionFile
    {
        public DateTimeOffset? EndsAtUtc { get; set; }
        public bool UntilManualOff { get; set; }
    }
}
