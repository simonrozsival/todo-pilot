namespace TodoPilot.Tests;

public sealed class TerminalScreenTests
{
    [Theory]
    [InlineData(true, "xterm-256color", false)]
    [InlineData(false, "dumb", false)]
    [InlineData(false, "DUMB", false)]
    [InlineData(false, null, true)]
    [InlineData(false, "xterm-256color", true)]
    public void CanUseAlternateScreen_DisablesUnsafeTerminalModes(bool outputRedirected, string? term, bool expected)
    {
        Assert.Equal(expected, TerminalScreen.CanUseAlternateScreen(outputRedirected, term));
    }
}
