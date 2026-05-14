using System.Text.Json;
using System.ComponentModel;
using System.Diagnostics;

namespace TodoPilot;

public sealed class SessionDiscovery
{
    private readonly AppPaths _paths;
    private readonly SessionMetadataReader _metadataReader;
    private readonly Func<SessionRegistryEntry, bool> _isExtensionProcessRunning;
    private readonly Func<int, bool> _isProcessRunning;
    private readonly int _currentProcessId;

    public SessionDiscovery(AppPaths paths)
        : this(paths, IsLiveExtensionProcess, IsProcessRunning, Environment.ProcessId)
    {
    }

    internal SessionDiscovery(
        AppPaths paths,
        Func<SessionRegistryEntry, bool> isExtensionProcessRunning,
        Func<int, bool> isProcessRunning,
        int currentProcessId)
    {
        _paths = paths;
        _metadataReader = new SessionMetadataReader(paths);
        _isExtensionProcessRunning = isExtensionProcessRunning;
        _isProcessRunning = isProcessRunning;
        _currentProcessId = currentProcessId;
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
        var attachedViewers = ReadLiveViewerAttachments(now, staleAfter);
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
            var isExtensionProcessRunning = _isExtensionProcessRunning(entry);
            var hasSessionDatabase = File.Exists(_paths.GetSessionDatabasePath(entry.SessionId));
            metadata.TryGetValue(entry.SessionId, out var sessionMetadata);
            var attachedViewerCount = attachedViewers.TryGetValue(entry.SessionId, out var viewers)
                ? viewers.Count
                : 0;

            sessions.Add(new DiscoveredSession(
                entry,
                isStale,
                hasSessionDatabase,
                sessionMetadata,
                isExtensionProcessRunning,
                HasAttachedViewer: attachedViewerCount > 0,
                AttachedViewerCount: attachedViewerCount));
        }

        return sessions
            .OrderBy(s => s.IsStale)
            .ThenByDescending(IsCurrentDirectorySession)
            .ThenBy(s => s.HasAttachedViewer)
            .ThenByDescending(s => s.IsExtensionProcessRunning)
            .ThenByDescending(GetLastSeenSortKey)
            .ThenBy(GetStableSessionName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => NormalizePathForComparison(s.DisplayCwd) ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Registry.SessionId, StringComparer.OrdinalIgnoreCase)
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

    private Dictionary<string, List<ViewerAttachmentRegistryEntry>> ReadLiveViewerAttachments(DateTimeOffset now, TimeSpan staleAfter)
    {
        var result = new Dictionary<string, List<ViewerAttachmentRegistryEntry>>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(_paths.ViewerAttachmentsDirectory))
        {
            return result;
        }

        foreach (var file in EnumerateRegistryFiles(_paths.ViewerAttachmentsDirectory))
        {
            ViewerAttachmentRegistryEntry? entry;
            try
            {
                var json = File.ReadAllText(file);
                entry = JsonSerializer.Deserialize(json, AppJsonContext.Default.ViewerAttachmentRegistryEntry);
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

            if (entry is null || !IsLiveViewerAttachment(entry, now, staleAfter))
            {
                continue;
            }

            if (!result.TryGetValue(entry.SessionId, out var viewers))
            {
                viewers = [];
                result[entry.SessionId] = viewers;
            }

            viewers.Add(entry);
        }

        return result;
    }

    private bool IsLiveViewerAttachment(ViewerAttachmentRegistryEntry entry, DateTimeOffset now, TimeSpan staleAfter)
    {
        if (string.IsNullOrWhiteSpace(entry.SessionId)
            || entry.Pid <= 0
            || entry.Pid == _currentProcessId
            || entry.Status is "shutdown" or "stopped")
        {
            return false;
        }

        var lastSeen = ParseDate(entry.LastSeen);
        return lastSeen is not null
            && now - lastSeen.Value <= staleAfter
            && _isProcessRunning(entry.Pid);
    }

    private bool IsCurrentDirectorySession(DiscoveredSession session) =>
        PathsEqual(session.DisplayCwd, _paths.CurrentDirectory);

    private static DateTimeOffset GetLastSeenSortKey(DiscoveredSession session) =>
        session.IsStale
            ? ParseDate(session.Registry.LastSeen) ?? DateTimeOffset.MinValue
            : DateTimeOffset.MinValue;

    private static string GetStableSessionName(DiscoveredSession session) =>
        FirstNonEmpty(
            session.Metadata?.Summary,
            session.Metadata?.Repository,
            Path.GetFileName(session.DisplayCwd),
            ShortId(session.Registry.SessionId))
        ?? ShortId(session.Registry.SessionId);

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string ShortId(string value) =>
        value.Length <= 8 ? value : value[..8];

    internal static bool PathsEqual(string? left, string? right)
    {
        var normalizedLeft = NormalizePathForComparison(left);
        var normalizedRight = NormalizePathForComparison(right);
        return normalizedLeft is not null
            && normalizedRight is not null
            && string.Equals(normalizedLeft, normalizedRight, GetPathComparison());
    }

    private static string? NormalizePathForComparison(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static StringComparison GetPathComparison() =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static bool IsProcessRunning(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
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
