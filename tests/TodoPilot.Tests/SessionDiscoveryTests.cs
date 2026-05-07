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

    private static void WriteEntry(AppPaths paths, SessionRegistryEntry entry)
    {
        var path = Path.Combine(paths.RegistrySessionsDirectory, $"{entry.SessionId}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(entry, AppJsonContext.Default.SessionRegistryEntry));
    }
}
