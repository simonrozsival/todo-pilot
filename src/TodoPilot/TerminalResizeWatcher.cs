using System.Runtime.InteropServices;

namespace TodoPilot;

public sealed class TerminalResizeWatcher : IDisposable
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(75);
    private PosixSignalRegistration? _registration;
    private readonly Timer? _pollTimer;
    private readonly Func<(int Width, int Height)> _getSize;
    private readonly Action _onResize;
    private readonly TimeSpan _debounce;
    private readonly Lock _gate = new();
    private (int Width, int Height) _lastSize;
    private DateTimeOffset _lastNotification = DateTimeOffset.MinValue;
    private bool _disposed;

    private TerminalResizeWatcher(
        Action onResize,
        Func<(int Width, int Height)> getSize,
        PosixSignalRegistration? registration,
        Timer? pollTimer,
        TimeSpan debounce)
    {
        _onResize = onResize;
        _getSize = getSize;
        _registration = registration;
        _pollTimer = pollTimer;
        _debounce = debounce;
        _lastSize = getSize();
    }

    public static TerminalResizeWatcher Create(Action onResize) =>
        Create(onResize, getSize: GetConsoleSize, pollInterval: null, debounce: null);

    public static TerminalResizeWatcher Create(
        Action onResize,
        Func<(int Width, int Height)> getSize,
        TimeSpan? pollInterval = null,
        TimeSpan? debounce = null)
    {
        ArgumentNullException.ThrowIfNull(onResize);
        ArgumentNullException.ThrowIfNull(getSize);

        TerminalResizeWatcher? watcher = null;
        var timer = new Timer(_ => watcher?.Poll(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        watcher = new TerminalResizeWatcher(onResize, getSize, registration: null, timer, debounce ?? DefaultDebounce);
        watcher.TryRegisterPosixResizeSignal();
        timer.Change(pollInterval ?? DefaultPollInterval, pollInterval ?? DefaultPollInterval);
        return watcher;
    }

    private void TryRegisterPosixResizeSignal()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            _registration = PosixSignalRegistration.Create(
                PosixSignal.SIGWINCH,
                _ => NotifyDebounced());
        }
        catch (PlatformNotSupportedException)
        {
        }
    }

    private void Poll()
    {
        if (_disposed)
        {
            return;
        }

        var size = _getSize();
        lock (_gate)
        {
            if (size == _lastSize)
            {
                return;
            }

            _lastSize = size;
        }

        NotifyDebounced();
    }

    private void NotifyDebounced()
    {
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            if (now - _lastNotification < _debounce)
            {
                return;
            }

            _lastNotification = now;
        }

        _onResize();
    }

    private static (int Width, int Height) GetConsoleSize()
    {
        try
        {
            return (Console.WindowWidth, Console.WindowHeight);
        }
        catch (IOException)
        {
            return (0, 0);
        }
        catch (InvalidOperationException)
        {
            return (0, 0);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _pollTimer?.Dispose();
        _registration?.Dispose();
    }
}
