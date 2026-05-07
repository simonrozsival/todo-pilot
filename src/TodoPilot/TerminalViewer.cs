using Spectre.Console;
using Spectre.Console.Rendering;

namespace TodoPilot;

public sealed class TerminalViewer
{
    private readonly AppPaths _paths;
    private readonly SessionDiscovery _discovery;
    private readonly TodoDatabaseReader _todoReader = new();

    public TerminalViewer(AppPaths paths)
    {
        _paths = paths;
        _discovery = new SessionDiscovery(paths);
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var staleAfter = TimeSpan.FromSeconds(20);
        var refreshInterval = TimeSpan.FromSeconds(1);
        var sessions = _discovery.Discover(DateTimeOffset.UtcNow, staleAfter);

        if (sessions.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No enabled sessions found.[/]");
            AnsiConsole.MarkupLine("[grey]Start or reload Copilot CLI after installing the extension.[/]");
            return;
        }

        var selectedSession = PromptForSession(sessions);

        using var screen = TerminalScreen.Enter();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true;
            linkedCts.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            string? renderedKey = null;
            var scrollOffset = 0;
            var pageSize = 1;
            var maxScrollOffset = 0;
            var resizeRequested = 0;
            using var resizeWatcher = TerminalResizeWatcher.Create(() => Interlocked.Exchange(ref resizeRequested, 1));
            var databasePath = _paths.GetSessionDatabasePath(selectedSession.Registry.SessionId);
            var initialSnapshotTask = Task.Run(() => _todoReader.Read(databasePath), linkedCts.Token);
            var initialRenderable = BuildLoadingView(selectedSession, spinnerFrame: 0);

            await AnsiConsole.Live(initialRenderable)
                .AutoClear(false)
                .Overflow(VerticalOverflow.Crop)
                .Cropping(VerticalOverflowCropping.Bottom)
                .StartAsync(async ctx =>
                {
                    var loadingFrame = 0;
                    while (!initialSnapshotTask.IsCompleted && !linkedCts.IsCancellationRequested)
                    {
                        while (Console.KeyAvailable)
                        {
                            var key = Console.ReadKey(intercept: true);
                            if (key.Key is ConsoleKey.Q or ConsoleKey.Escape)
                            {
                                linkedCts.Cancel();
                                break;
                            }
                        }

                        ctx.UpdateTarget(BuildLoadingView(selectedSession, ++loadingFrame));
                        ctx.Refresh();
                        await Task.Delay(100, linkedCts.Token).ConfigureAwait(false);
                    }

                    linkedCts.Token.ThrowIfCancellationRequested();

                    var initialNow = DateTimeOffset.Now;
                    var initialSnapshot = await initialSnapshotTask.ConfigureAwait(false);
                    renderedKey = CreateRenderKey(initialSnapshot, initialNow);
                    var initialView = BuildTodoList(selectedSession, initialSnapshot, initialNow, scrollOffset);
                    scrollOffset = initialView.Scroll.Offset;
                    pageSize = initialView.Scroll.PageSize;
                    maxScrollOffset = initialView.Scroll.MaxOffset;
                    ctx.UpdateTarget(initialView.Renderable);
                    ctx.Refresh();

                    while (!linkedCts.IsCancellationRequested)
                    {
                        linkedCts.Token.ThrowIfCancellationRequested();
                        var now = DateTimeOffset.Now;
                        var snapshot = _todoReader.Read(databasePath);
                        var renderKey = CreateRenderKey(snapshot, now);
                        var resizeRequestedNow = Interlocked.Exchange(ref resizeRequested, 0) != 0;
                        var shouldRender = ShouldRender(renderedKey, renderKey, resizeRequestedNow);

                        if (shouldRender)
                        {
                            var view = BuildTodoList(selectedSession, snapshot, now, scrollOffset);
                            scrollOffset = view.Scroll.Offset;
                            pageSize = view.Scroll.PageSize;
                            maxScrollOffset = view.Scroll.MaxOffset;
                            ctx.UpdateTarget(view.Renderable);
                            ctx.Refresh();
                            renderedKey = renderKey;
                        }

                        var until = DateTimeOffset.UtcNow + refreshInterval;
                        while (DateTimeOffset.UtcNow < until && !linkedCts.IsCancellationRequested)
                        {
                            while (Console.KeyAvailable)
                            {
                                var key = Console.ReadKey(intercept: true);
                                switch (key.Key)
                                {
                                    case ConsoleKey.Q:
                                    case ConsoleKey.Escape:
                                        linkedCts.Cancel();
                                        break;
                                    case ConsoleKey.R:
                                        renderedKey = null;
                                        until = DateTimeOffset.UtcNow;
                                        break;
                                    case ConsoleKey.PageDown:
                                        scrollOffset = Math.Min(maxScrollOffset, scrollOffset + pageSize);
                                        renderedKey = null;
                                        until = DateTimeOffset.UtcNow;
                                        break;
                                    case ConsoleKey.PageUp:
                                        scrollOffset = Math.Max(0, scrollOffset - pageSize);
                                        renderedKey = null;
                                        until = DateTimeOffset.UtcNow;
                                        break;
                                    case ConsoleKey.Home:
                                        scrollOffset = 0;
                                        renderedKey = null;
                                        until = DateTimeOffset.UtcNow;
                                        break;
                                    case ConsoleKey.End:
                                        scrollOffset = maxScrollOffset;
                                        renderedKey = null;
                                        until = DateTimeOffset.UtcNow;
                                        break;
                                }
                            }

                            await Task.Delay(100, linkedCts.Token).ConfigureAwait(false);
                        }
                    }
                }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
        {
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            AnsiConsole.Clear();
        }
    }

    private static TerminalRenderer.TodoListView BuildTodoList(
        DiscoveredSession session,
        TodoSnapshot snapshot,
        DateTimeOffset now,
        int scrollOffset)
    {
        var terminalSize = TerminalSize.GetCurrent();
        return TerminalRenderer.BuildTodoListView(session, snapshot, now, terminalSize.Width, terminalSize.Height, scrollOffset);
    }

    private static IRenderable BuildLoadingView(DiscoveredSession session, int spinnerFrame)
    {
        var terminalSize = TerminalSize.GetCurrent();
        return TerminalRenderer.BuildLoadingView(session, spinnerFrame, terminalSize.Width);
    }

    private static DiscoveredSession PromptForSession(IReadOnlyList<DiscoveredSession> sessions)
    {
        var choices = sessions.Select(SessionChoice.From).ToArray();
        var prompt = new SelectionPrompt<SessionChoice>
        {
            Title = "Choose a Copilot session",
            PageSize = 12,
            SearchEnabled = true,
            SearchPlaceholderText = "Filter by session name, repo, directory, or UUID",
            MoreChoicesText = "Move up and down to reveal more sessions"
        };

        prompt.AddChoices(choices);
        return AnsiConsole.Prompt(prompt).Session;
    }

    public static string CreateRenderKey(TodoSnapshot snapshot, DateTimeOffset now) =>
        string.Join(
            '\u001f',
            snapshot.State,
            snapshot.DataHash,
            snapshot.Message,
            snapshot.Todos.Count,
            TerminalRenderer.HasDisplayedTimestamps(snapshot) ? now.ToUnixTimeSeconds() / 60 : "",
            TerminalRenderer.CreateTimestampKey(snapshot, now));

    public static string CreateRenderKey(TodoSnapshot snapshot, DateTimeOffset now, TerminalSize terminalSize) =>
        string.Join(
            '\u001f',
            CreateRenderKey(snapshot, now),
            terminalSize.Width,
            terminalSize.Height);

    public static bool ShouldRender(string? renderedKey, string candidateKey, bool resizeRequested) =>
        resizeRequested || !StringComparer.Ordinal.Equals(renderedKey, candidateKey);

    public static string FormatSessionChoice(DiscoveredSession session)
    {
        var name = TerminalRenderer.GetSessionName(session);
        var state = session.IsStale ? "stale" : "active";
        return Markup.Escape($"{name} [{session.Registry.SessionId}] {state}");
    }

    public readonly record struct TerminalSize(int Width, int Height)
    {
        public static TerminalSize GetCurrent()
        {
            try
            {
                return new TerminalSize(Console.WindowWidth, Console.WindowHeight);
            }
            catch (IOException)
            {
                return new TerminalSize(100, 40);
            }
        }
    }

    private sealed record SessionChoice(DiscoveredSession Session)
    {
        public static SessionChoice From(DiscoveredSession session) => new(session);

        public override string ToString()
        {
            return FormatSessionChoice(Session);
        }
    }
}
