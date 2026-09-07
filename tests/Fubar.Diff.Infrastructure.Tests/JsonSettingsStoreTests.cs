using System.Text.Json;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Settings;
using Fubar.Diff.Infrastructure.Settings;

namespace Fubar.Diff.Infrastructure.Tests;

/// <summary>
/// Settings persistence. The forgiving paths matter most: losing a preference is a nuisance, but
/// refusing to start because a settings file is corrupt is not acceptable, so every failure has to
/// degrade to defaults rather than throw.
/// </summary>
public class JsonSettingsStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly JsonSettingsStore _store;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public JsonSettingsStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"fubar-diff-settings-{Guid.NewGuid():N}");
        _store = new JsonSettingsStore(Path.Combine(_directory, "settings.json"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_missing_file_yields_defaults() =>
        Assert.Equal(AppSettings.Default, _store.Load());

    [Fact]
    public async Task Settings_round_trip()
    {
        var settings = AppSettings.Default with
        {
            Appearance = AppSettings.Default.Appearance with { Theme = "Dark" },
            Comparison = AppSettings.Default.Comparison with { IgnoreWhitespace = true, Mode = ComparisonMode.Json },
            Json = AppSettings.Default.Json with { ReportPropertyOrder = true },
            Recent = [new RecentComparison("a", "b")],
        };

        Assert.True(await _store.SaveAsync(settings, Token));

        var loaded = _store.Load();

        Assert.Equal("Dark", loaded.Appearance.Theme);
        Assert.True(loaded.Comparison.IgnoreWhitespace);
        Assert.True(loaded.Json.ReportPropertyOrder);
        Assert.Equal(ComparisonMode.Json, loaded.Comparison.Mode);
        Assert.Equal("a", Assert.Single(loaded.Recent).Left);
    }

    [Fact]
    public async Task Array_key_overrides_round_trip()
    {
        var settings = AppSettings.Default with
        {
            Json = AppSettings.Default.Json with
            {
                ArrayKeyOverrides = new Dictionary<string, string> { ["$.items"] = "sku" },
            },
        };

        await _store.SaveAsync(settings, Token);

        Assert.Equal("sku", _store.Load().Json.ArrayKeyOverrides["$.items"]);
    }

    [Fact]
    public async Task Ignored_paths_round_trip()
    {
        var settings = AppSettings.Default with
        {
            Json = AppSettings.Default.Json with
            {
                IgnoredPaths = ["$.requestId", "$.items[*].timestamp"],
            },
        };

        await _store.SaveAsync(settings, Token);

        Assert.Equal(["$.requestId", "$.items[*].timestamp"], _store.Load().Json.IgnoredPaths);
    }

    [Fact]
    public async Task Saving_creates_the_directory()
    {
        Assert.False(Directory.Exists(_directory));

        Assert.True(await _store.SaveAsync(AppSettings.Default, Token));
        Assert.True(File.Exists(_store.Path));
    }

    [Fact]
    public async Task A_corrupt_file_yields_defaults_rather_than_throwing()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(_store.Path, "{ this is not json", Token);

        Assert.Equal(AppSettings.Default, _store.Load());
    }

    [Fact]
    public async Task A_file_missing_half_its_properties_still_loads()
    {
        // A settings file written by an older version, or hand-edited. Everything absent falls back to
        // its default rather than failing the whole load.
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(_store.Path, """{ "theme": "Light" }""", Token);

        var loaded = _store.Load();

        Assert.Equal("Light", loaded.Appearance.Theme);
        Assert.False(loaded.Comparison.IgnoreWhitespace);
        Assert.Empty(loaded.Recent);
    }

    [Fact]
    public async Task Enums_are_written_by_name_so_the_file_is_readable()
    {
        await _store.SaveAsync(AppSettings.Default with
        {
            Comparison = AppSettings.Default.Comparison with { Mode = ComparisonMode.Text },
        }, Token);

        Assert.Contains("\"Text\"", await File.ReadAllTextAsync(_store.Path, Token), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unwritable_path_reports_failure_rather_than_throwing()
    {
        // A path whose parent is a FILE, not a directory - the closest portable "cannot create this".
        var blocker = Path.Combine(Path.GetTempPath(), $"fubar-blocker-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(blocker, "x", Token);

        try
        {
            var store = new JsonSettingsStore(Path.Combine(blocker, "settings.json"));
            Assert.False(await store.SaveAsync(AppSettings.Default, Token));
        }
        finally
        {
            File.Delete(blocker);
        }
    }

    [Fact]
    public async Task Derived_values_are_not_persisted()
    {
        // DisplayName is computed from the two paths. Writing it bloats the file and invites someone
        // hand-editing it to change a value that is ignored on load.
        await _store.SaveAsync(
            AppSettings.Default with { Recent = [new RecentComparison("a.json", "b.json")] },
            Token);

        var json = await File.ReadAllTextAsync(_store.Path, Token);

        Assert.DoesNotContain("displayName", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Saving_leaves_no_temporary_file_behind()
    {
        await _store.SaveAsync(AppSettings.Default, Token);

        Assert.False(File.Exists(_store.Path + ".tmp"));
    }

    [Fact]
    public async Task A_flat_file_from_before_the_grouping_still_loads_every_preference()
    {
        // The settings were thirty-one properties at one level until they were grouped. Every one of
        // them is somebody's saved preference, and a shape change that silently reset them all reads
        // as the app losing settings, not as a migration - so the old names are still read.
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            _store.Path,
            """
            {
              "theme": "Dark",
              "syntaxHighlighting": false,
              "showInvisibles": true,
              "collapseUnchanged": false,
              "wordWrap": true,
              "normalizeStructure": true,
              "mode": "Json",
              "ignoreWhitespace": true,
              "ignoreCase": true,
              "normalizeUnicode": true,
              "ignoreComments": true,
              "ignoreBlankLines": true,
              "codeStructure": false,
              "ignoredLinePatterns": ["^build:"],
              "reportPropertyOrder": true,
              "matchArraysByPosition": true,
              "ignoreNullVsMissing": true,
              "arrayKeyOverrides": { "$.items": "sku" },
              "ignoredPaths": ["$.requestId"],
              "jsonIndentSize": 4,
              "jsonUseTabs": true,
              "jsonInlineSimpleContainers": false,
              "jsonSortProperties": true,
              "autoRefresh": false,
              "liveDiff": false,
              "folderShowIdentical": true,
              "folderExclude": ["bin"],
              "folderLinkedMode": true,
              "folderLinkRules": [".a = .b"],
              "recent": [{ "left": "a", "right": "b" }]
            }
            """,
            Token);

        var loaded = _store.Load();

        Assert.Equal("Dark", loaded.Appearance.Theme);
        Assert.False(loaded.Appearance.SyntaxHighlighting);
        Assert.True(loaded.Appearance.ShowInvisibles);
        Assert.False(loaded.Appearance.CollapseUnchanged);
        Assert.True(loaded.Appearance.WordWrap);
        Assert.True(loaded.Appearance.NormalizeStructure);

        Assert.Equal(ComparisonMode.Json, loaded.Comparison.Mode);
        Assert.True(loaded.Comparison.IgnoreWhitespace);
        Assert.True(loaded.Comparison.IgnoreCase);
        Assert.True(loaded.Comparison.NormalizeUnicode);
        Assert.True(loaded.Comparison.IgnoreComments);
        Assert.True(loaded.Comparison.IgnoreBlankLines);
        Assert.False(loaded.Comparison.CodeStructure);
        Assert.Equal(["^build:"], loaded.Comparison.IgnoredLinePatterns);

        Assert.True(loaded.Json.ReportPropertyOrder);
        Assert.True(loaded.Json.MatchArraysByPosition);
        Assert.True(loaded.Json.IgnoreNullVsMissing);
        Assert.Equal("sku", loaded.Json.ArrayKeyOverrides["$.items"]);
        Assert.Equal(["$.requestId"], loaded.Json.IgnoredPaths);

        Assert.Equal(4, loaded.JsonFormatting.IndentSize);
        Assert.True(loaded.JsonFormatting.UseTabs);
        Assert.False(loaded.JsonFormatting.InlineSimpleContainers);
        Assert.True(loaded.JsonFormatting.SortProperties);

        Assert.False(loaded.Refresh.AutoRefresh);
        Assert.False(loaded.Refresh.LiveDiff);

        Assert.True(loaded.Folders.ShowIdentical);
        Assert.Equal(["bin"], loaded.Folders.Exclude);
        Assert.True(loaded.Folders.LinkedMode);
        Assert.Equal([".a = .b"], loaded.Folders.LinkRules);

        Assert.Equal("a", Assert.Single(loaded.Recent).Left);
    }

    [Fact]
    public async Task The_old_flat_names_are_not_written_back()
    {
        // Read-only shims: they exist so an old file still means something, not so a new one carries
        // two copies of every preference and no answer for which one wins.
        await _store.SaveAsync(
            AppSettings.Default with
            {
                Appearance = AppSettings.Default.Appearance with { Theme = "Dark" },
            },
            Token);

        // Asserted against the TOP-LEVEL property names rather than by searching the text: "theme" is
        // a perfectly legitimate substring of the file now, nested inside "appearance".
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(_store.Path, Token));
        var names = document.RootElement.EnumerateObject().Select(p => p.Name).ToArray();

        Assert.Contains("appearance", names);
        Assert.DoesNotContain("theme", names);
        Assert.DoesNotContain("folderExclude", names);
        Assert.DoesNotContain("jsonIndentSize", names);
    }

    [Fact]
    public async Task Whether_the_panes_are_editable_is_not_persisted()
    {
        // It was. A tool for READING two files reopening with both panes editable, days after one
        // accidental toggle, is not a preference anybody set - see ComparisonViewModel.CaptureOptions.
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(_store.Path, """{ "editing": true, "theme": "Dark" }""", Token);

        // The theme beside it is what makes this an assertion rather than a tautology: the file IS
        // read, and "editing" is the one thing in it that no longer lands anywhere.
        Assert.Equal("Dark", _store.Load().Appearance.Theme);

        await _store.SaveAsync(AppSettings.Default, Token);
        Assert.DoesNotContain(
            "editing",
            await File.ReadAllTextAsync(_store.Path, Token),
            StringComparison.OrdinalIgnoreCase);
    }
}
