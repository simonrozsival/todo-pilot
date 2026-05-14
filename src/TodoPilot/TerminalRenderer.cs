using System.Buffers;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.Globalization;
using System.Text;

namespace TodoPilot;

public static class TerminalRenderer
{
    private const int HorizontalPadding = 2;
    private const int MinimumContentWidth = 24;
    private const int DefaultContentWidth = 100;
    private const string ContinuationIndent = "    ";
    private const string DetailValueContinuationIndent = " ";
    private const string TimestampStyle = "grey";
    private const string MutedTodoStyle = "dim";
    private const string DependencyMutedTodoStyle = "grey35";
    private const string BlockedTodoStyle = "orange1";
    private static readonly TimeSpan FreshCompletionWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan JustNowWindow = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan TodoBatchWindow = TimeSpan.FromSeconds(5);
    private static readonly string[] LoadingSpinnerFrames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];
    private static readonly IReadOnlyDictionary<string, int> ExpandedDetailKeyOrder = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["description"] = 0,
        ["id"] = 10,
        ["status"] = 20,
        ["needs"] = 30,
        ["blocks"] = 40
    };

    public static IRenderable BuildTodoList(
        DiscoveredSession session,
        TodoSnapshot snapshot,
        DateTimeOffset? now,
        int? consoleWidth = null,
        int? consoleHeight = null,
        int scrollOffset = 0,
        SessionSidebarDetails? details = null,
        TodoListDisplayState? displayState = null,
        int focusedContextItemCountBefore = 0)
    {
        return BuildTodoListView(session, snapshot, now, consoleWidth, consoleHeight, scrollOffset, details, displayState, focusedContextItemCountBefore).Renderable;
    }

    public static TodoListView BuildTodoListView(
        DiscoveredSession session,
        TodoSnapshot snapshot,
        DateTimeOffset? now,
        int? consoleWidth = null,
        int? consoleHeight = null,
        int scrollOffset = 0,
        SessionSidebarDetails? details = null,
        TodoListDisplayState? displayState = null,
        int focusedContextItemCountBefore = 0)
    {
        var renderedAt = now ?? DateTimeOffset.Now;
        var contentWidth = GetContentWidth(consoleWidth);
        var headerRows = BuildHeaderRows(session, contentWidth);
        var bodyRows = BuildBodyRows(snapshot, renderedAt, contentWidth, displayState ?? new TodoListDisplayState());
        var content = BuildListLayoutContent(
            headerRows,
            bodyRows,
            scroll => BuildFooterRows(contentWidth, scroll),
            consoleHeight,
            scrollOffset,
            totalItemCount: snapshot.Todos.Count,
            focusedItemId: displayState?.FocusedTodoId,
            focusedContextItemCountBefore: focusedContextItemCountBefore);

        return new TodoListView(
            ToRows(content.Lines),
            content.Lines,
            content.Scroll,
            content.VisibleItemCount,
            content.TotalItemCount,
            content.HasMoreAbove,
            content.HasMoreBelow);
    }

    public static IRenderable BuildLoadingView(DiscoveredSession session, int spinnerFrame, int? consoleWidth = null)
    {
        var contentWidth = GetContentWidth(consoleWidth);
        var rows = BuildHeaderRows(session, contentWidth);
        AddWrappedMarkup(rows, $"{GetLoadingSpinnerFrame(spinnerFrame)} Loading TODOs...", contentWidth, "yellow");
        rows.Add("");
        AddWrappedMarkup(rows, "Reading the session database. This can take a moment for large or busy sessions.", contentWidth, TimestampStyle);
        return ToRows(rows);
    }

    public static string GetLoadingSpinnerFrame(int spinnerFrame)
    {
        var index = spinnerFrame % LoadingSpinnerFrames.Length;
        if (index < 0)
        {
            index += LoadingSpinnerFrames.Length;
        }

        return LoadingSpinnerFrames[index];
    }

    public static ScrollMetrics CalculateScrollMetrics(int bodyLineCount, int headerLineCount, int footerLineCount, int? consoleHeight, int requestedOffset)
    {
        var pageSize = consoleHeight is null
            ? Math.Max(1, bodyLineCount)
            : Math.Max(1, consoleHeight.Value - headerLineCount - footerLineCount);
        var maxOffset = Math.Max(0, bodyLineCount - pageSize);
        var offset = Math.Clamp(requestedOffset, 0, maxOffset);
        return new ScrollMetrics(offset, maxOffset, pageSize, bodyLineCount);
    }

    public static ListLayoutContent BuildListLayoutContent(
        IReadOnlyList<string> headerRows,
        IReadOnlyList<ListLine> bodyRows,
        Func<ScrollMetrics?, IReadOnlyList<string>> buildFooterRows,
        int? consoleHeight,
        int scrollOffset,
        int totalItemCount,
        string? focusedItemId = null,
        int focusedContextItemCountBefore = 0)
    {
        var initialFooterRows = buildFooterRows(null);
        var scroll = CalculateScrollMetrics(bodyRows.Count, headerRows.Count, initialFooterRows.Count, consoleHeight, scrollOffset);
        if (focusedItemId is not null)
        {
            scroll = AdjustScrollForFocusedItem(
                bodyRows,
                headerRows.Count,
                initialFooterRows.Count,
                consoleHeight,
                scroll,
                focusedItemId,
                focusedContextItemCountBefore);
        }

        var visibleBodyRows = BuildVisibleBodyRows(bodyRows, scroll);
        var visibleItemCount = visibleBodyRows
            .Select(row => row.ItemId)
            .Where(id => id is not null)
            .Distinct(StringComparer.Ordinal)
            .Count();
        var footerRows = buildFooterRows(scroll);
        var rows = new List<string>(headerRows.Count + scroll.PageSize + footerRows.Count);
        rows.AddRange(headerRows);
        rows.AddRange(visibleBodyRows.Select(row => row.Markup));
        rows.AddRange(footerRows);

        return new ListLayoutContent(
            rows,
            scroll,
            visibleItemCount,
            totalItemCount,
            HasMoreAbove: scroll.Offset > 0,
            HasMoreBelow: scroll.Offset < scroll.MaxOffset);
    }

    private static List<string> BuildHeaderRows(DiscoveredSession session, int contentWidth)
    {
        var rows = new List<string>();
        rows.Add("");
        AddWrappedMarkup(rows, $"# TODOs for \"{GetSessionName(session)}\"", contentWidth, "bold");
        foreach (var line in FormatCwdLines(session, contentWidth))
        {
            rows.Add($"{Padding()}{line}");
        }

        rows.Add("");
        return rows;
    }

    private static List<ListLine> BuildBodyRows(
        TodoSnapshot snapshot,
        DateTimeOffset renderedAt,
        int contentWidth,
        TodoListDisplayState displayState)
    {
        var rows = new List<ListLine>();
        if (snapshot.State != TodoReadState.Available)
        {
            var color = snapshot.State == TodoReadState.Error ? "red" : TimestampStyle;
            AddWrappedBodyMarkup(rows, snapshot.Message, contentWidth, color, todoId: null);
            return rows;
        }

        var dependencyContext = CreateExpandedDependencyContext(snapshot, displayState.ExpandedTodoId);
        if (snapshot.Todos.Count == 0)
        {
            AddWrappedBodyMarkup(rows, TodoSnapshot.EmptyMessage, contentWidth, TimestampStyle, todoId: null);
        }
        else
        {
            foreach (var todo in snapshot.Todos)
            {
                var focused = displayState.ShowFocusMarker
                    && string.Equals(todo.Id, displayState.FocusedTodoId, StringComparison.Ordinal);
                var shouldDim = dependencyContext.ShouldDim(todo.Id);
                var todoLines = FormatTodoLines(
                    todo,
                    renderedAt,
                    contentWidth,
                    muteCompletedTodo: shouldDim,
                    styleOverride: shouldDim ? DependencyMutedTodoStyle : null,
                    timestampStyle: shouldDim ? DependencyMutedTodoStyle : TimestampStyle);

                for (var i = 0; i < todoLines.Count; i++)
                {
                    var padding = focused && i == 0 ? $"[white]›[/] " : Padding();
                    rows.Add(new ListLine($"{padding}{todoLines[i]}", todo.Id));
                }

                if (string.Equals(todo.Id, displayState.ExpandedTodoId, StringComparison.Ordinal))
                {
                    AddExpandedTodoRows(rows, todo, contentWidth, dependencyContext);
                }
            }
        }

        return rows;
    }

    private static void AddExpandedTodoRows(List<ListLine> rows, TodoItem todo, int contentWidth, ExpandedDependencyContext dependencyContext)
    {
        var detailRows = new List<ExpandedDetailRow>();
        if (!string.IsNullOrWhiteSpace(todo.Description))
        {
            AddExpandedDetail(detailRows, "description", todo.Description);
        }

        AddExpandedDetail(detailRows, "id", todo.Id);
        AddExpandedDetail(detailRows, "status", FormatStatusForDisplay(todo.Status));

        if (dependencyContext.Needs.Count > 0)
        {
            AddExpandedDetail(detailRows, "needs", string.Join(", ", dependencyContext.Needs));
        }

        if (dependencyContext.Blocks.Count > 0)
        {
            AddExpandedDetail(detailRows, "blocks", string.Join(", ", dependencyContext.Blocks));
        }

        foreach (var detail in SortExpandedDetails(detailRows))
        {
            AddWrappedDetailMarkup(rows, detail.Key, detail.Value, contentWidth, todo.Id);
        }
    }

    private static ExpandedDependencyContext CreateExpandedDependencyContext(TodoSnapshot snapshot, string? expandedTodoId)
    {
        if (snapshot.State != TodoReadState.Available || string.IsNullOrWhiteSpace(expandedTodoId))
        {
            return ExpandedDependencyContext.Empty;
        }

        var todosById = snapshot.Todos.ToDictionary(todo => todo.Id, StringComparer.Ordinal);
        if (!todosById.TryGetValue(expandedTodoId, out var expandedTodo))
        {
            return ExpandedDependencyContext.Empty;
        }

        var relatedIds = new HashSet<string>(StringComparer.Ordinal) { expandedTodoId };
        var needs = expandedTodo.Dependencies
            .Distinct(StringComparer.Ordinal)
            .Select(dependencyId =>
            {
                if (todosById.TryGetValue(dependencyId, out var dependency))
                {
                    relatedIds.Add(dependency.Id);
                    return FormatDependencyTitle(dependency);
                }

                return $"{dependencyId} (missing)";
            })
            .ToArray();
        var blocks = snapshot.Todos
            .Where(todo => todo.Dependencies.Contains(expandedTodoId, StringComparer.Ordinal))
            .Select(todo =>
            {
                relatedIds.Add(todo.Id);
                return todo.Title;
            })
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new ExpandedDependencyContext(needs, blocks, relatedIds);
    }

    private static void AddExpandedDetail(List<ExpandedDetailRow> rows, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        rows.Add(new ExpandedDetailRow(key, value.Trim(), rows.Count));
    }

    private static string FormatDependencyTitle(TodoItem todo) =>
        todo.Status == "done" ? AddStrikethrough(todo.Title) : todo.Title;

    private static string AddStrikethrough(string value)
    {
        const char strike = '\u0336';
        var builder = new StringBuilder(value.Length * 2);
        foreach (var rune in value.EnumerateRunes())
        {
            builder.Append(rune);
            builder.Append(strike);
        }

        return builder.ToString();
    }

    private static IEnumerable<ExpandedDetailRow> SortExpandedDetails(IEnumerable<ExpandedDetailRow> rows) =>
        rows
            .Select(row => (Row: row, Order: ExpandedDetailKeyOrder.TryGetValue(row.Key, out var order) ? order : int.MaxValue))
            .OrderBy(item => item.Order)
            .ThenBy(item => item.Order == int.MaxValue ? item.Row.Key : "", StringComparer.Ordinal)
            .ThenBy(item => item.Row.Sequence)
            .Select(item => item.Row);

    private static ListLine[] BuildVisibleBodyRows(IReadOnlyList<ListLine> bodyRows, ScrollMetrics scroll)
    {
        var hasMoreAbove = scroll.Offset > 0;
        var hasMoreBelow = scroll.Offset < scroll.MaxOffset;
        var showAboveMarker = hasMoreAbove;
        var showBelowMarker = hasMoreBelow;
        if (showAboveMarker && showBelowMarker && scroll.PageSize < 3)
        {
            showAboveMarker = false;
            showBelowMarker = false;
        }
        else if ((showAboveMarker || showBelowMarker) && scroll.PageSize < 2)
        {
            showAboveMarker = false;
            showBelowMarker = false;
        }

        var markerCount = (showAboveMarker ? 1 : 0) + (showBelowMarker ? 1 : 0);
        var contentCapacity = Math.Max(1, scroll.PageSize - markerCount);
        var start = hasMoreBelow
            ? scroll.Offset
            : Math.Max(0, bodyRows.Count - contentCapacity);

        var rows = new List<ListLine>(scroll.PageSize);
        if (showAboveMarker)
        {
            rows.Add(CreateScrollHint("⋯ more above (PgUp)"));
        }

        rows.AddRange(bodyRows.Skip(start).Take(contentCapacity));

        if (showBelowMarker)
        {
            rows.Add(CreateScrollHint("⋯ more below (PgDn)"));
        }

        return rows.ToArray();
    }

    private static ListLine CreateScrollHint(string text) =>
        new($"{Padding()}[{TimestampStyle}]{Markup.Escape(text)}[/]", ItemId: null);

    private static ScrollMetrics AdjustScrollForFocusedItem(
        IReadOnlyList<ListLine> bodyRows,
        int headerLineCount,
        int footerLineCount,
        int? consoleHeight,
        ScrollMetrics currentScroll,
        string focusedItemId,
        int focusedContextItemCountBefore)
    {
        if (focusedContextItemCountBefore <= 0
            && VisibleRowsContain(BuildVisibleBodyRows(bodyRows, currentScroll), focusedItemId))
        {
            return currentScroll;
        }

        var focusedLineIndex = FirstLineIndexOf(bodyRows, focusedItemId);
        if (focusedLineIndex < 0)
        {
            return currentScroll;
        }

        var requestedOffset = focusedContextItemCountBefore <= 0
            ? focusedLineIndex
            : GetContextStartLineIndex(bodyRows, focusedItemId, focusedContextItemCountBefore);
        var minimumOffsetForFocusedLine = Math.Max(0, focusedLineIndex - currentScroll.PageSize + 1);
        requestedOffset = Math.Clamp(requestedOffset, minimumOffsetForFocusedLine, focusedLineIndex);

        var adjusted = CalculateScrollMetrics(bodyRows.Count, headerLineCount, footerLineCount, consoleHeight, requestedOffset);
        if (!VisibleRowsContain(BuildVisibleBodyRows(bodyRows, adjusted), focusedItemId))
        {
            adjusted = CalculateScrollMetrics(bodyRows.Count, headerLineCount, footerLineCount, consoleHeight, focusedLineIndex);
        }

        return adjusted;
    }

    private static int GetContextStartLineIndex(IReadOnlyList<ListLine> bodyRows, string focusedItemId, int itemCountBeforeFocused)
    {
        var itemStarts = GetItemStartLines(bodyRows);
        var focusedItemIndex = itemStarts.FindIndex(item => string.Equals(item.Id, focusedItemId, StringComparison.Ordinal));
        if (focusedItemIndex < 0)
        {
            return FirstLineIndexOf(bodyRows, focusedItemId);
        }

        var contextItemIndex = Math.Max(0, focusedItemIndex - itemCountBeforeFocused);
        return itemStarts[contextItemIndex].FirstLineIndex;
    }

    private static List<ItemStart> GetItemStartLines(IReadOnlyList<ListLine> bodyRows)
    {
        var items = new List<ItemStart>();
        string? previousItemId = null;
        for (var i = 0; i < bodyRows.Count; i++)
        {
            var itemId = bodyRows[i].ItemId;
            if (itemId is null || string.Equals(itemId, previousItemId, StringComparison.Ordinal))
            {
                continue;
            }

            items.Add(new ItemStart(itemId, i));
            previousItemId = itemId;
        }

        return items;
    }

    private static List<string> BuildFooterRows(int contentWidth, ScrollMetrics? scroll)
    {
        var rows = new List<string>();
        rows.Add("");
        var text = scroll is { CanScroll: true } scrollMetrics
            ? $"{FormatFooterStatus(scrollMetrics)} · ↑↓ focus · enter expand · r refresh · ctrl+x sessions · q quit"
            : "↑↓ focus · enter expand · r refresh · ctrl+x sessions · q quit";
        AddWrappedMarkup(rows, text, contentWidth, TimestampStyle);
        rows.Add("");
        return rows;
    }

    public static string FormatFooterStatus(ScrollMetrics scroll) =>
        $"page {scroll.CurrentPage}/{scroll.TotalPages}";

    public static string GetSessionName(DiscoveredSession session)
    {
        var name = FirstNonEmpty(
            session.Metadata?.Summary,
            session.Metadata?.Repository,
            Path.GetFileName(session.DisplayCwd),
            ShortId(session.Registry.SessionId));

        return name ?? ShortId(session.Registry.SessionId);
    }

    public static string FormatTodoLine(TodoItem todo, DateTimeOffset now)
    {
        return string.Join(Environment.NewLine, FormatTodoLines(todo, now, DefaultContentWidth));
    }

    public static IReadOnlyList<string> FormatTodoLines(TodoItem todo, DateTimeOffset now, int contentWidth)
    {
        return FormatTodoLines(todo, now, contentWidth, muteCompletedTodo: false);
    }

    public static IReadOnlyList<string> FormatTodoLines(TodoItem todo, DateTimeOffset now, int contentWidth, bool muteCompletedTodo)
    {
        return FormatTodoLines(todo, now, contentWidth, muteCompletedTodo, styleOverride: null, timestampStyle: TimestampStyle);
    }

    private static IReadOnlyList<string> FormatTodoLines(
        TodoItem todo,
        DateTimeOffset now,
        int contentWidth,
        bool muteCompletedTodo,
        string? styleOverride,
        string timestampStyle)
    {
        return todo.Status switch
        {
            "done" => FormatCompletedTodoLines(todo, now, contentWidth, muteCompletedTodo, styleOverride, timestampStyle),
            "in_progress" => FormatInProgressTodoLines(todo, now, contentWidth, styleOverride, timestampStyle),
            "blocked" => FormatBlockedTodoLines(todo, now, contentWidth, styleOverride, timestampStyle),
            _ => FormatPendingTodoLines(todo, now, contentWidth, styleOverride, timestampStyle)
        };
    }

    public static IReadOnlyList<string> FormatCwdLines(DiscoveredSession session, int contentWidth)
    {
        var cwd = session.DisplayCwd;
        if (string.IsNullOrWhiteSpace(cwd))
        {
            return [];
        }

        return WrapText(AbbreviateHomePath(cwd.Trim()), contentWidth, contentWidth)
            .Select(line => $"[{TimestampStyle}]{Markup.Escape(line)}[/]")
            .ToArray();
    }

    public static string AbbreviateHomePath(string path, string? homeDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        var home = string.IsNullOrWhiteSpace(homeDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : homeDirectory;
        if (string.IsNullOrWhiteSpace(home))
        {
            return path;
        }

        var trimmedPath = path.Trim();
        var trimmedHome = home.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (trimmedPath.Length == trimmedHome.Length
            && string.Equals(trimmedPath, trimmedHome, GetPathComparison()))
        {
            return "~";
        }

        if (trimmedPath.Length > trimmedHome.Length
            && string.Equals(trimmedPath[..trimmedHome.Length], trimmedHome, GetPathComparison())
            && IsDirectorySeparator(trimmedPath[trimmedHome.Length]))
        {
            return $"~{trimmedPath[trimmedHome.Length..]}";
        }

        return path;
    }

    public static string CreateCompletedTimestampKey(TodoSnapshot snapshot, DateTimeOffset now)
    {
        if (snapshot.State != TodoReadState.Available)
        {
            return "";
        }

        return string.Join(
            '\u001e',
            snapshot.Todos
                .Where(todo => todo.Status == "done")
                .Select(todo => FormatDoneTimestamp(todo.UpdatedAt, now) ?? ""));
    }

    public static bool HasCompletedTimestamps(TodoSnapshot snapshot) =>
        snapshot.State == TodoReadState.Available
        && snapshot.Todos.Any(todo => todo.Status == "done" && CanParseTimestamp(todo.UpdatedAt));

    public static string CreateTimestampKey(TodoSnapshot snapshot, DateTimeOffset now)
    {
        if (snapshot.State != TodoReadState.Available)
        {
            return "";
        }

        return string.Join(
            '\u001e',
            snapshot.Todos.Select(todo => todo.Status switch
            {
                "done" => FormatTodoTimestamp(todo.UpdatedAt, now) ?? "",
                "in_progress" => FormatStartedTimestamp(todo.UpdatedAt, now) ?? "",
                _ => FormatAddedTimestamp(todo.CreatedAt, now) ?? ""
            }));
    }

    public static bool HasDisplayedTimestamps(TodoSnapshot snapshot) =>
        snapshot.State == TodoReadState.Available
        && snapshot.Todos.Any(todo => todo.Status switch
        {
            "done" => CanParseTimestamp(todo.UpdatedAt),
            "in_progress" => CanParseTimestamp(todo.UpdatedAt),
            _ => CanParseTimestamp(todo.CreatedAt)
        });

    public static IReadOnlyList<IReadOnlyList<TodoItem>> CreateTodoBatches(IReadOnlyList<TodoItem> todos)
    {
        if (todos.Count == 0)
        {
            return [];
        }

        var batches = new List<IReadOnlyList<TodoItem>>();
        var currentBatch = new List<TodoItem> { todos[0] };
        var previousCreatedAt = TryGetCreatedAt(todos[0]);

        for (var i = 1; i < todos.Count; i++)
        {
            var todo = todos[i];
            var createdAt = TryGetCreatedAt(todo);
            if (ShouldStartNewBatch(previousCreatedAt, createdAt))
            {
                batches.Add(currentBatch);
                currentBatch = [];
            }

            currentBatch.Add(todo);
            previousCreatedAt = createdAt;
        }

        batches.Add(currentBatch);
        return batches;
    }

    private static IReadOnlyList<string> FormatCompletedTodoLines(
        TodoItem todo,
        DateTimeOffset now,
        int contentWidth,
        bool muted,
        string? styleOverride,
        string timestampStyle)
    {
        var style = styleOverride ?? (!muted && IsFreshTimestamp(todo.UpdatedAt, now)
            ? "bold green"
            : MutedTodoStyle);
        var lines = FormatStyledTodoLines("[✓]", todo.Title, contentWidth, style).ToList();
        return AppendStyledSuffix(lines, FormatDoneTimestamp(todo.UpdatedAt, now), contentWidth, timestampStyle);
    }

    private static IReadOnlyList<string> FormatInProgressTodoLines(
        TodoItem todo,
        DateTimeOffset now,
        int contentWidth,
        string? styleOverride,
        string timestampStyle)
    {
        var lines = FormatStyledTodoLines("[•]", todo.Title, contentWidth, styleOverride ?? "yellow").ToList();
        return AppendStyledSuffix(lines, FormatStartedTimestamp(todo.UpdatedAt, now), contentWidth, timestampStyle);
    }

    private static IReadOnlyList<string> FormatPendingTodoLines(
        TodoItem todo,
        DateTimeOffset now,
        int contentWidth,
        string? styleOverride,
        string timestampStyle)
    {
        var lines = FormatStyledTodoLines("[ ]", todo.Title, contentWidth, styleOverride).ToList();
        return AppendStyledSuffix(lines, FormatAddedTimestamp(todo.CreatedAt, now), contentWidth, timestampStyle);
    }

    private static IReadOnlyList<string> FormatBlockedTodoLines(
        TodoItem todo,
        DateTimeOffset now,
        int contentWidth,
        string? styleOverride,
        string timestampStyle)
    {
        var lines = FormatStyledTodoLines("[⊘]", todo.Title, contentWidth, styleOverride ?? BlockedTodoStyle).ToList();
        return AppendStyledSuffix(lines, FormatAddedTimestamp(todo.CreatedAt, now), contentWidth, timestampStyle);
    }

    private static string FormatStatusForDisplay(string status) => status;

    private static IReadOnlyList<string> AppendStyledSuffix(List<string> lines, string? suffix, int contentWidth, string style)
    {
        if (suffix is null || lines.Count == 0)
        {
            return lines;
        }

        var lastLinePlain = RemoveMarkupForLength(lines[^1]);
        if (DisplayLength(lastLinePlain) + 1 + suffix.Length <= contentWidth)
        {
            var escapedSuffix = $"[{style}]{Markup.Escape(suffix)}[/]";
            lines[^1] = $"{lines[^1]} {escapedSuffix}";
        }
        else
        {
            var suffixWidth = Math.Max(1, contentWidth - DisplayLength(ContinuationIndent));
            foreach (var line in WrapText(suffix, suffixWidth, suffixWidth))
            {
                lines.Add($"{ContinuationIndent}[{style}]{Markup.Escape(line)}[/]");
            }
        }

        return lines;
    }

    private static string? FormatTodoTimestamp(string? value, DateTimeOffset now)
    {
        if (!TryParseTimestamp(value, out var completedAt))
        {
            return null;
        }

        return FormatTimestamp(completedAt, now);
    }

    private static string FormatTimestamp(DateTimeOffset timestamp, DateTimeOffset now)
    {
        var localCompletedAt = timestamp.ToLocalTime();
        var elapsed = now.ToLocalTime() - localCompletedAt;
        var relative = FormatRelativeAge(elapsed);
        return $"{relative} ⋅ {localCompletedAt:HH:mm}";
    }

    private static string? FormatStartedTimestamp(string? value, DateTimeOffset now)
    {
        var timestamp = FormatTodoTimestamp(value, now);
        return timestamp is null ? null : $"started {timestamp}";
    }

    private static string? FormatAddedTimestamp(string? value, DateTimeOffset now)
    {
        var timestamp = FormatTodoTimestamp(value, now);
        return timestamp is null ? null : $"added {timestamp}";
    }

    private static string? FormatDoneTimestamp(string? value, DateTimeOffset now)
    {
        var timestamp = FormatTodoTimestamp(value, now);
        return timestamp is null ? null : $"done {timestamp}";
    }

    private static DateTimeOffset? TryGetCreatedAt(TodoItem todo) =>
        TryParseTimestamp(todo.CreatedAt, out var createdAt) ? createdAt : null;

    private static bool ShouldStartNewBatch(DateTimeOffset? previousCreatedAt, DateTimeOffset? createdAt)
    {
        if (previousCreatedAt is null || createdAt is null)
        {
            return false;
        }

        return (previousCreatedAt.Value - createdAt.Value).Duration() > TodoBatchWindow;
    }

    private static bool CanParseTimestamp(string? value) =>
        TryParseTimestamp(value, out _);

    private static bool IsFreshTimestamp(string? value, DateTimeOffset now)
    {
        if (!TryParseTimestamp(value, out var timestamp))
        {
            return false;
        }

        return now.ToLocalTime() - timestamp.ToLocalTime() <= FreshCompletionWindow;
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
        if (elapsed < JustNowWindow)
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

    private static string ShortId(string id) => id.Length <= 8 ? id : id[..8];

    private static IReadOnlyList<string> FormatStyledTodoLines(string badge, string title, int contentWidth, string? style)
    {
        var badgePrefix = $"{badge} ";
        var firstWidth = Math.Max(1, contentWidth - DisplayLength(badgePrefix));
        var continuationWidth = Math.Max(1, contentWidth - DisplayLength(ContinuationIndent));
        var wrappedTitle = WrapText(title, firstWidth, continuationWidth);
        var lines = new List<string>(wrappedTitle.Count);

        for (var i = 0; i < wrappedTitle.Count; i++)
        {
            var prefix = i == 0 ? badgePrefix : ContinuationIndent;
            var text = $"{prefix}{wrappedTitle[i]}";
            var escaped = Markup.Escape(text);
            lines.Add(style is null ? escaped : $"[{style}]{escaped}[/]");
        }

        return lines;
    }

    public static IReadOnlyList<string> WrapText(string text, int firstWidth, int continuationWidth)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [""];
        }

        var remaining = text;
        var width = Math.Max(1, firstWidth);
        var lines = new List<string>();
        while (remaining.Length > 0)
        {
            if (DisplayWidth(remaining) <= width)
            {
                lines.Add(remaining);
                break;
            }

            var breakAt = FindWrapPoint(remaining, width);
            lines.Add(remaining[..breakAt].TrimEnd());
            remaining = remaining[breakAt..].TrimStart();
            width = Math.Max(1, continuationWidth);
        }

        return lines.Count == 0 ? [""] : lines;
    }

    private static int FindWrapPoint(string text, int width)
    {
        var maxWidth = Math.Max(1, width);
        var displayWidth = 0;
        var lastWhitespace = 0;
        var lastSafeBreak = 0;
        for (var i = 0; i < text.Length;)
        {
            var status = Rune.DecodeFromUtf16(text.AsSpan(i), out var rune, out var consumed);
            if (status != OperationStatus.Done)
            {
                consumed = 1;
            }

            var runeWidth = GetRuneDisplayWidth(rune);
            if (displayWidth + runeWidth > maxWidth)
            {
                return lastWhitespace > 0
                    ? lastWhitespace
                    : Math.Max(lastSafeBreak, i + consumed);
            }

            displayWidth += runeWidth;
            lastSafeBreak = i + consumed;
            if (rune.Value <= char.MaxValue && char.IsWhiteSpace((char)rune.Value))
            {
                lastWhitespace = i + consumed;
            }

            i += consumed;
        }

        return text.Length;
    }

    private static void AddWrappedMarkup(List<string> rows, string text, int contentWidth, string style)
    {
        foreach (var line in WrapText(text, contentWidth, contentWidth))
        {
            rows.Add($"{Padding()}[{style}]{Markup.Escape(line)}[/]");
        }
    }

    private static void AddWrappedBodyMarkup(List<ListLine> rows, string text, int contentWidth, string style, string? todoId)
    {
        foreach (var line in WrapText(text, contentWidth, contentWidth))
        {
            rows.Add(new ListLine($"{Padding()}[{style}]{Markup.Escape(line)}[/]", todoId));
        }
    }

    private static void AddWrappedDetailMarkup(List<ListLine> rows, string key, string value, int contentWidth, string todoId)
    {
        var label = $"{key}: ";
        var firstValueWidth = Math.Max(1, contentWidth - DisplayLength(ContinuationIndent) - DisplayLength(label));
        var continuationValueWidth = Math.Max(1, contentWidth - DisplayLength(ContinuationIndent) - DisplayLength(DetailValueContinuationIndent));
        var valueLines = WrapText(value, firstValueWidth, continuationValueWidth);
        for (var i = 0; i < valueLines.Count; i++)
        {
            var line = i == 0
                ? $"{Padding()}{ContinuationIndent}[{TimestampStyle}]{Markup.Escape(key)}:[/] {Markup.Escape(valueLines[i])}"
                : $"{Padding()}{ContinuationIndent}{DetailValueContinuationIndent}{Markup.Escape(valueLines[i])}";
            rows.Add(new ListLine(line, todoId));
        }
    }

    public static int GetContentWidth(int? consoleWidth)
    {
        var width = consoleWidth ?? GetConsoleWidth();
        return Math.Max(MinimumContentWidth, width - HorizontalPadding * 2);
    }

    private static int GetConsoleWidth()
    {
        try
        {
            return Console.WindowWidth;
        }
        catch (IOException)
        {
            return DefaultContentWidth + HorizontalPadding * 2;
        }
    }

    public static string Padding() => new(' ', HorizontalPadding);

    public static int DisplayWidth(string text)
    {
        var width = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            width += GetRuneDisplayWidth(rune);
        }

        return width;
    }

    private static int DisplayLength(string text) => DisplayWidth(text);

    private static int GetRuneDisplayWidth(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);
        if (category is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.EnclosingMark
            or UnicodeCategory.Control
            or UnicodeCategory.Format)
        {
            return 0;
        }

        return IsWideRune(rune.Value) ? 2 : 1;
    }

    private static bool IsWideRune(int value) =>
        value is >= 0x1100 and <= 0x115F
            or >= 0x2329 and <= 0x232A
            or >= 0x2E80 and <= 0xA4CF
            or >= 0xAC00 and <= 0xD7A3
            or >= 0xF900 and <= 0xFAFF
            or >= 0xFE10 and <= 0xFE19
            or >= 0xFE30 and <= 0xFE6F
            or >= 0xFF00 and <= 0xFF60
            or >= 0xFFE0 and <= 0xFFE6
            or >= 0x1F300 and <= 0x1FAFF
            or >= 0x20000 and <= 0x3FFFD;

    private static bool IsDirectorySeparator(char value) =>
        value == Path.DirectorySeparatorChar || value == Path.AltDirectorySeparatorChar;

    private static StringComparison GetPathComparison() =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string RemoveMarkupForLength(string markup)
    {
        var builder = new StringBuilder(markup.Length);
        for (var i = 0; i < markup.Length;)
        {
            if (markup[i] == '[')
            {
                if (i + 1 < markup.Length && markup[i + 1] == '[')
                {
                    builder.Append('[');
                    i += 2;
                    continue;
                }

                var tagEnd = markup.IndexOf(']', i + 1);
                if (tagEnd >= 0)
                {
                    i = tagEnd + 1;
                    continue;
                }
            }
            else if (markup[i] == ']' && i + 1 < markup.Length && markup[i + 1] == ']')
            {
                builder.Append(']');
                i += 2;
                continue;
            }

            builder.Append(markup[i]);
            i++;
        }

        return builder.ToString();
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string? JoinPresent(params string?[] values)
    {
        var present = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        return present.Length == 0 ? null : string.Join(" · ", present);
    }

    private static bool VisibleRowsContain(IReadOnlyList<ListLine> rows, string itemId) =>
        rows.Any(row => string.Equals(row.ItemId, itemId, StringComparison.Ordinal));

    private static int FirstLineIndexOf(IReadOnlyList<ListLine> rows, string itemId)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (string.Equals(rows[i].ItemId, itemId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    public static Rows ToRows(IEnumerable<string> lines) =>
        new(lines.Select<string, IRenderable>(line => string.IsNullOrEmpty(line) ? Text.Empty : new Markup(line)));

    public sealed record ListLine(string Markup, string? ItemId);

    private sealed record ExpandedDetailRow(string Key, string Value, int Sequence);

    private sealed record ItemStart(string Id, int FirstLineIndex);

    private sealed record ExpandedDependencyContext(
        IReadOnlyList<string> Needs,
        IReadOnlyList<string> Blocks,
        IReadOnlySet<string> RelatedIds)
    {
        public static ExpandedDependencyContext Empty { get; } = new([], [], new HashSet<string>(StringComparer.Ordinal));

        public bool ShouldDim(string todoId) =>
            RelatedIds.Count > 0 && !RelatedIds.Contains(todoId);
    }

    public sealed record ListLayoutContent(
        IReadOnlyList<string> Lines,
        ScrollMetrics Scroll,
        int VisibleItemCount,
        int TotalItemCount,
        bool HasMoreAbove,
        bool HasMoreBelow);

    public sealed record TodoListView(
        IRenderable Renderable,
        IReadOnlyList<string> Lines,
        ScrollMetrics Scroll,
        int VisibleTodoCount,
        int TotalTodoCount,
        bool HasMoreAbove,
        bool HasMoreBelow);

    public readonly record struct ScrollMetrics(int Offset, int MaxOffset, int PageSize, int TotalLines)
    {
        public bool CanScroll => MaxOffset > 0;

        public int TotalPages => TotalLines == 0
            ? 1
            : Math.Max(1, (int)Math.Ceiling((double)TotalLines / PageSize));

        public int CurrentPage => TotalLines == 0
            ? 1
            : Offset >= MaxOffset && MaxOffset > 0
                ? TotalPages
                : Math.Min(TotalPages, Offset / PageSize + 1);
    }
}
