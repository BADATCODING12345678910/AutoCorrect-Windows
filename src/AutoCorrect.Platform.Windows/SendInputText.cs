using System.Runtime.InteropServices;

namespace AutoCorrect.Platform.Windows;

/// <summary>Inject text / backspaces via SendInput (for as-you-type autocorrect).</summary>
public static class SendInputText
{
    private const uint InputKeyboard = 1;
    private const uint KeyeventfKeyup = 0x0002;
    private const uint KeyeventfUnicode = 0x0004;
    private const ushort VkBack = 0x08;
    private const ushort VkTab = 0x09;
    private const ushort VkReturn = 0x0D;
    private const ushort VkSpace = 0x20;

    /// <summary>Magic dwExtraInfo so our LL hook can ignore self-injected keys.</summary>
    public static readonly IntPtr InjectMarker = new(0x0AC01);

    /// <summary>x64 INPUT must be 40 bytes — wrong size makes SendInput a silent no-op.</summary>
    public static int InputStructSize => Marshal.SizeOf<INPUT>();

    /// <returns>True when SendInput accepted the full batch (host may still ignore keys).</returns>
    public static bool ReplaceWord(int typedLength, string replacement, char endChar, bool paced = false)
    {
        if (typedLength < 0)
            typedLength = 0;

        if (!paced)
        {
            var inputs = new List<INPUT>(Math.Max(8, (typedLength + replacement.Length + 2) * 4));

            for (var i = 0; i < typedLength; i++)
                AddVk(inputs, VkBack, markInjected: true);

            foreach (var ch in replacement)
                AddChar(inputs, ch, markInjected: true);

            if (endChar != '\0')
                AddChar(inputs, endChar, markInjected: true);

            return Flush(inputs);
        }

        // Electron/Chromium often drops huge single-batch SendInput; pace in small chunks.
        // Chunked backspaces stay under ~100ms for typical typos (vs 4ms×N per key).
        var ok = true;
        const int backspaceChunk = 4;
        for (var i = 0; i < typedLength; i += backspaceChunk)
        {
            var n = Math.Min(backspaceChunk, typedLength - i);
            var chunk = new List<INPUT>(n * 2);
            for (var b = 0; b < n; b++)
                AddVk(chunk, VkBack, markInjected: true);
            if (!Flush(chunk))
                ok = false;
            if (i + n < typedLength)
                Thread.Sleep(1);
        }

        var type = new List<INPUT>(Math.Max(4, (replacement.Length + 1) * 2));
        foreach (var ch in replacement)
            AddChar(type, ch, markInjected: true);
        if (endChar != '\0')
            AddChar(type, endChar, markInjected: true);
        if (!Flush(type))
            ok = false;
        return ok;
    }

    /// <summary>
    /// Type text as a real user (no inject marker) so the autocorrect LL hook can see it.
    /// Used by Feature Debug Lab live typing tests.
    /// </summary>
    public static void TypeAsUser(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;
        var inputs = new List<INPUT>(Math.Max(8, text.Length * 4));
        foreach (var ch in text)
            AddChar(inputs, ch, markInjected: false);
        Flush(inputs);
    }

    public static void SendCtrlChordAsUser(ushort vk)
    {
        var inputs = new List<INPUT>(4)
        {
            MakeVk(0x11, 0, markInjected: false),
            MakeVk(vk, 0, markInjected: false),
            MakeVk(vk, KeyeventfKeyup, markInjected: false),
            MakeVk(0x11, KeyeventfKeyup, markInjected: false)
        };
        Flush(inputs);
    }

    /// <summary>
    /// Send a modifier chord as a real user (no inject marker) so GlobalHotkey / LL hooks see it.
    /// Example: Win+Alt+F → modifiers Win+Alt, key F.
    /// </summary>
    public static void SendChordAsUser(bool win, bool alt, bool ctrl, bool shift, ushort vk)
    {
        const ushort VkShift = 0x10;
        const ushort VkControl = 0x11;
        const ushort VkMenu = 0x12;
        const ushort VkLWin = 0x5B;

        var inputs = new List<INPUT>(16);
        if (win) inputs.Add(MakeVk(VkLWin, 0, markInjected: false));
        if (ctrl) inputs.Add(MakeVk(VkControl, 0, markInjected: false));
        if (alt) inputs.Add(MakeVk(VkMenu, 0, markInjected: false));
        if (shift) inputs.Add(MakeVk(VkShift, 0, markInjected: false));
        inputs.Add(MakeVk(vk, 0, markInjected: false));
        inputs.Add(MakeVk(vk, KeyeventfKeyup, markInjected: false));
        if (shift) inputs.Add(MakeVk(VkShift, KeyeventfKeyup, markInjected: false));
        if (alt) inputs.Add(MakeVk(VkMenu, KeyeventfKeyup, markInjected: false));
        if (ctrl) inputs.Add(MakeVk(VkControl, KeyeventfKeyup, markInjected: false));
        if (win) inputs.Add(MakeVk(VkLWin, KeyeventfKeyup, markInjected: false));
        Flush(inputs);
    }

    private static bool Flush(List<INPUT> inputs)
    {
        if (inputs.Count == 0)
            return true;

        var arr = inputs.ToArray();
        var cb = Marshal.SizeOf<INPUT>();
        var sent = SendInput((uint)arr.Length, arr, cb);
        if (sent == arr.Length)
            return true;

        Thread.Sleep(5);
        sent = SendInput((uint)arr.Length, arr, cb);
        return sent == arr.Length;
    }

    private static void AddChar(List<INPUT> list, char ch, bool markInjected)
    {
        switch (ch)
        {
            case ' ':
                AddVk(list, VkSpace, markInjected);
                return;
            case '\t':
                AddVk(list, VkTab, markInjected);
                return;
            case '\n':
            case '\r':
                AddVk(list, VkReturn, markInjected);
                return;
            case '\b':
                AddVk(list, VkBack, markInjected);
                return;
        }

        // UNICODE is the most compatible path across Win32 / Electron / browsers for letters.
        // (VK-only paths mis-handle CapsLock and some Chromium hosts.)
        AddUnicode(list, ch, markInjected);
    }

    private static void AddVk(List<INPUT> list, ushort vk, bool markInjected)
    {
        list.Add(MakeVk(vk, 0, markInjected));
        list.Add(MakeVk(vk, KeyeventfKeyup, markInjected));
    }

    private static void AddUnicode(List<INPUT> list, char ch, bool markInjected)
    {
        var extra = markInjected ? InjectMarker : IntPtr.Zero;
        list.Add(new INPUT
        {
            type = InputKeyboard,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = ch,
                    dwFlags = KeyeventfUnicode,
                    time = 0,
                    dwExtraInfo = extra
                }
            }
        });
        list.Add(new INPUT
        {
            type = InputKeyboard,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = ch,
                    dwFlags = KeyeventfUnicode | KeyeventfKeyup,
                    time = 0,
                    dwExtraInfo = extra
                }
            }
        });
    }

    private static INPUT MakeVk(ushort vk, uint flags, bool markInjected) => new()
    {
        type = InputKeyboard,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                wScan = 0,
                dwFlags = flags,
                time = 0,
                dwExtraInfo = markInjected ? InjectMarker : IntPtr.Zero
            }
        }
    };

    // ---- Win32 INPUT layout (must match user32 on x64 = 40 bytes) ----

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
}
