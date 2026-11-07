namespace AutoCorrect.Platform.Windows;

/// <summary>Parsed chord like <c>Win+Alt+F</c> or <c>Ctrl+Shift+F9</c>.</summary>
public sealed class HotkeyChord
{
    public bool Win { get; init; }
    public bool Ctrl { get; init; }
    public bool Alt { get; init; }
    public bool Shift { get; init; }
    public uint Vk { get; init; }
    public string Display { get; init; } = "";

    public bool IsEmpty => Vk == 0;

    public static HotkeyChord? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return null;

        var win = false;
        var ctrl = false;
        var alt = false;
        var shift = false;
        uint vk = 0;

        foreach (var raw in parts)
        {
            var p = raw.Trim();
            if (p.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("Windows", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("Meta", StringComparison.OrdinalIgnoreCase))
            {
                win = true;
                continue;
            }
            if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                ctrl = true;
                continue;
            }
            if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase) ||
                p.Equals("Menu", StringComparison.OrdinalIgnoreCase))
            {
                alt = true;
                continue;
            }
            if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                shift = true;
                continue;
            }

            vk = ParseKey(p);
            if (vk == 0)
                return null;
        }

        if (vk == 0)
            return null;

        // Require at least one modifier so bare letters don't steal typing.
        if (!win && !ctrl && !alt && !shift)
            return null;

        var display = Format(win, ctrl, alt, shift, vk);
        return new HotkeyChord
        {
            Win = win,
            Ctrl = ctrl,
            Alt = alt,
            Shift = shift,
            Vk = vk,
            Display = display
        };
    }

    public bool Matches(uint vkCode, bool winDown, bool ctrlDown, bool altDown, bool shiftDown) =>
        vkCode == Vk &&
        winDown == Win &&
        ctrlDown == Ctrl &&
        altDown == Alt &&
        shiftDown == Shift;

    public static string Format(bool win, bool ctrl, bool alt, bool shift, uint vk)
    {
        var parts = new List<string>();
        if (win) parts.Add("Win");
        if (ctrl) parts.Add("Ctrl");
        if (alt) parts.Add("Alt");
        if (shift) parts.Add("Shift");
        parts.Add(KeyName(vk));
        return string.Join('+', parts);
    }

    private static uint ParseKey(string p)
    {
        if (p.Length == 1)
        {
            var c = char.ToUpperInvariant(p[0]);
            if (c is >= 'A' and <= 'Z')
                return c;
            if (c is >= '0' and <= '9')
                return c;
        }

        if (p.Length >= 2 && (p[0] is 'F' or 'f') &&
            int.TryParse(p[1..], out var fn) && fn is >= 1 and <= 24)
            return (uint)(0x70 + fn - 1); // VK_F1 = 0x70

        return p.ToUpperInvariant() switch
        {
            "SPACE" => 0x20,
            "TAB" => 0x09,
            "ENTER" or "RETURN" => 0x0D,
            "ESC" or "ESCAPE" => 0x1B,
            "INSERT" or "INS" => 0x2D,
            "DELETE" or "DEL" => 0x2E,
            "HOME" => 0x24,
            "END" => 0x23,
            "PGUP" or "PAGEUP" => 0x21,
            "PGDN" or "PAGEDOWN" => 0x22,
            "LEFT" => 0x25,
            "UP" => 0x26,
            "RIGHT" => 0x27,
            "DOWN" => 0x28,
            _ => 0
        };
    }

    private static string KeyName(uint vk)
    {
        if (vk is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A)
            return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87)
            return "F" + (vk - 0x70 + 1);
        return vk switch
        {
            0x20 => "Space",
            0x09 => "Tab",
            0x0D => "Enter",
            0x1B => "Esc",
            0x2D => "Insert",
            0x2E => "Delete",
            0x24 => "Home",
            0x23 => "End",
            0x21 => "PgUp",
            0x22 => "PgDn",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            _ => "Vk" + vk
        };
    }
}
