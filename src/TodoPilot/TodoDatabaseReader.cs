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
            using var connection = SessionMetadataReader.OpenReadOnlyConnection(databasePath);

            if (!SessionMetadataReader.TableExists(connection, "todos"))
            {
                return TodoSnapshot.MissingTodosTable();
            }

            var dependenciesByTodoId = SessionMetadataReader.TableExists(connection, "todo_deps")
                ? ReadDependencies(connection)
                : new Dictionary<string, List<string>>(StringComparer.Ordinal);

            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, title, description, status, created_at, updated_at
                FROM todos
                ORDER BY rowid
                """;

            var rows = new List<TodoRow>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var id = GetString(reader, "id") ?? "";
                if (id.Length == 0)
                {
                    continue;
                }

                rows.Add(new TodoRow(
                    id,
                    GetString(reader, "title") ?? id,
                    GetString(reader, "status") ?? "pending",
                    GetString(reader, "description"),
                    GetString(reader, "created_at"),
                    GetString(reader, "updated_at")));
            }

            var statusesByTodoId = rows.ToDictionary(row => row.Id, row => row.Status, StringComparer.Ordinal);
            var todos = rows
                .Select(row =>
                {
                    dependenciesByTodoId.TryGetValue(row.Id, out var rawDependencies);
                    var dependencies = rawDependencies?
                        .Distinct(StringComparer.Ordinal)
                        .ToArray()
                        ?? [];
                    var blockedBy = dependencies
                        .Where(dependency => !statusesByTodoId.TryGetValue(dependency, out var status) || status != "done")
                        .Order(StringComparer.Ordinal)
                        .ToArray();

                    return new TodoItem(
                        row.Id,
                        row.Title,
                        row.Status,
                        row.Description,
                        row.CreatedAt,
                        row.UpdatedAt,
                        dependencies)
                    {
                        BlockedBy = blockedBy
                    };
                })
                .ToArray();

            return new TodoSnapshot(TodoReadState.Available, todos, ComputeHash(todos), $"{todos.Length} todo(s)");
        }
        catch (SqliteException ex)
        {
            return TodoSnapshot.Error($"Unable to read session database: {ex.SqliteErrorCode} {ex.Message}");
        }
    }

    private static Dictionary<string, List<string>> ReadDependencies(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT todo_id, depends_on FROM todo_deps ORDER BY todo_id, depends_on";
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
                .AppendJoin(',', todo.Dependencies).Append('\u001f')
                .AppendJoin(',', todo.BlockedBy).Append('\u001e');
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(bytes);
    }

    private static string? GetString(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private sealed record TodoRow(
        string Id,
        string Title,
        string Status,
        string? Description,
        string? CreatedAt,
        string? UpdatedAt);
}
