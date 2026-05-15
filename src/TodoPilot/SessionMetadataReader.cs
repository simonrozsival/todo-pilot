using Microsoft.Data.Sqlite;

namespace TodoPilot;

public sealed class SessionMetadataReader
{
    private readonly AppPaths _paths;

    public SessionMetadataReader(AppPaths paths)
    {
        _paths = paths;
    }

    public IReadOnlyDictionary<string, SessionMetadata> ReadAll()
    {
        if (!File.Exists(_paths.GlobalSessionStorePath))
        {
            return new Dictionary<string, SessionMetadata>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            using var connection = OpenReadOnlyConnection(_paths.GlobalSessionStorePath);

            if (!HasColumns(connection, "sessions", "id", "cwd", "repository", "branch", "summary", "created_at", "updated_at"))
            {
                return new Dictionary<string, SessionMetadata>(StringComparer.OrdinalIgnoreCase);
            }

            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, cwd, repository, branch, summary, created_at, updated_at
                FROM sessions
                """;

            using var reader = command.ExecuteReader();
            var result = new Dictionary<string, SessionMetadata>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read())
            {
                var id = GetString(reader, "id") ?? "";
                if (id.Length == 0)
                {
                    continue;
                }

                result[id] = new SessionMetadata(
                    id,
                    GetString(reader, "cwd"),
                    GetString(reader, "repository"),
                    GetString(reader, "branch"),
                    GetString(reader, "summary"),
                    GetString(reader, "created_at"),
                    GetString(reader, "updated_at"),
                    ReadUserProvidedSessionName(id));
            }

            return result;
        }
        catch (SqliteException)
        {
            return new Dictionary<string, SessionMetadata>(StringComparer.OrdinalIgnoreCase);
        }
    }

    internal static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1";
        command.Parameters.AddWithValue("$name", tableName);
        return command.ExecuteScalar() is not null;
    }

    internal static string CreateReadOnlyConnectionString(string path)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = 1
        };
        return builder.ToString();
    }

    internal static SqliteConnection OpenReadOnlyConnection(string path)
    {
        SQLitePCL.Batteries_V2.Init();
        var connection = new SqliteConnection(CreateReadOnlyConnectionString(path));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout = 1000";
        command.ExecuteNonQuery();
        return connection;
    }

    internal static bool HasColumns(SqliteConnection connection, string tableName, params string[] columnNames)
    {
        if (!TableExists(connection, tableName))
        {
            return false;
        }

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({QuoteIdentifier(tableName)})";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var name = GetString(reader, "name");
            if (!string.IsNullOrEmpty(name))
            {
                columns.Add(name);
            }
        }

        return columnNames.All(columns.Contains);
    }

    private static string QuoteIdentifier(string value) =>
        "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private string? ReadUserProvidedSessionName(string sessionId)
    {
        var workspacePath = Path.Combine(_paths.SessionStateDirectory, sessionId, "workspace.yaml");
        if (!File.Exists(workspacePath))
        {
            return null;
        }

        try
        {
            var userNamed = false;
            var name = default(string);
            foreach (var rawLine in File.ReadLines(workspacePath))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("user_named:", StringComparison.Ordinal))
                {
                    userNamed = string.Equals(line["user_named:".Length..].Trim(), "true", StringComparison.OrdinalIgnoreCase);
                }
                else if (line.StartsWith("name:", StringComparison.Ordinal))
                {
                    name = UnquoteYamlScalar(line["name:".Length..].Trim());
                }
            }

            return userNamed && !string.IsNullOrWhiteSpace(name)
                ? name
                : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string UnquoteYamlScalar(string value)
    {
        var result = value.Trim();
        while (result.Length >= 2
            && ((result[0] == '\'' && result[^1] == '\'')
                || (result[0] == '"' && result[^1] == '"')))
        {
            result = result[1..^1].Trim();
        }

        return result;
    }

    private static string? GetString(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }
}
