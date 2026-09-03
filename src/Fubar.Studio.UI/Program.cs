using Avalonia;
using Fubar.Studio.Application.Running;
using Fubar.Studio.Core.Workspaces;
using Fubar.Studio.UI.Cli;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Fubar.Studio.UI;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        // The headless check comes first, before Avalonia is configured and before a window can be
        // created: a run that has to exit with a status code cannot also be showing a window. Only
        // flags that mean nothing on screen count (see CommandLine.IsHeadless), so starting the app
        // normally is untouched.
        if (CommandLine.IsHeadless(args))
        {
            return RunHeadless(args);
        }

        using var host = Composition.BuildHost(args);
        App.Services = host.Services;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        return 0;
    }

    private static int RunHeadless(string[] args)
    {
        ParentConsole.Attach();

        using var host = Composition.BuildHost(args);

        return CliRunner
            .RunAsync(
                CommandLine.Parse(args),
                host.Services.GetRequiredService<ICollectionRunService>(),
                host.Services.GetRequiredService<IWorkspaceStore>(),
                host.Services.GetRequiredService<IRequestStore>(),
                host.Services.GetRequiredService<IEnvironmentStore>(),
                Console.Out,
                Console.Error)
            .GetAwaiter()
            .GetResult();
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
