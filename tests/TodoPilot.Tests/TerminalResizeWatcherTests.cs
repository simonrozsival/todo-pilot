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

    [Fact]
    public async Task Create_PollsForSizeChanges()
    {
        var size = (Width: 80, Height: 24);
        var count = 0;
        using var watcher = TerminalResizeWatcher.Create(
            () => Interlocked.Increment(ref count),
            () => size,
            TimeSpan.FromMilliseconds(10),
            TimeSpan.Zero);

        size = (100, 24);
        await WaitForAsync(() => Volatile.Read(ref count) > 0);

        Assert.True(Volatile.Read(ref count) > 0);
    }

    [Fact]
    public async Task Create_DoesNotNotifyWhenSizeIsStable()
    {
        var size = (Width: 80, Height: 24);
        var count = 0;
        using var watcher = TerminalResizeWatcher.Create(
            () => Interlocked.Increment(ref count),
            () => size,
            TimeSpan.FromMilliseconds(10),
            TimeSpan.Zero);

        await Task.Delay(50);

        Assert.Equal(0, Volatile.Read(ref count));
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(10, cts.Token);
        }
    }
}
