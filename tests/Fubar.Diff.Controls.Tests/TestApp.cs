using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Fubar.Diff.Controls.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Fubar.Diff.Controls.Tests;

/// <summary>
/// Headless Avalonia app for the diff control tests: Fluent plus the whole Fubar.Controls design
/// system, including the palette the diff renderers resolve their tints from by DynamicResource. A
/// pane built without the palette still renders - <c>DiffLineColors</c> returns null for a missing
/// token rather than throwing - so leaving it out would quietly test an uncoloured version of the
/// thing under test.
/// </summary>
public sealed class TestApp : Application
{
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
