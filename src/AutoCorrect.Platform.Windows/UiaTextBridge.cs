using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using AutoCorrect.Platform.Abstractions;

namespace AutoCorrect.Platform.Windows;

/// <summary>
/// Windows UI Automation text bridge. Prefers ValuePattern / TextPattern;
/// optional clipboard fallback for Chromium/Electron contenteditable.
/// Must run on an STA thread.
/// </summary>
public sealed class UiaTextBridge : ITextBridge, IElementTextBridge
{
    public TextReadResult TryReadFocused()
    {
        try
        {
            EnsureSta();
            var focused = AutomationElement.FocusedElement;
            if (focused is null)
                return TextReadResult.Fail("No focused AutomationElement.");
            return TryReadElement(focused);
        }
        catch (Exception ex)
        {
            return TextReadResult.Fail($"UIA read exception: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Find the largest safe Edit/Document in <paramref name="processId"/>, click-focus it,
    /// and optionally seed <paramref name="probeText"/> via ValuePattern.
    /// </summary>
    public bool TryFocusEditableInProcess(int processId, string? probeText, out FocusedFieldInfo? field, out string detail)
    {
        field = null;
        detail = "";
        try
        {
            EnsureSta();
            AutomationElement? best = null;
            var bestScore = int.MinValue;

            void Consider(AutomationElement? candidate, int bonus = 0)
            {
                if (candidate is null || !IsSafeLabEditable(candidate))
                    return;
                var score = ScoreEditable(candidate) + bonus;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            // Prefer the process main window handle tree (more reliable than Root children alone).
            try
            {
                using var p = Process.GetProcessById(processId);
                p.Refresh();
                if (p.MainWindowHandle != IntPtr.Zero)
                {
                    var main = AutomationElement.FromHandle(p.MainWindowHandle);
                    if (main is not null)
                    {
                        Consider(FindLargestSafeEditable(main), bonus: 5);
                        Consider(FindBestTextDescendant(main), bonus: 3);
                    }
                }
            }
            catch { /* ignore */ }

            var root = AutomationElement.RootElement;
            var procCond = new PropertyCondition(AutomationElement.ProcessIdProperty, processId);
            var windows = root.FindAll(TreeScope.Children, procCond);
            foreach (AutomationElement win in windows)
            {
                try
                {
                    if (win.Current.IsOffscreen)
                        continue;
                    Consider(FindLargestSafeEditable(win));
                    Consider(FindBestTextDescendant(win));
                }
                catch
                {
                    // next window
                }
            }

            try
            {
                var focused = AutomationElement.FocusedElement;
                if (focused is not null && focused.Current.ProcessId == processId)
                {
                    Consider(FindLargestSafeEditable(focused), bonus: 2);
                    Consider(IsSafeLabEditable(focused) ? focused : null, bonus: 2);
                }
            }
            catch { /* ignore */ }

            if (best is null)
            {
                detail = "no usable Edit/Document (need a real document/body field)";
                return false;
            }

            ClickElementCenter(best);
            try { best.SetFocus(); } catch { /* ignore */ }
            Thread.Sleep(80);
            field = Describe(best);

            if (!string.IsNullOrEmpty(probeText))
            {
                if (TrySetValue(best, probeText))
                {
                    detail = $"focused + ValuePattern ({field.ControlType}, score={bestScore})";
                    return true;
                }
                detail = $"focused {field.ControlType} (needs keystrokes; score={bestScore})";
                return true;
            }

            detail = $"focused {field.ControlType}";
            return true;
        }
        catch (Exception ex)
        {
            detail = ex.Message;
            return false;
        }
    }

    /// <summary>True when the foreground window belongs to <paramref name="processId"/>.</summary>
    public bool IsForegroundProcess(int processId)
    {
        try
        {
            var fg = GetForegroundWindow();
            if (fg == IntPtr.Zero)
                return false;
            _ = GetWindowThreadProcessId(fg, out var pid);
            return pid == (uint)processId;
        }
        catch
        {
            return false;
        }
    }

    private static AutomationElement? FindLargestSafeEditable(AutomationElement root)
    {
        try
        {
            var condition = new OrCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));
            var found = root.FindAll(TreeScope.Descendants, condition);
            AutomationElement? best = null;
            var bestScore = int.MinValue;
            foreach (AutomationElement el in found)
            {
                try
                {
                    if (!IsSafeLabEditable(el))
                        continue;
                    var score = ScoreEditable(el);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = el;
                    }
                }
                catch { /* next */ }
            }
            return best;
        }
        catch
        {
            return null;
        }
    }

    private static void ClickElementCenter(AutomationElement el)
    {
        try
        {
            var r = el.Current.BoundingRectangle;
            if (r.Width < 2 || r.Height < 2 || double.IsInfinity(r.X) || double.IsNaN(r.X))
                return;
            var x = (int)Math.Round(r.X + r.Width / 2);
            var y = (int)Math.Round(r.Y + r.Height / 2);
            SetCursorPos(x, y);
            mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); // LEFTDOWN
            mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); // LEFTUP
            Thread.Sleep(40);
        }
        catch
        {
            // ignore
        }
    }

    private static int ScoreEditable(AutomationElement el)
    {
        var score = 0;
        try
        {
            if (el.TryGetCurrentPattern(ValuePattern.Pattern, out _))
                score += 3;
            if (el.TryGetCurrentPattern(TextPattern.Pattern, out _))
                score += 4;
            if (el.Current.IsKeyboardFocusable)
                score += 2;
            var name = (el.Current.Name ?? "") + " " + (el.Current.AutomationId ?? "");
            if (el.Current.ControlType == ControlType.Document)
                score += 8;
            // Prefer large document bodies over tiny search/combo edits.
            var r = el.Current.BoundingRectangle;
            if (!double.IsNaN(r.Width) && !double.IsNaN(r.Height) &&
                !double.IsInfinity(r.Width) && r.Width > 0 && r.Height > 0)
            {
                var area = Math.Min(r.Width * r.Height, 2_000_000);
                score += (int)(area / 8000);
                if (r.Height >= 60 && r.Width >= 120)
                    score += 10;
                if (r.Height < 28 || r.Width < 60)
                    score -= 12; // toolbar/search sized
            }
            if (name.Contains("search", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("address", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("path", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("filter", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Find", StringComparison.OrdinalIgnoreCase))
                score -= 20;
        }
        catch { /* ignore */ }
        return score;
    }

    /// <summary>Reject shell list/rename surfaces and tiny chrome edits.</summary>
    private static bool IsSafeLabEditable(AutomationElement el)
    {
        try
        {
            var type = el.Current.ControlType;
            if (type != ControlType.Edit && type != ControlType.Document)
                return false;
            if (!el.Current.IsKeyboardFocusable && !el.TryGetCurrentPattern(ValuePattern.Pattern, out _))
                return false;

            var cls = el.Current.ClassName ?? "";
            var name = el.Current.Name ?? "";
            var autoId = el.Current.AutomationId ?? "";
            var blob = $"{cls} {name} {autoId}";

            if (cls.Contains("UIItemsView", StringComparison.OrdinalIgnoreCase) ||
                blob.Contains("Rename", StringComparison.OrdinalIgnoreCase) ||
                blob.Contains("System.ItemNameDisplay", StringComparison.OrdinalIgnoreCase))
                return false;

            var r = el.Current.BoundingRectangle;
            if (!double.IsNaN(r.Height) && r.Height > 0 && r.Height < 22 &&
                type == ControlType.Edit)
                return false; // single-line chrome crumbs

            // Walk parents — skip list/tree/rename hosts. Allow Pane/Group/Window/Document.
            var walker = TreeWalker.ControlViewWalker;
            var node = walker.GetParent(el);
            for (var i = 0; i < 10 && node is not null; i++)
            {
                var pt = node.Current.ControlType;
                if (pt == ControlType.List || pt == ControlType.ListItem ||
                    pt == ControlType.DataGrid || pt == ControlType.DataItem ||
                    pt == ControlType.Tree || pt == ControlType.TreeItem ||
                    pt == ControlType.Menu || pt == ControlType.MenuBar ||
                    pt == ControlType.ToolBar || pt == ControlType.StatusBar)
                    return false;
                node = walker.GetParent(node);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    public TextWriteResult TryWriteFocused(string newText, TextWriteOptions? options = null)
    {
        try
        {
            EnsureSta();
            var focused = AutomationElement.FocusedElement;
            if (focused is null)
                return TextWriteResult.Fail("No focused AutomationElement.");
            return TryWriteElement(focused, newText, options);
        }
        catch (Exception ex)
        {
            return TextWriteResult.Fail($"UIA write exception: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public TextReadResult TryReadElement(object automationElement)
    {
        if (automationElement is not AutomationElement el)
            return TextReadResult.Fail("Expected System.Windows.Automation.AutomationElement.");

        try
        {
            EnsureSta();
            return ReadElementCore(el, allowDescendants: true);
        }
        catch (Exception ex)
        {
            return TextReadResult.Fail($"UIA read exception: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public TextWriteResult TryWriteElement(object automationElement, string newText, TextWriteOptions? options = null)
    {
        options ??= new TextWriteOptions();
        if (automationElement is not AutomationElement el)
            return TextWriteResult.Fail("Expected System.Windows.Automation.AutomationElement.");

        try
        {
            EnsureSta();
            var field = Describe(el);
            if (options.ExpectedWindowHandle is { } expected && expected != 0 &&
                field.WindowHandle != 0 && field.WindowHandle != expected)
            {
                return TextWriteResult.Fail(
                    $"Focus moved (expected hwnd 0x{expected:X}, got 0x{field.WindowHandle:X}).",
                    field);
            }

            if (TrySetValue(el, newText))
                return TextWriteResult.Ok(field, "ValuePattern.SetValue");

            if (TrySetViaTextPattern(el, newText))
                return TextWriteResult.Ok(field, "TextPattern.Replace");

            var descendant = FindBestTextDescendant(el);
            if (descendant is not null && !Equals(descendant, el))
            {
                var childField = Describe(descendant);
                if (TrySetValue(descendant, newText))
                    return TextWriteResult.Ok(childField, "ValuePattern.SetValue(descendant)");
                if (TrySetViaTextPattern(descendant, newText))
                    return TextWriteResult.Ok(childField, "TextPattern.Replace(descendant)");
            }

            // Focus then clipboard — Chromium/Electron often need this.
            try { el.SetFocus(); } catch { /* ignore */ }
            Thread.Sleep(options.PreferLowLatency ? 8 : 40);

            if (options.AllowClipboardFallback)
            {
                if (TryClipboardReplace(newText, options.PreferLowLatency))
                    return TextWriteResult.Ok(field, "ClipboardCtrlAV", clipboardFallback: true);
                return TextWriteResult.Fail("UIA write failed and clipboard fallback failed.", field);
            }

            return TextWriteResult.Fail(
                "UIA write failed (Value/Text patterns unavailable or read-only). Clipboard fallback disabled.",
                field);
        }
        catch (Exception ex)
        {
            return TextWriteResult.Fail($"UIA write exception: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static TextReadResult ReadElementCore(AutomationElement el, bool allowDescendants)
    {
        var field = Describe(el);

        // Never treat Explorer/tree/chrome as a dictation field.
        if (IsNonEditableChrome(field))
            return TextReadResult.Fail("Focused UI is not a text field (tree/list/toolbar).", field);

        if (TryGetValuePattern(el, out var valuePattern) && valuePattern is not null)
        {
            var text = valuePattern.Current.Value ?? string.Empty;
            // Edit ValuePattern is usually trustworthy except Chromium/Electron URL leaks.
            if (!IsBogusFieldValue(text, field) && !LooksLikeUiDump(text))
                return TextReadResult.Ok(text, field, "ValuePattern");
        }

        if (TryGetTextPattern(el, out var textPattern) && textPattern is not null)
        {
            var text = textPattern.DocumentRange.GetText(-1) ?? string.Empty;
            if (!IsBogusFieldValue(text, field) && !LooksLikeUiDump(text))
                return TextReadResult.Ok(text, field, "TextPattern");
        }

        if (allowDescendants)
        {
            var descendant = FindBestTextDescendant(el);
            if (descendant is not null && !Equals(descendant, el))
            {
                var child = ReadElementCore(descendant, allowDescendants: false);
                if (child.Success)
                    return child with { Method = $"{child.Method}(descendant)" };
            }
        }

        // Top-level Electron/window HWND: don't Ctrl+A the whole shell (file tree, etc.).
        if (IsTopLevelShell(field))
            return TextReadResult.Fail(
                "Couldn't find an editable field. Click inside the text box, then Fix again.",
                field);

        // Electron/Chromium contenteditable: ValuePattern often leaks page URL; TextPattern empty.
        // Mirror AHK DictationFixer — retries, ^+Home then ^a, reject tab/window titles.
        try { el.SetFocus(); } catch { /* ignore */ }
        Thread.Sleep(IsBrowserOrElectron(field) ? 120 : 40);
        if (TryClipboardReadHardened(field, out var clipText, out var clipMethod) &&
            !IsBogusFieldValue(clipText, field) &&
            !LooksLikeUiDump(clipText))
            return TextReadResult.Ok(clipText, field, clipMethod);

        var hint = IsBrowserOrElectron(field)
            ? "Browser/Electron: could not copy field text. Click in the text box, then Fix again."
            : "Element has no usable Value/Text pattern and clipboard read failed.";
        return TextReadResult.Fail(hint, field);
    }

    /// <summary>
    /// Detects accidental capture of Explorer/sidebar UI (OBJ glyphs + folder names).
    /// </summary>
    public static bool LooksLikeUiDump(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        // Object replacement char often marks icon/tree nodes from accessibility dumps.
        var objCount = 0;
        foreach (var ch in text)
        {
            if (ch == '\uFFFC' || ch == '\uFFFD')
                objCount++;
        }
        if (objCount >= 2)
            return true;

        var lower = text.ToLowerInvariant();
        var folderHits = 0;
        foreach (var token in new[]
                 {
                     ".cursor", ".vscode", ".vs", "node_modules", "bin\\debug", "obj\\debug",
                     "explorer", "solution explorer", "file explorer", "open editors",
                     "outline", "timeline", "source control"
                 })
        {
            if (lower.Contains(token, StringComparison.Ordinal))
                folderHits++;
        }
        if (folderHits >= 2)
            return true;

        // Many quoted empty strings — classic Cursor/VS tree dump from Ctrl+A.
        var emptyQuoteHits = 0;
        for (var i = 0; i + 1 < text.Length; i++)
        {
            if (text[i] == '"' && text[i + 1] == '"')
                emptyQuoteHits++;
        }
        if (emptyQuoteHits >= 8)
            return true;

        // Dense path-like dump with almost no sentence punctuation/spaces relative to length.
        if (text.Length > 80)
        {
            var spaces = text.Count(c => c == ' ' || c == '\n' || c == '\t');
            if (spaces < text.Length / 40 && folderHits >= 1)
                return true;
        }

        // Title-bar / tab chrome mistaken for body text.
        if (text.Length < 120 &&
            (lower.EndsWith(" - cursor") || lower.EndsWith(" - visual studio code") ||
             lower.EndsWith(" - brave") || lower.EndsWith(" - google chrome") ||
             lower.StartsWith("new tab")))
            return true;

        return false;
    }

    private static bool IsNonEditableChrome(FocusedFieldInfo field)
    {
        var type = field.ControlType ?? "";
        return type.Contains("Tree", StringComparison.OrdinalIgnoreCase) ||
               type.Contains("List", StringComparison.OrdinalIgnoreCase) ||
               type.Contains("DataGrid", StringComparison.OrdinalIgnoreCase) ||
               type.Contains("ToolBar", StringComparison.OrdinalIgnoreCase) ||
               type.Contains("Menu", StringComparison.OrdinalIgnoreCase) ||
               type.Contains("Tab", StringComparison.OrdinalIgnoreCase) ||
               type.Contains("Button", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTopLevelShell(FocusedFieldInfo field)
    {
        var type = field.ControlType ?? "";
        if (type.Contains("Window", StringComparison.OrdinalIgnoreCase))
            return true;

        // Electron main HWND often reports as Pane with Chrome_WidgetWin class.
        var cls = field.ClassName ?? "";
        if ((cls.Contains("Chrome_WidgetWin", StringComparison.OrdinalIgnoreCase) ||
             cls.Contains("Electron", StringComparison.OrdinalIgnoreCase)) &&
            !type.Contains("Edit", StringComparison.OrdinalIgnoreCase) &&
            !type.Contains("Document", StringComparison.OrdinalIgnoreCase))
        {
            // No own text patterns → treating as shell is safer than Ctrl+A.
            return !field.SupportsValuePattern && !field.SupportsTextPattern;
        }

        return false;
    }

    private static bool TrySetValue(AutomationElement el, string text)
    {
        if (!TryGetValuePattern(el, out var pattern) || pattern is null)
            return false;
        if (pattern.Current.IsReadOnly)
            return false;
        try
        {
            pattern.SetValue(text);
            // Must be exact — Contains(short) would false-pass when shortening a field.
            var after = pattern.Current.Value ?? string.Empty;
            return string.Equals(after, text, StringComparison.Ordinal);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static bool TrySetViaTextPattern(AutomationElement el, string text)
    {
        if (!TryGetTextPattern(el, out var pattern) || pattern is null)
            return false;
        try
        {
            pattern.DocumentRange.Select();
            if (TryGetValuePattern(el, out var value) && value is not null && !value.Current.IsReadOnly)
            {
                value.SetValue(text);
                var after = value.Current.Value ?? string.Empty;
                return after == text;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryClipboardReadHardened(
        FocusedFieldInfo field,
        out string text,
        out string method)
    {
        text = "";
        method = "ClipboardCtrlAC";
        var browserLike = IsBrowserOrElectron(field);
        var retries = browserLike ? 5 : 3;
        string? previous = null;

        try
        {
            if (System.Windows.Clipboard.ContainsText())
                previous = System.Windows.Clipboard.GetText();
        }
        catch
        {
            // ignore
        }

        try
        {
            for (var attempt = 0; attempt < retries; attempt++)
            {
                // Pass 1: caret → start (best after Voice Typing leaves caret at end).
                if (TryClipboardSelectCopy(
                        selectHome: true,
                        settleMs: 50 + attempt * 40,
                        out var homeText) &&
                    !IsBogusFieldValue(homeText, field) &&
                    !IsProbablyWindowTitle(homeText, field))
                {
                    text = homeText;
                    method = "ClipboardCtrlShiftHomeC";
                }

                // Pass 2: Ctrl+A — prefer when longer but not a giant document dump.
                if (TryClipboardSelectCopy(
                        selectHome: false,
                        settleMs: 70 + attempt * 100,
                        out var allText) &&
                    !IsBogusFieldValue(allText, field) &&
                    !IsProbablyWindowTitle(allText, field))
                {
                    if (string.IsNullOrEmpty(text))
                    {
                        text = allText;
                        method = "ClipboardCtrlAC";
                    }
                    else if (allText.Length > text.Length &&
                             allText.Length <= Math.Max(10000, text.Length * 3))
                    {
                        text = allText;
                        method = "ClipboardCtrlAC";
                    }
                    else if (allText.Length <= 10000 && allText.Contains(text, StringComparison.Ordinal))
                    {
                        text = allText;
                        method = "ClipboardCtrlAC";
                    }
                }

                if (!string.IsNullOrWhiteSpace(text))
                    return true;

                Thread.Sleep(browserLike ? 150 : 80);
            }
        }
        finally
        {
            try
            {
                if (previous is not null)
                    System.Windows.Clipboard.SetText(previous);
            }
            catch
            {
                // ignore
            }
        }

        return false;
    }

    private static bool TryClipboardSelectCopy(bool selectHome, int settleMs, out string text)
    {
        text = "";
        try
        {
            System.Windows.Clipboard.Clear();
        }
        catch
        {
            // ignore
        }

        try
        {
            if (selectHome)
                SendCtrlShiftHome();
            else
                SendCtrlChord('A');

            Thread.Sleep(Math.Max(30, settleMs));
            SendCtrlChord('C');
            Thread.Sleep(40);

            // ClipWait-ish polling
            for (var i = 0; i < 12; i++)
            {
                if (System.Windows.Clipboard.ContainsText())
                {
                    text = System.Windows.Clipboard.GetText() ?? "";
                    return text.Length > 0;
                }
                Thread.Sleep(50);
            }
        }
        catch
        {
            // ignore
        }

        return false;
    }

    private static void SendCtrlShiftHome()
    {
        const byte vkControl = 0x11;
        const byte vkShift = 0x10;
        const byte vkHome = 0x24;
        keybd_event(vkControl, 0, 0, UIntPtr.Zero);
        keybd_event(vkShift, 0, 0, UIntPtr.Zero);
        keybd_event(vkHome, 0, 0, UIntPtr.Zero);
        keybd_event(vkHome, 0, 2, UIntPtr.Zero);
        keybd_event(vkShift, 0, 2, UIntPtr.Zero);
        keybd_event(vkControl, 0, 2, UIntPtr.Zero);
    }

    private static bool TryClipboardReplace(string newText, bool lowLatency = false)
    {
        string? previous = null;
        try
        {
            if (System.Windows.Clipboard.ContainsText())
                previous = System.Windows.Clipboard.GetText();
            System.Windows.Clipboard.SetText(newText);
            SendCtrlChord('A');
            Thread.Sleep(lowLatency ? 8 : 25);
            SendCtrlChord('V');
            Thread.Sleep(lowLatency ? 12 : 40);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            try
            {
                if (previous is not null)
                    System.Windows.Clipboard.SetText(previous);
                else
                    System.Windows.Clipboard.Clear();
            }
            catch
            {
                // ignore
            }
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

    private static AutomationElement? FindBestTextDescendant(AutomationElement root)
    {
        try
        {
            var condition = new OrCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));
            var found = root.FindAll(TreeScope.Descendants, condition);
            AutomationElement? best = null;
            var bestScore = -1;
            foreach (AutomationElement el in found)
            {
                try
                {
                    if (IsChromeNoise(el))
                        continue;
                    var score = 0;
                    if (el.TryGetCurrentPattern(ValuePattern.Pattern, out _))
                        score += 2;
                    if (el.TryGetCurrentPattern(TextPattern.Pattern, out _))
                        score += 2;
                    if (el.Current.IsKeyboardFocusable)
                        score += 1;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = el;
                    }
                }
                catch
                {
                    // ignore
                }
            }
            return best;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsChromeNoise(AutomationElement el)
    {
        try
        {
            var id = el.Current.AutomationId ?? "";
            var name = el.Current.Name ?? "";
            if (id.Contains("omnibox", StringComparison.OrdinalIgnoreCase))
                return true;
            if (name.Contains("Address and search bar", StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetValuePattern(AutomationElement el, out ValuePattern? pattern)
    {
        pattern = null;
        try
        {
            if (el.TryGetCurrentPattern(ValuePattern.Pattern, out var obj) && obj is ValuePattern vp)
            {
                pattern = vp;
                return true;
            }
        }
        catch
        {
            // ignore
        }
        return false;
    }

    private static bool TryGetTextPattern(AutomationElement el, out TextPattern? pattern)
    {
        pattern = null;
        try
        {
            if (el.TryGetCurrentPattern(TextPattern.Pattern, out var obj) && obj is TextPattern tp)
            {
                pattern = tp;
                return true;
            }
        }
        catch
        {
            // ignore
        }
        return false;
    }

    /// <summary>
    /// Screen bounds of a character span in the field (for Grammarly-style underlines).
    /// Prefers locating <paramref name="needle"/> via FindText so offsets stay accurate
    /// even when ValuePattern vs TextPattern disagree (CRLF, etc.).
    /// </summary>
    public bool TryGetTextRangeScreenBounds(
        object automationElement,
        int start,
        int length,
        string? needle,
        string? workingText,
        out int x,
        out int y,
        out int width,
        out int height)
    {
        x = y = width = height = 0;
        if (automationElement is not AutomationElement el)
            return false;

        try
        {
            EnsureSta();
            if (!TryGetTextPattern(el, out var pattern) || pattern is null)
            {
                var descendant = FindBestTextDescendant(el);
                if (descendant is null || !TryGetTextPattern(descendant, out pattern) || pattern is null)
                    return false;
            }

            var doc = pattern.DocumentRange;
            var full = doc.GetText(-1) ?? "";
            if (full.Length == 0)
                return false;

            var mappedStart = start;
            var mappedLen = Math.Max(1, length);
            if (!string.IsNullOrEmpty(workingText))
                mappedStart = MapOffset(workingText, full, start);

            // Prefer exact needle match near the mapped offset.
            if (!string.IsNullOrEmpty(needle))
            {
                var idx = FindNear(full, needle, mappedStart);
                if (idx >= 0)
                {
                    mappedStart = idx;
                    mappedLen = needle.Length;
                }
            }

            if (mappedStart < 0 || mappedStart >= full.Length)
                return false;
            mappedLen = Math.Min(mappedLen, full.Length - mappedStart);
            if (mappedLen <= 0)
                return false;

            TextPatternRange? range = null;
            if (!TryBuildCharRange(doc, mappedStart, mappedLen, out range) || range is null)
            {
                // FindText fallback — more accurate when Character moves don't map 1:1.
                if (string.IsNullOrEmpty(needle))
                    return false;
                range = TryFindTextRange(doc, full, needle, mappedStart);
                if (range is null)
                    return false;
            }

            var rects = range.GetBoundingRectangles();
            if (rects is null || rects.Length == 0)
                return false;

            // Skip empty/degenerate rects some hosts return.
            System.Windows.Rect? union = null;
            foreach (var r in rects)
            {
                if (r.Width <= 0.5 || r.Height <= 0.5)
                    continue;
                union = union is null ? r : System.Windows.Rect.Union(union.Value, r);
            }
            if (union is null)
                return false;

            var u = union.Value;
            x = (int)Math.Round(u.X);
            y = (int)Math.Round(u.Y);
            width = Math.Max(1, (int)Math.Round(u.Width));
            height = Math.Max(1, (int)Math.Round(u.Height));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static TextPatternRange? TryFindTextRange(
        TextPatternRange doc,
        string full,
        string needle,
        int preferredStart)
    {
        try
        {
            var idx = FindNear(full, needle, preferredStart);
            if (idx < 0)
                return null;

            // Search from a range that starts at/near the match so FindText hits the right occurrence.
            var search = doc.Clone();
            search.MoveEndpointByRange(TextPatternRangeEndpoint.End, search, TextPatternRangeEndpoint.Start);
            if (idx > 0)
            {
                var moved = search.Move(TextUnit.Character, idx);
                if (moved != idx)
                {
                    // Can't seek — FindText from document start and accept first match only if unique.
                    var count = 0;
                    var pos = 0;
                    while ((pos = full.IndexOf(needle, pos, StringComparison.Ordinal)) >= 0)
                    {
                        count++;
                        pos += Math.Max(1, needle.Length);
                    }
                    if (count != 1)
                        return null;
                    return doc.FindText(needle, false, false);
                }
            }

            var found = search.FindText(needle, false, false);
            return found;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryBuildCharRange(
        TextPatternRange doc,
        int start,
        int length,
        out TextPatternRange? range)
    {
        range = null;
        try
        {
            range = doc.Clone();
            range.MoveEndpointByRange(TextPatternRangeEndpoint.End, range, TextPatternRangeEndpoint.Start);
            // Move() returns chars actually moved — verify we landed correctly.
            if (start > 0)
            {
                var moved = range.Move(TextUnit.Character, start);
                if (moved != start)
                {
                    // Fallback: Expand from document start via FindText of a slice.
                    range = null;
                    return false;
                }
            }

            var end = range.Clone();
            var movedLen = end.Move(TextUnit.Character, length);
            if (movedLen <= 0)
            {
                range = null;
                return false;
            }
            range.MoveEndpointByRange(TextPatternRangeEndpoint.End, end, TextPatternRangeEndpoint.Start);
            return true;
        }
        catch
        {
            range = null;
            return false;
        }
    }

    private static int FindNear(string haystack, string needle, int preferredStart)
    {
        if (needle.Length == 0 || haystack.Length == 0)
            return -1;
        preferredStart = Math.Clamp(preferredStart, 0, haystack.Length);
        var idx = haystack.IndexOf(needle, preferredStart, StringComparison.Ordinal);
        if (idx >= 0)
            return idx;
        var back = Math.Max(0, preferredStart - 80);
        idx = haystack.IndexOf(needle, back, StringComparison.Ordinal);
        if (idx >= 0)
            return idx;
        return haystack.IndexOf(needle, StringComparison.Ordinal);
    }

    /// <summary>Map a character offset from working/Value text into TextPattern document text.</summary>
    private static int MapOffset(string fromText, string toText, int offset)
    {
        if (offset <= 0)
            return 0;
        if (string.Equals(fromText, toText, StringComparison.Ordinal))
            return Math.Clamp(offset, 0, toText.Length);

        // Common case: CRLF vs LF.
        var fromNorm = fromText.Replace("\r\n", "\n").Replace('\r', '\n');
        var toNorm = toText.Replace("\r\n", "\n").Replace('\r', '\n');
        if (string.Equals(fromNorm, toNorm, StringComparison.Ordinal))
        {
            var normOffset = 0;
            for (var i = 0; i < offset && i < fromText.Length; i++)
            {
                if (fromText[i] == '\r' && i + 1 < fromText.Length && fromText[i + 1] == '\n')
                    continue;
                normOffset++;
            }
            // Map norm offset back into toText (expand LF → CRLF if needed).
            var ti = 0;
            var count = 0;
            while (ti < toText.Length && count < normOffset)
            {
                if (toText[ti] == '\r' && ti + 1 < toText.Length && toText[ti + 1] == '\n')
                {
                    ti += 2;
                    count++;
                }
                else
                {
                    ti++;
                    count++;
                }
            }
            return ti;
        }

        return Math.Clamp(offset, 0, toText.Length);
    }

    private static FocusedFieldInfo Describe(AutomationElement el)
    {
        string processName = "?";
        string windowTitle = "";
        string controlType = "?";
        string className = "";
        nint hwnd = 0;
        string? automationId = null;
        string? name = null;
        bool valueOk = false;
        bool textOk = false;

        try
        {
            var pid = el.Current.ProcessId;
            try
            {
                processName = Process.GetProcessById(pid).ProcessName;
            }
            catch
            {
                processName = $"pid:{pid}";
            }

            controlType = el.Current.ControlType.ProgrammaticName;
            className = el.Current.ClassName ?? "";
            automationId = el.Current.AutomationId;
            name = el.Current.Name;
            hwnd = new nint(el.Current.NativeWindowHandle);
            valueOk = el.TryGetCurrentPattern(ValuePattern.Pattern, out _);
            textOk = el.TryGetCurrentPattern(TextPattern.Pattern, out _);

            var walker = TreeWalker.ControlViewWalker;
            var node = walker.GetParent(el);
            for (var i = 0; i < 16 && node is not null; i++)
            {
                try
                {
                    if (hwnd == 0 && node.Current.NativeWindowHandle != 0)
                        hwnd = new nint(node.Current.NativeWindowHandle);

                    var n = node.Current.Name;
                    if (!string.IsNullOrWhiteSpace(n))
                        windowTitle = n;
                }
                catch
                {
                    // ignore
                }
                node = walker.GetParent(node);
            }
        }
        catch
        {
            // ignore
        }

        return new FocusedFieldInfo(
            processName, windowTitle, controlType, className, hwnd,
            valueOk, textOk, automationId, name);
    }

    private static bool IsBogusFieldValue(string text, FocusedFieldInfo field)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        if (text.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("vscode-file://", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return true;
        return IsProbablyWindowTitle(text, field);
    }

    private static bool IsProbablyWindowTitle(string text, FocusedFieldInfo field)
    {
        text = text.Trim();
        if (text.Length == 0 || text.Contains('\n') || text.Contains('\r'))
            return false;

        var title = (field.WindowTitle ?? "").Trim();
        if (title.Length > 0 &&
            string.Equals(text, title, StringComparison.OrdinalIgnoreCase))
            return true;

        // Common browser/Electron tab titles when capture grabs the wrong surface.
        if (text.Contains(" - Brave", StringComparison.OrdinalIgnoreCase) ||
            text.Contains(" - Google Chrome", StringComparison.OrdinalIgnoreCase) ||
            text.Contains(" - Microsoft Edge", StringComparison.OrdinalIgnoreCase) ||
            text.Contains(" - Cursor", StringComparison.OrdinalIgnoreCase) ||
            text.Contains(" - Visual Studio Code", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("New Tab", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("New Tab - ", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static bool IsBrowserOrElectron(FocusedFieldInfo field)
    {
        var exe = (field.ProcessName ?? "").Trim();
        if (exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            exe = exe[..^4];

        if (exe.Equals("chrome", StringComparison.OrdinalIgnoreCase) ||
            exe.Equals("brave", StringComparison.OrdinalIgnoreCase) ||
            exe.Equals("msedge", StringComparison.OrdinalIgnoreCase) ||
            exe.Equals("msedgewebview2", StringComparison.OrdinalIgnoreCase) ||
            exe.Equals("firefox", StringComparison.OrdinalIgnoreCase) ||
            exe.Equals("opera", StringComparison.OrdinalIgnoreCase) ||
            exe.Equals("vivaldi", StringComparison.OrdinalIgnoreCase) ||
            exe.Equals("chromium", StringComparison.OrdinalIgnoreCase) ||
            exe.Equals("cursor", StringComparison.OrdinalIgnoreCase) ||
            exe.Equals("code", StringComparison.OrdinalIgnoreCase) ||
            exe.Equals("Code - Insiders", StringComparison.OrdinalIgnoreCase) ||
            exe.Equals("discord", StringComparison.OrdinalIgnoreCase) ||
            exe.Equals("slack", StringComparison.OrdinalIgnoreCase) ||
            exe.Equals("Teams", StringComparison.OrdinalIgnoreCase) ||
            exe.Equals("OUTLOOK", StringComparison.OrdinalIgnoreCase) ||
            exe.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase) ||
            exe.Equals("Notion", StringComparison.OrdinalIgnoreCase) ||
            exe.Equals("Obsidian", StringComparison.OrdinalIgnoreCase))
            return true;

        var cls = field.ClassName ?? "";
        return cls.Contains("Chrome_WidgetWin", StringComparison.OrdinalIgnoreCase) ||
               cls.Contains("Electron", StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureSta()
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("UIA calls must run on an STA thread.");
    }
}
