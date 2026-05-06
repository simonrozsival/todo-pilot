namespace TodoPilot.Tests;

public sealed class TerminalResizeWatcherTests
{
    [Fact]
    public void Create_RequiresCallback()
    {
        Assert.Throws<ArgumentNullException>(() => TerminalResizeWatcher.Create(null!));
    }

    [Fact]
    public void Create_ReturnsDisposableWatcher()
    {
        using var watcher = TerminalResizeWatcher.Create(() => { });

        Assert.NotNull(watcher);
    }
}
