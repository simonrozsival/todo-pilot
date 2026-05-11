using System.Text.Json;
using System.ComponentModel;
using System.Diagnostics;

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

    public DiscoveredSession RefreshMetadata(DiscoveredSession session)
    {
        var metadata = _metadataReader.ReadAll();
        metadata.TryGetValue(session.Registry.SessionId, out var sessionMetadata);
        return session with { Metadata = sessionMetadata };
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
            var isExtensionProcessRunning = IsLiveExtensionProcess(entry);
            var hasSessionDatabase = File.Exists(_paths.GetSessionDatabasePath(entry.SessionId));
            metadata.TryGetValue(entry.SessionId, out var sessionMetadata);

            sessions.Add(new DiscoveredSession(entry, isStale, hasSessionDatabase, sessionMetadata, isExtensionProcessRunning));
        }

        return sessions
            .OrderBy(s => s.IsStale)
            .ThenByDescending(s => s.IsExtensionProcessRunning)
            .ThenByDescending(s => ParseDate(s.Registry.LastSeen) ?? DateTimeOffset.MinValue)
            .ToArray();
    }

    private static DateTimeOffset? ParseDate(string value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private static bool IsLiveExtensionProcess(SessionRegistryEntry entry)
    {
        if (entry.Pid <= 0 || entry.Status is "shutdown" or "stopped")
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(entry.Pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

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
