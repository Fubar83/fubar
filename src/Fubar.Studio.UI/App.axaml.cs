using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Fubar.Studio.UI.ViewModels;
using Fubar.Studio.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Fubar.Studio.UI;

public partial class App : Avalonia.Application
{
    /// <summary>Set by <see cref="Program.Main"/> before Avalonia starts, from the DI host.</summary>
    public static IServiceProvider Services { get; set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Apply the persisted Dark/Light/System preference before the window is constructed so
            // the very first frame already renders in the right theme (no startup flash/flip).
            var themeManager = Services.GetRequiredService<ThemeManagerViewModel>();
            themeManager.Initialize();

            // The primary window owns session persistence; torn-off windows (created later by
            // WindowManager) are session-only. Shut down on the LAST window closing, not the first,
            // so closing the primary doesn't kill windows tabs were torn off into.
            desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;

            var windowManager = Services.GetRequiredService<WindowManager>();
            var mainWindow = windowManager.CreateWindow(isPrimary: true);
            desktop.MainWindow = mainWindow;

            // Fire-and-forget: reopens whichever workspace tabs were open last session (see
            // WorkspaceExplorerViewModel.RestoreLastSessionAsync). Deliberately async/non-blocking -
            // unlike the theme, showing the window a moment before the tabs populate is fine, and
            // the dispatcher is already pumping by this point so awaiting here isn't even needed.
            var mainViewModel = (MainViewModel)mainWindow.DataContext!;
            _ = mainViewModel.WorkspaceExplorer.RestoreLastSessionAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}