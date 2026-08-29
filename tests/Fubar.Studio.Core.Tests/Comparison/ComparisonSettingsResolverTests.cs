using Fubar.Studio.Core.Comparison;
using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Tests.Comparison;

/// <summary>
/// The precedence rules for global → folder → request comparison settings. The one that matters most
/// is per-SETTING inheritance: overriding one option must not silently pin every other one.
/// </summary>
public class ComparisonSettingsResolverTests
{
    private static ComparisonSettingsLayer Global(ComparisonSettings? s) => new(s, ComparisonScope.Global, "Global");

    private static ComparisonSettingsLayer Folder(ComparisonSettings? s, string name = "api") =>
        new(s, ComparisonScope.Folder, $"Folder: {name}");

    private static ComparisonSettingsLayer Request(ComparisonSettings? s) => new(s, ComparisonScope.Request, "Request");

    [Fact]
    public void With_no_layers_everything_falls_back_to_the_built_in_defaults()
    {
        var resolved = ComparisonSettingsResolver.Resolve([]);

        Assert.False(resolved.IgnoreWhitespace.Value);
        Assert.False(resolved.IgnoreCase.Value);
        Assert.False(resolved.ReportPropertyOrder.Value);
        Assert.Empty(resolved.IgnoredPaths.Value);
        Assert.Empty(resolved.ArrayKeyOverrides.Value);
        Assert.Equal(ComparisonScope.Default, resolved.IgnoreWhitespace.Scope);
    }

    [Fact]
    public void A_layer_that_is_null_contributes_nothing()
    {
        var resolved = ComparisonSettingsResolver.Resolve([Global(null), Folder(null), Request(null)]);

        Assert.False(resolved.IgnoreCase.Value);
        Assert.Equal(ComparisonScope.Default, resolved.IgnoreCase.Scope);
    }

    [Fact]
    public void The_closest_layer_that_sets_a_value_wins()
    {
        var resolved = ComparisonSettingsResolver.Resolve([
            Global(new ComparisonSettings { IgnoreCase = false }),
            Folder(new ComparisonSettings { IgnoreCase = true }),
        ]);

        Assert.True(resolved.IgnoreCase.Value);
        Assert.Equal(ComparisonScope.Folder, resolved.IgnoreCase.Scope);
        Assert.Equal("Folder: api", resolved.IgnoreCase.SourceName);
    }

    [Fact]
    public void The_request_beats_every_folder_and_the_global_level()
    {
        var resolved = ComparisonSettingsResolver.Resolve([
            Global(new ComparisonSettings { IgnoreWhitespace = true }),
            Folder(new ComparisonSettings { IgnoreWhitespace = true }),
            Request(new ComparisonSettings { IgnoreWhitespace = false }),
        ]);

        Assert.False(resolved.IgnoreWhitespace.Value);
        Assert.Equal(ComparisonScope.Request, resolved.IgnoreWhitespace.Scope);
    }

    /// <summary>
    /// The whole reason every member of <see cref="ComparisonSettings"/> is nullable: a request that
    /// overrides ONE option keeps inheriting the rest independently, rather than the nearest level
    /// winning wholesale.
    /// </summary>
    [Fact]
    public void Overriding_one_setting_leaves_the_others_inheriting()
    {
        var resolved = ComparisonSettingsResolver.Resolve([
            Global(new ComparisonSettings { IgnoreWhitespace = true, IgnoreCase = true }),
            Folder(new ComparisonSettings { IgnoredPaths = ["$.traceId"] }),
            Request(new ComparisonSettings { IgnoreCase = false }),
        ]);

        // Overridden at the request.
        Assert.False(resolved.IgnoreCase.Value);
        Assert.Equal(ComparisonScope.Request, resolved.IgnoreCase.Scope);

        // Still the global's, even though nearer levels exist and set OTHER things.
        Assert.True(resolved.IgnoreWhitespace.Value);
        Assert.Equal(ComparisonScope.Global, resolved.IgnoreWhitespace.Scope);

        // Still the folder's.
        Assert.Equal(["$.traceId"], resolved.IgnoredPaths.Value);
        Assert.Equal(ComparisonScope.Folder, resolved.IgnoredPaths.Scope);
    }

    [Fact]
    public void A_nearer_folder_beats_a_further_one()
    {
        var resolved = ComparisonSettingsResolver.Resolve([
            Folder(new ComparisonSettings { ReportPropertyOrder = false }, "root"),
            Folder(new ComparisonSettings { ReportPropertyOrder = true }, "users"),
        ]);

        Assert.True(resolved.ReportPropertyOrder.Value);
        Assert.Equal("Folder: users", resolved.ReportPropertyOrder.SourceName);
    }

    /// <summary>
    /// Lists REPLACE rather than merge, so reading one level tells you exactly what applies - see
    /// <see cref="ComparisonSettings.IgnoredPaths"/>' own note on why union was rejected.
    /// </summary>
    [Fact]
    public void Ignored_paths_replace_rather_than_union()
    {
        var resolved = ComparisonSettingsResolver.Resolve([
            Folder(new ComparisonSettings { IgnoredPaths = ["$.traceId", "$..timestamp"] }),
            Request(new ComparisonSettings { IgnoredPaths = ["$.meta.requestId"] }),
        ]);

        Assert.Equal(["$.meta.requestId"], resolved.IgnoredPaths.Value);
    }

    /// <summary>An empty list is an override meaning "ignore nothing here", not "inherit".</summary>
    [Fact]
    public void An_empty_list_is_a_real_override_not_an_absent_one()
    {
        var resolved = ComparisonSettingsResolver.Resolve([
            Folder(new ComparisonSettings { IgnoredPaths = ["$.traceId"] }),
            Request(new ComparisonSettings { IgnoredPaths = [] }),
        ]);

        Assert.Empty(resolved.IgnoredPaths.Value);
        Assert.Equal(ComparisonScope.Request, resolved.IgnoredPaths.Scope);
    }

    [Fact]
    public void Array_key_overrides_resolve_like_every_other_setting()
    {
        var resolved = ComparisonSettingsResolver.Resolve([
            Global(new ComparisonSettings { ArrayKeyOverrides = new() { ["$.users"] = "id" } }),
            Request(new ComparisonSettings { ArrayKeyOverrides = new() { ["$.users"] = "sku" } }),
        ]);

        Assert.Equal("sku", resolved.ArrayKeyOverrides.Value["$.users"]);
        Assert.Equal(ComparisonScope.Request, resolved.ArrayKeyOverrides.Scope);
    }

    /// <summary>
    /// The resolved collections must be copies: handing back the caller's own list would let a later
    /// edit of the settings object silently change what a running comparison thinks it resolved.
    /// </summary>
    [Fact]
    public void Resolved_collections_are_copies_not_the_stored_instances()
    {
        var settings = new ComparisonSettings { IgnoredPaths = ["$.a"] };

        var resolved = ComparisonSettingsResolver.Resolve([Request(settings)]);
        settings.IgnoredPaths!.Add("$.b");

        Assert.Equal(["$.a"], resolved.IgnoredPaths.Value);
    }
}
