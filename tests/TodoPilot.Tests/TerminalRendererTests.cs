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
    public void FormatTodoLine_AppendsGrayCompletedTimestampOutsideDimCompletedText()
    {
        const string updatedAt = "2026-05-06T12:48:00+02:00";
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

        Assert.Contains("[dim]", line, StringComparison.Ordinal);
        Assert.Contains("[/]", line, StringComparison.Ordinal);
        Assert.DoesNotContain("[grey][[✓]]", line, StringComparison.Ordinal);
        Assert.Contains($"[grey]done 6m ago ⋅ {expectedTime}[/]", line, StringComparison.Ordinal);
        Assert.Matches($@"\[/\]\s\[grey\]done 6m ago ⋅ {expectedTime}\[/\]$", line);
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
    public void FormatTodoLine_KeepsCompletedTodoBoldGreenForFiveMinutes()
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

        Assert.Contains("[bold green]", line, StringComparison.Ordinal);
        Assert.Contains($"[grey]done 5m ago ⋅ {expectedTime}[/]", line, StringComparison.Ordinal);
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

        Assert.Contains("[dim][[✓]] Completed todo[/]", line, StringComparison.Ordinal);
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
    public void FormatTodoLine_RendersDependencyBlockedPendingTodoAsRegularPending()
    {
        const string createdAt = "2026-05-06T13:30:00+02:00";
        var todo = new TodoItem(
            "pending",
            "Dependent [todo]",
            "pending",
            Description: null,
            CreatedAt: createdAt,
            UpdatedAt: null,
            Dependencies: ["z-dep", "a-dep"])
        {
            BlockedBy = ["z-dep", "a-dep"]
        };

        var line = TerminalRenderer.FormatTodoLine(todo, DateTimeOffset.Parse("2026-05-06T13:42:00+02:00"));

        Assert.StartsWith("[[ ]] Dependent [[todo]]", line, StringComparison.Ordinal);
        Assert.DoesNotContain("[dim]", line, StringComparison.Ordinal);
        Assert.DoesNotContain("[[-]]", line, StringComparison.Ordinal);
        Assert.DoesNotContain("blocked", line, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"[grey]added 12m ago ⋅ {LocalClock(createdAt)}[/]", line, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatTodoLine_RendersPendingTodoWithSatisfiedDependenciesAsRegularPending()
    {
        var todo = new TodoItem(
            "pending",
            "Ready todo",
            "pending",
            Description: null,
            CreatedAt: null,
            UpdatedAt: null,
            Dependencies: ["done-dep"]);

        var line = TerminalRenderer.FormatTodoLine(todo, DateTimeOffset.Parse("2026-05-06T13:42:00+02:00"));

        Assert.StartsWith("[[ ]] Ready todo", line, StringComparison.Ordinal);
        Assert.DoesNotContain("blocked by", line, StringComparison.Ordinal);
        Assert.DoesNotContain("[dim]", line, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatTodoLine_RendersExplicitBlockedStatusAsOrangeBlocked()
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

        Assert.StartsWith("[orange1][[⊘]] Blocked todo[/]", line, StringComparison.Ordinal);
        Assert.DoesNotContain("[red]", line, StringComparison.Ordinal);
        Assert.DoesNotContain("[[ ]]", line, StringComparison.Ordinal);
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
    public void FormatTodoLines_RendersMutedCompletedTodoInDimSeparateFromGrayTimestamp()
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
        Assert.StartsWith("[dim][[✓]] Older completed todo[/]", line, StringComparison.Ordinal);
        Assert.DoesNotContain("[green]", line, StringComparison.Ordinal);
        Assert.DoesNotContain("[bold green]", line, StringComparison.Ordinal);
        Assert.DoesNotContain("[grey][[✓]]", line, StringComparison.Ordinal);
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
    public void BuildTodoListView_TreatsMissingDatabaseAsQuietEmptyState()
    {
        var session = CreateSession(metadataCwd: null, registryCwd: null);
        var snapshot = TodoSnapshot.MissingDatabase("/tmp/session.db");

        var view = TerminalRenderer.BuildTodoListView(
            session,
            snapshot,
            DateTimeOffset.Parse("2026-05-06T14:02:00+02:00"),
            consoleWidth: 80,
            consoleHeight: 20);

        Assert.Equal(1, view.Scroll.TotalLines);
        Assert.Equal(0, view.VisibleTodoCount);
        Assert.Equal(0, view.TotalTodoCount);
        Assert.Equal(TodoSnapshot.EmptyMessage, snapshot.Message);
        Assert.DoesNotContain("/tmp/session.db", snapshot.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildTodoListView_RendersFocusedAndExpandedTodoDetails()
    {
        var session = CreateSession(metadataCwd: null, registryCwd: null);
        var snapshot = new TodoSnapshot(
            TodoReadState.Available,
            [
                new TodoItem(
                    "todo-1",
                    "Expand details",
                    "pending",
                    "Show details inline.",
                    "2026-05-07T12:00:00+02:00",
                    "2026-05-07T12:05:00+02:00",
                    ["todo-0"])
            ],
            "hash",
            "1 todo(s)");
        var view = TerminalRenderer.BuildTodoListView(
            session,
            snapshot,
            DateTimeOffset.Parse("2026-05-07T12:10:00+02:00"),
            consoleWidth: 100,
            consoleHeight: 30,
            scrollOffset: 0,
            displayState: new TodoListDisplayState("todo-1", "todo-1", ShowFocusMarker: true));

        var rendered = string.Join('\n', view.Lines);
        Assert.Contains("[white]›[/] [[ ]] Expand details", rendered, StringComparison.Ordinal);
        Assert.Contains("[grey]description:[/] Show details inline.", rendered, StringComparison.Ordinal);
        Assert.Contains("[grey]id:[/] todo-1", rendered, StringComparison.Ordinal);
        Assert.Contains("[grey]status:[/] pending", rendered, StringComparison.Ordinal);
        Assert.Contains("[grey]dependencies:[/] todo-0", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("[grey]created:[/]", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("[grey]updated:[/]", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("[grey]inbox:[/]", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("[grey]file:[/]", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("[grey]ref:[/]", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("[grey]user:[/]", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("[grey]assistant:[/]", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("[grey]checkpoint:[/]", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("[grey]description: Show details inline.[/]", rendered, StringComparison.Ordinal);
        Assert.True(rendered.IndexOf("[grey]description:[/]", StringComparison.Ordinal) < rendered.IndexOf("[grey]id:[/]", StringComparison.Ordinal));
        Assert.True(rendered.IndexOf("[grey]status:[/]", StringComparison.Ordinal) < rendered.IndexOf("[grey]dependencies:[/]", StringComparison.Ordinal));
        Assert.Equal(1, view.VisibleTodoCount);
        Assert.True(view.Scroll.TotalLines > 1);
    }

    [Fact]
    public void BuildTodoListView_ShowsExplicitBlockedStatusInExpandedDetails()
    {
        var session = CreateSession(metadataCwd: null, registryCwd: null);
        var snapshot = new TodoSnapshot(
            TodoReadState.Available,
            [
                new TodoItem(
                    "blocked",
                    "Blocked item",
                    "blocked",
                    "Waiting on an external decision.",
                    "2026-05-07T12:00:00+02:00",
                    null,
                    [])
            ],
            "hash",
            "1 todo(s)");

        var view = TerminalRenderer.BuildTodoListView(
            session,
            snapshot,
            DateTimeOffset.Parse("2026-05-07T12:10:00+02:00"),
            consoleWidth: 100,
            consoleHeight: 30,
            scrollOffset: 0,
            displayState: new TodoListDisplayState("blocked", "blocked", ShowFocusMarker: true));

        var rendered = string.Join('\n', view.Lines);
        Assert.Contains("[white]›[/] [orange1][[⊘]] Blocked item[/]", rendered, StringComparison.Ordinal);
        Assert.Contains("[grey]status:[/] blocked", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("[grey]status:[/] pending", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildTodoListView_HidesFocusMarkerWhenDisplayStateDoesNotRequestIt()
    {
        var session = CreateSession(metadataCwd: null, registryCwd: null);
        var snapshot = new TodoSnapshot(
            TodoReadState.Available,
            [new TodoItem("todo-1", "Focused but hidden", "pending", null, null, null, [])],
            "hash",
            "1 todo(s)");

        var view = TerminalRenderer.BuildTodoListView(
            session,
            snapshot,
            DateTimeOffset.Parse("2026-05-07T12:10:00+02:00"),
            consoleWidth: 80,
            consoleHeight: 10,
            scrollOffset: 0,
            displayState: new TodoListDisplayState("todo-1"));

        Assert.DoesNotContain("[white]›[/]", string.Join('\n', view.Lines), StringComparison.Ordinal);
    }

    [Fact]
    public void BuildTodoListView_WrapsExpandedValuesWithStableSmallIndent()
    {
        var session = CreateSession(metadataCwd: null, registryCwd: null);
        var snapshot = new TodoSnapshot(
            TodoReadState.Available,
            [
                new TodoItem(
                    "todo-1",
                    "Expand details",
                    "pending",
                    "This description wraps onto a continuation line.",
                    null,
                    null,
                    [])
            ],
            "hash",
            "1 todo(s)");

        var view = TerminalRenderer.BuildTodoListView(
            session,
            snapshot,
            DateTimeOffset.Parse("2026-05-07T12:10:00+02:00"),
            consoleWidth: 40,
            consoleHeight: 30,
            scrollOffset: 0,
            SessionSidebarDetails.Empty,
            new TodoListDisplayState("todo-1", "todo-1", ShowFocusMarker: true));

        var descriptionIndex = view.Lines.ToList().FindIndex(line => line.Contains("[grey]description:[/]", StringComparison.Ordinal));
        Assert.True(descriptionIndex >= 0);
        Assert.DoesNotContain("[grey]This description", view.Lines[descriptionIndex], StringComparison.Ordinal);
        Assert.DoesNotContain("[grey]", view.Lines[descriptionIndex + 1], StringComparison.Ordinal);
        Assert.StartsWith(new string(' ', 7), view.Lines[descriptionIndex + 1], StringComparison.Ordinal);
        Assert.False(view.Lines[descriptionIndex + 1].StartsWith(new string(' ', 19), StringComparison.Ordinal));
    }

    [Fact]
    public void BuildTodoListView_ExpandedDoneTodoDoesNotShowGenericRevisitPolicy()
    {
        var session = CreateSession(metadataCwd: null, registryCwd: null);
        var snapshot = new TodoSnapshot(
            TodoReadState.Available,
            [new TodoItem("done", "Done todo", "done", null, null, "2026-05-07T12:00:00+02:00", [])],
            "hash",
            "1 todo(s)");

        var view = TerminalRenderer.BuildTodoListView(
            session,
            snapshot,
            DateTimeOffset.Parse("2026-05-07T12:10:00+02:00"),
            consoleWidth: 80,
            consoleHeight: 30,
            scrollOffset: 0,
            SessionSidebarDetails.Empty,
            new TodoListDisplayState("done", "done", ShowFocusMarker: true));

        var rendered = string.Join('\n', view.Lines);
        Assert.DoesNotContain("[grey]revisit:[/]", rendered, StringComparison.Ordinal);
        Assert.True(view.Scroll.TotalLines > TerminalRenderer.FormatTodoLines(snapshot.Todos[0], DateTimeOffset.Parse("2026-05-07T12:10:00+02:00"), 76).Count);
    }

    [Fact]
    public void BuildTodoListView_RendersBlockedStatusInExpandedDetails()
    {
        var session = CreateSession(metadataCwd: null, registryCwd: null);
        var snapshot = new TodoSnapshot(
            TodoReadState.Available,
            [new TodoItem("todo-1", "Blocked status", "blocked", null, null, null, [])],
            "hash",
            "1 todo(s)");

        var view = TerminalRenderer.BuildTodoListView(
            session,
            snapshot,
            DateTimeOffset.Parse("2026-05-07T12:10:00+02:00"),
            consoleWidth: 80,
            consoleHeight: 10,
            scrollOffset: 0,
            SessionSidebarDetails.Empty,
            new TodoListDisplayState("todo-1", "todo-1", ShowFocusMarker: true));

        var rendered = string.Join('\n', view.Lines);
        Assert.Contains("[grey]status:[/] blocked", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("[grey]status:[/] pending", rendered, StringComparison.Ordinal);
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
    public void DisplayWidth_TreatsCombiningMarksAsZeroAndWideCharactersAsTwoCells()
    {
        Assert.Equal(1, TerminalRenderer.DisplayWidth("e\u0301"));
        Assert.Equal(2, TerminalRenderer.DisplayWidth("界"));
        Assert.Equal(2, TerminalRenderer.DisplayWidth("😀"));
        Assert.Equal(1, TerminalRenderer.DisplayWidth("✓"));
    }

    [Fact]
    public void WrapText_UsesDisplayWidthInsteadOfUtf16Length()
    {
        var lines = TerminalRenderer.WrapText("ab 界 cd", firstWidth: 5, continuationWidth: 5);

        Assert.Equal(["ab", "界 cd"], lines);
        Assert.All(lines, line => Assert.True(TerminalRenderer.DisplayWidth(line) <= 5));
    }

    [Fact]
    public void WrapText_DoesNotThrowOnInvalidUtf16()
    {
        var exception = Record.Exception(() => TerminalRenderer.WrapText("hello\ud800world", firstWidth: 6, continuationWidth: 6));

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
    public void BuildTodoListView_FollowsFocusedTodoWithCompletedContextAbove()
    {
        var session = CreateSession(metadataCwd: null, registryCwd: null);
        var snapshot = new TodoSnapshot(
            TodoReadState.Available,
            [
                new TodoItem("done-1", "Done 1", "done", null, null, null, []),
                new TodoItem("done-2", "Done 2", "done", null, null, null, []),
                new TodoItem("done-3", "Done 3", "done", null, null, null, []),
                new TodoItem("done-4", "Done 4", "done", null, null, null, []),
                new TodoItem("done-5", "Done 5", "done", null, null, null, []),
                new TodoItem("wip", "Current work", "in_progress", null, null, null, []),
                new TodoItem("next-1", "Next 1", "pending", null, null, null, []),
                new TodoItem("next-2", "Next 2", "pending", null, null, null, []),
                new TodoItem("next-3", "Next 3", "pending", null, null, null, []),
                new TodoItem("next-4", "Next 4", "pending", null, null, null, []),
                new TodoItem("next-5", "Next 5", "pending", null, null, null, []),
                new TodoItem("next-6", "Next 6", "pending", null, null, null, [])
            ],
            "hash",
            "12 todo(s)");

        var view = TerminalRenderer.BuildTodoListView(
            session,
            snapshot,
            DateTimeOffset.Parse("2026-05-06T12:54:00+02:00"),
            consoleWidth: 80,
            consoleHeight: 13,
            scrollOffset: 0,
            displayState: new TodoListDisplayState("wip"),
            focusedContextItemCountBefore: 3);
        var rendered = string.Join('\n', view.Lines);

        Assert.True(view.HasMoreAbove);
        Assert.True(view.HasMoreBelow);
        Assert.DoesNotContain("Done 2", rendered, StringComparison.Ordinal);
        Assert.Contains("Done 3", rendered, StringComparison.Ordinal);
        Assert.Contains("Done 4", rendered, StringComparison.Ordinal);
        Assert.Contains("Done 5", rendered, StringComparison.Ordinal);
        Assert.Contains("[[•]] Current work", rendered, StringComparison.Ordinal);
        Assert.Contains("Next 1", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("Next 2", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildTodoListView_KeepsFocusedTodoVisibleWhenContextDoesNotFit()
    {
        var session = CreateSession(metadataCwd: null, registryCwd: null);
        var snapshot = new TodoSnapshot(
            TodoReadState.Available,
            [
                new TodoItem("done-1", "Done 1", "done", null, null, null, []),
                new TodoItem("done-2", "Done 2", "done", null, null, null, []),
                new TodoItem("done-3", "Done 3", "done", null, null, null, []),
                new TodoItem("done-4", "Done 4", "done", null, null, null, []),
                new TodoItem("done-5", "Done 5", "done", null, null, null, []),
                new TodoItem("wip", "Current work", "in_progress", null, null, null, []),
                new TodoItem("next-1", "Next 1", "pending", null, null, null, []),
                new TodoItem("next-2", "Next 2", "pending", null, null, null, [])
            ],
            "hash",
            "8 todo(s)");

        var view = TerminalRenderer.BuildTodoListView(
            session,
            snapshot,
            DateTimeOffset.Parse("2026-05-06T12:54:00+02:00"),
            consoleWidth: 80,
            consoleHeight: 8,
            scrollOffset: 0,
            displayState: new TodoListDisplayState("wip"),
            focusedContextItemCountBefore: 3);

        Assert.Contains("[[•]] Current work", string.Join('\n', view.Lines), StringComparison.Ordinal);
    }

    [Fact]
    public void BuildTodoListView_FooterIncludesSessionSwitchShortcut()
    {
        var session = CreateSession(metadataCwd: null, registryCwd: null);
        var snapshot = new TodoSnapshot(TodoReadState.Available, [], "hash", "0 todo(s)");

        var view = TerminalRenderer.BuildTodoListView(
            session,
            snapshot,
            DateTimeOffset.Parse("2026-05-06T12:54:00+02:00"),
            consoleWidth: 120,
            consoleHeight: 10,
            scrollOffset: 0);

        Assert.Contains("ctrl+x sessions", string.Join('\n', view.Lines), StringComparison.OrdinalIgnoreCase);
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
    public void CreateRenderKey_ChangesWhenFocusExpansionOrDetailsChange()
    {
        var snapshot = new TodoSnapshot(TodoReadState.Available, [], "hash", "0 todo(s)");
        var now = DateTimeOffset.Parse("2026-05-06T12:54:00+02:00");

        var focused = TerminalViewer.CreateRenderKey(snapshot, now, new TodoListDisplayState("todo-1", null));
        var expanded = TerminalViewer.CreateRenderKey(snapshot, now, new TodoListDisplayState("todo-1", "todo-1"));
        var otherFocus = TerminalViewer.CreateRenderKey(snapshot, now, new TodoListDisplayState("todo-2", null));
        var visibleMarker = TerminalViewer.CreateRenderKey(snapshot, now, new TodoListDisplayState("todo-1", null, ShowFocusMarker: true));
        var followedContext = TerminalViewer.CreateRenderKey(snapshot, now, new TodoListDisplayState("todo-1", null), focusedContextItemCountBefore: 3);

        Assert.NotEqual(focused, expanded);
        Assert.NotEqual(focused, otherFocus);
        Assert.NotEqual(focused, visibleMarker);
        Assert.NotEqual(focused, followedContext);
    }

    [Fact]
    public void CreateDisplayState_ShowsFocusMarkerAfterRecentNavigationOrWhileExpanded()
    {
        var now = DateTimeOffset.Parse("2026-05-07T17:30:00+02:00");
        var timeout = TimeSpan.FromMinutes(1);

        Assert.False(TerminalViewer.CreateDisplayState("todo-1", null, null, now, timeout).ShowFocusMarker);
        Assert.True(TerminalViewer.CreateDisplayState("todo-1", null, now.AddSeconds(-30), now, timeout).ShowFocusMarker);
        Assert.False(TerminalViewer.CreateDisplayState("todo-1", null, now.AddSeconds(-61), now, timeout).ShowFocusMarker);
        Assert.True(TerminalViewer.CreateDisplayState("todo-1", "todo-1", now.AddMinutes(-5), now, timeout).ShowFocusMarker);
    }

    [Fact]
    public void SelectDefaultFocusedTodoId_PrefersCurrentThenInProgressThenPending()
    {
        var todos = new[]
        {
            new TodoItem("done", "Done", "done", null, null, null, []),
            new TodoItem("pending", "Pending", "pending", null, null, null, []),
            new TodoItem("wip", "WIP", "in_progress", null, null, null, [])
        };

        Assert.Equal("pending", TerminalViewer.SelectDefaultFocusedTodoId(todos, "pending"));
        Assert.Equal("wip", TerminalViewer.SelectDefaultFocusedTodoId(todos, "missing"));
        Assert.Null(TerminalViewer.SelectDefaultFocusedTodoId([], null));
    }

    [Fact]
    public void SelectAutoFollowedTodoId_RecomputesCurrentWorkWithoutPreservingPreviousFocus()
    {
        var todos = new[]
        {
            new TodoItem("done", "Done", "done", null, null, null, []),
            new TodoItem("pending", "Pending", "pending", null, null, null, []),
            new TodoItem("wip", "WIP", "in_progress", null, null, null, [])
        };

        Assert.Equal("wip", TerminalViewer.SelectAutoFollowedTodoId(todos));
        Assert.Equal("pending", TerminalViewer.SelectAutoFollowedTodoId(todos.Where(todo => todo.Id != "wip").ToArray()));
        Assert.Equal("done", TerminalViewer.SelectAutoFollowedTodoId(todos.Where(todo => todo.Status == "done").ToArray()));
    }

    [Fact]
    public void SelectManualFocusedTodoId_PreservesManualFocusUntilMissingOrCompleted()
    {
        var before = new[]
        {
            new TodoItem("manual", "Manual", "pending", null, null, null, []),
            new TodoItem("wip", "WIP", "in_progress", null, null, null, [])
        };
        var afterCompleted = new[]
        {
            new TodoItem("manual", "Manual", "done", null, null, null, []),
            new TodoItem("wip", "WIP", "in_progress", null, null, null, [])
        };

        Assert.Equal("manual", TerminalViewer.SelectManualFocusedTodoId(before, "manual", "pending"));
        Assert.Null(TerminalViewer.SelectManualFocusedTodoId(before, "missing", "pending"));
        Assert.Null(TerminalViewer.SelectManualFocusedTodoId(afterCompleted, "manual", "pending"));
        Assert.Equal("manual", TerminalViewer.SelectManualFocusedTodoId(afterCompleted, "manual", "done"));
    }

    [Fact]
    public void MoveFocusedTodoId_ClampsByTodoId()
    {
        var todos = new[]
        {
            new TodoItem("a", "A", "pending", null, null, null, []),
            new TodoItem("b", "B", "pending", null, null, null, []),
            new TodoItem("c", "C", "pending", null, null, null, [])
        };

        Assert.Equal("c", TerminalViewer.MoveFocusedTodoId(todos, "a", 10));
        Assert.Equal("a", TerminalViewer.MoveFocusedTodoId(todos, "c", -10));
        Assert.Equal("b", TerminalViewer.MoveFocusedTodoId(todos, "a", 1));
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

    [Fact]
    public void ShouldRenderSessionSelection_ReturnsTrueForResizeOrSizeChange()
    {
        var renderedSize = new TerminalViewer.TerminalSize(80, 24);

        Assert.True(TerminalViewer.ShouldRenderSessionSelection(renderedSize, renderedSize, stateChanged: false, resizeRequested: true));
        Assert.True(TerminalViewer.ShouldRenderSessionSelection(renderedSize, new TerminalViewer.TerminalSize(100, 24), stateChanged: false, resizeRequested: false));
        Assert.False(TerminalViewer.ShouldRenderSessionSelection(renderedSize, renderedSize, stateChanged: false, resizeRequested: false));
    }

    [Fact]
    public void IsQuitKey_OnlyTreatsQAsKeyboardQuit()
    {
        Assert.True(TerminalViewer.IsQuitKey(new ConsoleKeyInfo('q', ConsoleKey.Q, shift: false, alt: false, control: false)));
        Assert.False(TerminalViewer.IsQuitKey(new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, shift: false, alt: false, control: false)));
    }

    [Fact]
    public void FormatSessionChoice_EscapesSessionIdAndNameForMarkupRendering()
    {
        const string sessionId = "ff8d2dee-053b-401d-a01c-9ddd5672bb8f";
        var session = new DiscoveredSession(
            new SessionRegistryEntry { SessionId = sessionId },
            IsStale: false,
            HasSessionDatabase: true,
            Metadata: new SessionMetadata(
                sessionId,
                Cwd: null,
                Repository: null,
                Branch: null,
                Summary: "Session [preview]",
                CreatedAt: null,
                UpdatedAt: null));

        var choice = TerminalViewer.FormatSessionChoice(session);
        var exception = Record.Exception(() => new Spectre.Console.Markup(choice));

        Assert.Null(exception);
        Assert.Equal("Session [[preview]] [[ff8d2dee-053b-401d-a01c-9ddd5672bb8f]] active", choice);
    }

    [Fact]
    public void FormatSessionSelectionChoice_RendersSelectedDotAndWrapsMetadata()
    {
        const string sessionId = "ff8d2dee-053b-401d-a01c-9ddd5672bb8f";
        var session = new DiscoveredSession(
            new SessionRegistryEntry { SessionId = sessionId, LastSeen = "2026-05-06T12:49:00+02:00" },
            IsStale: false,
            HasSessionDatabase: true,
            Metadata: new SessionMetadata(
                sessionId,
                Cwd: null,
                Repository: null,
                Branch: null,
                Summary: "Session [preview] with a very long name",
                CreatedAt: null,
                UpdatedAt: null));

        var lines = TerminalViewer.FormatSessionSelectionChoiceLines(
            session,
            selected: true,
            maxWidth: 40,
            now: DateTimeOffset.Parse("2026-05-06T12:54:00+02:00"),
            showSessionId: true);
        var exception = Record.Exception(() =>
        {
            foreach (var line in lines)
            {
                _ = new Spectre.Console.Markup(line);
            }
        });

        Assert.Null(exception);
        Assert.StartsWith("[blue][[•]] ", lines[0], StringComparison.Ordinal);
        Assert.Contains("Session [[preview]]", lines[0], StringComparison.Ordinal);
        Assert.DoesNotContain("[reverse]", string.Join('\n', lines), StringComparison.Ordinal);
        Assert.DoesNotContain("...", string.Join('\n', lines), StringComparison.Ordinal);
        Assert.Contains(lines, line => line.Contains("[grey]", StringComparison.Ordinal)
            && line.Contains("ff8d2dee", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("last active", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("5m ago", StringComparison.Ordinal));
    }

    [Fact]
    public void FormatSessionSelectionChoice_RendersUnselectedCheckboxAndEscapesMarkup()
    {
        const string sessionId = "ff8d2dee-053b-401d-a01c-9ddd5672bb8f";
        var session = new DiscoveredSession(
            new SessionRegistryEntry { SessionId = sessionId },
            IsStale: true,
            HasSessionDatabase: true,
            Metadata: new SessionMetadata(
                sessionId,
                Cwd: null,
                Repository: null,
                Branch: null,
                Summary: "Session [preview]",
                CreatedAt: null,
                UpdatedAt: null));

        var lines = TerminalViewer.FormatSessionSelectionChoiceLines(
            session,
            selected: false,
            maxWidth: 120,
            now: DateTimeOffset.Parse("2026-05-06T12:54:00+02:00"),
            showSessionId: true);
        var exception = Record.Exception(() =>
        {
            foreach (var line in lines)
            {
                _ = new Spectre.Console.Markup(line);
            }
        });

        Assert.Null(exception);
        Assert.StartsWith("[[ ]] Session [[preview]]", lines[0], StringComparison.Ordinal);
        Assert.Contains(lines, line => line.Contains("[grey]", StringComparison.Ordinal)
            && line.Contains("stale", StringComparison.Ordinal)
            && line.Contains(sessionId, StringComparison.Ordinal));
    }

    [Fact]
    public void FormatSessionSelectionChoice_KeepsMetadataInlineWhenRowFits()
    {
        const string sessionId = "0e218750-5887-4aa1-8b15-01d55577457a";
        var session = new DiscoveredSession(
            new SessionRegistryEntry { SessionId = sessionId, LastSeen = "2026-05-07T12:59:00+02:00" },
            IsStale: false,
            HasSessionDatabase: true,
            Metadata: new SessionMetadata(
                sessionId,
                Cwd: null,
                Repository: null,
                Branch: null,
                Summary: "Todolist ordering",
                CreatedAt: null,
                UpdatedAt: null));

        var lines = TerminalViewer.FormatSessionSelectionChoiceLines(
            session,
            selected: true,
            maxWidth: 120,
            now: DateTimeOffset.Parse("2026-05-07T12:59:30+02:00"),
            showSessionId: true);

        var line = Assert.Single(lines);
        Assert.StartsWith("[blue][[•]] Todolist ordering[/] [grey][[0e218750-5887-4aa1-8b15-01d55577457a]]", line, StringComparison.Ordinal);
        Assert.Contains("active · last active just now", line, StringComparison.Ordinal);
        Assert.DoesNotContain("[reverse]", line, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatSessionSelectionChoice_HidesSessionUuidByDefault()
    {
        const string sessionId = "0e218750-5887-4aa1-8b15-01d55577457a";
        var session = new DiscoveredSession(
            new SessionRegistryEntry { SessionId = sessionId, LastSeen = "2026-05-07T12:59:00+02:00" },
            IsStale: false,
            HasSessionDatabase: true,
            Metadata: new SessionMetadata(
                sessionId,
                Cwd: null,
                Repository: null,
                Branch: null,
                Summary: "Todolist ordering",
                CreatedAt: null,
                UpdatedAt: null));

        var lines = TerminalViewer.FormatSessionSelectionChoiceLines(
            session,
            selected: true,
            maxWidth: 120,
            now: DateTimeOffset.Parse("2026-05-07T12:59:30+02:00"));

        var rendered = string.Join('\n', lines);
        Assert.DoesNotContain(sessionId, rendered, StringComparison.Ordinal);
        Assert.Contains("[grey]active · last active just now", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void FilterSessions_SearchesMetadataNotJustTrimmedDisplayText()
    {
        var first = CreateSessionWithMetadata(
            sessionId: "11111111-1111-1111-1111-111111111111",
            summary: "First",
            repository: "repo-one",
            branch: "main",
            cwd: "/tmp/first");
        var second = CreateSessionWithMetadata(
            sessionId: "22222222-2222-2222-2222-222222222222",
            summary: "Second",
            repository: "repo-two",
            branch: "feature",
            cwd: "/tmp/second");

        var filtered = TerminalViewer.FilterSessions([first, second], "repo-two");

        var session = Assert.Single(filtered);
        Assert.Equal("22222222-2222-2222-2222-222222222222", session.Registry.SessionId);
    }

    [Fact]
    public void FilterSessions_SearchesHiddenSessionUuid()
    {
        var first = CreateSessionWithMetadata(
            sessionId: "11111111-1111-1111-1111-111111111111",
            summary: "First",
            repository: "repo-one",
            branch: "main",
            cwd: "/tmp/first");
        var second = CreateSessionWithMetadata(
            sessionId: "22222222-2222-2222-2222-222222222222",
            summary: "Second",
            repository: "repo-two",
            branch: "feature",
            cwd: "/tmp/second");

        var filtered = TerminalViewer.FilterSessions([first, second], "22222222");

        var session = Assert.Single(filtered);
        Assert.Equal("22222222-2222-2222-2222-222222222222", session.Registry.SessionId);
    }

    [Fact]
    public void GetSessionsForView_DefaultNarrowedViewReturnsOnlyRunningExtensionProcesses()
    {
        var running = CreateSessionWithMetadata(
            sessionId: "11111111-1111-1111-1111-111111111111",
            summary: "Running",
            repository: "repo-one",
            branch: "main",
            cwd: "/tmp/running",
            isExtensionProcessRunning: true);
        var stopped = CreateSessionWithMetadata(
            sessionId: "22222222-2222-2222-2222-222222222222",
            summary: "Stopped",
            repository: "repo-two",
            branch: "main",
            cwd: "/tmp/stopped",
            isExtensionProcessRunning: false);

        var narrowed = TerminalViewer.GetSessionsForView([running, stopped], showOnlyRunningExtensionSessions: true);
        var full = TerminalViewer.GetSessionsForView([running, stopped], showOnlyRunningExtensionSessions: false);

        var session = Assert.Single(narrowed);
        Assert.Equal("11111111-1111-1111-1111-111111111111", session.Registry.SessionId);
        Assert.Equal([running, stopped], full);
    }

    [Fact]
    public void FilterSessions_SearchesExtensionProcessState()
    {
        var running = CreateSessionWithMetadata(
            sessionId: "11111111-1111-1111-1111-111111111111",
            summary: "First",
            repository: "repo-one",
            branch: "main",
            cwd: "/tmp/first",
            isExtensionProcessRunning: true);
        var stopped = CreateSessionWithMetadata(
            sessionId: "22222222-2222-2222-2222-222222222222",
            summary: "Second",
            repository: "repo-two",
            branch: "feature",
            cwd: "/tmp/second",
            isExtensionProcessRunning: false);

        var filtered = TerminalViewer.FilterSessions([running, stopped], "stopped extension");

        var session = Assert.Single(filtered);
        Assert.Equal("22222222-2222-2222-2222-222222222222", session.Registry.SessionId);
    }

    [Fact]
    public void FilterSessions_SearchesAttachedViewerState()
    {
        var running = CreateSessionWithMetadata(
            sessionId: "11111111-1111-1111-1111-111111111111",
            summary: "First",
            repository: "repo-one",
            branch: "main",
            cwd: "/tmp/first");
        var attached = CreateSessionWithMetadata(
            sessionId: "22222222-2222-2222-2222-222222222222",
            summary: "Second",
            repository: "repo-two",
            branch: "feature",
            cwd: "/tmp/second",
            hasAttachedViewer: true,
            attachedViewerCount: 1);

        var filtered = TerminalViewer.FilterSessions([running, attached], "attached");

        var session = Assert.Single(filtered);
        Assert.Equal("22222222-2222-2222-2222-222222222222", session.Registry.SessionId);
    }

    [Fact]
    public void FormatSessionSelectionChoice_ShowsCwdAndAttachedViewerState()
    {
        var session = CreateSessionWithMetadata(
            sessionId: "11111111-1111-1111-1111-111111111111",
            summary: "First",
            repository: "repo-one",
            branch: "main",
            cwd: "/tmp/first",
            hasAttachedViewer: true,
            attachedViewerCount: 2);

        var lines = TerminalViewer.FormatSessionSelectionChoiceLines(
            session,
            selected: false,
            maxWidth: 120,
            now: DateTimeOffset.Parse("2026-05-07T12:59:30+02:00"));
        var rendered = string.Join('\n', lines);

        Assert.Contains("/tmp/first", rendered, StringComparison.Ordinal);
        Assert.Contains("attached elsewhere x2", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateSessionSelectionDataKey_ChangesWhenSessionNameChanges()
    {
        var before = CreateSessionWithMetadata(
            sessionId: "11111111-1111-1111-1111-111111111111",
            summary: "Before",
            repository: "repo-one",
            branch: "main",
            cwd: "/tmp/first");
        var after = CreateSessionWithMetadata(
            sessionId: "11111111-1111-1111-1111-111111111111",
            summary: "After",
            repository: "repo-one",
            branch: "main",
            cwd: "/tmp/first");

        Assert.NotEqual(TerminalViewer.CreateSessionSelectionDataKey([before]), TerminalViewer.CreateSessionSelectionDataKey([after]));
    }

    [Fact]
    public void PreserveSelectedSessionIndex_KeepsSelectionBySessionId()
    {
        var first = CreateSessionWithMetadata(
            sessionId: "11111111-1111-1111-1111-111111111111",
            summary: "First",
            repository: "repo-one",
            branch: "main",
            cwd: "/tmp/first");
        var second = CreateSessionWithMetadata(
            sessionId: "22222222-2222-2222-2222-222222222222",
            summary: "Second",
            repository: "repo-two",
            branch: "main",
            cwd: "/tmp/second");

        var selectedIndex = TerminalViewer.PreserveSelectedSessionIndex([second, first], first.Registry.SessionId, selectedIndex: 0);

        Assert.Equal(1, selectedIndex);
    }

    [Fact]
    public void ClampSelectionScrollOffset_KeepsSelectedSessionVisible()
    {
        Assert.Equal(0, TerminalViewer.ClampSelectionScrollOffset(itemCount: 10, pageSize: 4, selectedIndex: 0, requestedOffset: 3));
        Assert.Equal(2, TerminalViewer.ClampSelectionScrollOffset(itemCount: 10, pageSize: 4, selectedIndex: 5, requestedOffset: 0));
        Assert.Equal(6, TerminalViewer.ClampSelectionScrollOffset(itemCount: 10, pageSize: 4, selectedIndex: 9, requestedOffset: 100));
    }

    [Fact]
    public void BuildSessionSelectionContent_UsesSharedSpacingAndNoLeadingBlankLine()
    {
        var session = CreateSessionWithMetadata(
            sessionId: "11111111-1111-1111-1111-111111111111",
            summary: "First",
            repository: "repo-one",
            branch: "main",
            cwd: "/tmp/first");

        var content = TerminalViewer.BuildSessionSelectionContent(
            [session],
            selectedIndex: 0,
            scrollOffset: 0,
            filter: "",
            consoleWidth: 80,
            consoleHeight: 8);

        Assert.NotEmpty(content.Lines);
        Assert.Equal("  [bold]# Choose a Copilot session[/]", content.Lines[0]);
        Assert.Equal("  [grey]Type to filter by session name, repo, directory, UUID, or state[/]", content.Lines[1]);
        Assert.Equal("", content.Lines[2]);
        Assert.Contains("[blue][[•]]", content.Lines[3], StringComparison.Ordinal);
        Assert.DoesNotContain("[reverse]", string.Join('\n', content.Lines), StringComparison.Ordinal);
        Assert.StartsWith("  ", content.Lines[3], StringComparison.Ordinal);
        Assert.Contains("ctrl+u show UUIDs", string.Join('\n', content.Lines), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ctrl+a show all", string.Join('\n', content.Lines), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("q quit", string.Join('\n', content.Lines), StringComparison.Ordinal);
        Assert.DoesNotContain("11111111-1111-1111-1111-111111111111", string.Join('\n', content.Lines), StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSessionSelectionContent_ShowsUuidsWhenToggledOn()
    {
        const string sessionId = "11111111-1111-1111-1111-111111111111";
        var session = CreateSessionWithMetadata(
            sessionId: sessionId,
            summary: "First",
            repository: "repo-one",
            branch: "main",
            cwd: "/tmp/first");

        var content = TerminalViewer.BuildSessionSelectionContent(
            [session],
            selectedIndex: 0,
            scrollOffset: 0,
            filter: "",
            consoleWidth: 120,
            consoleHeight: 8,
            showSessionIds: true,
            showOnlyRunningExtensionSessions: false);

        var rendered = string.Join('\n', content.Lines);
        Assert.Contains(sessionId, rendered, StringComparison.Ordinal);
        Assert.Contains("ctrl+u hide UUIDs", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ctrl+a running only", rendered, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSessionSelectionContent_EmptyNarrowedViewExplainsShowAllToggle()
    {
        var content = TerminalViewer.BuildSessionSelectionContent(
            [],
            selectedIndex: 0,
            scrollOffset: 0,
            filter: "",
            consoleWidth: 100,
            consoleHeight: 8,
            showOnlyRunningExtensionSessions: true);

        var rendered = string.Join('\n', content.Lines);
        Assert.Contains("No sessions with a running extension process", rendered, StringComparison.Ordinal);
        Assert.Contains("ctrl+a show all", rendered, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSessionSelectionContent_ShowsPagedFooterWhenScrollable()
    {
        var sessions = Enumerable.Range(1, 5)
            .Select(i => CreateSessionWithMetadata(
                sessionId: $"{i}{i}{i}{i}{i}{i}{i}{i}-{i}{i}{i}{i}-{i}{i}{i}{i}-{i}{i}{i}{i}-{i}{i}{i}{i}{i}{i}{i}{i}{i}{i}{i}{i}",
                summary: $"Session {i}",
                repository: "repo",
                branch: "main",
                cwd: $"/tmp/{i}"))
            .ToArray();

        var content = TerminalViewer.BuildSessionSelectionContent(
            sessions,
            selectedIndex: 0,
            scrollOffset: 0,
            filter: "",
            consoleWidth: 60,
            consoleHeight: 7);

        var rendered = string.Join('\n', content.Lines);
        Assert.Contains("page 1/", rendered, StringComparison.Ordinal);
        Assert.Contains("PgUp/PgDn scroll", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("esc quit", rendered, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSessionSelectionContent_KeepsSelectedWrappedRowFirstLineVisible()
    {
        var sessions = Enumerable.Range(1, 4)
            .Select(i => CreateSessionWithMetadata(
                sessionId: $"{i}{i}{i}{i}{i}{i}{i}{i}-{i}{i}{i}{i}-{i}{i}{i}{i}-{i}{i}{i}{i}-{i}{i}{i}{i}{i}{i}{i}{i}{i}{i}{i}{i}",
                summary: $"Session {i} with long title that wraps",
                repository: "repo",
                branch: "main",
                cwd: $"/tmp/{i}"))
            .ToArray();

        var content = TerminalViewer.BuildSessionSelectionContent(
            sessions,
            selectedIndex: 3,
            scrollOffset: 0,
            filter: "",
            consoleWidth: 36,
            consoleHeight: 8);

        Assert.Contains(content.Lines, line => line.Contains("[blue][[•]] Session 4", StringComparison.Ordinal));
        Assert.DoesNotContain(content.Lines, line => line.Contains("[blue]    Session 4", StringComparison.Ordinal)
            && !content.Lines.Any(candidate => candidate.Contains("[blue][[•]] Session 4", StringComparison.Ordinal)));
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

    private static DiscoveredSession CreateSessionWithMetadata(
        string sessionId,
        string summary,
        string repository,
        string branch,
        string cwd,
        string? lastSeen = null,
        bool isStale = false,
        bool isExtensionProcessRunning = true,
        bool hasAttachedViewer = false,
        int attachedViewerCount = 0)
    {
        return new DiscoveredSession(
            new SessionRegistryEntry { SessionId = sessionId, Cwd = cwd, LastSeen = lastSeen ?? "" },
            IsStale: isStale,
            HasSessionDatabase: true,
            Metadata: new SessionMetadata(
                sessionId,
                Cwd: cwd,
                Repository: repository,
                Branch: branch,
                Summary: summary,
                CreatedAt: null,
                UpdatedAt: null),
            isExtensionProcessRunning,
            hasAttachedViewer,
            attachedViewerCount);
    }
}
