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

public sealed record SessionMetadata(
    string SessionId,
    string? Cwd,
    string? Repository,
    string? Branch,
    string? Summary,
    string? CreatedAt,
    string? UpdatedAt);

public sealed record DiscoveredSession(
    SessionRegistryEntry Registry,
    bool IsStale,
    bool HasSessionDatabase,
    SessionMetadata? Metadata)
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
    IReadOnlyList<string> Dependencies);

public sealed record TodoSnapshot(
    TodoReadState State,
    IReadOnlyList<TodoItem> Todos,
    string DataHash,
    string Message)
{
    public static TodoSnapshot MissingDatabase(string path) =>
        new(TodoReadState.MissingDatabase, [], "", $"Session database not found: {path}");

    public static TodoSnapshot MissingTodosTable() =>
        new(TodoReadState.MissingTodosTable, [], "", "No todos table exists in this session yet.");

    public static TodoSnapshot Error(string message) =>
        new(TodoReadState.Error, [], "", message);
}
