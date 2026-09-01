using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Fubar.Diff.UI.ViewModels;
using Fubar.Diff.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Fubar.Diff.UI;

public partial class App : Avalonia.Application
{
    /// <summary>Set by <see cref="Program.Main"/> before Avalonia starts, from the DI host.</summary>
    public static IServiceProvider Services { get; set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Apply the theme before the window is constructed so the very first frame already
            // renders in the right variant (no startup flash). The shell has already restored the
            // persisted choice by this point.
            var shell = Services.GetRequiredService<ShellViewModel>();
            shell.ThemeManager.Apply();

            desktop.MainWindow = new MainWindow { DataContext = shell };

            var startup = Services.GetRequiredService<StartupFiles>();

            // Fire-and-forget: opens the first tab and, if two files were named on the command line,
            // compares them once the dispatcher is pumping. Deliberately not awaited - showing the
            // window a moment before the rows populate is correct, and errors surface in the tab's
            // own error banner.
            _ = shell.InitializeAsync(startup);

            if (startup.IsMerge)
            {
                // The merge window is opened ON TOP of the main one rather than instead of it: closing
                // a merge should leave the app running, and `git mergetool` invoking this per
                // conflicted file wants the comparison window there anyway.
                //
                // Deferred to Opened, not done here: an owned window cannot be shown before its owner
                // is, and at this point the main window has been constructed but not yet displayed -
                // showing one now throws "Cannot show window with non-visible owner".
                var main = desktop.MainWindow;

                void OnOpened(object? sender, EventArgs args)
                {
                    main.Opened -= OnOpened;
                    OpenMerge(shell, main, startup);
                }

                main.Opened += OnOpened;
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Opens a three-way merge for the files named on the command line.
    ///
    /// The window is shown before the merge finishes, so a large trio shows an empty window that fills
    /// in rather than a delay with nothing on screen at all.
    /// </summary>
    private static void OpenMerge(ShellViewModel shell, Avalonia.Controls.Window owner, StartupFiles startup)
    {
        var merge = shell.CreateMerge();

        merge.BasePath = startup.Base!;
        merge.LeftPath = startup.Left!;
        merge.RightPath = startup.Right!;

        new MergeWindow { DataContext = merge }.Show(owner);

        _ = merge.MergeAsync();
    }
}
