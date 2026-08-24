using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Application.Merge;
using Fubar.Diff.Infrastructure;
using Fubar.Diff.UI.Services;
using Fubar.Diff.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Fubar.Diff.UI;

/// <summary>
/// DI composition root. This is the ONE place in the UI allowed to name Fubar.Diff.Infrastructure -
/// view models depend on Core ports and Application services only, which the architecture tests
/// enforce.
/// </summary>
internal static class Composition
{
    public static IHost BuildHost(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((_, services) =>
            {
                // Core ports -> Infrastructure adapters (diff engine, normalizer, file reader).
                services.AddFubarDiffInfrastructure();

                // Application-layer use cases.
                services.AddSingleton<IFileComparisonService, FileComparisonService>();
                services.AddSingleton<JsonSemanticPass>();
                services.AddSingleton<IMergeService, MergeService>();

                services.AddSingleton<IFilePickerService, FilePickerService>();

                // Two files may be named on the command line: FubarDiff left.txt right.txt
                services.AddSingleton(StartupFiles.FromArgs(args));

                services.AddSingleton<ThemeManagerViewModel>();
                services.AddSingleton<MainViewModel>();
            })
            .Build();
}
