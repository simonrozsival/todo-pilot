using System.Runtime.InteropServices;

namespace TodoPilot;

public sealed class TerminalResizeWatcher : IDisposable
{
    private readonly PosixSignalRegistration? _registration;

    private TerminalResizeWatcher(PosixSignalRegistration? registration)
    {
        _registration = registration;
    }

    public static TerminalResizeWatcher Create(Action onResize)
    {
        ArgumentNullException.ThrowIfNull(onResize);

        if (OperatingSystem.IsWindows())
        {
            return new TerminalResizeWatcher(null);
        }

        try
        {
            return new TerminalResizeWatcher(PosixSignalRegistration.Create(
                PosixSignal.SIGWINCH,
                _ => onResize()));
        }
        catch (PlatformNotSupportedException)
        {
            return new TerminalResizeWatcher(null);
        }
    }

    public void Dispose() => _registration?.Dispose();
}
