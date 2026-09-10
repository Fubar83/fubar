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

    private static InheritedPaths Paths(params string[] add) => InheritedPaths.FromAdded(add);

    private static InheritedPaths Dropping(params string[] remove) => new() { Remove = [.. remove] };

    [Fact]
    public void With_no_layers_everything_falls_back_to_the_built_in_defaults()
    {
        var resolved = ComparisonSettingsResolver.Resolve([]);

        Assert.False(resolved.IgnoreWhitespace.Value);
        Assert.False(resolved.IgnoreCase.Value);
        Assert.False(resolved.ReportPropertyOrder.Value);
        Assert.Empty(resolved.IgnoredPaths);
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
            Folder(new ComparisonSettings { IgnoredPaths = Paths("$.traceId") }),
            Request(new ComparisonSettings { IgnoreCase = false }),
        ]);

        // Overridden at the request.
        Assert.False(resolved.IgnoreCase.Value);
        Assert.Equal(ComparisonScope.Request, resolved.IgnoreCase.Scope);

        // Still the global's, even though nearer levels exist and set OTHER things.
        Assert.True(resolved.IgnoreWhitespace.Value);
        Assert.Equal(ComparisonScope.Global, resolved.IgnoreWhitespace.Scope);

        // Still the folder's, and the entry says so.
        Assert.Equal(["$.traceId"], resolved.IgnoredPathValues);
        Assert.Equal(ComparisonScope.Folder, resolved.IgnoredPaths[0].Scope);
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
    /// Lists ACCUMULATE down the chain rather than the nearest level replacing everything above it.
    /// This is the change that lets a request add one rule without restating its folder's - which is
    /// what the UI used to do silently, ending inheritance for that setting.
    /// </summary>
    [Fact]
    public void Ignored_paths_accumulate_down_the_chain()
    {
        var resolved = ComparisonSettingsResolver.Resolve([
            Folder(new ComparisonSettings { IgnoredPaths = Paths("$.traceId", "$..timestamp") }),
            Request(new ComparisonSettings { IgnoredPaths = Paths("$.meta.requestId") }),
        ]);

        Assert.Equal(["$.traceId", "$..timestamp", "$.meta.requestId"], resolved.IgnoredPathValues);
    }

    /// <summary>Each entry carries the level that added it, which is what a chip's "inherited from"
    /// label and its ✕ both need.</summary>
    [Fact]
    public void Each_resolved_path_names_the_level_that_added_it()
    {
        var resolved = ComparisonSettingsResolver.Resolve([
            Folder(new ComparisonSettings { IgnoredPaths = Paths("$.traceId") }, "users"),
            Request(new ComparisonSettings { IgnoredPaths = Paths("$.meta.requestId") }),
        ]);

        Assert.Equal(ComparisonScope.Folder, resolved.IgnoredPaths[0].Scope);
        Assert.Equal("Folder: users", resolved.IgnoredPaths[0].SourceName);
        Assert.Equal(ComparisonScope.Request, resolved.IgnoredPaths[1].Scope);
    }

    /// <summary>The other half of add/remove: a level can drop a rule it inherited without touching
    /// the level that set it, which is the only way to say "wrong for this one endpoint".</summary>
    [Fact]
    public void A_level_can_remove_an_inherited_path()
    {
        var resolved = ComparisonSettingsResolver.Resolve([
            Folder(new ComparisonSettings { IgnoredPaths = Paths("$.traceId", "$..timestamp") }),
            Request(new ComparisonSettings { IgnoredPaths = Dropping("$.traceId") }),
        ]);

        Assert.Equal(["$..timestamp"], resolved.IgnoredPathValues);
    }

    /// <summary>
    /// Removing something nothing added is fine. A level is allowed to say "not here" about a rule an
    /// ancestor might grow later, and failing over it would make the answer depend on the order the
    /// files happened to be written in.
    /// </summary>
    [Fact]
    public void Removing_a_path_nothing_added_is_not_an_error()
    {
        var resolved = ComparisonSettingsResolver.Resolve([
            Request(new ComparisonSettings { IgnoredPaths = Dropping("$.neverSet") }),
        ]);

        Assert.Empty(resolved.IgnoredPaths);
    }

    /// <summary>A level re-adding what it already inherited must not duplicate it, and the DEEPEST
    /// level to add it is the one reported - that is the file a reader would edit to be rid of it.</summary>
    [Fact]
    public void Re_adding_an_inherited_path_moves_its_origin_rather_than_duplicating_it()
    {
        var resolved = ComparisonSettingsResolver.Resolve([
            Folder(new ComparisonSettings { IgnoredPaths = Paths("$.traceId") }),
            Request(new ComparisonSettings { IgnoredPaths = Paths("$.traceId") }),
        ]);

        Assert.Equal(["$.traceId"], resolved.IgnoredPathValues);
        Assert.Equal(ComparisonScope.Request, resolved.IgnoredPaths[0].Scope);
    }

    /// <summary>A removal at one level does not stop a deeper one adding it back.</summary>
    [Fact]
    public void A_deeper_level_can_add_back_what_a_nearer_ancestor_removed()
    {
        var resolved = ComparisonSettingsResolver.Resolve([
            Global(new ComparisonSettings { IgnoredPaths = Paths("$.traceId") }),
            Folder(new ComparisonSettings { IgnoredPaths = Dropping("$.traceId") }),
            Request(new ComparisonSettings { IgnoredPaths = Paths("$.traceId") }),
        ]);

        Assert.Equal(["$.traceId"], resolved.IgnoredPathValues);
        Assert.Equal(ComparisonScope.Request, resolved.IgnoredPaths[0].Scope);
    }

    /// <summary>A contribution that says nothing leaves what it inherited exactly as it was - the same
    /// meaning "null" has for every scalar here.</summary>
    [Fact]
    public void An_empty_contribution_changes_nothing()
    {
        var resolved = ComparisonSettingsResolver.Resolve([
            Folder(new ComparisonSettings { IgnoredPaths = Paths("$.traceId") }),
            Request(new ComparisonSettings { IgnoredPaths = new InheritedPaths() }),
        ]);

        Assert.Equal(["$.traceId"], resolved.IgnoredPathValues);
        Assert.Equal(ComparisonScope.Folder, resolved.IgnoredPaths[0].Scope);
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
    /// The other two thirds of how one array is matched. They replace rather than merge, like the key
    /// overrides beside them: these three lists answer ONE question per array - how is it matched -
    /// and an array can only be matched one way, so building them up across levels would produce
    /// contradictions no level wrote.
    /// </summary>
    [Fact]
    public void The_order_lists_resolve_like_the_key_overrides()
    {
        var resolved = ComparisonSettingsResolver.Resolve([
            Folder(new ComparisonSettings { UnorderedArrays = ["$.tags"], PositionalArrays = ["$.steps"] }),
            Request(new ComparisonSettings { UnorderedArrays = ["$.tags", "$.roles"] }),
        ]);

        Assert.Equal(["$.tags", "$.roles"], resolved.UnorderedArrays.Value);
        Assert.Equal(ComparisonScope.Request, resolved.UnorderedArrays.Scope);

        // Untouched by the request, so still the folder's - per-setting inheritance, as everywhere else.
        Assert.Equal(["$.steps"], resolved.PositionalArrays.Value);
        Assert.Equal(ComparisonScope.Folder, resolved.PositionalArrays.Scope);
    }

    /// <summary>
    /// An EMPTY non-null list is a real override meaning "nothing here", not silence. It is what stops
    /// a folder's rule coming back to contradict a request that has just chosen something else for the
    /// same array.
    /// </summary>
    [Fact]
    public void An_empty_list_at_a_closer_level_overrides_an_inherited_one()
    {
        var resolved = ComparisonSettingsResolver.Resolve([
            Folder(new ComparisonSettings { UnorderedArrays = ["$.tags"] }),
            Request(new ComparisonSettings { UnorderedArrays = [] }),
        ]);

        Assert.Empty(resolved.UnorderedArrays.Value);
        Assert.Equal(ComparisonScope.Request, resolved.UnorderedArrays.Scope);
    }

    [Fact]
    public void With_no_layers_no_array_is_spoken_about()
    {
        var resolved = ComparisonSettingsResolver.Resolve([]);

        Assert.Empty(resolved.UnorderedArrays.Value);
        Assert.Empty(resolved.PositionalArrays.Value);
    }

    /// <summary>
    /// The resolved collections must be copies: handing back the caller's own list would let a later
    /// edit of the settings object silently change what a running comparison thinks it resolved.
    /// </summary>
    [Fact]
    public void Resolved_collections_are_copies_not_the_stored_instances()
    {
        var settings = new ComparisonSettings { IgnoredPaths = Paths("$.a") };

        var resolved = ComparisonSettingsResolver.Resolve([Request(settings)]);
        settings.IgnoredPaths!.Add.Add("$.b");

        Assert.Equal(["$.a"], resolved.IgnoredPathValues);
    }
}
