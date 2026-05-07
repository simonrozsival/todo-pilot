namespace TodoPilot;

public sealed class TerminalScreen : IDisposable
{
    private static readonly Lock Gate = new();
    private static bool _active;
    private static TextWriter _output = Console.Out;
    private bool _disposed;

    private TerminalScreen()
    {
    }

    public static TerminalScreen Enter()
    {
        if (!CanUseAlternateScreen(Console.IsOutputRedirected, Environment.GetEnvironmentVariable("TERM")))
        {
            return new TerminalScreen();
        }

        lock (Gate)
        {
            if (!_active)
            {
                _output = Console.Out;
                _output.Write("\u001b[?1049h\u001b[2J\u001b[H\u001b[?25l");
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

    public static bool CanUseAlternateScreen(bool outputRedirected, string? term) =>
        !outputRedirected
        && !string.Equals(term, "dumb", StringComparison.OrdinalIgnoreCase);

    private static void Restore()
    {
        lock (Gate)
        {
            if (!_active)
            {
                return;
            }

            _output.Write("\u001b[?25h\u001b[?1049l");
            AppDomain.CurrentDomain.ProcessExit -= RestoreOnExit;
            _active = false;
        }
    }
}
