namespace TodoPilot.Tests;

public sealed class TerminalRendererTests
{
    private static string LocalClock(string timestamp) =>
        DateTimeOffset.Parse(timestamp).ToLocalTime().ToString("HH:mm");

    [Fact]
    public void BuildTodoList_DoesNotTreatStatusBadgesAsMarkupTags()
    {
        var session = new DiscoveredSession(
            new SessionRegistryEntry { SessionId = "11111111-1111-1111-1111-111111111111" },
            IsStale: false,
            HasSessionDatabase: true,
            Metadata: new SessionMetadata(
                "11111111-1111-1111-1111-111111111111",
                Cwd: null,
                Repository: null,
                Branch: null,
                Summary: "Test Session",
                CreatedAt: null,
                UpdatedAt: null));

        var snapshot = new TodoSnapshot(
            TodoReadState.Available,
            [
                new TodoItem("done", "Completed todo", "done", null, null, "2026-05-06T12:49:00+02:00", []),
                new TodoItem("current", "Current todo", "in_progress", null, null, null, []),
                new TodoItem("blocked", "Blocked todo", "blocked", null, null, null, []),
                new TodoItem("pending", "Next todo", "pending", null, null, null, [])
            ],
            "hash",
            "4 todo(s)");

        var exception = Record.Exception(() => TerminalRenderer.BuildTodoList(session, snapshot, DateTimeOffset.Parse("2026-05-06T12:54:00+02:00")));

        Assert.Null(exception);
    }

    [Fact]
    public void FormatTodoLine_AppendsGrayCompletedTimestampOutsideGreenText()
    {
        const string updatedAt = "2026-05-06T12:49:00+02:00";
        var todo = new TodoItem(
            "done",
            "Completed todo",
            "done",
            Description: null,
            CreatedAt: null,
            UpdatedAt: updatedAt,
            Dependencies: []);
        var expectedTime = LocalClock(updatedAt);

        var line = TerminalRenderer.FormatTodoLine(todo, DateTimeOffset.Parse("2026-05-06T12:54:00+02:00"));

        Assert.Contains("[green]", line, StringComparison.Ordinal);
        Assert.Contains("[/]", line, StringComparison.Ordinal);
        Assert.Contains($"[grey]done 5m ago ⋅ {expectedTime}[/]", line, StringComparison.Ordinal);
        Assert.Matches($@"\[/\]\s\[grey\]done 5m ago ⋅ {expectedTime}\[/\]$", line);
    }

