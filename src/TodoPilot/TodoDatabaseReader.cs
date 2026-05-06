using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace TodoPilot;

public sealed class TodoDatabaseReader
{
    public TodoSnapshot Read(string databasePath)
    {
        if (!File.Exists(databasePath))
        {
            return TodoSnapshot.MissingDatabase(databasePath);
        }

        try
        {
            SQLitePCL.Batteries_V2.Init();
            using var connection = new SqliteConnection(SessionMetadataReader.CreateReadOnlyConnectionString(databasePath));
            connection.Open();

            if (!SessionMetadataReader.TableExists(connection, "todos"))
            {
                return TodoSnapshot.MissingTodosTable();
            }

            var dependencies = SessionMetadataReader.TableExists(connection, "todo_deps")
                ? ReadDependencies(connection)
                : new Dictionary<string, List<string>>(StringComparer.Ordinal);

            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, title, description, status, created_at, updated_at
                FROM todos
                ORDER BY created_at DESC, id DESC
                """;

            var todos = new List<TodoItem>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var id = GetString(reader, "id") ?? "";
                if (id.Length == 0)
                {
                    continue;
                }

                dependencies.TryGetValue(id, out var deps);
                todos.Add(new TodoItem(
                    id,
                    GetString(reader, "title") ?? id,
                    GetString(reader, "status") ?? "pending",
                    GetString(reader, "description"),
                    GetString(reader, "created_at"),
                    GetString(reader, "updated_at"),
                    deps ?? []));
            }

            return new TodoSnapshot(TodoReadState.Available, todos, ComputeHash(todos), $"{todos.Count} todo(s)");
        }
        catch (SqliteException ex)
        {
            return TodoSnapshot.Error($"Unable to read session database: {ex.SqliteErrorCode} {ex.Message}");
        }
    }

    private static Dictionary<string, List<string>> ReadDependencies(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT todo_id, depends_on FROM todo_deps";
        using var reader = command.ExecuteReader();

        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        while (reader.Read())
        {
            var todoId = GetString(reader, "todo_id");
            var dependsOn = GetString(reader, "depends_on");
            if (string.IsNullOrEmpty(todoId) || string.IsNullOrEmpty(dependsOn))
            {
                continue;
            }

            if (!result.TryGetValue(todoId, out var deps))
            {
                deps = [];
                result[todoId] = deps;
            }

            deps.Add(dependsOn);
        }

        return result;
    }

    private static string ComputeHash(IReadOnlyList<TodoItem> todos)
    {
        var builder = new StringBuilder();
        foreach (var todo in todos)
        {
            builder
                .Append(todo.Id).Append('\u001f')
                .Append(todo.Title).Append('\u001f')
                .Append(todo.Status).Append('\u001f')
                .Append(todo.Description).Append('\u001f')
                .Append(todo.CreatedAt).Append('\u001f')
                .Append(todo.UpdatedAt).Append('\u001f')
                .AppendJoin(',', todo.Dependencies).Append('\u001e');
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(bytes);
    }

    private static string? GetString(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }
}
