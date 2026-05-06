namespace TodoPilot;

public sealed class AppPaths
{
    public const string ExtensionName = "todo-pilot";

    public AppPaths(string? homeDirectory = null, string? currentDirectory = null)
    {
        HomeDirectory = Path.GetFullPath(homeDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        CurrentDirectory = Path.GetFullPath(currentDirectory ?? Environment.CurrentDirectory);
    }

    public string HomeDirectory { get; }

    public string CurrentDirectory { get; }

    public string CopilotDirectory => Path.Combine(HomeDirectory, ".copilot");

    public string GlobalSessionStorePath => Path.Combine(CopilotDirectory, "session-store.db");

    public string SessionStateDirectory => Path.Combine(CopilotDirectory, "session-state");

    public string RegistrySessionsDirectory => Path.Combine(CopilotDirectory, ExtensionName, "sessions");

    public string UserExtensionDirectory => Path.Combine(CopilotDirectory, "extensions", ExtensionName);

    public string ProjectRoot => FindProjectRoot(CurrentDirectory);

    public string ProjectExtensionDirectory => Path.Combine(ProjectRoot, ".github", "extensions", ExtensionName);

    public string GetExtensionDirectory(InstallScope scope) =>
        scope == InstallScope.User ? UserExtensionDirectory : ProjectExtensionDirectory;

    public string GetSessionDatabasePath(string sessionId) =>
        Path.Combine(SessionStateDirectory, sessionId, "session.db");

    public static string FindProjectRoot(string startDirectory)
    {
        var current = new DirectoryInfo(Path.GetFullPath(startDirectory));
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return Path.GetFullPath(startDirectory);
    }
}
