using Microsoft.Data.Sqlite;

namespace TodoPilot.Tests;

public sealed class SessionMetadataReaderTests
{
    [Fact]
    public void ReadAll_ReturnsEmptyWhenSessionsSchemaIsIncomplete()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var paths = new AppPaths(directory.FullName, directory.FullName);
            Directory.CreateDirectory(paths.CopilotDirectory);
            ExecuteSql(paths.GlobalSessionStorePath, """
                CREATE TABLE sessions (id TEXT PRIMARY KEY, summary TEXT);
                INSERT INTO sessions (id, summary) VALUES ('session-one', 'Incomplete');
                """);

            var metadata = new SessionMetadataReader(paths).ReadAll();

            Assert.Empty(metadata);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void ReadAll_LoadsCompleteSessionMetadata()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var paths = new AppPaths(directory.FullName, directory.FullName);
            Directory.CreateDirectory(paths.CopilotDirectory);
            ExecuteSql(paths.GlobalSessionStorePath, """
                CREATE TABLE sessions (
                    id TEXT PRIMARY KEY,
                    cwd TEXT,
                    repository TEXT,
                    branch TEXT,
                    summary TEXT,
                    created_at TEXT,
                    updated_at TEXT
                );
                INSERT INTO sessions (id, cwd, repository, branch, summary, created_at, updated_at)
                VALUES ('session-one', '/tmp/project', 'repo', 'main', 'Summary', 'created', 'updated');
                """);

            var metadata = new SessionMetadataReader(paths).ReadAll();

            var session = Assert.Single(metadata.Values);
            Assert.Equal("session-one", session.SessionId);
            Assert.Equal("/tmp/project", session.Cwd);
            Assert.Equal("repo", session.Repository);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void ReadAll_PrefersUserNamedWorkspaceNameOverGeneratedSummary()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var paths = new AppPaths(directory.FullName, directory.FullName);
            Directory.CreateDirectory(paths.CopilotDirectory);
            ExecuteSql(paths.GlobalSessionStorePath, """
                CREATE TABLE sessions (
                    id TEXT PRIMARY KEY,
                    cwd TEXT,
                    repository TEXT,
                    branch TEXT,
                    summary TEXT,
                    created_at TEXT,
                    updated_at TEXT
                );
                INSERT INTO sessions (id, cwd, repository, branch, summary, created_at, updated_at)
                VALUES ('session-one', '/tmp/project', 'repo', 'main', 'Generated summary', 'created', 'updated');
                """);
            var sessionDirectory = Directory.CreateDirectory(Path.Combine(paths.SessionStateDirectory, "session-one"));
            File.WriteAllText(Path.Combine(sessionDirectory.FullName, "workspace.yaml"), """
                id: session-one
                name: '"Mono x86 crash"'
                user_named: true
                """);

            var metadata = new SessionMetadataReader(paths).ReadAll();

            var session = Assert.Single(metadata.Values);
            Assert.Equal("Generated summary", session.Summary);
            Assert.Equal("Mono x86 crash", session.UserName);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static void ExecuteSql(string path, string sql)
    {
        SQLitePCL.Batteries_V2.Init();
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
