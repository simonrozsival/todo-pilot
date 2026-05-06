using System.Reflection;
using System.Text.Json;

namespace TodoPilot;

public sealed class ExtensionInstaller
{
    private readonly AppPaths _paths;

    public ExtensionInstaller(AppPaths paths)
    {
        _paths = paths;
    }

    public static string ToolVersion =>
        typeof(ExtensionInstaller).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(ExtensionInstaller).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    public bool IsInstalled(InstallScope scope) =>
        File.Exists(Path.Combine(_paths.GetExtensionDirectory(scope), "extension.mjs"));

    public IReadOnlyList<InstallScope> GetInstalledScopes()
    {
        var scopes = new List<InstallScope>();
        if (IsInstalled(InstallScope.User))
        {
            scopes.Add(InstallScope.User);
        }

        if (IsInstalled(InstallScope.Project))
        {
            scopes.Add(InstallScope.Project);
        }

        return scopes;
    }

    public void Install(InstallScope scope)
    {
        var extensionDirectory = _paths.GetExtensionDirectory(scope);
        Directory.CreateDirectory(extensionDirectory);

        AtomicWrite(
            Path.Combine(extensionDirectory, "extension.mjs"),
            ExtensionTemplates.CreateExtensionScript(ToolVersion, scope));

        var manifest = new ExtensionManifest
        {
            Version = ToolVersion,
            Scope = scope == InstallScope.User ? "user" : "project",
            InstalledAt = DateTimeOffset.UtcNow.ToString("O")
        };

        AtomicWrite(
            Path.Combine(extensionDirectory, "manifest.json"),
            JsonSerializer.Serialize(manifest, AppJsonContext.Default.ExtensionManifest));
    }

    public bool Uninstall(InstallScope scope)
    {
        var extensionDirectory = _paths.GetExtensionDirectory(scope);
        if (!Directory.Exists(extensionDirectory))
        {
            return false;
        }

        Directory.Delete(extensionDirectory, recursive: true);
        return true;
    }

    private static void AtomicWrite(string path, string content)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException($"Path has no directory: {path}");
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(tempPath, content);
        File.Move(tempPath, path, overwrite: true);
    }
}
