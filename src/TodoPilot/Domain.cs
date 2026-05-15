namespace TodoPilot;

public enum InstallScope
{
    User,
    Project
}

public sealed record ExtensionManifest
{
    public string Name { get; init; } = AppPaths.ExtensionName;

    public string Version { get; init; } = "";

    public string Scope { get; init; } = "";

    public string InstalledAt { get; init; } = "";
}

public sealed record SessionRegistryEntry
{
    public string SessionId { get; init; } = "";

    public string? WorkspacePath { get; init; }

    public string? Cwd { get; init; }

    public string Scope { get; init; } = "";

    public int Pid { get; init; }

    public string StartedAt { get; init; } = "";

    public string LastSeen { get; init; } = "";

    public string Status { get; init; } = "active";

    public string Version { get; init; } = "";
}

public sealed record ViewerAttachmentRegistryEntry
{
    public string SessionId { get; init; } = "";

    public int Pid { get; init; }

    public string? Cwd { get; init; }

    public string StartedAt { get; init; } = "";

    public string LastSeen { get; init; } = "";

    public string Status { get; init; } = "active";

    public string Version { get; init; } = "";
}

public sealed record SessionMetadata(
    string SessionId,
    string? Cwd,
    string? Repository,
    string? Branch,
    string? Summary,
    string? CreatedAt,
    string? UpdatedAt,
    string? UserName = null);

public sealed record DiscoveredSession(
    SessionRegistryEntry Registry,
    bool IsStale,
    bool HasSessionDatabase,
    SessionMetadata? Metadata,
    bool IsExtensionProcessRunning = true,
    bool HasAttachedViewer = false,
    int AttachedViewerCount = 0)
{
    public string DisplayCwd => Metadata?.Cwd ?? Registry.Cwd ?? "";
}

public enum TodoReadState
{
    Available,
    MissingDatabase,
    MissingTodosTable,
    Error
}

public sealed record TodoItem(
    string Id,
    string Title,
    string Status,
    string? Description,
    string? CreatedAt,
    string? UpdatedAt,
    IReadOnlyList<string> Dependencies)
{
    public IReadOnlyList<string> BlockedBy { get; init; } = [];
}

public sealed record TodoSnapshot(
    TodoReadState State,
    IReadOnlyList<TodoItem> Todos,
    string DataHash,
    string Message)
{
    public const string EmptyMessage = "No TODOs in this session yet.";

    public static TodoSnapshot MissingDatabase(string _) =>
        new(TodoReadState.MissingDatabase, [], "", EmptyMessage);

    public static TodoSnapshot MissingTodosTable() =>
        new(TodoReadState.MissingTodosTable, [], "", EmptyMessage);

    public static TodoSnapshot Error(string message) =>
        new(TodoReadState.Error, [], "", message);
}

public sealed record TodoListDisplayState(
    string? FocusedTodoId = null,
    string? ExpandedTodoId = null,
    bool ShowFocusMarker = false);

/// <summary>
/// Optional read-only sidebar context collected from per-session <c>session.db</c> and the global
/// <c>session-store.db</c>. Missing tables or schema drift should produce empty sections instead of
/// preventing the compact TODO list from rendering.
/// </summary>
public sealed record SessionSidebarDetails(
    LatestCheckpointSummary? LatestCheckpoint,
    IReadOnlyList<SessionFileActivity> Files,
    IReadOnlyList<SessionReference> References,
    IReadOnlyList<RecentTurnSummary> RecentTurns,
    int UnreadInboxCount,
    IReadOnlyList<InboxEntrySummary> InboxEntries,
    string DataHash)
{
    public static SessionSidebarDetails Empty { get; } = new(null, [], [], [], 0, [], "");

    public bool HasAnyContext =>
        LatestCheckpoint is not null
        || Files.Count > 0
        || References.Count > 0
        || RecentTurns.Count > 0
        || UnreadInboxCount > 0
        || InboxEntries.Count > 0;
}

public sealed record LatestCheckpointSummary(string Title, string? Overview);

public sealed record SessionFileActivity(string FilePath, string ToolName);

public sealed record SessionReference(string RefType, string RefValue);

public sealed record RecentTurnSummary(string Role, string Preview);

public sealed record InboxEntrySummary(string SenderName, string Summary);

/// <summary>
/// Revisit policy for future write-capable flows: create a new pending revisit TODO instead of
/// reopening completed work, and only send prompts back to Copilot when a supported extension API
/// exists. The current sidebar remains read-only and can surface this policy in expanded details.
/// </summary>
public static class RevisitPolicy
{
    public const string Summary = "create a new pending TODO instead of reopening completed work; prompt sending is not wired in this read-only viewer yet";
}
