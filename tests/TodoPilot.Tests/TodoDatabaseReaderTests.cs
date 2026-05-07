using Microsoft.Data.Sqlite;

namespace TodoPilot.Tests;

public sealed class TodoDatabaseReaderTests
{
    [Fact]
    public void Read_ReturnsMissingDatabaseForAbsentFile()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "session.db");
        var snapshot = new TodoDatabaseReader().Read(path);

        Assert.Equal(TodoReadState.MissingDatabase, snapshot.State);
        Assert.Equal(TodoSnapshot.EmptyMessage, snapshot.Message);
        Assert.DoesNotContain(path, snapshot.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_ReturnsEmptyMessageForDatabaseWithoutTodosTable()
    {
        var directory = Directory.CreateTempSubdirectory();
        var dbPath = Path.Combine(directory.FullName, "session.db");
        try
        {
            SQLitePCL.Batteries_V2.Init();
            using (var connection = new SqliteConnection($"Data Source={dbPath}"))
            {
                connection.Open();
            }

            var snapshot = new TodoDatabaseReader().Read(dbPath);

            Assert.Equal(TodoReadState.MissingTodosTable, snapshot.State);
            Assert.Equal(TodoSnapshot.EmptyMessage, snapshot.Message);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Read_ReturnsTodosDependenciesAndUnfinishedDependencies()
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
            Assert.Equal(["read-db"], todo.BlockedBy);
            Assert.NotEmpty(snapshot.DataHash);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Read_OrdersByWorkflowReadinessThenIdDescending()
    {
        var directory = Directory.CreateTempSubdirectory();
        var dbPath = Path.Combine(directory.FullName, "session.db");
        try
        {
            CreateEmptyTodoDatabase(dbPath);
            ExecuteSql(dbPath, """
                INSERT INTO todos (id, title, description, status, created_at, updated_at)
                VALUES
                    ('wip-a', 'WIP A', NULL, 'in_progress', NULL, NULL),
                    ('wip-z', 'WIP Z', NULL, 'in_progress', NULL, NULL),
                    ('ready-a', 'Ready A', NULL, 'pending', NULL, NULL),
                    ('ready-z', 'Ready Z', NULL, 'pending', NULL, NULL),
                    ('dep-open', 'Open dependency', NULL, 'pending', NULL, NULL),
                    ('blocked-by-dep-a', 'Blocked by dependency A', NULL, 'pending', NULL, NULL),
                    ('blocked-by-dep-z', 'Blocked by dependency Z', NULL, 'pending', NULL, NULL),
                    ('explicit-a', 'Explicitly blocked A', NULL, 'blocked', NULL, NULL),
                    ('explicit-z', 'Explicitly blocked Z', NULL, 'blocked', NULL, NULL),
                    ('done-a', 'Done A', NULL, 'done', NULL, NULL),
                    ('done-z', 'Done Z', NULL, 'done', NULL, NULL);
                INSERT INTO todo_deps (todo_id, depends_on)
                VALUES
                    ('blocked-by-dep-a', 'missing-dependency'),
                    ('blocked-by-dep-z', 'dep-open');
                """);

            var snapshot = new TodoDatabaseReader().Read(dbPath);

            Assert.Equal(
                [
                    "wip-z",
                    "wip-a",
                    "ready-z",
                    "ready-a",
                    "dep-open",
                    "blocked-by-dep-z",
                    "blocked-by-dep-a",
                    "explicit-z",
                    "explicit-a",
                    "done-z",
                    "done-a"
                ],
                snapshot.Todos.Select(todo => todo.Id));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Read_TreatsDependenciesAsSatisfiedOnlyWhenTargetIsDone()
    {
        var directory = Directory.CreateTempSubdirectory();
        var dbPath = Path.Combine(directory.FullName, "session.db");
        try
        {
            CreateEmptyTodoDatabase(dbPath);
            ExecuteSql(dbPath, """
                INSERT INTO todos (id, title, description, status, created_at, updated_at)
                VALUES
                    ('wait', 'Wait', NULL, 'pending', NULL, NULL),
                    ('alpha', 'Alpha', NULL, 'done', NULL, NULL),
                    ('beta', 'Beta', NULL, 'pending', NULL, NULL);
                INSERT INTO todo_deps (todo_id, depends_on)
                VALUES
                    ('wait', 'missing'),
                    ('wait', 'beta'),
                    ('wait', 'alpha'),
                    ('wait', 'beta');
                """);

            var snapshot = new TodoDatabaseReader().Read(dbPath);
            var todo = snapshot.Todos.Single(todo => todo.Id == "wait");

            Assert.Equal(["alpha", "beta", "missing"], todo.Dependencies);
            Assert.Equal(["beta", "missing"], todo.BlockedBy);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Read_MovesDependencyBlockedTodoWhenDependencyCompletesAndHashChanges()
    {
        var directory = Directory.CreateTempSubdirectory();
        var dbPath = Path.Combine(directory.FullName, "session.db");
        try
        {
            CreateEmptyTodoDatabase(dbPath);
            ExecuteSql(dbPath, """
                INSERT INTO todos (id, title, description, status, created_at, updated_at)
                VALUES
                    ('wait', 'Wait', NULL, 'pending', NULL, NULL),
                    ('target', 'Target', NULL, 'pending', NULL, NULL);
                INSERT INTO todo_deps (todo_id, depends_on)
                VALUES ('wait', 'target');
                """);

            var before = new TodoDatabaseReader().Read(dbPath);

            ExecuteSql(dbPath, "UPDATE todos SET status = 'done' WHERE id = 'target';");
            var after = new TodoDatabaseReader().Read(dbPath);

            Assert.Equal(["target", "wait"], before.Todos.Select(todo => todo.Id));
            Assert.Equal(["wait", "target"], after.Todos.Select(todo => todo.Id));
            Assert.NotEqual(before.DataHash, after.DataHash);
            Assert.Empty(after.Todos.Single(todo => todo.Id == "wait").BlockedBy);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Read_ClassifiesDependencyCyclesAsBlocked()
    {
        var directory = Directory.CreateTempSubdirectory();
        var dbPath = Path.Combine(directory.FullName, "session.db");
        try
        {
            CreateEmptyTodoDatabase(dbPath);
            ExecuteSql(dbPath, """
                INSERT INTO todos (id, title, description, status, created_at, updated_at)
                VALUES
                    ('cycle-a', 'Cycle A', NULL, 'pending', NULL, NULL),
                    ('cycle-b', 'Cycle B', NULL, 'pending', NULL, NULL);
                INSERT INTO todo_deps (todo_id, depends_on)
                VALUES
                    ('cycle-a', 'cycle-b'),
                    ('cycle-b', 'cycle-a');
                """);

            var snapshot = new TodoDatabaseReader().Read(dbPath);

            Assert.Equal(["cycle-b", "cycle-a"], snapshot.Todos.Select(todo => todo.Id));
            Assert.Equal(["cycle-b"], snapshot.Todos.Single(todo => todo.Id == "cycle-a").BlockedBy);
            Assert.Equal(["cycle-a"], snapshot.Todos.Single(todo => todo.Id == "cycle-b").BlockedBy);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static void CreateTodoDatabase(string path)
    {
        CreateEmptyTodoDatabase(path);
        ExecuteSql(path, """
            INSERT INTO todos (id, title, description, status, created_at, updated_at)
            VALUES ('implement-ui', 'Build UI', 'Use Spectre.Console', 'pending', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
            INSERT INTO todo_deps (todo_id, depends_on)
            VALUES ('implement-ui', 'read-db');
            """);
    }

    private static void CreateEmptyTodoDatabase(string path)
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
            """;
        command.ExecuteNonQuery();
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
