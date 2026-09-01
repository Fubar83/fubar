using System;
using Avalonia;
using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Core.Settings;
using Fubar.Diff.UI.Cli;
using Microsoft.Extensions.DependencyInjection;

namespace Fubar.Diff.UI;

internal sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized yet.
    [STAThread]
    public static int Main(string[] args)
    {
        // The headless check comes first, before Avalonia is configured and before a window can be
        // created: a run that has to exit with a status code cannot also be showing a window. Only
        // flags that mean nothing on screen count as headless (see CommandLine.IsHeadless) - two file
        // names still open a comparison, which is what a difftool configuration passes.
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
                host.Services.GetRequiredService<IFileComparisonService>(),
                Console.Out,
                Console.Error,
                host.Services.GetRequiredService<IProjectConfigStore>())
            .GetAwaiter()
            .GetResult();
    }

    // Avalonia configuration, don't remove; also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
