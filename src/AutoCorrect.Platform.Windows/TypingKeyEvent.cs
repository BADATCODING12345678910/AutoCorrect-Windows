namespace AutoCorrect.Platform.Windows;

/// <summary>Passthrough key observation for as-you-type autocorrect.</summary>
public sealed class TypingKeyEvent
{
    public required uint VkCode { get; init; }
    public required uint ScanCode { get; init; }
    public bool Shift { get; init; }
    public bool Ctrl { get; init; }
    public bool Alt { get; init; }
    public bool Win { get; init; }
    public bool Injected { get; init; }

    /// <summary>True when this event came from AutoCorrect's own SendInput (InjectMarker).</summary>
    public bool FromAutoCorrectInject { get; init; }

    /// <summary>Resolved character when printable; null for non-character keys.</summary>
    public char? Character { get; init; }

    public bool IsBackspace => VkCode == 0x08;
    public bool IsEscape => VkCode == 0x1B;
    public bool IsDelete => VkCode == 0x2E;
    public bool IsNavigation =>
        VkCode is 0x25 or 0x26 or 0x27 or 0x28 or 0x24 or 0x23 or 0x21 or 0x22; // arrows, home, end, pgup, pgdn
}

/// <summary>
/// Handle a typing key. Return <c>true</c> to swallow the key (do not pass to the app).
/// Hotkey chords are handled separately and never reach this handler.
/// </summary>
public delegate bool TypingKeyHandler(TypingKeyEvent e);
