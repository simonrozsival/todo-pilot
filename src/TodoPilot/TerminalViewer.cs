using Spectre.Console;
using Spectre.Console.Rendering;
using System.Globalization;

namespace TodoPilot;

public sealed class TerminalViewer
{
    private readonly AppPaths _paths;
    private readonly SessionDiscovery _discovery;
    private readonly TodoDatabaseReader _todoReader = new();

    private enum TodoListExitAction
    {
        Quit,
        SwitchSession
    }

    public TerminalViewer(AppPaths paths)
    {
        _paths = paths;
        _discovery = new SessionDiscovery(paths);
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var staleAfter = TimeSpan.FromSeconds(20);
        var refreshInterval = TimeSpan.FromSeconds(1);
        var focusMarkerTimeout = TimeSpan.FromMinutes(1);
        var sessions = _discovery.Discover(DateTimeOffset.UtcNow, staleAfter);
        var showNoSessionsMessage = false;

        if (sessions.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No enabled sessions found.[/]");
            AnsiConsole.MarkupLine("[grey]Start or reload Copilot CLI after installing the extension.[/]");
            return;
        }

        using (TerminalScreen.Enter())
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            ConsoleCancelEventHandler cancelHandler = (_, e) =>
            {
                e.Cancel = true;
                linkedCts.Cancel();
            };
            Console.CancelKeyPress += cancelHandler;

            try
            {
                while (!linkedCts.IsCancellationRequested)
                {
                    var selectedSession = PromptForSession(sessions, linkedCts.Token);
                    if (selectedSession is null)
                    {
                        break;
                    }

                    Console.Write("\u001b[H\u001b[2J");
                    var action = await RunTodoListAsync(
                        selectedSession,
                        refreshInterval,
                        focusMarkerTimeout,
                        linkedCts.Token).ConfigureAwait(false);

                    if (action == TodoListExitAction.Quit)
                    {
                        break;
                    }

                    sessions = _discovery.Discover(DateTimeOffset.UtcNow, staleAfter);
                    if (sessions.Count == 0)
                    {
                        showNoSessionsMessage = true;
                        break;
                    }

                    Console.Write("\u001b[H\u001b[2J");
                }
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

        if (showNoSessionsMessage)
        {
            AnsiConsole.MarkupLine("[yellow]No enabled sessions found.[/]");
            AnsiConsole.MarkupLine("[grey]Start or reload Copilot CLI after installing the extension.[/]");
        }
    }

    private async Task<TodoListExitAction> RunTodoListAsync(
        DiscoveredSession selectedSession,
        TimeSpan refreshInterval,
        TimeSpan focusMarkerTimeout,
        CancellationToken cancellationToken)
    {
        string? renderedKey = null;
        var scrollOffset = 0;
        var visibleTodoCount = 1;
        string? focusedTodoId = null;
        string? expandedTodoId = null;
        DateTimeOffset? lastFocusNavigationAt = null;
        var resizeRequested = 0;
        var quitRequested = false;
        var switchSessionRequested = false;
        using var resizeWatcher = TerminalResizeWatcher.Create(() => Interlocked.Exchange(ref resizeRequested, 1));
        var databasePath = _paths.GetSessionDatabasePath(selectedSession.Registry.SessionId);
        var initialSnapshotTask = Task.Run(() => _todoReader.Read(databasePath), cancellationToken);
        var initialRenderable = BuildLoadingView(selectedSession, spinnerFrame: 0);

        await AnsiConsole.Live(initialRenderable)
            .AutoClear(false)
            .Overflow(VerticalOverflow.Crop)
            .Cropping(VerticalOverflowCropping.Bottom)
            .StartAsync(async ctx =>
            {
                var loadingFrame = 0;
                while (!initialSnapshotTask.IsCompleted
                    && !cancellationToken.IsCancellationRequested
                    && !quitRequested
                    && !switchSessionRequested)
                {
                    while (TerminalKeyboard.TryReadKey(out var key))
                    {
                        var keyAction = TerminalKeyboard.MapTodoListKey(key);
                        if (keyAction == TodoListKeyAction.Quit)
                        {
                            quitRequested = true;
                            break;
                        }

                        if (keyAction == TodoListKeyAction.SwitchSession)
                        {
                            switchSessionRequested = true;
                            break;
                        }
                    }

                    if (quitRequested || switchSessionRequested)
                    {
                        return;
                    }

                    ctx.UpdateTarget(BuildLoadingView(selectedSession, ++loadingFrame));
                    ctx.Refresh();
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }

                if (quitRequested || switchSessionRequested)
                {
                    return;
                }

                cancellationToken.ThrowIfCancellationRequested();

                var initialNow = DateTimeOffset.Now;
                var initialSnapshot = await initialSnapshotTask.ConfigureAwait(false);
                focusedTodoId = SelectDefaultFocusedTodoId(initialSnapshot.Todos, focusedTodoId);
                var initialDisplay = CreateDisplayState(focusedTodoId, expandedTodoId, lastFocusNavigationAt, initialNow, focusMarkerTimeout);
                renderedKey = CreateRenderKey(initialSnapshot, initialNow, initialDisplay);
                var initialView = BuildTodoList(selectedSession, initialSnapshot, initialNow, scrollOffset, initialDisplay);
                scrollOffset = initialView.Scroll.Offset;
                visibleTodoCount = Math.Max(1, initialView.VisibleTodoCount);
                ctx.UpdateTarget(initialView.Renderable);
                ctx.Refresh();

                while (!cancellationToken.IsCancellationRequested && !quitRequested && !switchSessionRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var now = DateTimeOffset.Now;
                    var snapshot = _todoReader.Read(databasePath);
                    focusedTodoId = SelectDefaultFocusedTodoId(snapshot.Todos, focusedTodoId);
                    if (expandedTodoId is not null && !snapshot.Todos.Any(todo => todo.Id == expandedTodoId))
                    {
                        expandedTodoId = null;
                    }

                    var display = CreateDisplayState(focusedTodoId, expandedTodoId, lastFocusNavigationAt, now, focusMarkerTimeout);
                    var renderKey = CreateRenderKey(snapshot, now, display);
                    var resizeRequestedNow = Interlocked.Exchange(ref resizeRequested, 0) != 0;
                    var shouldRender = ShouldRender(renderedKey, renderKey, resizeRequestedNow);

                    if (shouldRender)
                    {
                        var view = BuildTodoList(selectedSession, snapshot, now, scrollOffset, display);
                        scrollOffset = view.Scroll.Offset;
                        visibleTodoCount = Math.Max(1, view.VisibleTodoCount);
                        ctx.UpdateTarget(view.Renderable);
                        ctx.Refresh();
                        renderedKey = renderKey;
                    }

                    var until = DateTimeOffset.UtcNow + refreshInterval;
                    while (DateTimeOffset.UtcNow < until
                        && !cancellationToken.IsCancellationRequested
                        && !quitRequested
                        && !switchSessionRequested)
                    {
                        while (TerminalKeyboard.TryReadKey(out var key))
                        {
                            switch (TerminalKeyboard.MapTodoListKey(key))
                            {
                                case TodoListKeyAction.Quit:
                                    quitRequested = true;
                                    break;
                                case TodoListKeyAction.SwitchSession:
                                    switchSessionRequested = true;
                                    break;
                                case TodoListKeyAction.Refresh:
                                    renderedKey = null;
                                    until = DateTimeOffset.UtcNow;
                                    break;
                                case TodoListKeyAction.FocusPrevious:
                                    focusedTodoId = MoveFocusedTodoId(snapshot.Todos, focusedTodoId, -1);
                                    lastFocusNavigationAt = DateTimeOffset.Now;
                                    renderedKey = null;
                                    until = DateTimeOffset.UtcNow;
                                    break;
                                case TodoListKeyAction.FocusNext:
                                    focusedTodoId = MoveFocusedTodoId(snapshot.Todos, focusedTodoId, 1);
                                    lastFocusNavigationAt = DateTimeOffset.Now;
                                    renderedKey = null;
                                    until = DateTimeOffset.UtcNow;
                                    break;
                                case TodoListKeyAction.ToggleExpanded:
                                    if (focusedTodoId is not null)
                                    {
                                        expandedTodoId = string.Equals(expandedTodoId, focusedTodoId, StringComparison.Ordinal)
                                            ? null
                                            : focusedTodoId;
                                        renderedKey = null;
                                        until = DateTimeOffset.UtcNow;
                                    }
                                    break;
                                case TodoListKeyAction.PageNext:
                                    focusedTodoId = MoveFocusedTodoId(snapshot.Todos, focusedTodoId, visibleTodoCount);
                                    lastFocusNavigationAt = DateTimeOffset.Now;
                                    renderedKey = null;
                                    until = DateTimeOffset.UtcNow;
                                    break;
                                case TodoListKeyAction.PagePrevious:
                                    focusedTodoId = MoveFocusedTodoId(snapshot.Todos, focusedTodoId, -visibleTodoCount);
                                    lastFocusNavigationAt = DateTimeOffset.Now;
                                    renderedKey = null;
                                    until = DateTimeOffset.UtcNow;
                                    break;
                                case TodoListKeyAction.FocusFirst:
                                    focusedTodoId = snapshot.Todos.FirstOrDefault()?.Id;
                                    lastFocusNavigationAt = DateTimeOffset.Now;
                                    renderedKey = null;
                                    until = DateTimeOffset.UtcNow;
                                    break;
                                case TodoListKeyAction.FocusLast:
                                    focusedTodoId = snapshot.Todos.LastOrDefault()?.Id;
                                    lastFocusNavigationAt = DateTimeOffset.Now;
                                    renderedKey = null;
                                    until = DateTimeOffset.UtcNow;
                                    break;
                            }

                            if (quitRequested || switchSessionRequested)
                            {
                                break;
                            }
                        }

                        if (quitRequested || switchSessionRequested)
                        {
                            break;
                        }

                        await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                    }
                }
            }).ConfigureAwait(false);

        return switchSessionRequested ? TodoListExitAction.SwitchSession : TodoListExitAction.Quit;
    }

    private static TerminalRenderer.TodoListView BuildTodoList(
        DiscoveredSession session,
        TodoSnapshot snapshot,
        DateTimeOffset now,
        int scrollOffset,
        TodoListDisplayState displayState)
    {
        var terminalSize = TerminalSize.GetCurrent();
        return TerminalRenderer.BuildTodoListView(session, snapshot, now, terminalSize.Width, terminalSize.Height, scrollOffset, displayState: displayState);
    }

    private static IRenderable BuildLoadingView(DiscoveredSession session, int spinnerFrame)
    {
        var terminalSize = TerminalSize.GetCurrent();
        return TerminalRenderer.BuildLoadingView(session, spinnerFrame, terminalSize.Width);
    }

    public static TodoListDisplayState CreateDisplayState(
        string? focusedTodoId,
        string? expandedTodoId,
        DateTimeOffset? lastFocusNavigationAt,
        DateTimeOffset now,
        TimeSpan focusMarkerTimeout)
    {
        var showFocusMarker = expandedTodoId is not null
            || lastFocusNavigationAt is not null && now - lastFocusNavigationAt.Value <= focusMarkerTimeout;
        return new TodoListDisplayState(focusedTodoId, expandedTodoId, showFocusMarker);
    }

    private static DiscoveredSession? PromptForSession(IReadOnlyList<DiscoveredSession> sessions, CancellationToken cancellationToken)
    {
        var selectedIndex = 0;
        var scrollOffset = 0;
        var filter = "";
        var visibleItemCount = 1;
        var showSessionIds = false;
        var showOnlyRunningExtensionSessions = true;
        var resizeRequested = 1;
        var shouldRender = true;
        TerminalSize? renderedSize = null;
        using var resizeWatcher = TerminalResizeWatcher.Create(() => Interlocked.Exchange(ref resizeRequested, 1));

        while (!cancellationToken.IsCancellationRequested)
        {
            var filteredSessions = FilterSessions(
                GetSessionsForView(sessions, showOnlyRunningExtensionSessions),
                filter);
            if (filteredSessions.Count == 0)
            {
                selectedIndex = 0;
                scrollOffset = 0;
            }
            else
            {
                selectedIndex = Math.Clamp(selectedIndex, 0, filteredSessions.Count - 1);
            }

            var terminalSize = TerminalSize.GetCurrent();
            var resizeRequestedNow = Interlocked.Exchange(ref resizeRequested, 0) != 0;
            if (ShouldRenderSessionSelection(renderedSize, terminalSize, shouldRender, resizeRequestedNow))
            {
                var view = BuildSessionSelectionView(
                    filteredSessions,
                    selectedIndex,
                    scrollOffset,
                    filter,
                    terminalSize.Width,
                    terminalSize.Height,
                    showSessionIds: showSessionIds,
                    showOnlyRunningExtensionSessions: showOnlyRunningExtensionSessions);
                scrollOffset = view.Scroll.Offset;
                visibleItemCount = Math.Max(1, view.VisibleItemCount);

                Console.Write("\u001b[H\u001b[2J");
                AnsiConsole.Write(view.Renderable);
                renderedSize = terminalSize;
                shouldRender = false;
            }

            if (!TerminalKeyboard.TryReadKey(out var key))
            {
                cancellationToken.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(50));
                continue;
            }

            switch (TerminalKeyboard.MapSessionSelectionKey(key))
            {
                case SessionSelectionKeyAction.Accept when filteredSessions.Count > 0:
                    return filteredSessions[selectedIndex];
                case SessionSelectionKeyAction.Quit:
                    return null;
                case SessionSelectionKeyAction.Previous:
                    selectedIndex = Math.Max(0, selectedIndex - 1);
                    shouldRender = true;
                    break;
                case SessionSelectionKeyAction.Next:
                    selectedIndex = Math.Min(Math.Max(0, filteredSessions.Count - 1), selectedIndex + 1);
                    shouldRender = true;
                    break;
                case SessionSelectionKeyAction.PagePrevious:
                    selectedIndex = Math.Max(0, selectedIndex - visibleItemCount);
                    shouldRender = true;
                    break;
                case SessionSelectionKeyAction.PageNext:
                    selectedIndex = Math.Min(Math.Max(0, filteredSessions.Count - 1), selectedIndex + visibleItemCount);
                    shouldRender = true;
                    break;
                case SessionSelectionKeyAction.First:
                    selectedIndex = 0;
                    shouldRender = true;
                    break;
                case SessionSelectionKeyAction.Last:
                    selectedIndex = Math.Max(0, filteredSessions.Count - 1);
                    shouldRender = true;
                    break;
                case SessionSelectionKeyAction.Backspace:
                    if (filter.Length > 0)
                    {
                        filter = filter[..^1];
                        selectedIndex = 0;
                        scrollOffset = 0;
                        shouldRender = true;
                    }
                    break;
                case SessionSelectionKeyAction.ToggleSessionIds:
                    showSessionIds = !showSessionIds;
                    shouldRender = true;
                    break;
                case SessionSelectionKeyAction.ToggleRunningOnly:
                    showOnlyRunningExtensionSessions = !showOnlyRunningExtensionSessions;
                    selectedIndex = 0;
                    scrollOffset = 0;
                    shouldRender = true;
                    break;
                case SessionSelectionKeyAction.AppendFilter:
                    filter += key.KeyChar;
                    selectedIndex = 0;
                    scrollOffset = 0;
                    shouldRender = true;
                    break;
            }
        }

        return null;
    }

