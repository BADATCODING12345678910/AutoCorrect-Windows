using System.Diagnostics;
using System.Windows.Automation;

namespace AutoCorrect.Platform.Windows;

/// <summary>
/// Remembers the last focused UI Automation element outside this process.
/// Lets Fix/Accept work after the user clicks our window (which steals focus).
/// </summary>
public sealed class ExternalFocusTracker : IDisposable
{
    private readonly int _selfPid = Environment.ProcessId;
    private readonly object _gate = new();
    private AutomationElement? _lastExternal;
    private string _lastProcessName = "";
    private string _lastSummary = "(none yet — click a text field in another app)";
    private bool _started;

    public event EventHandler? Changed;

    public string LastProcessName
    {
        get { lock (_gate) return _lastProcessName; }
    }

    public string LastSummary
    {
        get { lock (_gate) return _lastSummary; }
    }

    public void Start()
    {
        if (_started)
            return;
        Automation.AddAutomationFocusChangedEventHandler(OnFocusChanged);
        _started = true;
        // Seed from current focus if it's already outside us
        TryCapture(AutomationElement.FocusedElement);
    }

    public AutomationElement? GetLastExternal()
    {
        lock (_gate)
            return _lastExternal;
    }

    public void Dispose()
    {
        if (!_started)
            return;
        try
        {
            Automation.RemoveAutomationFocusChangedEventHandler(OnFocusChanged);
        }
        catch
        {
            // ignore
        }
        _started = false;
    }

    private void OnFocusChanged(object sender, AutomationFocusChangedEventArgs e)
    {
        try
        {
            TryCapture(AutomationElement.FocusedElement);
        }
        catch
        {
            // ignore transient UIA errors
        }
    }

    private void TryCapture(AutomationElement? el)
    {
        if (el is null)
            return;

        int pid;
        try { pid = el.Current.ProcessId; }
        catch { return; }

        if (pid == _selfPid)
            return;

        string processName;
        try { processName = Process.GetProcessById(pid).ProcessName; }
        catch { processName = $"pid:{pid}"; }

        // Voice Typing / IME overlay must not overwrite the real dictation target.
        if (processName.Equals("TextInputHost", StringComparison.OrdinalIgnoreCase) ||
            processName.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase) ||
            processName.Equals("SearchHost", StringComparison.OrdinalIgnoreCase) ||
            processName.Equals("SearchApp", StringComparison.OrdinalIgnoreCase))
            return;

        string type;
        string name;
        try
        {
            type = el.Current.ControlType.ProgrammaticName;
            name = el.Current.Name ?? "";
        }
        catch
        {
            type = "?";
            name = "";
        }

        // Prefer edit-like targets; still remember others so Brave/Electron Document counts
        var summary = $"{processName} · {type}" +
                      (string.IsNullOrWhiteSpace(name) ? "" : $" · {Truncate(name, 40)}");

        lock (_gate)
        {
            _lastExternal = el;
            _lastProcessName = processName;
            _lastSummary = summary;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
