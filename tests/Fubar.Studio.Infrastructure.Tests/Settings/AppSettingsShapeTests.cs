using System.Text.Json;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Infrastructure.Json;

namespace Fubar.Studio.Infrastructure.Tests.Settings;

/// <summary>
/// The settings file's shape, at the serializer rather than through <c>AppSettingsService</c> - that
/// class writes to one fixed path under %AppData% and a test cannot point it elsewhere. The shims are
/// what these are really about: they are the only thing standing between a grouping change and every
/// user's preferences being silently reset.
/// </summary>
public class AppSettingsShapeTests
{
    private static AppSettings Read(string json) =>
        JsonSerializer.Deserialize<AppSettings>(json, FubarJson.Options)!;

    private static string Write(AppSettings settings) =>
        JsonSerializer.Serialize(settings, FubarJson.Options);

    [Fact]
    public void A_flat_file_from_before_the_grouping_keeps_its_preferences()
    {
        var loaded = Read(
            """
            {
              "theme": "Dark",
              "openWorkspacePaths": ["C:/one", "C:/two"],
              "activeWorkspacePath": "C:/two"
            }
            """);

        Assert.Equal("Dark", loaded.Appearance.Theme);
        Assert.Equal(["C:/one", "C:/two"], loaded.Session.OpenWorkspacePaths);
        Assert.Equal("C:/two", loaded.Session.ActiveWorkspacePath);
    }

    [Fact]
    public void The_old_flat_names_are_not_written_back()
    {
        // Read-only shims. A file carrying both spellings has no answer for which one wins.
        //
        // Asserted against the TOP-LEVEL property names rather than by searching the text: "theme" is
        // a perfectly legitimate substring of the file now, nested inside "appearance".
        var written = Write(new AppSettings
        {
            Appearance = new AppearanceSettings { Theme = "Light" },
            Session = new SessionState { ActiveWorkspacePath = "C:/one" },
        });

        using var document = JsonDocument.Parse(written);
        var names = document.RootElement.EnumerateObject().Select(p => p.Name).ToArray();

        Assert.Contains("appearance", names);
        Assert.Contains("session", names);
        Assert.DoesNotContain("theme", names);
        Assert.DoesNotContain("activeWorkspacePath", names);
        Assert.DoesNotContain("openWorkspacePaths", names);
    }

    [Fact]
    public void A_grouped_file_round_trips()
    {
        var written = Write(new AppSettings
        {
            Appearance = new AppearanceSettings { Theme = "Dark" },
            Requests = new RequestSettings { DefaultTimeoutSeconds = 5, MaxResponseMegabytes = 8 },
            History = new HistorySettings
            {
                Enabled = false,
                MaxEntriesPerRequest = 10,
                MaxResponseBodyKilobytes = 0,
            },
        });

        var loaded = Read(written);

        Assert.Equal("Dark", loaded.Appearance.Theme);
        Assert.Equal(5, loaded.Requests.DefaultTimeoutSeconds);
        Assert.Equal(8, loaded.Requests.MaxResponseMegabytes);
        Assert.False(loaded.History.Enabled);
        Assert.Equal(10, loaded.History.MaxEntriesPerRequest);
        Assert.Equal(0, loaded.History.MaxResponseBodyKilobytes);
    }

    [Fact]
    public void A_file_missing_everything_loads_as_defaults_rather_than_nulls()
    {
        // Every group is initialized at the property, so a file written before that group existed
        // leaves a real object behind rather than a null nobody checks for.
        var loaded = Read("{}");

        Assert.Equal("System", loaded.Appearance.Theme);
        Assert.Equal(100, loaded.Requests.DefaultTimeoutSeconds);
        Assert.True(loaded.History.Enabled);
        Assert.Empty(loaded.Session.OpenWorkspacePaths);
        Assert.Null(loaded.Comparison);
    }

    [Fact]
    public void No_global_comparison_opinion_is_written_as_nothing_at_all()
    {
        // Null rather than an object full of nulls: an unticked box means "no global opinion", which
        // is what lets a folder or request decide instead. It is not the same as globally false.
        Assert.DoesNotContain("comparison", Write(new AppSettings()), StringComparison.OrdinalIgnoreCase);
    }
}
