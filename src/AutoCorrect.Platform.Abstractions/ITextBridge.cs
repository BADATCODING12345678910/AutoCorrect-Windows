namespace AutoCorrect.Platform.Abstractions;

/// <summary>
/// Cross-platform contract for reading/writing the focused text field.
/// Windows: UIA; Linux: AT-SPI; macOS: Accessibility.
/// </summary>
public interface ITextBridge
{
    /// <summary>Snapshot focused text + metadata without mutating the field.</summary>
    TextReadResult TryReadFocused();

    /// <summary>
    /// Replace focused field text. Prefers surgical UIA write; callers may fall back
    /// to clipboard paste when <see cref="TextWriteResult.UsedClipboardFallback"/> is true.
    /// </summary>
    TextWriteResult TryWriteFocused(string newText, TextWriteOptions? options = null);
}

/// <summary>
/// Optional Windows helper: operate on a known AutomationElement without relying on focus.
/// Implemented by <c>UiaTextBridge</c>; other platforms ignore.
/// </summary>
public interface IElementTextBridge
{
    TextReadResult TryReadElement(object automationElement);
    TextWriteResult TryWriteElement(object automationElement, string newText, TextWriteOptions? options = null);
}

public sealed class TextWriteOptions
{
    /// <summary>Allow Ctrl+A / clipboard paste when UIA Value/Text patterns fail.</summary>
    public bool AllowClipboardFallback { get; init; } = true;

    /// <summary>If set, only write when the focused HWND still matches (stale-focus guard).</summary>
    public nint? ExpectedWindowHandle { get; init; }

    /// <summary>
    /// Prefer shorter settle sleeps on clipboard fallback (local apply budget).
    /// ValuePattern / TextPattern paths are unchanged (already no artificial delay).
    /// </summary>
    public bool PreferLowLatency { get; init; }
}

public sealed record FocusedFieldInfo(
    string ProcessName,
    string WindowTitle,
    string ControlType,
    string ClassName,
    nint WindowHandle,
    bool SupportsValuePattern,
    bool SupportsTextPattern,
    string? AutomationId,
    string? Name);

public sealed record TextReadResult(
    bool Success,
    string? Text,
    FocusedFieldInfo? Field,
    string? Method,
    string? Error)
{
    public static TextReadResult Fail(string error, FocusedFieldInfo? field = null) =>
        new(false, null, field, null, error);

    public static TextReadResult Ok(string text, FocusedFieldInfo field, string method) =>
        new(true, text, field, method, null);
}

public sealed record TextWriteResult(
    bool Success,
    FocusedFieldInfo? Field,
    string? Method,
    bool UsedClipboardFallback,
    string? Error)
{
    public static TextWriteResult Fail(string error, FocusedFieldInfo? field = null) =>
        new(false, field, null, false, error);

    public static TextWriteResult Ok(FocusedFieldInfo field, string method, bool clipboardFallback = false) =>
        new(true, field, method, clipboardFallback, null);
}
