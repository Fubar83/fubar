using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Fubar.Studio.UI.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Fubar.Studio.UI.Tests;

/// <summary>
/// A headless app for API Studio's view-model and window tests.
/// </summary>
/// <remarks>
/// Qualified as <c>Avalonia.Application</c> because the <c>Fubar.Studio.Application</c> namespace
/// shadows the type - the collision CLAUDE.md documents, which a using-alias cannot fix since a
/// namespace member outranks one.
/// </remarks>
public sealed class TestApp : Avalonia.Application
{
    /// <summary>
    /// Loads the design system, so a test that constructs a real WINDOW gets the same control themes
    /// the app does. Without them the Fubar.Controls types resolve but render untemplated, which would
    /// hide a template-level mistake.
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
