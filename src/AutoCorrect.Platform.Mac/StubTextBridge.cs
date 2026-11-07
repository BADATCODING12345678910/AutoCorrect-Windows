using AutoCorrect.Platform.Abstractions;

namespace AutoCorrect.Platform.Mac;

/// <summary>Phase 5 stub — macOS Accessibility adapter not implemented yet.</summary>
public sealed class StubTextBridge : ITextBridge
{
    public TextReadResult TryReadFocused() =>
        TextReadResult.Fail("macOS Accessibility text bridge not implemented (Phase 5).");

    public TextWriteResult TryWriteFocused(string newText, TextWriteOptions? options = null) =>
        TextWriteResult.Fail("macOS Accessibility text bridge not implemented (Phase 5).");
}
