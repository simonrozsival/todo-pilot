using System.Text.Json;

namespace TodoPilot;

public sealed class SessionDiscovery
{
    private readonly AppPaths _paths;
    private readonly SessionMetadataReader _metadataReader;

    public SessionDiscovery(AppPaths paths)
    {
        _paths = paths;
        _metadataReader = new SessionMetadataReader(paths);
    }

    public IReadOnlyList<DiscoveredSession> Discover(DateTimeOffset now, TimeSpan staleAfter)
    {
        if (!Directory.Exists(_paths.RegistrySessionsDirectory))
        {
            return [];
        }

        var metadata = _metadataReader.ReadAll();
        var sessions = new List<DiscoveredSession>();

        foreach (var file in EnumerateRegistryFiles(_paths.RegistrySessionsDirectory))
        {
            SessionRegistryEntry? entry;
            try
            {
                var json = File.ReadAllText(file);
                entry = JsonSerializer.Deserialize(json, AppJsonContext.Default.SessionRegistryEntry);
            }
            catch (JsonException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            if (entry is null || string.IsNullOrWhiteSpace(entry.SessionId))
            {
                continue;
            }

            var lastSeen = ParseDate(entry.LastSeen);
            var isStale = lastSeen is null || now - lastSeen.Value > staleAfter || entry.Status is "shutdown" or "stopped";
            var hasSessionDatabase = File.Exists(_paths.GetSessionDatabasePath(entry.SessionId));
            metadata.TryGetValue(entry.SessionId, out var sessionMetadata);

            sessions.Add(new DiscoveredSession(entry, isStale, hasSessionDatabase, sessionMetadata));
        }

        return sessions
            .OrderBy(s => s.IsStale)
            .ThenByDescending(s => ParseDate(s.Registry.LastSeen) ?? DateTimeOffset.MinValue)
            .ToArray();
    }

    private static DateTimeOffset? ParseDate(string value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private static IEnumerable<string> EnumerateRegistryFiles(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*.json").ToArray();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }
}
