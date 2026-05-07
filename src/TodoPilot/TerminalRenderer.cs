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
    private const string TimestampStyle = "grey";
    private const string MutedTodoStyle = "dim";
    private static readonly TimeSpan FreshCompletionWindow = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan TodoBatchWindow = TimeSpan.FromSeconds(5);
    private static readonly string[] LoadingSpinnerFrames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    public static IRenderable BuildTodoList(
        DiscoveredSession session,
        TodoSnapshot snapshot,
        DateTimeOffset? now,
        int? consoleWidth = null,
        int? consoleHeight = null,
        int scrollOffset = 0)
    {
        return BuildTodoListView(session, snapshot, now, consoleWidth, consoleHeight, scrollOffset).Renderable;
    }

    public static TodoListView BuildTodoListView(
        DiscoveredSession session,
        TodoSnapshot snapshot,
        DateTimeOffset? now,
        int? consoleWidth = null,
        int? consoleHeight = null,
        int scrollOffset = 0)
    {
        var renderedAt = now ?? DateTimeOffset.Now;
        var contentWidth = GetContentWidth(consoleWidth);
        var headerRows = BuildHeaderRows(session, contentWidth);
        var bodyRows = BuildBodyRows(snapshot, renderedAt, contentWidth);
        var content = BuildListLayoutContent(
            headerRows,
            bodyRows,
            scroll => BuildFooterRows(contentWidth, scroll),
            consoleHeight,
            scrollOffset,
            totalItemCount: snapshot.Todos.Count);

        return new TodoListView(
            ToRows(content.Lines),
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
        string? focusedItemId = null)
    {
        var initialFooterRows = buildFooterRows(null);
        var scroll = CalculateScrollMetrics(bodyRows.Count, headerRows.Count, initialFooterRows.Count, consoleHeight, scrollOffset);
        if (focusedItemId is not null && !VisibleRowsContain(BuildVisibleBodyRows(bodyRows, scroll), focusedItemId))
        {
            var focusedLineIndex = FirstLineIndexOf(bodyRows, focusedItemId);
            if (focusedLineIndex >= 0)
            {
                scroll = CalculateScrollMetrics(bodyRows.Count, headerRows.Count, initialFooterRows.Count, consoleHeight, focusedLineIndex);
            }
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
        int contentWidth)
    {
        var rows = new List<ListLine>();
        if (snapshot.State != TodoReadState.Available)
        {
            var color = snapshot.State == TodoReadState.Error ? "red" : TimestampStyle;
            AddWrappedBodyMarkup(rows, snapshot.Message, contentWidth, color, todoId: null);
            return rows;
        }

        if (snapshot.Todos.Count == 0)
        {
            AddWrappedBodyMarkup(rows, TodoSnapshot.EmptyMessage, contentWidth, TimestampStyle, todoId: null);
        }
        else
        {
            foreach (var todo in snapshot.Todos)
            {
                foreach (var line in FormatTodoLines(todo, renderedAt, contentWidth))
                {
                    rows.Add(new ListLine($"{Padding()}{line}", todo.Id));
                }
            }
        }

        return rows;
    }

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

    private static List<string> BuildFooterRows(int contentWidth, ScrollMetrics? scroll)
    {
        var rows = new List<string>();
        rows.Add("");
        var text = scroll is { CanScroll: true } scrollMetrics
            ? $"{FormatFooterStatus(scrollMetrics)} · r refresh · q quit"
            : "r refresh · q quit";
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
        return todo.Status switch
        {
            "done" => FormatCompletedTodoLines(todo, now, contentWidth, muteCompletedTodo),
            "in_progress" => FormatInProgressTodoLines(todo, now, contentWidth),
            "blocked" => FormatBlockedTodoLines(todo, now, contentWidth),
            _ => FormatPendingTodoLines(todo, now, contentWidth)
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

    private static IReadOnlyList<string> FormatCompletedTodoLines(TodoItem todo, DateTimeOffset now, int contentWidth, bool muted)
    {
        var style = !muted && IsFreshTimestamp(todo.UpdatedAt, now)
            ? "bold green"
            : MutedTodoStyle;
        var lines = FormatStyledTodoLines("[✓]", todo.Title, contentWidth, style).ToList();
        return AppendGraySuffix(lines, FormatDoneTimestamp(todo.UpdatedAt, now), contentWidth);
    }

    private static IReadOnlyList<string> FormatInProgressTodoLines(TodoItem todo, DateTimeOffset now, int contentWidth)
    {
        var lines = FormatStyledTodoLines("[•]", todo.Title, contentWidth, "yellow").ToList();
        return AppendGraySuffix(lines, FormatStartedTimestamp(todo.UpdatedAt, now), contentWidth);
    }

    private static IReadOnlyList<string> FormatBlockedTodoLines(TodoItem todo, DateTimeOffset now, int contentWidth)
    {
        var lines = FormatStyledTodoLines("[!]", todo.Title, contentWidth, "red").ToList();
        return AppendGraySuffix(lines, FormatAddedTimestamp(todo.CreatedAt, now), contentWidth);
    }

    private static IReadOnlyList<string> FormatPendingTodoLines(TodoItem todo, DateTimeOffset now, int contentWidth)
    {
        var lines = FormatStyledTodoLines("[ ]", todo.Title, contentWidth, style: null).ToList();
        return AppendGraySuffix(lines, FormatAddedTimestamp(todo.CreatedAt, now), contentWidth);
    }

    private static IReadOnlyList<string> AppendGraySuffix(List<string> lines, string? suffix, int contentWidth)
    {
        if (suffix is null || lines.Count == 0)
        {
            return lines;
        }

        var lastLinePlain = RemoveMarkupForLength(lines[^1]);
        if (DisplayLength(lastLinePlain) + 1 + suffix.Length <= contentWidth)
        {
            var escapedSuffix = $"[{TimestampStyle}]{Markup.Escape(suffix)}[/]";
            lines[^1] = $"{lines[^1]} {escapedSuffix}";
        }
        else
        {
            var suffixWidth = Math.Max(1, contentWidth - DisplayLength(ContinuationIndent));
            foreach (var line in WrapText(suffix, suffixWidth, suffixWidth))
            {
                lines.Add($"{ContinuationIndent}[{TimestampStyle}]{Markup.Escape(line)}[/]");
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

        return now.ToLocalTime() - timestamp.ToLocalTime() < FreshCompletionWindow;
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
        if (elapsed < FreshCompletionWindow)
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
            if (remaining.Length <= width)
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
        var max = Math.Min(text.Length, Math.Max(1, width));
        for (var i = max; i > 0; i--)
        {
            if (char.IsWhiteSpace(text[i - 1]))
            {
                return i;
            }
        }

        return max;
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

    private static int DisplayLength(string text) => text.Length;

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

    public sealed record ListLayoutContent(
        IReadOnlyList<string> Lines,
        ScrollMetrics Scroll,
        int VisibleItemCount,
        int TotalItemCount,
        bool HasMoreAbove,
        bool HasMoreBelow);

    public sealed record TodoListView(
        IRenderable Renderable,
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