    public static SessionSelectionView BuildSessionSelectionView(
        IReadOnlyList<DiscoveredSession> sessions,
        int selectedIndex,
        int scrollOffset,
        string filter,
        int consoleWidth,
        int consoleHeight,
        DateTimeOffset? now = null,
        bool showSessionIds = false,
        bool showOnlyRunningExtensionSessions = true)
    {
        var content = BuildSessionSelectionContent(
            sessions,
            selectedIndex,
            scrollOffset,
            filter,
            consoleWidth,
            consoleHeight,
            now,
            showSessionIds,
            showOnlyRunningExtensionSessions);
        return new SessionSelectionView(
            TerminalRenderer.ToRows(content.Lines),
            content.Scroll,
            content.VisibleItemCount);
    }

    public static SessionSelectionContent BuildSessionSelectionContent(
        IReadOnlyList<DiscoveredSession> sessions,
        int selectedIndex,
        int scrollOffset,
        string filter,
        int consoleWidth,
        int consoleHeight,
        DateTimeOffset? now = null,
        bool showSessionIds = false,
        bool showOnlyRunningExtensionSessions = true)
    {
        var contentWidth = TerminalRenderer.GetContentWidth(consoleWidth);
        var renderedAt = now ?? DateTimeOffset.Now;
        var selectedSessionId = sessions.Count == 0
            ? null
            : sessions[Math.Clamp(selectedIndex, 0, sessions.Count - 1)].Registry.SessionId;
        var headerRows = new List<string>
        {
            $"{TerminalRenderer.Padding()}[bold]# Choose a Copilot session[/]",
            $"{TerminalRenderer.Padding()}[grey]{Markup.Escape(GetSelectionFilterText(filter))}[/]",
            ""
        };
        var bodyRows = new List<TerminalRenderer.ListLine>();

        if (sessions.Count == 0)
        {
            bodyRows.Add(new TerminalRenderer.ListLine(
                $"{TerminalRenderer.Padding()}[grey]{Markup.Escape(GetEmptySessionSelectionText(filter, showOnlyRunningExtensionSessions))}[/]",
                ItemId: null));
        }
        else
        {
            for (var i = 0; i < sessions.Count; i++)
            {
                var selected = i == selectedIndex;
                foreach (var line in FormatSessionSelectionChoiceLines(sessions[i], selected, contentWidth, renderedAt, showSessionIds))
                {
                    bodyRows.Add(new TerminalRenderer.ListLine($"{TerminalRenderer.Padding()}{line}", sessions[i].Registry.SessionId));
                }
            }
        }

        var content = TerminalRenderer.BuildListLayoutContent(
            headerRows,
            bodyRows,
            scroll => BuildSessionSelectionFooterRows(contentWidth, scroll, showSessionIds, showOnlyRunningExtensionSessions),
            consoleHeight,
            scrollOffset,
            totalItemCount: sessions.Count,
            selectedSessionId);
        return new SessionSelectionContent(
            content.Lines,
            content.Scroll,
            content.VisibleItemCount);
    }

