using Microsoft.Data.Sqlite;

namespace TodoPilot.Tests;

public sealed class TodoDatabaseReaderTests
{
    [Fact]
    public void Read_ReturnsMissingDatabaseForAbsentFile()
    {
        var snapshot = new TodoDatabaseReader().Read(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "session.db"));

        Assert.Equal(TodoReadState.MissingDatabase, snapshot.State);
    }

    [Fact]
    public void Read_ReturnsTodosAndDependencies()
    {
        var directory = Directory.CreateTempSubdirectory();
        var dbPath = Path.Combine(directory.FullName, "session.db");
        try
        {
            CreateTodoDatabase(dbPath);

            var snapshot = new TodoDatabaseReader().Read(dbPath);

            Assert.Equal(TodoReadState.Available, snapshot.State);
            var todo = Assert.Single(snapshot.Todos);
            Assert.Equal("implement-ui", todo.Id);
            Assert.Equal("Build UI", todo.Title);
            Assert.Equal("pending", todo.Status);
            Assert.Equal(["read-db"], todo.Dependencies);
            Assert.NotEmpty(snapshot.DataHash);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Read_ReturnsNewestTodosFirst()
    {
        var directory = Directory.CreateTempSubdirectory();
        var dbPath = Path.Combine(directory.FullName, "session.db");
        try
        {
            CreateTodoDatabase(dbPath);
            SQLitePCL.Batteries_V2.Init();
            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO todos (id, title, description, status, created_at, updated_at)
                VALUES
                    ('oldest', 'Oldest', NULL, 'pending', '2026-01-01T00:00:00Z', NULL),
                    ('newest', 'Newest', NULL, 'pending', '2026-01-03T00:00:00Z', NULL),
                    ('middle', 'Middle', NULL, 'pending', '2026-01-02T00:00:00Z', NULL);
                """;
            command.ExecuteNonQuery();

            var snapshot = new TodoDatabaseReader().Read(dbPath);

            Assert.Equal(["newest", "middle", "oldest", "implement-ui"], snapshot.Todos.Select(todo => todo.Id));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static void CreateTodoDatabase(string path)
    {
        SQLitePCL.Batteries_V2.Init();
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE todos (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                description TEXT,
                status TEXT NOT NULL,
                created_at TEXT,
                updated_at TEXT
            );
            CREATE TABLE todo_deps (
                todo_id TEXT NOT NULL,
                depends_on TEXT NOT NULL
            );
            INSERT INTO todos (id, title, description, status, created_at, updated_at)
            VALUES ('implement-ui', 'Build UI', 'Use Spectre.Console', 'pending', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
            INSERT INTO todo_deps (todo_id, depends_on)
            VALUES ('implement-ui', 'read-db');
            """;
        command.ExecuteNonQuery();
    }
}
