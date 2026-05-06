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
            using var connection = new SqliteConnection(CreateReadOnlyConnectionString(_paths.GlobalSessionStorePath));
            connection.Open();

            if (!TableExists(connection, "sessions"))
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
                    GetString(reader, "updated_at"));
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
            Pooling = false
        };
        return builder.ToString();
    }

    private static string? GetString(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }
}
