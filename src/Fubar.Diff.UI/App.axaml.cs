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
            // renders in the right variant (no startup flash).
            Services.GetRequiredService<ThemeManagerViewModel>().Apply();

            var mainViewModel = Services.GetRequiredService<MainViewModel>();
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel,
            };

            // Fire-and-forget: if two files were named on the command line, compare them once the
            // dispatcher is pumping. Deliberately not awaited - showing the window a moment before
            // the rows populate is correct, and errors surface in the view models error banner.
            _ = mainViewModel.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
