namespace AutoCorrect.Platform.Abstractions;

/// <summary>
/// OS-specific dictation session detection (Win+H / speech UI / etc.).
/// </summary>
public interface IDictationSession : IDisposable
{
    bool IsSupported { get; }
    bool IsSessionActive { get; }

    /// <summary>Fired when a dictation session ends and a Fix should be scheduled.</summary>
    event EventHandler<DictationSessionEndedEventArgs>? SessionEnded;

    /// <summary>Optional: session began (pause typing autocorrect, etc.).</summary>
    event EventHandler? SessionStarted;

    void Start();
    void Stop();
}

public sealed class DictationSessionEndedEventArgs : EventArgs
{
    public required nint TargetWindowHandle { get; init; }

    /// <summary>Focused control HWND at session start (may differ from top-level window).</summary>
    public nint TargetControlHandle { get; init; }

    public required string Reason { get; init; }

    /// <summary>Suggested delay before capturing/fixing (Voice Typing UI teardown).</summary>
    public int AutoDelayMs { get; init; } = 1500;

    /// <summary>True if the Voice Typing mic UI was confirmed open during the session.</summary>
    public bool VoiceUiWasSeen { get; init; }

    /// <summary>How long the session was active (ms).</summary>
    public long ElapsedMs { get; init; }

    public int? CaretScreenX { get; init; }
    public int? CaretScreenY { get; init; }
}
