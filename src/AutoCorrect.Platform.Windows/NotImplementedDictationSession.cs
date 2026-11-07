using AutoCorrect.Platform.Abstractions;

namespace AutoCorrect.Platform.Windows;

/// <summary>Unused stub — prefer <see cref="WinHDictationSession"/> on Windows.</summary>
public sealed class NotImplementedDictationSession : IDictationSession
{
    public bool IsSupported => false;
    public bool IsSessionActive => false;

    public event EventHandler<DictationSessionEndedEventArgs>? SessionEnded
    {
        add { }
        remove { }
    }

    public event EventHandler? SessionStarted
    {
        add { }
        remove { }
    }

    public void Start() { }
    public void Stop() { }
    public void Dispose() { }
}
