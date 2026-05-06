namespace TodoPilot.Tests;

public sealed class ExtensionInstallerTests
{
    [Fact]
    public void Install_WritesExtensionAndManifestToSelectedScope()
    {
        var home = Directory.CreateTempSubdirectory();
        var project = Directory.CreateTempSubdirectory();
        try
        {
            var paths = new AppPaths(home.FullName, project.FullName);
            var installer = new ExtensionInstaller(paths);

            installer.Install(InstallScope.User);

            var extensionPath = Path.Combine(paths.UserExtensionDirectory, "extension.mjs");
            var manifestPath = Path.Combine(paths.UserExtensionDirectory, "manifest.json");
            Assert.True(File.Exists(extensionPath));
            Assert.True(File.Exists(manifestPath));

            var extension = File.ReadAllText(extensionPath);
            Assert.Contains("joinSession", extension, StringComparison.Ordinal);
            Assert.Contains("session.log", extension, StringComparison.Ordinal);
            Assert.DoesNotContain("console.log", extension, StringComparison.Ordinal);
        }
        finally
        {
            home.Delete(recursive: true);
            project.Delete(recursive: true);
        }
    }

    [Fact]
    public void Uninstall_RemovesSelectedScope()
    {
        var home = Directory.CreateTempSubdirectory();
        var project = Directory.CreateTempSubdirectory();
        try
        {
            var paths = new AppPaths(home.FullName, project.FullName);
            var installer = new ExtensionInstaller(paths);
            installer.Install(InstallScope.Project);

            Assert.True(installer.Uninstall(InstallScope.Project));
            Assert.False(Directory.Exists(paths.ProjectExtensionDirectory));
        }
        finally
        {
            home.Delete(recursive: true);
            project.Delete(recursive: true);
        }
    }
}
