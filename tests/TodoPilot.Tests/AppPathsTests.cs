namespace TodoPilot.Tests;

public sealed class AppPathsTests
{
    [Fact]
    public void FindProjectRoot_ReturnsNearestGitRoot()
    {
        var root = Directory.CreateTempSubdirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, ".git"));
            var nested = Directory.CreateDirectory(Path.Combine(root.FullName, "src", "app"));

            Assert.Equal(root.FullName, AppPaths.FindProjectRoot(nested.FullName));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void AppPaths_ResolvesCopilotLocationsFromHomeAndCurrentDirectory()
    {
        var home = Directory.CreateTempSubdirectory();
        var project = Directory.CreateTempSubdirectory();
        try
        {
            var paths = new AppPaths(home.FullName, project.FullName);

            Assert.Equal(Path.Combine(home.FullName, ".copilot", "todo-pilot", "sessions"), paths.RegistrySessionsDirectory);
            Assert.Equal(Path.Combine(home.FullName, ".copilot", "extensions", "todo-pilot"), paths.UserExtensionDirectory);
            Assert.Equal(Path.Combine(project.FullName, ".github", "extensions", "todo-pilot"), paths.ProjectExtensionDirectory);
        }
        finally
        {
            home.Delete(recursive: true);
            project.Delete(recursive: true);
        }
    }
}
