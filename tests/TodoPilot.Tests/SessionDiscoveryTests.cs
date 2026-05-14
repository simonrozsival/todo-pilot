using System.Text.Json;

namespace TodoPilot.Tests;

public sealed class SessionDiscoveryTests
{
    [Fact]
    public void Discover_ReturnsActiveAndStaleSessions()
    {
        var home = Directory.CreateTempSubdirectory();
        var project = Directory.CreateTempSubdirectory();
        try
        {
            var paths = new AppPaths(home.FullName, project.FullName);
            Directory.CreateDirectory(paths.RegistrySessionsDirectory);

            WriteEntry(paths, new SessionRegistryEntry
            {
                SessionId = "11111111-1111-1111-1111-111111111111",
                LastSeen = DateTimeOffset.UtcNow.ToString("O"),
                StartedAt = DateTimeOffset.UtcNow.ToString("O"),
                Status = "active"
            });

            WriteEntry(paths, new SessionRegistryEntry
            {
                SessionId = "22222222-2222-2222-2222-222222222222",
                LastSeen = DateTimeOffset.UtcNow.AddMinutes(-10).ToString("O"),
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10).ToString("O"),
                Status = "active"
            });

            var sessions = new SessionDiscovery(paths).Discover(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(20));

            Assert.Equal(2, sessions.Count);
            Assert.False(sessions[0].IsStale);
            Assert.True(sessions[1].IsStale);
        }
        finally
        {
            home.Delete(recursive: true);
            project.Delete(recursive: true);
        }
    }

    [Fact]
    public void Discover_SkipsCorruptAndIncompleteRegistryFiles()
    {
        var home = Directory.CreateTempSubdirectory();
        var project = Directory.CreateTempSubdirectory();
        try
        {
            var paths = new AppPaths(home.FullName, project.FullName);
            Directory.CreateDirectory(paths.RegistrySessionsDirectory);
            File.WriteAllText(Path.Combine(paths.RegistrySessionsDirectory, "corrupt.json"), "{");
            File.WriteAllText(Path.Combine(paths.RegistrySessionsDirectory, "missing-id.json"), "{}");
            WriteEntry(paths, new SessionRegistryEntry
            {
                SessionId = "11111111-1111-1111-1111-111111111111",
                LastSeen = DateTimeOffset.UtcNow.ToString("O"),
                StartedAt = DateTimeOffset.UtcNow.ToString("O"),
                Status = "active"
            });

            var sessions = new SessionDiscovery(paths).Discover(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(20));

            var session = Assert.Single(sessions);
            Assert.Equal("11111111-1111-1111-1111-111111111111", session.Registry.SessionId);
        }
        finally
        {
            home.Delete(recursive: true);
            project.Delete(recursive: true);
        }
    }

    [Fact]
    public void Discover_MarksEntriesWithLiveExtensionProcess()
    {
        var home = Directory.CreateTempSubdirectory();
        var project = Directory.CreateTempSubdirectory();
        try
        {
            var paths = new AppPaths(home.FullName, project.FullName);
            Directory.CreateDirectory(paths.RegistrySessionsDirectory);

            WriteEntry(paths, new SessionRegistryEntry
            {
                SessionId = "11111111-1111-1111-1111-111111111111",
                LastSeen = DateTimeOffset.UtcNow.ToString("O"),
                StartedAt = DateTimeOffset.UtcNow.ToString("O"),
                Status = "active",
                Pid = Environment.ProcessId
            });
            WriteEntry(paths, new SessionRegistryEntry
            {
                SessionId = "22222222-2222-2222-2222-222222222222",
                LastSeen = DateTimeOffset.UtcNow.ToString("O"),
                StartedAt = DateTimeOffset.UtcNow.ToString("O"),
                Status = "active",
                Pid = int.MaxValue
            });

            var sessions = new SessionDiscovery(paths).Discover(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(20));

            Assert.Collection(
                sessions,
                session =>
                {
                    Assert.Equal("11111111-1111-1111-1111-111111111111", session.Registry.SessionId);
                    Assert.True(session.IsExtensionProcessRunning);
                },
                session =>
                {
                    Assert.Equal("22222222-2222-2222-2222-222222222222", session.Registry.SessionId);
                    Assert.False(session.IsExtensionProcessRunning);
                });
        }
        finally
        {
            home.Delete(recursive: true);
            project.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("shutdown")]
    [InlineData("stopped")]
    public void Discover_DoesNotTreatStoppedStatusesAsRunning(string status)
    {
        var home = Directory.CreateTempSubdirectory();
        var project = Directory.CreateTempSubdirectory();
        try
        {
            var paths = new AppPaths(home.FullName, project.FullName);
            Directory.CreateDirectory(paths.RegistrySessionsDirectory);
            WriteEntry(paths, new SessionRegistryEntry
            {
                SessionId = "11111111-1111-1111-1111-111111111111",
                LastSeen = DateTimeOffset.UtcNow.ToString("O"),
                StartedAt = DateTimeOffset.UtcNow.ToString("O"),
                Status = status,
                Pid = Environment.ProcessId
            });

            var session = Assert.Single(new SessionDiscovery(paths).Discover(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(20)));

            Assert.False(session.IsExtensionProcessRunning);
        }
        finally
        {
            home.Delete(recursive: true);
            project.Delete(recursive: true);
        }
    }

    [Fact]
    public void Discover_ReturnsEmptyWhenRegistryDirectoryIsMissing()
    {
        var home = Directory.CreateTempSubdirectory();
        var project = Directory.CreateTempSubdirectory();
        try
        {
            var paths = new AppPaths(home.FullName, project.FullName);

            var sessions = new SessionDiscovery(paths).Discover(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(20));

            Assert.Empty(sessions);
        }
        finally
        {
            home.Delete(recursive: true);
            project.Delete(recursive: true);
        }
    }

    [Fact]
    public void Discover_PrioritizesSessionsWithCurrentDirectory()
    {
        var home = Directory.CreateTempSubdirectory();
        var project = Directory.CreateTempSubdirectory();
        try
        {
            var paths = new AppPaths(home.FullName, project.FullName);
            Directory.CreateDirectory(paths.RegistrySessionsDirectory);
            WriteEntry(paths, new SessionRegistryEntry
            {
                SessionId = "11111111-1111-1111-1111-111111111111",
                Cwd = "/tmp/other",
                LastSeen = DateTimeOffset.UtcNow.ToString("O"),
                StartedAt = DateTimeOffset.UtcNow.ToString("O"),
                Status = "active"
            });
            WriteEntry(paths, new SessionRegistryEntry
            {
                SessionId = "22222222-2222-2222-2222-222222222222",
                Cwd = project.FullName + Path.DirectorySeparatorChar,
                LastSeen = DateTimeOffset.UtcNow.AddSeconds(-10).ToString("O"),
                StartedAt = DateTimeOffset.UtcNow.AddSeconds(-10).ToString("O"),
                Status = "active"
            });

            var sessions = CreateDiscovery(paths).Discover(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(20));

            Assert.Equal("22222222-2222-2222-2222-222222222222", sessions[0].Registry.SessionId);
        }
        finally
        {
            home.Delete(recursive: true);
            project.Delete(recursive: true);
        }
    }

    [Fact]
    public void Discover_DeprioritizesSessionsAttachedToAnotherViewer()
    {
        var home = Directory.CreateTempSubdirectory();
        var project = Directory.CreateTempSubdirectory();
        try
        {
            var paths = new AppPaths(home.FullName, project.FullName);
            Directory.CreateDirectory(paths.RegistrySessionsDirectory);
            Directory.CreateDirectory(paths.ViewerAttachmentsDirectory);
            WriteEntry(paths, new SessionRegistryEntry
            {
                SessionId = "11111111-1111-1111-1111-111111111111",
                Cwd = project.FullName,
                LastSeen = DateTimeOffset.UtcNow.ToString("O"),
                StartedAt = DateTimeOffset.UtcNow.ToString("O"),
                Status = "active"
            });
            WriteEntry(paths, new SessionRegistryEntry
            {
                SessionId = "22222222-2222-2222-2222-222222222222",
                Cwd = project.FullName,
                LastSeen = DateTimeOffset.UtcNow.AddSeconds(-10).ToString("O"),
                StartedAt = DateTimeOffset.UtcNow.AddSeconds(-10).ToString("O"),
                Status = "active"
            });
            WriteAttachment(paths, new ViewerAttachmentRegistryEntry
            {
                SessionId = "11111111-1111-1111-1111-111111111111",
                Pid = 100,
                Cwd = project.FullName,
                StartedAt = DateTimeOffset.UtcNow.ToString("O"),
                LastSeen = DateTimeOffset.UtcNow.ToString("O"),
                Status = "active"
            });

            var sessions = CreateDiscovery(paths, livePids: new HashSet<int> { 100 }).Discover(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(20));

            Assert.Equal("22222222-2222-2222-2222-222222222222", sessions[0].Registry.SessionId);
            Assert.True(sessions[1].HasAttachedViewer);
            Assert.Equal(1, sessions[1].AttachedViewerCount);
        }
        finally
        {
            home.Delete(recursive: true);
            project.Delete(recursive: true);
        }
    }

    [Fact]
    public void Discover_UsesStableNameOrderForActiveSessionsIgnoringHeartbeatJitter()
    {
        var home = Directory.CreateTempSubdirectory();
        var project = Directory.CreateTempSubdirectory();
        try
        {
            var paths = new AppPaths(home.FullName, project.FullName);
            Directory.CreateDirectory(paths.RegistrySessionsDirectory);
            WriteEntry(paths, new SessionRegistryEntry
            {
                SessionId = "22222222-2222-2222-2222-222222222222",
                Cwd = Path.Combine(home.FullName, "beta"),
                LastSeen = DateTimeOffset.UtcNow.ToString("O"),
                StartedAt = DateTimeOffset.UtcNow.ToString("O"),
                Status = "active"
            });
            WriteEntry(paths, new SessionRegistryEntry
            {
                SessionId = "11111111-1111-1111-1111-111111111111",
                Cwd = Path.Combine(home.FullName, "alpha"),
                LastSeen = DateTimeOffset.UtcNow.AddSeconds(-10).ToString("O"),
                StartedAt = DateTimeOffset.UtcNow.AddSeconds(-10).ToString("O"),
                Status = "active"
            });

            var sessions = CreateDiscovery(paths).Discover(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(20));

            Assert.Collection(
                sessions,
                session => Assert.Equal("11111111-1111-1111-1111-111111111111", session.Registry.SessionId),
                session => Assert.Equal("22222222-2222-2222-2222-222222222222", session.Registry.SessionId));
        }
        finally
        {
            home.Delete(recursive: true);
            project.Delete(recursive: true);
        }
    }

    [Fact]
    public void Discover_IgnoresSelfAndStaleViewerAttachments()
    {
        var home = Directory.CreateTempSubdirectory();
        var project = Directory.CreateTempSubdirectory();
        try
        {
            var paths = new AppPaths(home.FullName, project.FullName);
            Directory.CreateDirectory(paths.RegistrySessionsDirectory);
            Directory.CreateDirectory(paths.ViewerAttachmentsDirectory);
            WriteEntry(paths, new SessionRegistryEntry
            {
                SessionId = "11111111-1111-1111-1111-111111111111",
                Cwd = project.FullName,
                LastSeen = DateTimeOffset.UtcNow.ToString("O"),
                StartedAt = DateTimeOffset.UtcNow.ToString("O"),
                Status = "active"
            });
            WriteAttachment(paths, new ViewerAttachmentRegistryEntry
            {
                SessionId = "11111111-1111-1111-1111-111111111111",
                Pid = 99,
                Cwd = project.FullName,
                StartedAt = DateTimeOffset.UtcNow.ToString("O"),
                LastSeen = DateTimeOffset.UtcNow.ToString("O"),
                Status = "active"
            });
            WriteAttachment(paths, new ViewerAttachmentRegistryEntry
            {
                SessionId = "11111111-1111-1111-1111-111111111111",
                Pid = 100,
                Cwd = project.FullName,
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"),
                LastSeen = DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"),
                Status = "active"
            });

            var session = Assert.Single(CreateDiscovery(paths, livePids: new HashSet<int> { 99, 100 }).Discover(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(20)));

            Assert.False(session.HasAttachedViewer);
            Assert.Equal(0, session.AttachedViewerCount);
        }
        finally
        {
            home.Delete(recursive: true);
            project.Delete(recursive: true);
        }
    }

    private static void WriteEntry(AppPaths paths, SessionRegistryEntry entry)
    {
        var path = Path.Combine(paths.RegistrySessionsDirectory, $"{entry.SessionId}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(entry, AppJsonContext.Default.SessionRegistryEntry));
    }

    private static void WriteAttachment(AppPaths paths, ViewerAttachmentRegistryEntry entry)
    {
        var path = paths.GetViewerAttachmentPath(entry.SessionId, entry.Pid);
        File.WriteAllText(path, JsonSerializer.Serialize(entry, AppJsonContext.Default.ViewerAttachmentRegistryEntry));
    }

    private static SessionDiscovery CreateDiscovery(AppPaths paths, IReadOnlySet<int>? livePids = null) =>
        new(paths, _ => true, pid => livePids?.Contains(pid) == true, currentProcessId: 99);
}
