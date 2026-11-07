using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using AutoCorrect.Platform.Windows;

namespace UiaSpike;

/// <summary>
/// Interactive gate for Phase 1: focused-field UIA read/write.
/// Target apps: Notepad, Brave, Cursor (Electron).
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // WPF Clipboard / STA dispatcher plumbing without showing a window.
        var _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        var bridge = new UiaTextBridge();

        if (args.Any(a => string.Equals(a, "--auto-textbox", StringComparison.OrdinalIgnoreCase)))
            return RunAutoTextBoxGate(bridge);

        if (args.Any(a => string.Equals(a, "--auto-notepad", StringComparison.OrdinalIgnoreCase)))
            return RunAutoNotepadGate(bridge);

        if (args.Any(a => string.Equals(a, "--target-notepad", StringComparison.OrdinalIgnoreCase)))
            return RunAgainstRunningNotepad(bridge, stripSpikeMarkers: args.Any(a =>
                string.Equals(a, "--strip-markers", StringComparison.OrdinalIgnoreCase)));

        var targetIdx = Array.FindIndex(args, a =>
            string.Equals(a, "--target-process", StringComparison.OrdinalIgnoreCase));
        if (targetIdx >= 0 && targetIdx + 1 < args.Length)
            return RunAgainstProcessEdit(bridge, args[targetIdx + 1],
                nameContains: GetArgValue(args, "--name-contains"));

        var probeIdx = Array.FindIndex(args, a =>
            string.Equals(a, "--probe", StringComparison.OrdinalIgnoreCase));
        if (probeIdx >= 0 && probeIdx + 1 < args.Length)
            return ProbeProcess(args[probeIdx + 1]);

        if (args.Any(a => string.Equals(a, "--once-read", StringComparison.OrdinalIgnoreCase)))
            return RunOnce(bridge, write: false, countdownSec: ParseCountdown(args));

        if (args.Any(a => string.Equals(a, "--once-write", StringComparison.OrdinalIgnoreCase)))
            return RunOnce(bridge, write: true, countdownSec: ParseCountdown(args));

        var marker = args.Length > 0
            ? string.Join(' ', args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)))
            : $" [UIA-SPIKE {DateTime.Now:HH:mm:ss}]";
        if (string.IsNullOrWhiteSpace(marker))
            marker = $" [UIA-SPIKE {DateTime.Now:HH:mm:ss}]";

        Console.WriteLine("=== AutoCorrect Native — UIA Spike ===");
        Console.WriteLine("Focus a text field in Notepad / Brave / Cursor, then choose an action.");
        Console.WriteLine($"Write marker appended on 'w': {marker}");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  r  — read focused text via UIA");
        Console.WriteLine("  w  — write: append marker (UIA, clipboard fallback OK)");
        Console.WriteLine("  s  — write: set entire field to 'SPIKE_OK' (destructive)");
        Console.WriteLine("  i  — inspect focused element metadata only");
        Console.WriteLine("  q  — quit");
        Console.WriteLine();
        Console.WriteLine("Also:");
        Console.WriteLine("  --auto-textbox     self-hosted WPF TextBox gate (CI-safe)");
        Console.WriteLine("  --auto-notepad     launches Notepad (needs interactive desktop)");
        Console.WriteLine("  --target-notepad   attach to an already-open Notepad window");
        Console.WriteLine("  --once-read|--once-write [--delay N]");
        Console.WriteLine();

        while (true)
        {
            Console.Write("> ");
            var raw = Console.ReadLine();
            if (raw is null)
                break; // stdin closed / non-interactive
            var line = raw.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(line))
                continue;
            if (line is "q" or "quit" or "exit")
                break;

            Console.WriteLine("… switch focus to target field within 3 seconds …");
            for (var i = 3; i >= 1; i--)
            {
                Console.Write($"\r  {i} ");
                Thread.Sleep(1000);
            }
            Console.WriteLine("\r  go");

            switch (line)
            {
                case "i":
                    Inspect(bridge);
                    break;
                case "r":
                    Read(bridge);
                    break;
                case "w":
                    WriteAppend(bridge, marker);
                    break;
                case "s":
                    WriteSet(bridge, "SPIKE_OK");
                    break;
                default:
                    Console.WriteLine("Unknown command.");
                    break;
            }

            Console.WriteLine();
        }

        return 0;
    }

    /// <summary>
    /// Attach to a visible Notepad process, focus its edit control, read + append marker.
    /// </summary>
    private static int RunAgainstRunningNotepad(UiaTextBridge bridge, bool stripSpikeMarkers = false)
    {
        Console.WriteLine(stripSpikeMarkers
            ? "=== UIA target-notepad strip-markers ==="
            : "=== UIA target-notepad gate ===");
        var procs = Process.GetProcessesByName("Notepad")
            .Where(p => p.MainWindowHandle != 0)
            .ToArray();
        if (procs.Length == 0)
        {
            Console.WriteLine("FAIL: no Notepad window with a HWND. Open Notepad first.");
            return 2;
        }

        var proc = procs[0];
        Console.WriteLine($"Using Notepad pid={proc.Id} title=[{proc.MainWindowTitle}] hwnd=0x{proc.MainWindowHandle:X}");

        try
        {
            var root = AutomationElement.FromHandle(proc.MainWindowHandle);
            var edit = root.FindFirst(
                TreeScope.Descendants,
                new OrCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)));
            if (edit is null)
            {
                Console.WriteLine("FAIL: No Edit/Document in Notepad.");
                return 3;
            }

            edit.SetFocus();
            Thread.Sleep(200);

            var read = bridge.TryReadFocused();
            Console.WriteLine($"Read: success={read.Success} method={read.Method} err={read.Error}");
            PrintField(read.Field);
            var preview = read.Text ?? "";
            if (preview.Length > 240)
                preview = preview[..240] + "…";
            Console.WriteLine($"Text preview: [{preview}]");
            if (!read.Success)
                return 4;

            if (stripSpikeMarkers)
            {
                var cleaned = System.Text.RegularExpressions.Regex.Replace(
                    read.Text ?? "",
                    @" \[NOTEPAD-GATE [^\]]+\]| \[UIA-SPIKE [^\]]+\]| \[GATE-OK\]",
                    "");
                if (cleaned == (read.Text ?? ""))
                {
                    Console.WriteLine("No spike markers found.");
                    return 0;
                }

                var restore = bridge.TryWriteFocused(cleaned);
                Console.WriteLine($"Strip write: success={restore.Success} method={restore.Method}");
                return restore.Success ? 0 : 7;
            }

            var marker = $" [NOTEPAD-GATE {DateTime.Now:HH:mm:ss}]";
            var write = bridge.TryWriteFocused((read.Text ?? "") + marker);
            Console.WriteLine($"Write: success={write.Success} method={write.Method} fallback={write.UsedClipboardFallback} err={write.Error}");
            if (!write.Success)
                return 5;

            Thread.Sleep(150);
            var verify = bridge.TryReadFocused();
            var ok = verify.Success && (verify.Text ?? "").Contains(marker, StringComparison.Ordinal);
            Console.WriteLine($"Verify: containsMarker={ok} method={verify.Method}");
            Console.WriteLine(ok ? "PASS: Notepad UIA read/write gate." : "FAIL: verify marker missing.");
            return ok ? 0 : 6;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL exception: {ex}");
            return 9;
        }
    }

    private static string? GetArgValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    /// <summary>
    /// Focus first usable Edit/Document under a process and append a marker (Brave/Cursor gate).
    /// </summary>
    private static int RunAgainstProcessEdit(UiaTextBridge bridge, string processName, string? nameContains)
    {
        Console.WriteLine($"=== UIA target-process={processName} ===");
        var procs = Process.GetProcessesByName(processName)
            .Where(p => p.MainWindowHandle != 0)
            .ToArray();
        if (procs.Length == 0)
        {
            Console.WriteLine("FAIL: no windowed process.");
            return 2;
        }

        foreach (var proc in procs)
        {
            Console.WriteLine($"Trying pid={proc.Id} title=[{proc.MainWindowTitle}]");
            try
            {
                var root = AutomationElement.FromHandle(proc.MainWindowHandle);
                var edits = root.FindAll(
                    TreeScope.Descendants,
                    new OrCondition(
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document)));

                foreach (AutomationElement edit in edits)
                {
                    try
                    {
                        var name = edit.Current.Name ?? "";
                        var cls = edit.Current.ClassName ?? "";
                        var id = edit.Current.AutomationId ?? "";
                        // Skip browser chrome / RootWebArea URL traps
                        if (cls.Contains("Omnibox", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (name.Contains("Address and search", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (id.Equals("RootWebArea", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (!string.IsNullOrEmpty(nameContains) &&
                            name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) < 0 &&
                            cls.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;

                        if (!edit.TryGetCurrentPattern(ValuePattern.Pattern, out _) &&
                            !edit.TryGetCurrentPattern(TextPattern.Pattern, out _))
                            continue;

                        Console.WriteLine($"Focusing type={edit.Current.ControlType.ProgrammaticName} class={cls} name=[{Truncate(name, 60)}] id={edit.Current.AutomationId}");
                        // Operate on the discovered element directly — Electron SetFocus often jumps to RootWebArea.
                        var read = bridge.TryReadElement(edit);
                        Console.WriteLine($"Read: success={read.Success} method={read.Method} err={read.Error}");
                        PrintField(read.Field);
                        var preview = read.Text ?? "";
                        if (preview.Length > 200)
                            preview = preview[..200] + "…";
                        Console.WriteLine($"Text: [{preview}]");
                        if (!read.Success)
                            continue;

                        if (!string.IsNullOrEmpty(read.Field?.WindowTitle) &&
                            string.Equals(read.Text?.Trim(), read.Field.WindowTitle.Trim(), StringComparison.Ordinal))
                        {
                            Console.WriteLine("Skipping title-like read.");
                            continue;
                        }

                        var marker = $" [PROC-GATE {DateTime.Now:HH:mm:ss}]";
                        var write = bridge.TryWriteElement(edit, (read.Text ?? "") + marker);
                        Console.WriteLine($"Write: success={write.Success} method={write.Method} fallback={write.UsedClipboardFallback} err={write.Error}");
                        if (!write.Success)
                            continue;

                        Thread.Sleep(200);
                        var verify = bridge.TryReadElement(edit);
                        var ok = verify.Success && (verify.Text ?? "").Contains(marker, StringComparison.Ordinal);
                        Console.WriteLine($"Verify: containsMarker={ok} method={verify.Method}");

                        if (ok)
                        {
                            var cleaned = System.Text.RegularExpressions.Regex.Replace(
                                verify.Text ?? "",
                                @"\s*\[(?:PROC-GATE|NOTEPAD-GATE|UIA-SPIKE)[^\]]*\]|\s*\[GATE-OK\]",
                                "");
                            edit.SetFocus();
                            Thread.Sleep(100);
                            var strip = bridge.TryWriteElement(edit, cleaned, new AutoCorrect.Platform.Abstractions.TextWriteOptions
                            {
                                AllowClipboardFallback = true
                            });
                            Console.WriteLine($"Strip: success={strip.Success} method={strip.Method} fallback={strip.UsedClipboardFallback} err={strip.Error}");
                            var afterStrip = bridge.TryReadElement(edit);
                            Console.WriteLine($"After strip text: [{Truncate(afterStrip.Text, 120)}]");
                            Console.WriteLine("PASS: process edit UIA gate.");
                            return 0;
                        }
                    }
                    catch
                    {
                        // try next
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  err: {ex.Message}");
            }
        }

        Console.WriteLine("FAIL: no usable editable field found/worked.");
        return 6;
    }

    /// <summary>List Edit/Document UIA nodes under a process main window (no mutation).</summary>
    private static int ProbeProcess(string processName)
    {
        Console.WriteLine($"=== UIA probe process={processName} ===");
        var procs = Process.GetProcessesByName(processName)
            .Where(p => p.MainWindowHandle != 0)
            .ToArray();
        if (procs.Length == 0)
        {
            Console.WriteLine("No windowed process found.");
            return 2;
        }

        foreach (var proc in procs.Take(2))
        {
            Console.WriteLine($"pid={proc.Id} title=[{proc.MainWindowTitle}] hwnd=0x{proc.MainWindowHandle:X}");
            try
            {
                var root = AutomationElement.FromHandle(proc.MainWindowHandle);
                var edits = root.FindAll(
                    TreeScope.Descendants,
                    new OrCondition(
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document)));
                Console.WriteLine($"  Edit/Document count={edits.Count}");
                var shown = 0;
                foreach (AutomationElement e in edits)
                {
                    if (shown >= 8)
                        break;
                    try
                    {
                        var vp = e.TryGetCurrentPattern(ValuePattern.Pattern, out _);
                        var tp = e.TryGetCurrentPattern(TextPattern.Pattern, out _);
                        Console.WriteLine(
                            $"  type={e.Current.ControlType.ProgrammaticName} class={e.Current.ClassName} " +
                            $"ValuePat={vp} TextPat={tp} name=[{Truncate(e.Current.Name, 50)}]");
                        shown++;
                    }
                    catch
                    {
                        // stale
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  probe err: {ex.Message}");
            }
        }

        return 0;
    }

    private static int ParseCountdown(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--delay", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[i + 1], out var sec) && sec > 0)
                return sec;
        }
        return 5;
    }

    /// <summary>
    /// Non-interactive: countdown, then read (and optionally append marker) focused field.
    /// Use with desktop automation: focus Notepad/Brave/Cursor during the countdown.
    /// </summary>
    private static int RunOnce(UiaTextBridge bridge, bool write, int countdownSec)
    {
        var marker = $" [UIA-SPIKE {DateTime.Now:HH:mm:ss}]";
        Console.WriteLine($"=== UIA once {(write ? "write" : "read")} (delay {countdownSec}s) ===");
        Console.WriteLine("Focus target text field now…");
        for (var i = countdownSec; i >= 1; i--)
        {
            Console.WriteLine($"  {i}");
            Thread.Sleep(1000);
        }

        if (!write)
        {
            Read(bridge);
            return 0;
        }

        WriteAppend(bridge, marker);
        return 0;
    }

    /// <summary>
    /// CI-safe gate: own WPF TextBox proves ValuePattern read/write without external apps.
    /// </summary>
    private static int RunAutoTextBoxGate(UiaTextBridge bridge)
    {
        Console.WriteLine("=== UIA auto-textbox gate ===");
        Window? window = null;
        try
        {
            var box = new System.Windows.Controls.TextBox
            {
                Text = "seed text",
                AcceptsReturn = true,
                Width = 400,
                Height = 120,
                Margin = new Thickness(12)
            };
            window = new Window
            {
                Title = "UiaSpike Host",
                Width = 440,
                Height = 200,
                Content = box,
                Topmost = true,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };
            window.Show();
            box.Focus();
            // Pump dispatcher so UIA sees the focused element
            for (var i = 0; i < 10; i++)
            {
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    new Action(() => { }));
                Thread.Sleep(50);
            }

            var read1 = bridge.TryReadFocused();
            Console.WriteLine($"Read1: success={read1.Success} method={read1.Method} text=[{read1.Text}] err={read1.Error}");
            PrintField(read1.Field);
            if (!read1.Success || read1.Text != "seed text")
                return 11;

            const string next = "seed text [GATE-OK]";
            var write = bridge.TryWriteFocused(next);
            Console.WriteLine($"Write: success={write.Success} method={write.Method} fallback={write.UsedClipboardFallback} err={write.Error}");
            if (!write.Success)
                return 12;

            // Pump again then verify via both UIA and WPF property
            for (var i = 0; i < 10; i++)
            {
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    new Action(() => { }));
                Thread.Sleep(50);
            }

            var read2 = bridge.TryReadFocused();
            Console.WriteLine($"Read2: success={read2.Success} method={read2.Method} text=[{read2.Text}]");
            Console.WriteLine($"WPF TextBox.Text=[{box.Text}]");
            var ok = read2.Success && read2.Text == next && box.Text == next;
            if (!ok)
                return 13;

            Console.WriteLine("PASS: in-process TextBox UIA read/write gate.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL exception: {ex}");
            return 19;
        }
        finally
        {
            try { window?.Close(); } catch { /* ignore */ }
        }
    }

    /// <summary>
    /// Automated gate: launch Notepad, seed text, focus edit, read + write via UIA.
    /// Requires an interactive desktop where Notepad can create a visible HWND.
    /// </summary>
    private static int RunAutoNotepadGate(UiaTextBridge bridge)
    {
        Console.WriteLine("=== UIA auto-notepad gate ===");
        Process? proc = null;
        try
        {
            var before = Process.GetProcessesByName("Notepad").Select(p => p.Id).ToHashSet();
            proc = Process.Start(new ProcessStartInfo
            {
                FileName = "notepad.exe",
                UseShellExecute = true
            });
            if (proc is null)
            {
                Console.WriteLine("FAIL: could not start notepad.exe");
                return 2;
            }

            try { proc.WaitForInputIdle(5000); } catch { /* packaged Notepad may throw */ }

            AutomationElement? root = null;
            AutomationElement? edit = null;
            for (var attempt = 0; attempt < 20 && edit is null; attempt++)
            {
                Thread.Sleep(250);
                root = FindNotepadRoot(proc.Id, before);
                if (root is null)
                    continue;

                edit = root.FindFirst(
                    TreeScope.Descendants,
                    new OrCondition(
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document),
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)));
            }

            if (root is null)
            {
                Console.WriteLine("FAIL: Notepad UI Automation tree not found.");
                return 3;
            }

            if (edit is null)
            {
                Console.WriteLine("FAIL: No Edit/Document control in Notepad.");
                return 4;
            }

            Console.WriteLine($"Found Notepad root name='{root.Current.Name}' pid={root.Current.ProcessId}");
            edit.SetFocus();
            Thread.Sleep(200);

            const string seed = "hello from uia spike seed";
            var writeSeed = bridge.TryWriteFocused(seed);
            Console.WriteLine($"Seed write: success={writeSeed.Success} method={writeSeed.Method} fallback={writeSeed.UsedClipboardFallback} err={writeSeed.Error}");
            if (!writeSeed.Success)
                return 5;

            Thread.Sleep(150);
            var read = bridge.TryReadFocused();
            Console.WriteLine($"Read: success={read.Success} method={read.Method} err={read.Error}");
            Console.WriteLine($"Text: {read.Text}");
            if (!read.Success || !(read.Text ?? "").Contains("hello from uia", StringComparison.Ordinal))
                return 6;

            const string marker = " [GATE-OK]";
            var write2 = bridge.TryWriteFocused((read.Text ?? "") + marker);
            Console.WriteLine($"Append write: success={write2.Success} method={write2.Method} fallback={write2.UsedClipboardFallback}");
            if (!write2.Success)
                return 7;

            Thread.Sleep(150);
            var verify = bridge.TryReadFocused();
            var ok = verify.Success && (verify.Text ?? "").Contains(marker, StringComparison.Ordinal);
            Console.WriteLine($"Verify: success={verify.Success} containsMarker={ok} text={verify.Text}");
            if (!ok)
                return 8;

            Console.WriteLine("PASS: Notepad UIA read/write gate.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL exception: {ex}");
            return 9;
        }
        finally
        {
            try
            {
                if (proc is { HasExited: false })
                {
                    proc.CloseMainWindow();
                    if (!proc.WaitForExit(1500))
                        proc.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // ignore cleanup
            }
        }
    }

    private static AutomationElement? FindNotepadRoot(int startedPid, HashSet<int> pidsBefore)
    {
        // 1) Exact process id from Process.Start
        var byPid = AutomationElement.RootElement.FindFirst(
            TreeScope.Children,
            new PropertyCondition(AutomationElement.ProcessIdProperty, startedPid));
        if (byPid is not null)
            return byPid;

        // 2) Any new Notepad process (Win11 Store Notepad often re-hosts)
        foreach (var p in Process.GetProcessesByName("Notepad"))
        {
            if (pidsBefore.Contains(p.Id))
                continue;
            var el = AutomationElement.RootElement.FindFirst(
                TreeScope.Children,
                new PropertyCondition(AutomationElement.ProcessIdProperty, p.Id));
            if (el is not null)
                return el;
        }

        // 3) Window title contains Notepad (Untitled - Notepad / *Untitled - Notepad)
        var all = AutomationElement.RootElement.FindAll(TreeScope.Children, System.Windows.Automation.Condition.TrueCondition);
        foreach (AutomationElement el in all)
        {
            try
            {
                var name = el.Current.Name ?? "";
                if (name.Contains("Notepad", StringComparison.OrdinalIgnoreCase))
                    return el;
            }
            catch
            {
                // stale element
            }
        }

        return null;
    }

    private static void Inspect(UiaTextBridge bridge)
    {
        var read = bridge.TryReadFocused();
        PrintField(read.Field);
        Console.WriteLine($"Read success: {read.Success}");
        Console.WriteLine($"Method: {read.Method ?? "-"}");
        if (!read.Success)
            Console.WriteLine($"Error: {read.Error}");
        else
            Console.WriteLine($"Text length: {read.Text?.Length ?? 0}");
    }

    private static void Read(UiaTextBridge bridge)
    {
        var read = bridge.TryReadFocused();
        PrintField(read.Field);
        if (!read.Success)
        {
            Console.WriteLine($"FAIL: {read.Error}");
            return;
        }

        Console.WriteLine($"OK via {read.Method}");
        var text = read.Text ?? "";
        var preview = text.Length <= 400 ? text : text[..400] + "…";
        Console.WriteLine("--- text ---");
        Console.WriteLine(preview);
        Console.WriteLine("------------");
        Console.WriteLine($"chars={text.Length}");
    }

    private static void WriteAppend(UiaTextBridge bridge, string marker)
    {
        var read = bridge.TryReadFocused();
        PrintField(read.Field);
        var baseText = read.Success ? (read.Text ?? "") : "";
        if (!read.Success)
            Console.WriteLine($"Note: pre-read failed ({read.Error}); write will replace via clipboard if needed.");

        var newText = baseText + marker;
        var write = bridge.TryWriteFocused(newText);
        if (!write.Success)
        {
            Console.WriteLine($"WRITE FAIL: {write.Error}");
            return;
        }

        Console.WriteLine($"WRITE OK via {write.Method} (clipboardFallback={write.UsedClipboardFallback})");

        Thread.Sleep(150);
        var verify = bridge.TryReadFocused();
        if (verify.Success)
        {
            var ok = (verify.Text ?? "").Contains(marker, StringComparison.Ordinal);
            Console.WriteLine($"Verify contains marker: {ok} (re-read via {verify.Method}, len={verify.Text?.Length})");
        }
        else
        {
            Console.WriteLine($"Verify re-read failed: {verify.Error}");
        }
    }

    private static void WriteSet(UiaTextBridge bridge, string text)
    {
        var write = bridge.TryWriteFocused(text);
        PrintField(write.Field);
        if (!write.Success)
        {
            Console.WriteLine($"WRITE FAIL: {write.Error}");
            return;
        }

        Console.WriteLine($"WRITE OK via {write.Method} (clipboardFallback={write.UsedClipboardFallback})");
    }

    private static void PrintField(AutoCorrect.Platform.Abstractions.FocusedFieldInfo? field)
    {
        if (field is null)
        {
            Console.WriteLine("Field: (none)");
            return;
        }

        Console.WriteLine(
            $"Field: process={field.ProcessName} type={field.ControlType} class={field.ClassName} " +
            $"ValuePat={field.SupportsValuePattern} TextPat={field.SupportsTextPattern} hwnd=0x{field.WindowHandle:X}");
        Console.WriteLine($"  title={Truncate(field.WindowTitle, 80)}");
        Console.WriteLine($"  name={Truncate(field.Name, 80)} id={field.AutomationId}");
    }

    private static string Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        return s.Length <= max ? s : s[..max] + "…";
    }
}
