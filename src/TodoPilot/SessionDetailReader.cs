using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace TodoPilot;

/// <summary>
/// Inventories and reads optional Phase 2 sidebar context. Per-session <c>session.db</c> contributes
/// <c>inbox_entries</c>; the global <c>session-store.db</c> contributes <c>checkpoints</c>,
/// <c>session_files</c>, <c>session_refs</c>, and <c>turns</c>.
/// </summary>
public sealed class SessionDetailReader
{
    private const int PreviewLimit = 140;
    private const int RecentTurnSummaryLimit = 3;
    private readonly AppPaths _paths;

    public SessionDetailReader(AppPaths paths)
    {
        _paths = paths;
    }

    public SessionSidebarDetails Read(string sessionId, string sessionDatabasePath)
    {
        var latestCheckpoint = default(LatestCheckpointSummary);
        var files = Array.Empty<SessionFileActivity>();
        var references = Array.Empty<SessionReference>();
        var recentTurns = Array.Empty<RecentTurnSummary>();
        var unreadInboxCount = 0;
        var inboxEntries = Array.Empty<InboxEntrySummary>();

        if (File.Exists(sessionDatabasePath))
        {
            try
            {
                using var connection = SessionMetadataReader.OpenReadOnlyConnection(sessionDatabasePath);
                if (SessionMetadataReader.HasColumns(connection, "inbox_entries", "recipient_session_id", "sender_name", "summary", "unread", "sent_at"))
                {
                    unreadInboxCount = ReadUnreadInboxCount(connection, sessionId);
                    inboxEntries = ReadInboxEntries(connection, sessionId);
                }
            }
            catch (SqliteException)
            {
                unreadInboxCount = 0;
                inboxEntries = [];
            }
        }

        if (File.Exists(_paths.GlobalSessionStorePath))
        {
            try
            {
                using var connection = SessionMetadataReader.OpenReadOnlyConnection(_paths.GlobalSessionStorePath);
                latestCheckpoint = ReadLatestCheckpoint(connection, sessionId);
                files = ReadFiles(connection, sessionId);
                references = ReadReferences(connection, sessionId);
                recentTurns = ReadRecentTurns(connection, sessionId);
            }
            catch (SqliteException)
            {
                latestCheckpoint = null;
                files = [];
                references = [];
                recentTurns = [];
            }
        }

        var details = new SessionSidebarDetails(
            latestCheckpoint,
            files,
            references,
            recentTurns,
            unreadInboxCount,
            inboxEntries,
            DataHash: "");
        return details with { DataHash = ComputeHash(details) };
    }

    private static int ReadUnreadInboxCount(SqliteConnection connection, string sessionId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM inbox_entries
            WHERE recipient_session_id = $session_id AND unread != 0
            """;
        command.Parameters.AddWithValue("$session_id", sessionId);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static InboxEntrySummary[] ReadInboxEntries(SqliteConnection connection, string sessionId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT sender_name, summary
            FROM inbox_entries
            WHERE recipient_session_id = $session_id AND unread != 0
            ORDER BY sent_at DESC
            LIMIT 3
            """;
        command.Parameters.AddWithValue("$session_id", sessionId);
        using var reader = command.ExecuteReader();

        var result = new List<InboxEntrySummary>();
        while (reader.Read())
        {
            result.Add(new InboxEntrySummary(
                GetString(reader, "sender_name") ?? "unknown",
                ShortPreview(GetString(reader, "summary"))));
        }

        return result.ToArray();
    }

