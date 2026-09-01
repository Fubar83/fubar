using System;
using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Application.Folders;
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
                // Core ports -> Infrastructure adapters (diff engine, normalizer, file reader/writer,
                // JSON parser, settings).
                services.AddFubarDiffInfrastructure();

                // Application-layer use cases.
                services.AddSingleton<IFileComparisonService, FileComparisonService>();
                services.AddSingleton<IThreeWayComparisonService, ThreeWayComparisonService>();
                services.AddSingleton<IFolderComparisonService, FolderComparisonService>();
                services.AddSingleton<JsonSemanticPass>();
                services.AddSingleton<CodeStructurePass>();
                services.AddSingleton<IMergeService, MergeService>();

                services.AddSingleton<IFilePickerService, FilePickerService>();
                services.AddSingleton<IClipboardService, ClipboardService>();

                // The pair that makes folder copying possible. Registered together deliberately: the
                // view model offers copying only when it has BOTH, so wiring the copier without a way
                // to ask the user would give a window that replaces files without confirming.
                services.AddSingleton<IConfirmationService, ConfirmationService>();

                // Two files may be named on the command line: FubarDiff left.txt right.txt
                services.AddSingleton(StartupFiles.FromArgs(args));

                // Shared across every tab.
                services.AddSingleton<ThemeManagerViewModel>();

                // Per tab: transient, so each one gets its own comparison, options and merge state.
                // The shell creates them through this factory rather than holding the container, which
                // keeps it testable with a plain lambda.
                services.AddTransient<ComparisonViewModel>();
                services.AddSingleton<Func<ComparisonViewModel>>(provider =>
                    provider.GetRequiredService<ComparisonViewModel>);

                // Per merge WINDOW rather than per tab - a three-way merge is its own window (see
                // MergeViewModel), so the shell hands one out rather than keeping a list of them.
                services.AddTransient<MergeViewModel>();
                services.AddSingleton<Func<MergeViewModel>>(provider =>
                    provider.GetRequiredService<MergeViewModel>);

                services.AddTransient<FolderViewModel>();
                services.AddSingleton<Func<FolderViewModel>>(provider =>
                    provider.GetRequiredService<FolderViewModel>);

                services.AddSingleton<ShellViewModel>();
            })
            .Build();
}
