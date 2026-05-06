namespace TodoPilot;

public sealed class TerminalScreen : IDisposable
{
    private static readonly Lock Gate = new();
    private static bool _active;
    private bool _disposed;

    private TerminalScreen()
    {
    }

    public static TerminalScreen Enter()
    {
        lock (Gate)
        {
            if (!_active)
            {
                Console.Write("\u001b[?1049h\u001b[2J\u001b[H\u001b[?25l");
                AppDomain.CurrentDomain.ProcessExit += RestoreOnExit;
                _active = true;
            }
        }

        return new TerminalScreen();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Restore();
        GC.SuppressFinalize(this);
    }

    private static void RestoreOnExit(object? sender, EventArgs e) => Restore();

    private static void Restore()
    {
        lock (Gate)
        {
            if (!_active)
            {
                return;
            }

            Console.Write("\u001b[?25h\u001b[?1049l");
            AppDomain.CurrentDomain.ProcessExit -= RestoreOnExit;
            _active = false;
        }
    }
}
