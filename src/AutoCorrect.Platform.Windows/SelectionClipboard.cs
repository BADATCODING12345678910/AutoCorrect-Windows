using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace AutoCorrect.Platform.Windows;

/// <summary>
/// Read/replace the current text selection with mass-app compatibility (UIA when possible, else Ctrl+C/V).
/// Capture must run while the target app still has focus — before showing AutoCorrect UI.
/// </summary>
public static class SelectionClipboard
{
    /// <summary>Try UIA selection, then clipboard Ctrl+C. Returns false when nothing is selected.</summary>
    public static bool TryCaptureSelection(AutomationElement? preferred, out string text, out string? previousClipboard)
    {
        text = "";
        previousClipboard = SnapshotClipboard();

        if (preferred is not null && TryUiaSelection(preferred, out var uia) && !string.IsNullOrWhiteSpace(uia))
        {
            text = uia;
            return true;
        }

        var focused = AutomationElement.FocusedElement;
        if (focused is not null &&
            !ReferenceEquals(focused, preferred) &&
            TryUiaSelection(focused, out uia) &&
            !string.IsNullOrWhiteSpace(uia))
        {
            text = uia;
            return true;
        }

        if (!TryCtrlCopy(out var clip) || string.IsNullOrWhiteSpace(clip))
            return false;

        // Ctrl+C with no selection often copies nothing or entire field in some apps — reject huge vs previous.
        if (previousClipboard is not null &&
            clip == previousClipboard &&
            clip.Length > 4000)
            return false;

        text = clip;
        return true;
    }

    /// <summary>Paste <paramref name="replacement"/> over the still-selected range (Ctrl+V), then restore clipboard.</summary>
    public static bool TryPasteOverSelection(string replacement, string? previousClipboard)
    {
        if (string.IsNullOrEmpty(replacement))
            return false;

        try
        {
            System.Windows.Clipboard.SetText(replacement);
            SendCtrlChord('V');
            Thread.Sleep(50);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            RestoreClipboard(previousClipboard);
        }
    }

    public static void RestoreClipboard(string? previousClipboard)
    {
        try
        {
            if (previousClipboard is not null)
                System.Windows.Clipboard.SetText(previousClipboard);
            else
                System.Windows.Clipboard.Clear();
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>Read plain text from the clipboard (no keystrokes).</summary>
    public static bool TryGetClipboardText(out string text)
    {
        text = "";
        try
        {
            if (!System.Windows.Clipboard.ContainsText())
                return false;
            text = System.Windows.Clipboard.GetText() ?? "";
            return !string.IsNullOrEmpty(text);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Replace clipboard text. Returns false on failure.</summary>
    public static bool TrySetClipboardText(string text)
    {
        try
        {
            System.Windows.Clipboard.SetText(text ?? "");
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Ctrl+V into the focused field (clipboard must already hold the payload).</summary>
    public static void PasteClipboard()
    {
        try { SendCtrlChord('V'); }
        catch { /* ignore */ }
    }

    private static bool TryUiaSelection(AutomationElement el, out string text)
    {
        text = "";
        try
        {
            if (!el.TryGetCurrentPattern(TextPattern.Pattern, out var obj) || obj is not TextPattern tp)
                return false;
            var sels = tp.GetSelection();
            if (sels is null || sels.Length == 0)
                return false;
            text = sels[0].GetText(-1) ?? "";
            return !string.IsNullOrEmpty(text);
        }
        catch
        {
            return false;
        }
    }

    private static string? SnapshotClipboard()
    {
        try
        {
            return System.Windows.Clipboard.ContainsText()
                ? System.Windows.Clipboard.GetText()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryCtrlCopy(out string text)
    {
        text = "";
        try
        {
            System.Windows.Clipboard.Clear();
            SendCtrlChord('C');
            Thread.Sleep(40);
            for (var i = 0; i < 6; i++)
            {
                if (System.Windows.Clipboard.ContainsText())
                {
                    text = System.Windows.Clipboard.GetText() ?? "";
                    if (!string.IsNullOrEmpty(text))
                        return true;
                }
                Thread.Sleep(25);
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static void SendCtrlChord(char vkLetter)
    {
        const byte vkControl = 0x11;
        var vk = (byte)char.ToUpperInvariant(vkLetter);
        keybd_event(vkControl, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, 2, UIntPtr.Zero);
        keybd_event(vkControl, 0, 2, UIntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
}
