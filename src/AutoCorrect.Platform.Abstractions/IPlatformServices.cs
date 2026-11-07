namespace AutoCorrect.Platform.Abstractions;

/// <summary>Factory surface for OS adapters registered by the host app.</summary>
public interface IPlatformServices
{
    string PlatformId { get; }
    ITextBridge TextBridge { get; }
    IDictationSession? DictationSession { get; }
}
