using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Fubar.Controls.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Fubar.Controls.Tests;

/// <summary>
/// Headless Avalonia app for the control tests: Fluent theme + the whole Fubar.Controls design system
/// (so TabStrip and its ListBoxItem container theme actually resolve a template and realize containers,
/// which the drag geometry depends on) + the palette resources the styles reference via DynamicResource.
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
