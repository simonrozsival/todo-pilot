using Microsoft.Data.Sqlite;

namespace TodoPilot.Tests;

public sealed class SessionDetailReaderTests
{
    [Fact]
    public void Read_ReturnsEmptyDetailsWhenDatabasesAreMissing()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var paths = new AppPaths(directory.FullName, directory.FullName);

            var details = new SessionDetailReader(paths).Read("session-one", Path.Combine(directory.FullName, "missing.db"));

            Assert.False(details.HasAnyContext);
            Assert.NotEmpty(details.DataHash);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Read_LoadsOptionalDetailsForOnlyRequestedSession()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var paths = new AppPaths(directory.FullName, directory.FullName);
            Directory.CreateDirectory(paths.CopilotDirectory);
            var sessionDbPath = Path.Combine(directory.FullName, "session.db");
            CreateSessionDatabase(sessionDbPath);
            CreateGlobalSessionStore(paths.GlobalSessionStorePath);

            var details = new SessionDetailReader(paths).Read("session-one", sessionDbPath);

            Assert.Equal(1, details.UnreadInboxCount);
            var inbox = Assert.Single(details.InboxEntries);
            Assert.Equal("Reviewer", inbox.SenderName);
            Assert.Equal("Please revisit the UI", inbox.Summary);
            Assert.NotNull(details.LatestCheckpoint);
            Assert.Equal("Latest checkpoint", details.LatestCheckpoint.Title);
            Assert.DoesNotContain("other session", details.LatestCheckpoint.Overview, StringComparison.OrdinalIgnoreCase);
            var file = Assert.Single(details.Files);
            Assert.Equal("src/TodoPilot/TerminalViewer.cs", file.FilePath);
            var reference = Assert.Single(details.References);
            Assert.Equal("issue", reference.RefType);
            Assert.Equal("42", reference.RefValue);
            Assert.Equal(3, details.RecentTurns.Count);
            Assert.Contains(details.RecentTurns, turn => turn.Preview == "Follow-up user message");
            Assert.All(details.RecentTurns, turn => Assert.True(turn.Preview.Length <= 140));
            Assert.NotEmpty(details.DataHash);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Read_IgnoresTablesWithMissingColumns()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var paths = new AppPaths(directory.FullName, directory.FullName);
            Directory.CreateDirectory(paths.CopilotDirectory);
            ExecuteSql(paths.GlobalSessionStorePath, """
                CREATE TABLE checkpoints (session_id TEXT, title TEXT);
                INSERT INTO checkpoints (session_id, title) VALUES ('session-one', 'Incomplete schema');
                """);

            var details = new SessionDetailReader(paths).Read("session-one", Path.Combine(directory.FullName, "missing.db"));

            Assert.Null(details.LatestCheckpoint);
            Assert.False(details.HasAnyContext);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void ShortPreview_CollapsesWhitespaceAndHardCapsLongText()
    {
        var text = "hello\n\nworld " + new string('x', 200);

        var preview = SessionDetailReader.ShortPreview(text);

        Assert.StartsWith("hello world ", preview, StringComparison.Ordinal);
        Assert.EndsWith("…", preview, StringComparison.Ordinal);
        Assert.True(preview.Length <= 140);
    }

    private static void CreateSessionDatabase(string path)
    {
        ExecuteSql(path, """
            CREATE TABLE inbox_entries (
                id TEXT PRIMARY KEY,
                recipient_session_id TEXT NOT NULL,
                sender_id TEXT NOT NULL,
                sender_name TEXT NOT NULL,
                sender_type TEXT NOT NULL,
                interaction_id TEXT NOT NULL,
                sequence INTEGER NOT NULL DEFAULT 0,
                summary TEXT NOT NULL,
                content TEXT NOT NULL,
                unread INTEGER NOT NULL DEFAULT 1,
                sent_at INTEGER NOT NULL,
                read_at INTEGER,
                notified_at INTEGER
            );
            INSERT INTO inbox_entries (id, recipient_session_id, sender_id, sender_name, sender_type, interaction_id, summary, content, unread, sent_at)
            VALUES
                ('one', 'session-one', 'sender', 'Reviewer', 'agent', 'interaction', 'Please revisit the UI', 'content', 1, 20),
                ('two', 'session-one', 'sender', 'Reviewer', 'agent', 'interaction', 'Already read', 'content', 0, 30),
                ('three', 'other-session', 'sender', 'Reviewer', 'agent', 'interaction', 'Wrong session', 'content', 1, 40);
            """);
    }

    private static void CreateGlobalSessionStore(string path)
    {
        ExecuteSql(path, """
            CREATE TABLE checkpoints (
                session_id TEXT NOT NULL,
                title TEXT NOT NULL,
                overview TEXT,
                created_at TEXT NOT NULL
            );
            CREATE TABLE session_files (
                session_id TEXT NOT NULL,
                file_path TEXT NOT NULL,
                tool_name TEXT NOT NULL,
                first_seen_at TEXT NOT NULL
            );
            CREATE TABLE session_refs (
                session_id TEXT NOT NULL,
                ref_type TEXT NOT NULL,
                ref_value TEXT NOT NULL,
                created_at TEXT NOT NULL
            );
            CREATE TABLE turns (
                session_id TEXT NOT NULL,
                turn_index INTEGER NOT NULL,
                user_message TEXT,
                assistant_response TEXT
            );
            INSERT INTO checkpoints (session_id, title, overview, created_at)
            VALUES
                ('session-one', 'Old checkpoint', 'old overview', '2026-05-07T10:00:00Z'),
                ('session-one', 'Latest checkpoint', 'Relevant overview', '2026-05-07T11:00:00Z'),
                ('other-session', 'Wrong checkpoint', 'other session overview', '2026-05-07T12:00:00Z');
            INSERT INTO session_files (session_id, file_path, tool_name, first_seen_at)
            VALUES
                ('session-one', 'src/TodoPilot/TerminalViewer.cs', 'edit', '2026-05-07T11:00:00Z'),
                ('other-session', 'wrong.cs', 'edit', '2026-05-07T12:00:00Z');
            INSERT INTO session_refs (session_id, ref_type, ref_value, created_at)
            VALUES
                ('session-one', 'issue', '42', '2026-05-07T11:00:00Z'),
                ('other-session', 'issue', '99', '2026-05-07T12:00:00Z');
            INSERT INTO turns (session_id, turn_index, user_message, assistant_response)
            VALUES
                ('session-one', 1, 'A user message with [markup] and lots of words that should stay safe', 'An assistant response'),
                ('session-one', 2, 'Follow-up user message', 'Follow-up assistant response'),
                ('other-session', 2, 'Wrong user', 'Wrong assistant');
            """);
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
