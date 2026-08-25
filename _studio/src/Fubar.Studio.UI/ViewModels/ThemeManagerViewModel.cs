using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Settings;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>Theme choice exposed by the Left Pane header's theme switcher (LeftPane.md §4.1).</summary>
public enum AppTheme
{
    System,
    Dark,
    Light,
}

/// <summary>
/// Drives the Left Pane header's theme switcher: Dark / Light / System Default. Bind
/// <see cref="CurrentTheme"/> two-way (e.g. a ComboBox's <c>SelectedItem</c>) - every change applies
/// instantly via <c>Avalonia.Application.Current.RequestedThemeVariant</c> (no restart - every view binds
/// <c>DynamicResource</c> tokens from Fubar.Controls' <c>Palette.axaml</c> <c>ThemeDictionaries</c>) and
/// persists via <see cref="IAppSettingsService"/> so the choice survives across sessions.
/// </summary>
public partial class ThemeManagerViewModel : ViewModelBase
{
    private readonly IAppSettingsService _settingsService;
    private bool _suppressPersist;

    public static IReadOnlyList<AppTheme> ThemeOptions { get; } = Enum.GetValues<AppTheme>();

    [ObservableProperty]
    public partial AppTheme CurrentTheme { get; set; } = AppTheme.System;

    public ThemeManagerViewModel(IAppSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    partial void OnCurrentThemeChanged(AppTheme value)
    {
        Apply(value);

        if (!_suppressPersist)
        {
            // Load-merge-save, not a fresh AppSettings - a bare `new AppSettings { Theme = ... }`
            // would silently wipe out OpenWorkspacePaths/ActiveWorkspacePath (WorkspaceExplorerViewModel
            // persists those to this same file).
            var settings = _settingsService.Load();
            settings.Theme = value.ToString();
            _ = _settingsService.SaveAsync(settings);
        }
    }

    /// <summary>
    /// Loads the persisted theme and applies it. Called once from <c>App.axaml.cs</c> before
    /// <c>MainWindow</c> is constructed, so the very first frame already renders in the right theme.
    /// Deliberately synchronous (<see cref="IAppSettingsService.Load"/>, not <c>LoadAsync</c>) -
    /// this runs on the UI thread before Avalonia's dispatcher loop is pumping, so blocking on the
    /// async path here would deadlock (the awaited continuation can never resume on the very thread
    /// that's blocked waiting for it).
    /// </summary>
    public void Initialize()
    {
        var settings = _settingsService.Load();
        var theme = Enum.TryParse<AppTheme>(settings.Theme, ignoreCase: true, out var parsed) ? parsed : AppTheme.System;

        // A restore, not a user choice - applying it is correct, re-persisting it back is not.
        // Apply() is called unconditionally (not just from the OnCurrentThemeChanged hook) because
        // that hook only fires on an actual change, and "System" is CurrentTheme's own field
        // default - a persisted "System" preference would otherwise never reach Apply().
        _suppressPersist = true;
        CurrentTheme = theme;
        _suppressPersist = false;
        Apply(theme);
    }

    private static void Apply(AppTheme theme)
    {
        if (Avalonia.Application.Current is null)
        {
            return;
        }

        Avalonia.Application.Current.RequestedThemeVariant = theme switch
        {
            AppTheme.Dark => ThemeVariant.Dark,
            AppTheme.Light => ThemeVariant.Light,
            _ => ThemeVariant.Default,
        };
    }
}