    [Fact]
    public void FormatTodoLine_UsesJustNowAndBoldGreenForFirstMinuteAfterCompletedTodo()
    {
        const string updatedAt = "2026-05-06T12:48:15+02:00";
        var todo = new TodoItem(
            "done",
            "Completed todo",
            "done",
            Description: null,
            CreatedAt: null,
            UpdatedAt: updatedAt,
            Dependencies: []);
        var expectedTime = LocalClock(updatedAt);

        var line = TerminalRenderer.FormatTodoLine(todo, DateTimeOffset.Parse("2026-05-06T12:49:10+02:00"));

        Assert.Contains("[bold green]", line, StringComparison.Ordinal);
        Assert.Contains($"[grey]done just now ⋅ {expectedTime}[/]", line, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildTodoList_DoesNotTreatFreshCompletedStyleAsInvalidMarkup()
    {
        var session = CreateSession(metadataCwd: null, registryCwd: null);
        var snapshot = new TodoSnapshot(
            TodoReadState.Available,
            [new TodoItem("done", "Freshly completed", "done", null, null, "2026-05-06T12:48:15+02:00", [])],
            "hash",
            "1 todo(s)");

        var exception = Record.Exception(() =>
            TerminalRenderer.BuildTodoList(session, snapshot, DateTimeOffset.Parse("2026-05-06T12:49:10+02:00")));

        Assert.Null(exception);
    }

    [Fact]
    public void FormatTodoLine_OmitsCompletedTimestampWhenUpdatedAtIsMissing()
    {
        var todo = new TodoItem(
            "done",
            "Completed todo",
            "done",
            Description: null,
            CreatedAt: null,
            UpdatedAt: null,
            Dependencies: []);

        var line = TerminalRenderer.FormatTodoLine(todo, DateTimeOffset.Parse("2026-05-06T12:54:00+02:00"));

        Assert.DoesNotContain("[grey]", line, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateCompletedTimestampKey_ChangesAsRelativeTimestampChanges()
    {
        var snapshot = new TodoSnapshot(
            TodoReadState.Available,
            [new TodoItem("done", "Completed todo", "done", null, null, "2026-05-06T12:49:00+02:00", [])],
            "hash",
            "1 todo(s)");

        var first = TerminalRenderer.CreateCompletedTimestampKey(snapshot, DateTimeOffset.Parse("2026-05-06T12:49:10+02:00"));
        var second = TerminalRenderer.CreateCompletedTimestampKey(snapshot, DateTimeOffset.Parse("2026-05-06T12:54:00+02:00"));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void HasCompletedTimestamps_RequiresCompletedTodoWithParsableUpdatedAt()
    {
        var snapshot = new TodoSnapshot(
            TodoReadState.Available,
            [
                new TodoItem("done", "Completed todo", "done", null, null, "2026-05-06T12:49:00+02:00", []),
                new TodoItem("pending", "Pending todo", "pending", null, null, null, [])
            ],
            "hash",
            "2 todo(s)");

        Assert.True(TerminalRenderer.HasCompletedTimestamps(snapshot));
    }

    [Fact]
    public void FormatTodoLine_TreatsTimestampWithoutOffsetAsUtc()
    {
        var todo = new TodoItem(
            "done",
            "Completed todo",
            "done",
            Description: null,
            CreatedAt: null,
            UpdatedAt: "2026-05-06T10:33:00",
            Dependencies: []);
        var now = DateTimeOffset.Parse("2026-05-06T11:33:00+00:00");
        var expectedLocalTime = DateTimeOffset.Parse("2026-05-06T10:33:00+00:00").ToLocalTime().ToString("HH:mm");

        var line = TerminalRenderer.FormatTodoLine(todo, now);

        Assert.Contains($"[grey]done 1h ago ⋅ {expectedLocalTime}[/]", line, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatTodoLine_UsesUpdatedAtForInProgressStartedTimestamp()
    {
        const string createdAt = "2026-05-06T10:24:00+02:00";
        const string updatedAt = "2026-05-06T10:28:30+02:00";
        var todo = new TodoItem(
            "current",
            "Current todo",
            "in_progress",
            Description: null,
            CreatedAt: createdAt,
            UpdatedAt: updatedAt,
            Dependencies: []);
        var expectedUpdatedTime = LocalClock(updatedAt);
        var unexpectedCreatedTime = LocalClock(createdAt);

        var line = TerminalRenderer.FormatTodoLine(todo, DateTimeOffset.Parse("2026-05-06T10:29:00+02:00"));

        Assert.Contains("[yellow]", line, StringComparison.Ordinal);
        Assert.Contains($"[grey]started just now ⋅ {expectedUpdatedTime}[/]", line, StringComparison.Ordinal);
        Assert.DoesNotContain(unexpectedCreatedTime, line, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateTimestampKey_IncludesInProgressUpdatedAtStartedTimestamp()
    {
        const string updatedAt = "2026-05-06T10:28:30+02:00";
        var snapshot = new TodoSnapshot(
            TodoReadState.Available,
            [new TodoItem("current", "Current todo", "in_progress", null, "2026-05-06T10:24:00+02:00", updatedAt, [])],
            "hash",
            "1 todo(s)");

        var key = TerminalRenderer.CreateTimestampKey(snapshot, DateTimeOffset.Parse("2026-05-06T10:29:00+02:00"));

        Assert.Contains($"started just now ⋅ {LocalClock(updatedAt)}", key, StringComparison.Ordinal);
        Assert.True(TerminalRenderer.HasDisplayedTimestamps(snapshot));
    }

    [Fact]
    public void FormatTodoLine_UsesCreatedAtForPendingAddedTimestamp()
    {
        const string createdAt = "2026-05-06T13:30:00+02:00";
        var todo = new TodoItem(
            "pending",
            "Pending todo",
            "pending",
            Description: null,
            CreatedAt: createdAt,
            UpdatedAt: null,
            Dependencies: []);

        var line = TerminalRenderer.FormatTodoLine(todo, DateTimeOffset.Parse("2026-05-06T13:42:00+02:00"));

        Assert.Contains($"[grey]added 12m ago ⋅ {LocalClock(createdAt)}[/]", line, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatTodoLine_UsesCreatedAtForBlockedAddedTimestamp()
    {
        const string createdAt = "2026-05-06T13:30:00+02:00";
        var todo = new TodoItem(
            "blocked",
            "Blocked todo",
            "blocked",
            Description: null,
            CreatedAt: createdAt,
            UpdatedAt: null,
            Dependencies: []);

        var line = TerminalRenderer.FormatTodoLine(todo, DateTimeOffset.Parse("2026-05-06T13:42:00+02:00"));

        Assert.Contains("[red]", line, StringComparison.Ordinal);
        Assert.Contains($"[grey]added 12m ago ⋅ {LocalClock(createdAt)}[/]", line, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateTimestampKey_IncludesPendingCreatedAtAddedTimestamp()
    {
        const string createdAt = "2026-05-06T13:30:00+02:00";
        var snapshot = new TodoSnapshot(
            TodoReadState.Available,
            [new TodoItem("pending", "Pending todo", "pending", null, createdAt, null, [])],
            "hash",
            "1 todo(s)");

        var key = TerminalRenderer.CreateTimestampKey(snapshot, DateTimeOffset.Parse("2026-05-06T13:42:00+02:00"));

        Assert.Contains($"added 12m ago ⋅ {LocalClock(createdAt)}", key, StringComparison.Ordinal);
        Assert.True(TerminalRenderer.HasDisplayedTimestamps(snapshot));
    }

    [Fact]
    public void CreateTodoBatches_GroupsAdjacentTodosCreatedWithinFiveSeconds()
    {
        var todos = new[]
        {
            new TodoItem("newest", "Newest", "pending", null, "2026-05-06T14:00:04+02:00", null, []),
            new TodoItem("same-batch", "Same batch", "pending", null, "2026-05-06T14:00:00+02:00", null, []),
            new TodoItem("older", "Older", "pending", null, "2026-05-06T13:59:54+02:00", null, [])
        };

        var batches = TerminalRenderer.CreateTodoBatches(todos);

        Assert.Equal(2, batches.Count);
        Assert.Equal(["newest", "same-batch"], batches[0].Select(todo => todo.Id));
        Assert.Equal(["older"], batches[1].Select(todo => todo.Id));
    }

    [Fact]
    public void FormatTodoLines_RendersMutedCompletedTodoInGray()
    {
        const string updatedAt = "2026-05-06T13:30:00+02:00";
        var todo = new TodoItem(
            "older-done",
            "Older completed todo",
            "done",
            Description: null,
            CreatedAt: "2026-05-06T13:00:00+02:00",
            UpdatedAt: updatedAt,
            Dependencies: []);

        var lines = TerminalRenderer.FormatTodoLines(
            todo,
            DateTimeOffset.Parse("2026-05-06T13:42:00+02:00"),
            contentWidth: 80,
            muteCompletedTodo: true);

        var line = Assert.Single(lines);
        Assert.StartsWith("[grey][[✓]] Older completed todo[/]", line, StringComparison.Ordinal);
        Assert.DoesNotContain("[green]", line, StringComparison.Ordinal);
        Assert.DoesNotContain("[bold green]", line, StringComparison.Ordinal);
        Assert.Contains($"[grey]done 12m ago ⋅ {LocalClock(updatedAt)}[/]", line, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildTodoListView_DoesNotAddBlankRowsBetweenCreatedAtBatches()
    {
        var session = CreateSession(metadataCwd: null, registryCwd: null);
        var snapshot = new TodoSnapshot(
            TodoReadState.Available,
            [
                new TodoItem("newest", "Newest", "pending", null, "2026-05-06T14:00:04+02:00", null, []),
                new TodoItem("same-batch", "Same batch", "pending", null, "2026-05-06T14:00:00+02:00", null, []),
                new TodoItem("older", "Older", "done", null, "2026-05-06T13:59:54+02:00", "2026-05-06T14:01:00+02:00", [])
            ],
            "hash",
            "3 todo(s)");

        var view = TerminalRenderer.BuildTodoListView(
            session,
            snapshot,
            DateTimeOffset.Parse("2026-05-06T14:02:00+02:00"),
            consoleWidth: 80,
            consoleHeight: 20);

        Assert.Equal(3, view.Scroll.TotalLines);
        Assert.Equal(3, view.VisibleTodoCount);
        Assert.Equal(3, view.TotalTodoCount);
    }

    [Fact]
    public void FormatTodoLines_WrapsPendingTodoWithContinuationIndent()
    {
        var todo = new TodoItem(
            "pending",
            "Aasdkjasdkajsd--dasdas",
            "pending",
            Description: null,
            CreatedAt: null,
            UpdatedAt: null,
            Dependencies: []);

        var lines = TerminalRenderer.FormatTodoLines(todo, DateTimeOffset.Parse("2026-05-06T12:54:00+02:00"), contentWidth: 16);

        Assert.True(lines.Count > 1);
        Assert.StartsWith("[[ ]] ", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("    ", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void BuildTodoList_AcceptsNarrowWidthForPaddedWrappedRendering()
    {
        var session = new DiscoveredSession(
            new SessionRegistryEntry { SessionId = "11111111-1111-1111-1111-111111111111" },
            IsStale: false,
            HasSessionDatabase: true,
            Metadata: new SessionMetadata(
                "11111111-1111-1111-1111-111111111111",
                Cwd: null,
                Repository: null,
                Branch: null,
                Summary: "Test Session",
                CreatedAt: null,
                UpdatedAt: null));

        var snapshot = new TodoSnapshot(
            TodoReadState.Available,
            [new TodoItem("pending", "Aasdkjasdkajsd--dasdas", "pending", null, null, null, [])],
            "hash",
            "1 todo(s)");

        var exception = Record.Exception(() =>
            TerminalRenderer.BuildTodoList(session, snapshot, DateTimeOffset.Parse("2026-05-06T12:54:00+02:00"), consoleWidth: 20));

        Assert.Null(exception);
    }

    [Fact]
    public void FormatCwdLines_UsesMetadataCwdWhenAvailable()
    {
        var session = CreateSession(metadataCwd: "/Users/simon/project[one]", registryCwd: "/fallback");

        var lines = TerminalRenderer.FormatCwdLines(session, contentWidth: 80);

        Assert.Single(lines);
        Assert.Equal("[grey]/Users/simon/project[[one]][/]", lines[0]);
    }

    [Fact]
    public void FormatCwdLines_UsesRegistryCwdAsFallback()
    {
        var session = CreateSession(metadataCwd: null, registryCwd: "/Users/simon/fallback");

        var lines = TerminalRenderer.FormatCwdLines(session, contentWidth: 80);

        Assert.Single(lines);
        Assert.Equal("[grey]/Users/simon/fallback[/]", lines[0]);
    }

    [Fact]
    public void FormatCwdLines_AbbreviatesCurrentUserHomeDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var session = CreateSession(metadataCwd: Path.Combine(home, "project[one]"), registryCwd: null);

        var lines = TerminalRenderer.FormatCwdLines(session, contentWidth: 80);

        Assert.Single(lines);
        Assert.Equal($"[grey]~{Path.DirectorySeparatorChar}project[[one]][/]", lines[0]);
    }

    [Fact]
    public void AbbreviateHomePath_OnlyAbbreviatesDirectoryBoundaryMatches()
    {
        var path = TerminalRenderer.AbbreviateHomePath("/Users/simonrozsival-other/project", "/Users/simonrozsival");

        Assert.Equal("/Users/simonrozsival-other/project", path);
    }

    [Fact]
    public void FormatCwdLines_OmitsLineWhenCwdIsMissing()
    {
        var session = CreateSession(metadataCwd: null, registryCwd: null);

        var lines = TerminalRenderer.FormatCwdLines(session, contentWidth: 80);

        Assert.Empty(lines);
    }

    [Fact]
    public void BuildTodoList_AcceptsNarrowWidthForWrappedCwd()
    {
        var session = CreateSession(
            metadataCwd: "/Users/simon/very-long-project-directory-name-without-spaces",
            registryCwd: null);
        var snapshot = new TodoSnapshot(TodoReadState.Available, [], "hash", "0 todo(s)");

        var exception = Record.Exception(() =>
            TerminalRenderer.BuildTodoList(session, snapshot, DateTimeOffset.Parse("2026-05-06T12:54:00+02:00"), consoleWidth: 20));

        Assert.Null(exception);
        Assert.True(TerminalRenderer.FormatCwdLines(session, contentWidth: 16).Count > 1);
    }

    [Fact]
    public void BuildLoadingView_DoesNotThrowWithSessionTitleAndCwd()
    {
        var session = CreateSession(
            metadataCwd: "/Users/simon/project[one]",
            registryCwd: null);

        var exception = Record.Exception(() => TerminalRenderer.BuildLoadingView(session, spinnerFrame: 0, consoleWidth: 40));

        Assert.Null(exception);
    }

    [Fact]
    public void GetLoadingSpinnerFrame_CyclesAndHandlesNegativeInput()
    {
        Assert.Equal("⠋", TerminalRenderer.GetLoadingSpinnerFrame(0));
        Assert.Equal("⠋", TerminalRenderer.GetLoadingSpinnerFrame(10));
        Assert.Equal("⠏", TerminalRenderer.GetLoadingSpinnerFrame(-1));
    }

    [Fact]
    public void BuildTodoListView_CalculatesScrollableTodoBody()
    {
        var session = CreateSession(metadataCwd: null, registryCwd: null);
        var snapshot = new TodoSnapshot(
            TodoReadState.Available,
            [
                new TodoItem("newest", "Newest", "pending", null, null, null, []),
                new TodoItem("middle", "Middle", "pending", null, null, null, []),
                new TodoItem("oldest", "Oldest", "pending", null, null, null, [])
            ],
            "hash",
            "3 todo(s)");

        var view = TerminalRenderer.BuildTodoListView(
            session,
            snapshot,
            DateTimeOffset.Parse("2026-05-06T12:54:00+02:00"),
            consoleWidth: 80,
            consoleHeight: 5,
            scrollOffset: 10);

        Assert.True(view.Scroll.CanScroll);
        Assert.Equal(1, view.Scroll.PageSize);
        Assert.Equal(2, view.Scroll.MaxOffset);
        Assert.Equal(2, view.Scroll.Offset);
        Assert.Equal(3, view.Scroll.CurrentPage);
        Assert.Equal(3, view.Scroll.TotalPages);
        Assert.Equal(1, view.VisibleTodoCount);
        Assert.Equal(3, view.TotalTodoCount);
        Assert.True(view.HasMoreAbove);
        Assert.False(view.HasMoreBelow);
    }

    [Fact]
    public void BuildTodoListView_ReservesRowsForScrollHints()
    {
        var session = CreateSession(metadataCwd: null, registryCwd: null);
        var snapshot = new TodoSnapshot(
            TodoReadState.Available,
            [
                new TodoItem("todo-1", "Todo 1", "pending", null, null, null, []),
                new TodoItem("todo-2", "Todo 2", "pending", null, null, null, []),
                new TodoItem("todo-3", "Todo 3", "pending", null, null, null, []),
                new TodoItem("todo-4", "Todo 4", "pending", null, null, null, []),
                new TodoItem("todo-5", "Todo 5", "pending", null, null, null, []),
                new TodoItem("todo-6", "Todo 6", "pending", null, null, null, [])
            ],
            "hash",
            "6 todo(s)");

        var firstPage = TerminalRenderer.BuildTodoListView(
            session,
            snapshot,
            DateTimeOffset.Parse("2026-05-06T12:54:00+02:00"),
            consoleWidth: 80,
            consoleHeight: 10,
            scrollOffset: 0);
        var middlePage = TerminalRenderer.BuildTodoListView(
            session,
            snapshot,
            DateTimeOffset.Parse("2026-05-06T12:54:00+02:00"),
            consoleWidth: 80,
            consoleHeight: 10,
            scrollOffset: 1);

        Assert.False(firstPage.HasMoreAbove);
        Assert.True(firstPage.HasMoreBelow);
        Assert.Equal(3, firstPage.VisibleTodoCount);
        Assert.True(middlePage.HasMoreAbove);
        Assert.True(middlePage.HasMoreBelow);
        Assert.Equal(2, middlePage.VisibleTodoCount);
    }

    [Fact]
    public void FormatFooterStatus_IncludesOnlyPage()
    {
        var scroll = new TerminalRenderer.ScrollMetrics(
            Offset: 10,
            MaxOffset: 30,
            PageSize: 10,
            TotalLines: 34);

        var status = TerminalRenderer.FormatFooterStatus(scroll);

        Assert.Equal("page 2/4", status);
    }

    [Fact]
    public void ScrollMetrics_ReportsFinalPartialPage()
    {
        var scroll = new TerminalRenderer.ScrollMetrics(
            Offset: 16,
            MaxOffset: 16,
            PageSize: 23,
            TotalLines: 39);

        Assert.Equal(2, scroll.CurrentPage);
        Assert.Equal(2, scroll.TotalPages);
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(10, 2)]
    public void CalculateScrollMetrics_ClampsRequestedOffset(int requestedOffset, int expectedOffset)
    {
        var metrics = TerminalRenderer.CalculateScrollMetrics(
            bodyLineCount: 5,
            headerLineCount: 2,
            footerLineCount: 1,
            consoleHeight: 6,
            requestedOffset);

        Assert.Equal(3, metrics.PageSize);
        Assert.Equal(2, metrics.MaxOffset);
        Assert.Equal(expectedOffset, metrics.Offset);
    }

    [Fact]
    public void CreateRenderKey_ChangesWhenTerminalSizeChanges()
    {
        var snapshot = new TodoSnapshot(TodoReadState.Available, [], "hash", "0 todo(s)");
        var now = DateTimeOffset.Parse("2026-05-06T12:54:00+02:00");

        var first = TerminalViewer.CreateRenderKey(snapshot, now, new TerminalViewer.TerminalSize(80, 24));
        var second = TerminalViewer.CreateRenderKey(snapshot, now, new TerminalViewer.TerminalSize(100, 24));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void ShouldRender_ReturnsTrueForResizeRequestEvenWhenRenderKeyIsUnchanged()
    {
        const string renderKey = "same";

        Assert.True(TerminalViewer.ShouldRender(renderKey, renderKey, resizeRequested: true));
    }

    [Fact]
    public void ShouldRender_ReturnsFalseWhenRenderKeyIsUnchangedAndResizeWasNotRequested()
    {
        const string renderKey = "same";

        Assert.False(TerminalViewer.ShouldRender(renderKey, renderKey, resizeRequested: false));
    }

    private static DiscoveredSession CreateSession(string? metadataCwd, string? registryCwd)
    {
        const string sessionId = "11111111-1111-1111-1111-111111111111";
        return new DiscoveredSession(
            new SessionRegistryEntry { SessionId = sessionId, Cwd = registryCwd },
            IsStale: false,
            HasSessionDatabase: true,
            Metadata: new SessionMetadata(
                sessionId,
                Cwd: metadataCwd,
                Repository: null,
                Branch: null,
                Summary: "Test Session",
                CreatedAt: null,
                UpdatedAt: null));
    }
}
