using AutoCorrect.Platform.Abstractions;

namespace AutoCorrect.Platform.Linux;

/// <summary>Phase 4 stub — AT-SPI2 adapter not implemented yet.</summary>
public sealed class StubTextBridge : ITextBridge
{
    public TextReadResult TryReadFocused() =>
        TextReadResult.Fail("Linux AT-SPI text bridge not implemented (Phase 4).");

    public TextWriteResult TryWriteFocused(string newText, TextWriteOptions? options = null) =>
        TextWriteResult.Fail("Linux AT-SPI text bridge not implemented (Phase 4).");
}
