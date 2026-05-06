using Spectre.Console;

namespace TodoPilot;

public static class TodoPilotApp
{
    public static async Task<int> RunAsync(string[] args)
    {
        var paths = new AppPaths();
        var installer = new ExtensionInstaller(paths);

        if (args.Length > 0)
        {
            return args[0] switch
            {
                "install" => RunInstall(paths, installer),
                "uninstall" => RunUninstall(paths, installer),
                "-h" or "--help" or "help" => ShowHelp(),
                _ => ShowUnknownCommand(args[0])
            };
        }

        if (installer.GetInstalledScopes().Count == 0)
        {
            if (!IsInteractive())
            {
                AnsiConsole.MarkupLine("[yellow]The Copilot extension is not installed. Run `dnx todo-pilot install` in an interactive terminal first.[/]");
                return 1;
            }

            AnsiConsole.MarkupLine("[bold]todo-pilot needs a Copilot CLI extension to discover live sessions.[/]");
            if (AnsiConsole.Confirm("Install the extension now?", defaultValue: true))
            {
                var scope = PromptForScope(paths, "Install scope");
                if (!ConfirmInstall(paths, scope))
                {
                    AnsiConsole.MarkupLine("[yellow]Installation cancelled. Continuing in read-only mode.[/]");
                }
                else
                {
                    installer.Install(scope);
                    AnsiConsole.MarkupLine($"[green]Installed extension to[/] {Markup.Escape(paths.GetExtensionDirectory(scope))}");
                }
            }
            else
            {
                AnsiConsole.MarkupLine("[yellow]Continuing in read-only mode; only already registered sessions can appear.[/]");
            }
        }

        await new TerminalViewer(paths).RunAsync().ConfigureAwait(false);
        return 0;
    }

    private static int RunInstall(AppPaths paths, ExtensionInstaller installer)
    {
        if (!IsInteractive())
        {
            AnsiConsole.MarkupLine("[red]The install command requires an interactive terminal for explicit consent.[/]");
            return 1;
        }

        var scope = PromptForScope(paths, "Install or upgrade scope");
        if (!ConfirmInstall(paths, scope))
        {
            AnsiConsole.MarkupLine("[yellow]Installation cancelled.[/]");
            return 1;
        }

        installer.Install(scope);
        AnsiConsole.MarkupLine($"[green]Installed extension to[/] {Markup.Escape(paths.GetExtensionDirectory(scope))}");
        AnsiConsole.MarkupLine("[grey]Reload or restart Copilot CLI sessions for the extension to load.[/]");
        return 0;
    }

    private static int RunUninstall(AppPaths paths, ExtensionInstaller installer)
    {
        if (!IsInteractive())
        {
            AnsiConsole.MarkupLine("[red]The uninstall command requires an interactive terminal for confirmation.[/]");
            return 1;
        }

        var installedScopes = installer.GetInstalledScopes();
        if (installedScopes.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No todo-pilot extension installation was found for the current user/project.[/]");
            return 0;
        }

        var scope = installedScopes.Count == 1
            ? installedScopes[0]
            : PromptForScope(paths, "Uninstall scope", installedScopes);

        var path = paths.GetExtensionDirectory(scope);
        if (!AnsiConsole.Confirm($"Remove extension directory [yellow]{Markup.Escape(path)}[/]?", defaultValue: false))
        {
            AnsiConsole.MarkupLine("[yellow]Uninstall cancelled.[/]");
            return 1;
        }

        if (installer.Uninstall(scope))
        {
            AnsiConsole.MarkupLine($"[green]Removed extension from[/] {Markup.Escape(path)}");
        }

        return 0;
    }

    private static InstallScope PromptForScope(AppPaths paths, string title, IReadOnlyList<InstallScope>? choices = null)
    {
        var available = choices ?? [InstallScope.User, InstallScope.Project];
        var labels = available.Select(scope => ScopeLabel(paths, scope)).ToArray();
        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title(title)
                .AddChoices(labels));

        return selected.StartsWith("User", StringComparison.Ordinal) ? InstallScope.User : InstallScope.Project;
    }

    private static bool ConfirmInstall(AppPaths paths, InstallScope scope)
    {
        var path = paths.GetExtensionDirectory(scope);
        AnsiConsole.MarkupLine("[yellow]This writes executable JavaScript that Copilot CLI will auto-load for the selected scope.[/]");
        AnsiConsole.MarkupLine($"Target: [bold]{Markup.Escape(path)}[/]");
        return AnsiConsole.Confirm("Proceed?", defaultValue: false);
    }

    private static string ScopeLabel(AppPaths paths, InstallScope scope) =>
        scope == InstallScope.User
            ? $"User - {paths.UserExtensionDirectory}"
            : $"Project - {paths.ProjectExtensionDirectory}";

    private static int ShowHelp()
    {
        AnsiConsole.Write(new Markup("""
            [bold]todo-pilot[/]

              todo-pilot             Open the live TODO viewer
              todo-pilot install     Install or upgrade the Copilot extension
              todo-pilot uninstall   Remove the Copilot extension
            """));
        return 0;
    }

    private static int ShowUnknownCommand(string command)
    {
        AnsiConsole.MarkupLine($"[red]Unknown command:[/] {Markup.Escape(command)}");
        ShowHelp();
        return 1;
    }

    private static bool IsInteractive() =>
        !Console.IsInputRedirected && !Console.IsOutputRedirected;
}
