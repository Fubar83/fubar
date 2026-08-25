using System;
using System.Collections.Generic;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Fubar.Diff.UI.ViewModels;

/// <summary>Theme choice exposed by the toolbar's theme switcher.</summary>
public enum AppTheme
{
    System,
    Dark,
    Light,
}

/// <summary>
/// Drives the theme switcher. Bind <see cref="CurrentTheme"/> two-way - every change applies instantly
/// via <c>Avalonia.Application.Current.RequestedThemeVariant</c>, with no restart, because every view
/// binds <c>DynamicResource</c> tokens from Fubar.Controls' <c>Palette.axaml</c> ThemeDictionaries.
/// </summary>
public partial class ThemeManagerViewModel : ViewModelBase
{
    public static IReadOnlyList<AppTheme> ThemeOptions { get; } = Enum.GetValues<AppTheme>();

    [ObservableProperty]
    public partial AppTheme CurrentTheme { get; set; } = AppTheme.System;

    /// <summary>
    /// Restores a persisted choice, e.g. the string held in settings. An unrecognised value falls back
    /// to System rather than failing - a settings file from a future version should not stop startup.
    /// </summary>
    public void Restore(string themeName)
    {
        CurrentTheme = Enum.TryParse<AppTheme>(themeName, ignoreCase: true, out var parsed)
            ? parsed
            : AppTheme.System;
    }

    partial void OnCurrentThemeChanged(AppTheme value) => Apply();

    /// <summary>
    /// Pushes the current choice onto Avalonia. Called from <c>App.axaml.cs</c> before the window is
    /// built so the first frame is already correct, and again on every change.
    /// </summary>
    public void Apply()
    {
        if (Avalonia.Application.Current is null)
        {
            return;
        }

        Avalonia.Application.Current.RequestedThemeVariant = CurrentTheme switch
        {
            AppTheme.Dark => ThemeVariant.Dark,
            AppTheme.Light => ThemeVariant.Light,
            _ => ThemeVariant.Default,
        };
    }
}