    private static LatestCheckpointSummary? ReadLatestCheckpoint(SqliteConnection connection, string sessionId)
    {
        if (!SessionMetadataReader.HasColumns(connection, "checkpoints", "session_id", "title", "overview", "created_at"))
        {
            return null;
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT title, overview
            FROM checkpoints
            WHERE session_id = $session_id
            ORDER BY created_at DESC
            LIMIT 1
            """;
        command.Parameters.AddWithValue("$session_id", sessionId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new LatestCheckpointSummary(
            ShortPreview(GetString(reader, "title")),
            ShortPreviewOrNull(GetString(reader, "overview")));
    }

    private static SessionFileActivity[] ReadFiles(SqliteConnection connection, string sessionId)
    {
        if (!SessionMetadataReader.HasColumns(connection, "session_files", "session_id", "file_path", "tool_name", "first_seen_at"))
        {
            return [];
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT file_path, tool_name
            FROM session_files
            WHERE session_id = $session_id
            ORDER BY first_seen_at DESC
            LIMIT 3
            """;
        command.Parameters.AddWithValue("$session_id", sessionId);
        using var reader = command.ExecuteReader();

        var result = new List<SessionFileActivity>();
        while (reader.Read())
        {
            var path = GetString(reader, "file_path");
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            result.Add(new SessionFileActivity(path, GetString(reader, "tool_name") ?? "file"));
        }

        return result.ToArray();
    }

    private static SessionReference[] ReadReferences(SqliteConnection connection, string sessionId)
    {
        if (!SessionMetadataReader.HasColumns(connection, "session_refs", "session_id", "ref_type", "ref_value", "created_at"))
        {
            return [];
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ref_type, ref_value
            FROM session_refs
            WHERE session_id = $session_id
            ORDER BY created_at DESC
            LIMIT 3
            """;
        command.Parameters.AddWithValue("$session_id", sessionId);
        using var reader = command.ExecuteReader();

        var result = new List<SessionReference>();
        while (reader.Read())
        {
            var value = GetString(reader, "ref_value");
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            result.Add(new SessionReference(GetString(reader, "ref_type") ?? "ref", value));
        }

        return result.ToArray();
    }

    private static RecentTurnSummary[] ReadRecentTurns(SqliteConnection connection, string sessionId)
    {
        if (!SessionMetadataReader.HasColumns(connection, "turns", "session_id", "turn_index", "user_message", "assistant_response"))
        {
            return [];
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT user_message, assistant_response
            FROM turns
            WHERE session_id = $session_id
            ORDER BY turn_index DESC
            LIMIT 3
            """;
        command.Parameters.AddWithValue("$session_id", sessionId);
        using var reader = command.ExecuteReader();

        var result = new List<RecentTurnSummary>();
        while (reader.Read() && result.Count < RecentTurnSummaryLimit)
        {
            var user = ShortPreviewOrNull(GetString(reader, "user_message"));
            if (user is not null)
            {
                result.Add(new RecentTurnSummary("user", user));
            }

            if (result.Count >= RecentTurnSummaryLimit)
            {
                break;
            }

            var assistant = ShortPreviewOrNull(GetString(reader, "assistant_response"));
            if (assistant is not null)
            {
                result.Add(new RecentTurnSummary("assistant", assistant));
            }
        }

        return result.ToArray();
    }

    private static string? GetString(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static string? ShortPreviewOrNull(string? value)
    {
        var preview = ShortPreview(value);
        return preview.Length == 0 ? null : preview;
    }

    public static string ShortPreview(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= PreviewLimit
            ? normalized
            : $"{normalized[..(PreviewLimit - 1)]}…";
    }

    private static string ComputeHash(SessionSidebarDetails details)
    {
        var builder = new StringBuilder()
            .Append(details.LatestCheckpoint?.Title).Append('\u001f')
            .Append(details.LatestCheckpoint?.Overview).Append('\u001f')
            .Append(details.UnreadInboxCount).Append('\u001f');
        foreach (var file in details.Files)
        {
            builder.Append(file.FilePath).Append(':').Append(file.ToolName).Append('\u001e');
        }

        foreach (var reference in details.References)
        {
            builder.Append(reference.RefType).Append(':').Append(reference.RefValue).Append('\u001e');
        }

        foreach (var turn in details.RecentTurns)
        {
            builder.Append(turn.Role).Append(':').Append(turn.Preview).Append('\u001e');
        }

        foreach (var inbox in details.InboxEntries)
        {
            builder.Append(inbox.SenderName).Append(':').Append(inbox.Summary).Append('\u001e');
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(bytes);
    }
}
