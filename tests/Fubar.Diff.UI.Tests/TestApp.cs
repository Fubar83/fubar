using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Fubar.Diff.UI.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Fubar.Diff.UI.Tests;

/// <summary>
/// A headless app for the view-model tests that need a DISPATCHER rather than a window.
///
/// <c>ComparisonViewModel</c> marshals file-system events onto the UI thread, which is correct - they
/// arrive on a watcher's background thread - and means the refresh policy cannot be exercised at all
/// without something to pump that queue. No styles are loaded: nothing here renders.
/// </summary>
/// <remarks>
/// Qualified as <c>Avalonia.Application</c> because the <c>Fubar.Diff.Application</c> namespace shadows
/// the type - the collision CLAUDE.md documents, and a using-alias cannot fix it, since a namespace
/// member outranks one.
/// </remarks>
public sealed class TestApp : Avalonia.Application
{
    /// <summary>
    /// Loads the design system, so a test that constructs a real WINDOW gets the same control themes
    /// the app does. Without them the Fubar.Controls types resolve but render untemplated, which is a
    /// different thing from what ships and would hide a template-level mistake.
    /// </summary>
    public override void Initialize()
    {
        Resources.MergedDictionaries.Add(
            new ResourceInclude((System.Uri?)null) { Source = new System.Uri("avares://Fubar.Controls/Themes/Palette.axaml") });

        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude((System.Uri?)null) { Source = new System.Uri("avares://Fubar.Controls/Themes/Fubar.Controls.axaml") });
    }
}

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
