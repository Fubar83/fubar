using Avalonia;
using Avalonia.Headless;
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
public sealed class TestApp : Avalonia.Application;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
