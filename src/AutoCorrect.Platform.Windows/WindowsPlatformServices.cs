using AutoCorrect.Platform.Abstractions;

namespace AutoCorrect.Platform.Windows;

/// <summary>
/// Platform entry surface. The Avalonia host currently owns <see cref="GlobalHotkey"/> +
/// <see cref="WinHDictationSession"/> together so they share one keyboard hook.
/// </summary>
public sealed class WindowsPlatformServices : IPlatformServices
{
    public string PlatformId => "windows";
    public ITextBridge TextBridge { get; } = new UiaTextBridge();

    /// <summary>Null here — App constructs <see cref="WinHDictationSession"/> with the shared hotkey.</summary>
    public IDictationSession? DictationSession => null;
}
