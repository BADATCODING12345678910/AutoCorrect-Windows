using AutoCorrect.Platform.Windows;

namespace FeatureDebugLab.Services;

/// <summary>Parse settings chords like <c>Win+Alt+F</c> and send them via SendInput.</summary>
public static class HotkeyChord
{
    public static bool TrySend(string? chord)
    {
        if (!TryParse(chord, out var win, out var alt, out var ctrl, out var shift, out var vk))
            return false;
        SendInputText.SendChordAsUser(win, alt, ctrl, shift, vk);
        return true;
    }

    public static bool TryParse(
        string? chord,
        out bool win,
        out bool alt,
        out bool ctrl,
        out bool shift,
        out ushort vk)
    {
        win = alt = ctrl = shift = false;
        vk = 0;
        if (string.IsNullOrWhiteSpace(chord))
            return false;

        var parts = chord.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string? key = null;
        foreach (var raw in parts)
        {
            var p = raw.Trim();
            if (p.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("Windows", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("LWin", StringComparison.OrdinalIgnoreCase))
            {
                win = true;
                continue;
            }
            if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("Menu", StringComparison.OrdinalIgnoreCase))
            {
                alt = true;
                continue;
            }
            if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                ctrl = true;
                continue;
            }
            if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                shift = true;
                continue;
            }
            key = p;
        }

        if (string.IsNullOrEmpty(key))
            return false;

        vk = key.ToUpperInvariant() switch
        {
            "F" => 0x46,
            "P" => 0x50,
            "R" => 0x52,
            "H" => 0x48,
            "SPACE" => 0x20,
            "ENTER" => 0x0D,
            "ESC" or "ESCAPE" => 0x1B,
            "LEFT" => 0x25,
            "UP" => 0x26,
            "RIGHT" => 0x27,
            "DOWN" => 0x28,
            "DELETE" or "DEL" => 0x2E,
            "BACK" or "BACKSPACE" => 0x08,
            _ when key.Length == 1 && char.IsLetterOrDigit(key[0]) =>
                (ushort)char.ToUpperInvariant(key[0]),
            _ => 0
        };
        return vk != 0;
    }
}
