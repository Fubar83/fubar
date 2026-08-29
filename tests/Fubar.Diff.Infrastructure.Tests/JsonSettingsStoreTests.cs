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
            Theme = "Dark",
            IgnoreWhitespace = true,
            ReportPropertyOrder = true,
            Mode = ComparisonMode.Json,
            Recent = [new RecentComparison("a", "b")],
        };

        Assert.True(await _store.SaveAsync(settings, Token));

        var loaded = _store.Load();

        Assert.Equal("Dark", loaded.Theme);
        Assert.True(loaded.IgnoreWhitespace);
        Assert.True(loaded.ReportPropertyOrder);
        Assert.Equal(ComparisonMode.Json, loaded.Mode);
        Assert.Equal("a", Assert.Single(loaded.Recent).Left);
    }

    [Fact]
    public async Task Array_key_overrides_round_trip()
    {
        var settings = AppSettings.Default with
        {
            ArrayKeyOverrides = new Dictionary<string, string> { ["$.items"] = "sku" },
        };

        await _store.SaveAsync(settings, Token);

        Assert.Equal("sku", _store.Load().ArrayKeyOverrides["$.items"]);
    }

    [Fact]
    public async Task Ignored_paths_round_trip()
    {
        var settings = AppSettings.Default with { IgnoredPaths = ["$.requestId", "$.items[*].timestamp"] };

        await _store.SaveAsync(settings, Token);

        Assert.Equal(["$.requestId", "$.items[*].timestamp"], _store.Load().IgnoredPaths);
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

        Assert.Equal("Light", loaded.Theme);
        Assert.False(loaded.IgnoreWhitespace);
        Assert.Empty(loaded.Recent);
    }

    [Fact]
    public async Task Enums_are_written_by_name_so_the_file_is_readable()
    {
        await _store.SaveAsync(AppSettings.Default with { Mode = ComparisonMode.Text }, Token);

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
}