    public static int ClampSelectionScrollOffset(int itemCount, int pageSize, int selectedIndex, int requestedOffset)
    {
        var maxOffset = Math.Max(0, itemCount - pageSize);
        var offset = Math.Clamp(requestedOffset, 0, maxOffset);
        if (selectedIndex < offset)
        {
            return selectedIndex;
        }

        if (selectedIndex >= offset + pageSize)
        {
            return Math.Clamp(selectedIndex - pageSize + 1, 0, maxOffset);
        }

        return offset;
    }

    public static IReadOnlyList<DiscoveredSession> FilterSessions(IReadOnlyList<DiscoveredSession> sessions, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return sessions;
        }

        return sessions
            .Where(session => GetSessionSearchText(session).Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public static IReadOnlyList<DiscoveredSession> GetSessionsForView(
        IReadOnlyList<DiscoveredSession> sessions,
        bool showOnlyRunningExtensionSessions)
    {
        if (!showOnlyRunningExtensionSessions)
        {
            return sessions;
        }

        return sessions
            .Where(session => session.IsExtensionProcessRunning)
            .ToArray();
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

    public static string CreateRenderKey(TodoSnapshot snapshot, DateTimeOffset now, TodoListDisplayState displayState) =>
        string.Join(
            '\u001f',
            CreateRenderKey(snapshot, now),
            displayState.FocusedTodoId ?? "",
            displayState.ExpandedTodoId ?? "",
            displayState.ShowFocusMarker);

    public static string CreateRenderKey(TodoSnapshot snapshot, DateTimeOffset now, TerminalSize terminalSize) =>
        string.Join(
            '\u001f',
            CreateRenderKey(snapshot, now),
            terminalSize.Width,
            terminalSize.Height);

    public static string? SelectDefaultFocusedTodoId(IReadOnlyList<TodoItem> todos, string? currentFocusedTodoId)
    {
        if (currentFocusedTodoId is not null && todos.Any(todo => todo.Id == currentFocusedTodoId))
        {
            return currentFocusedTodoId;
        }

        return todos.FirstOrDefault(todo => todo.Status == "in_progress")?.Id
            ?? todos.FirstOrDefault(todo => todo.Status == "pending")?.Id
            ?? todos.FirstOrDefault()?.Id;
    }

    public static string? MoveFocusedTodoId(IReadOnlyList<TodoItem> todos, string? currentFocusedTodoId, int delta)
    {
        if (todos.Count == 0)
        {
            return null;
        }

        var currentIndex = currentFocusedTodoId is null
            ? 0
            : todos.ToList().FindIndex(todo => todo.Id == currentFocusedTodoId);
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }

        var nextIndex = Math.Clamp(currentIndex + delta, 0, todos.Count - 1);
        return todos[nextIndex].Id;
    }

    public static bool ShouldRender(string? renderedKey, string candidateKey, bool resizeRequested) =>
        resizeRequested || !StringComparer.Ordinal.Equals(renderedKey, candidateKey);

    public static bool ShouldRenderSessionSelection(TerminalSize? renderedSize, TerminalSize currentSize, bool stateChanged, bool resizeRequested) =>
        stateChanged || resizeRequested || renderedSize != currentSize;

    public static bool IsQuitKey(ConsoleKeyInfo key) =>
        key.Key == ConsoleKey.Q;

    public static string FormatSessionChoice(DiscoveredSession session)
    {
        return Markup.Escape(GetSessionChoiceText(session));
    }

    public static string FormatSessionSelectionChoice(DiscoveredSession session, bool selected, int maxWidth)
    {
        return FormatSessionSelectionChoiceLines(session, selected, maxWidth, DateTimeOffset.Now, showSessionId: false)[0];
    }

    public static IReadOnlyList<string> FormatSessionSelectionChoiceLines(
        DiscoveredSession session,
        bool selected,
        int maxWidth,
        DateTimeOffset now,
        bool showSessionId = false)
    {
        const string selectedPrefix = "[•] ";
        const string unselectedPrefix = "[ ] ";
        const string continuationPrefix = "    ";
        var prefix = selected ? selectedPrefix : unselectedPrefix;
        var firstTitleWidth = Math.Max(1, maxWidth - prefix.Length);
        var continuationWidth = Math.Max(1, maxWidth - continuationPrefix.Length);
        var titleLines = TerminalRenderer.WrapText(TerminalRenderer.GetSessionName(session), firstTitleWidth, continuationWidth);
        var lines = new List<string>(titleLines.Count + 2);
        var metadata = GetSessionMetadataText(session, now, showSessionId);

        for (var i = 0; i < titleLines.Count; i++)
        {
            var linePrefix = i == 0 ? prefix : continuationPrefix;
            var titleLine = $"{linePrefix}{titleLines[i]}";
            var canAppendMetadata = i == titleLines.Count - 1
                && titleLine.Length + 1 + metadata.Length <= maxWidth;
            lines.Add(canAppendMetadata
                ? $"{StyleSessionSelectionLine(titleLine, selected)} {StyleSessionSelectionMetadataText(metadata)}"
                : StyleSessionSelectionLine(titleLine, selected));
            if (canAppendMetadata)
            {
                return lines;
            }
        }

        foreach (var line in TerminalRenderer.WrapText(metadata, continuationWidth, continuationWidth))
        {
            lines.Add(StyleSessionSelectionMetadataText($"{continuationPrefix}{line}"));
        }

        return lines;
    }

    private static string GetSessionChoiceText(DiscoveredSession session)
    {
        var name = TerminalRenderer.GetSessionName(session);
        var state = session.IsStale ? "stale" : "active";
        return $"{name} [{session.Registry.SessionId}] {state}";
    }

    private static string GetSessionSearchText(DiscoveredSession session) =>
        string.Join(
            ' ',
            TerminalRenderer.GetSessionName(session),
            session.Registry.SessionId,
            session.Registry.Cwd,
            session.DisplayCwd,
            session.Metadata?.Repository,
            session.Metadata?.Branch,
            session.Metadata?.Summary,
            session.IsExtensionProcessRunning ? "running extension process" : "stopped extension process",
            session.IsStale ? "stale" : "active");

    private static string GetSelectionFilterText(string filter) =>
        string.IsNullOrEmpty(filter)
            ? "Type to filter by session name, repo, directory, UUID, or state"
            : $"Filter: {filter}";

    private static string GetEmptySessionSelectionText(string filter, bool showOnlyRunningExtensionSessions)
    {
        if (!showOnlyRunningExtensionSessions)
        {
            return "No sessions match the current filter.";
        }

        return string.IsNullOrWhiteSpace(filter)
            ? "No sessions with a running extension process. Press ctrl+a to show all registered sessions."
            : "No running sessions match the current filter. Press ctrl+a to show all registered sessions.";
    }

    private static IReadOnlyList<string> BuildSessionSelectionFooterRows(
        int contentWidth,
        TerminalRenderer.ScrollMetrics? scroll,
        bool showSessionIds,
        bool showOnlyRunningExtensionSessions)
    {
        var rows = new List<string> { "" };
        var uuidToggle = showSessionIds ? "ctrl+u hide UUIDs" : "ctrl+u show UUIDs";
        var viewToggle = showOnlyRunningExtensionSessions ? "ctrl+a show all" : "ctrl+a running only";
        var text = scroll is { CanScroll: true } scrollMetrics
            ? $"{TerminalRenderer.FormatFooterStatus(scrollMetrics)} · j/k move · PgUp/PgDn scroll · enter select · type filter · {uuidToggle} · {viewToggle} · q quit"
            : $"j/k move · enter select · type filter · {uuidToggle} · {viewToggle} · q quit";
        foreach (var line in TerminalRenderer.WrapText(text, contentWidth, contentWidth))
        {
            rows.Add($"{TerminalRenderer.Padding()}[grey]{Markup.Escape(line)}[/]");
        }

        return rows;
    }

    private static string GetSessionMetadataText(DiscoveredSession session, DateTimeOffset now, bool showSessionId)
    {
        var state = session.IsStale ? "stale" : "active";
        var lastActive = FormatLastActive(session.Registry.LastSeen, now);
        var metadata = new List<string>();
        if (showSessionId)
        {
            metadata.Add($"[{session.Registry.SessionId}]");
        }

        metadata.Add(state);
        if (lastActive is not null)
        {
            metadata.Add(lastActive);
        }

        return string.Join(" · ", metadata);
    }

    private static string? FormatLastActive(string? value, DateTimeOffset now)
    {
        if (!TryParseTimestamp(value, out var timestamp))
        {
            return null;
        }

        var localTimestamp = timestamp.ToLocalTime();
        var elapsed = now.ToLocalTime() - localTimestamp;
        var relative = FormatRelativeAge(elapsed);
        return $"last active {relative} ⋅ {localTimestamp:HH:mm}";
    }

    private static bool TryParseTimestamp(string? value, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (HasExplicitOffset(trimmed))
        {
            return DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp);
        }

        if (!DateTime.TryParse(
                trimmed,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var utcDateTime))
        {
            return false;
        }

        timestamp = new DateTimeOffset(DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc));
        return true;
    }

    private static bool HasExplicitOffset(string value)
    {
        if (value.EndsWith('Z') || value.EndsWith('z'))
        {
            return true;
        }

        if (value.Length < 6)
        {
            return false;
        }

        var suffix = value[^6..];
        return (suffix[0] is '+' or '-')
            && char.IsDigit(suffix[1])
            && char.IsDigit(suffix[2])
            && suffix[3] == ':'
            && char.IsDigit(suffix[4])
            && char.IsDigit(suffix[5]);
    }

    private static string FormatRelativeAge(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"{Math.Max(1, (int)Math.Floor(elapsed.TotalMinutes))}m ago";
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            return $"{Math.Max(1, (int)Math.Floor(elapsed.TotalHours))}h ago";
        }

        return $"{Math.Max(1, (int)Math.Floor(elapsed.TotalDays))}d ago";
    }

    private static string StyleSessionSelectionLine(string text, bool selected)
    {
        var escaped = Markup.Escape(text);
        return selected ? $"[blue]{escaped}[/]" : escaped;
    }

    private static string StyleSessionSelectionMetadataText(string text)
    {
        var escaped = Markup.Escape(text);
        return $"[grey]{escaped}[/]";
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
            catch (InvalidOperationException)
            {
                return new TerminalSize(100, 40);
            }
        }
    }

    public sealed record SessionSelectionContent(
        IReadOnlyList<string> Lines,
        TerminalRenderer.ScrollMetrics Scroll,
        int VisibleItemCount);

    public sealed record SessionSelectionView(
        IRenderable Renderable,
        TerminalRenderer.ScrollMetrics Scroll,
        int VisibleItemCount);
}
