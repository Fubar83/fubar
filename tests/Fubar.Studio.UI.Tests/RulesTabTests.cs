using Fubar.Studio.Application.Comparison;
using Fubar.Studio.Core.Comparison;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Snapshots;
using Fubar.Studio.UI.ViewModels;

namespace Fubar.Studio.UI.Tests;

/// <summary>
/// The Rules tab: which rules apply here, where each came from, and what a click changes.
///
/// <para>The rule that shapes all of it: an inherited rule is never edited in place. Removing one
/// writes a REMOVAL at this level, because a click in one endpoint's window must not change what
/// every other endpoint under that folder does.</para>
/// </summary>
public class RulesTabTests
{
    private const string Folder = "Folder: orders";

    // ---- A resolver that answers with whatever the test set up --------------------------------

    private sealed class Chain : IRequestComparisonSettings
    {
        public ComparisonSettings? Inherited { get; set; }

        public List<Tolerance> InheritedTolerances { get; } = [];

        public SnapshotPolicy? InheritedSnapshot { get; set; }

        /// <summary>Set by the view model under test, through the level it was given.</summary>
        public Func<ComparisonSettings?>? Own { get; set; }

        public Func<List<Tolerance>?>? OwnTolerances { get; set; }

        public Func<SnapshotPolicy?>? OwnSnapshot { get; set; }

        public int Resolutions { get; private set; }

        /// <summary>The real fold, over the real layers - so the rows say what a RUN would do rather
        /// than what a second, agreeing-by-luck implementation in a test would.</summary>
        /// <summary>A folder resolves the same layers minus anything below it, which for this fake
        /// is the same call with no case.</summary>
        public Task<ResolvedRequestRules> ResolveFolderRulesAsync(
            Workspace workspace, string folderPath, CancellationToken ct = default) =>
            ResolveRulesAsync(workspace, folderPath, null, null, ct);

        public Task<ResolvedRequestRules> ResolveRulesAsync(
            Workspace workspace,
            string requestPath,
            string? casePath = null,
            BatchOverlay? overlay = null,
            CancellationToken cancellationToken = default)
        {
            Resolutions++;

            var scope = casePath is null ? ComparisonScope.Request : ComparisonScope.Case;
            var name = casePath is null ? "Request" : "Case: created";

            var comparison = ComparisonSettingsResolver.Resolve(
            [
                new ComparisonSettingsLayer(Inherited, ComparisonScope.Folder, Folder),
                new ComparisonSettingsLayer(Own?.Invoke(), scope, name),
            ]);

            var tolerances = ToleranceResolver.Resolve(
            [
                new ToleranceLayer(InheritedTolerances, ComparisonScope.Folder, Folder),
                new ToleranceLayer(OwnTolerances?.Invoke(), scope, name),
            ]);

            var snapshot = SnapshotPolicyResolver.Resolve(
            [
                new SnapshotPolicyLayer(InheritedSnapshot, ComparisonScope.Folder, Folder),
                new SnapshotPolicyLayer(OwnSnapshot?.Invoke(), ComparisonScope.Request, "Request"),
            ]);

            return Task.FromResult(new ResolvedRequestRules(comparison, tolerances, snapshot));
        }
    }

    private sealed class Level
    {
        public ComparisonSettings? Comparison;
        public List<Tolerance>? Tolerances;
        public SnapshotPolicy? Snapshot;
        public int Changes;
    }

    private static async Task<(RulesViewModel Rules, Level Own, Chain Chain)> OpenAsync(
        Action<Chain>? setUp = null, bool asCase = false)
    {
        var chain = new Chain();
        setUp?.Invoke(chain);

        var own = new Level();
        chain.Own = () => own.Comparison;
        chain.OwnTolerances = () => own.Tolerances;
        chain.OwnSnapshot = () => own.Snapshot;

        var level = new RuleLevel
        {
            Scope = asCase ? ComparisonScope.Case : ComparisonScope.Request,
            LevelName = asCase ? "this case" : "this request",
            GetComparison = () => own.Comparison,
            SetComparison = v => own.Comparison = v,
            GetTolerances = () => own.Tolerances,
            SetTolerances = v => own.Tolerances = v,
            GetSnapshot = asCase ? null : () => own.Snapshot,
            SetSnapshot = asCase ? null : v => own.Snapshot = v,
            Changed = () => own.Changes++,
        };

        var rules = new RulesViewModel(
            level,
            new Workspace { RootPath = "/w", Manifest = new AppManifest { Name = "w" } },
            "/w/collections/orders/get-order/endpoint.json",
            asCase ? "/w/collections/orders/get-order/cases/created.json" : null,
            chain,
            new StatusLogViewModel());

        await rules.RefreshAsync(TestContext.Current.CancellationToken);

        return (rules, own, chain);
    }

    // ---- Reading -------------------------------------------------------------------------------

    [Fact]
    public async Task An_option_says_what_it_resolves_to_and_who_decided()
    {
        var (rules, _, _) = await OpenAsync(
            c => c.Inherited = new ComparisonSettings { IgnoreNullVsMissing = true });

        var row = rules.Options.Single(o => o.Name == "Null is the same as missing");

        Assert.True(row.EffectiveValue);
        Assert.Equal(Folder, row.SourceName);
        Assert.Equal(RuleOptionRowViewModel.Inherit, row.Choice);
        Assert.Equal("on · from Folder: orders", row.EffectiveDescription);
    }

    /// <summary>An option nobody has overridden reads as the built-in default, not as something this
    /// level chose - which is what the third state is for.</summary>
    [Fact]
    public async Task An_option_nobody_set_says_Default()
    {
        var (rules, _, _) = await OpenAsync();

        var row = rules.Options.Single(o => o.Name == "Ignore case");

        Assert.False(row.EffectiveValue);
        Assert.Equal("Default", row.SourceName);
        Assert.Equal(RuleOptionRowViewModel.Inherit, row.Choice);
    }

    [Fact]
    public async Task An_inherited_rule_carries_its_origin_and_is_not_local()
    {
        var (rules, _, _) = await OpenAsync(c => c.Inherited = new ComparisonSettings
        {
            IgnoredPaths = new InheritedPaths { Add = ["$.meta.requestId"] },
        });

        var row = rules.IgnoredPaths.Single();

        Assert.Equal("$.meta.requestId", row.Path);
        Assert.Equal(Folder, row.SourceName);
        Assert.True(row.IsInherited);
        Assert.False(row.IsLocal);
    }

    [Fact]
    public async Task A_tolerance_says_what_it_allows()
    {
        var (rules, _, _) = await OpenAsync(c =>
        {
            c.InheritedTolerances.Add(new Tolerance { Path = "$.total", Numeric = 0.01 });
            c.InheritedTolerances.Add(new Tolerance { Path = "$.at", WithinSeconds = 30 });
            c.InheritedTolerances.Add(new Tolerance { Path = "$.id", Matches = "^[0-9a-f]+$" });
            c.InheritedTolerances.Add(new Tolerance { Path = "$.state", OneOf = ["queued", "running"] });
        });

        Assert.Equal(
            ["within 0.01", "within 30 s", "matches ^[0-9a-f]+$", "one of queued, running"],
            rules.Tolerances.Select(t => t.Detail));
    }

    /// <summary>A rule stating none or several allowances forgives nothing at run time, so the tab
    /// says so rather than showing a blank beside a path that looks covered.</summary>
    [Fact]
    public async Task A_tolerance_that_states_no_rule_says_so()
    {
        var (rules, _, _) = await OpenAsync(
            c => c.InheritedTolerances.Add(new Tolerance { Path = "$.total", Numeric = 1, WithinSeconds = 1 }));

        Assert.Contains("allows nothing", rules.Tolerances.Single().Detail, StringComparison.Ordinal);
    }

    // ---- The three states ----------------------------------------------------------------------

    [Fact]
    public async Task Turning_an_option_on_writes_it_at_this_level()
    {
        var (rules, own, _) = await OpenAsync();

        rules.Options.Single(o => o.Name == "Ignore case").Choice = RuleOptionRowViewModel.On;

        Assert.True(own.Comparison?.IgnoreCase);
        Assert.Equal(1, own.Changes);
    }

    /// <summary>"Off" is not "inherit". An option turned off here has to keep saying off when the
    /// folder above turns it on.</summary>
    [Fact]
    public async Task Turning_an_option_off_over_an_inherited_on_is_an_override()
    {
        var (rules, own, _) = await OpenAsync(
            c => c.Inherited = new ComparisonSettings { IgnoreCase = true });

        rules.Options.Single(o => o.Name == "Ignore case").Choice = RuleOptionRowViewModel.Off;

        Assert.False(own.Comparison?.IgnoreCase);
        Assert.False(rules.Options.Single(o => o.Name == "Ignore case").EffectiveValue);
    }

    /// <summary>Back to Inherit, and the level's section goes with it - so the file says what it
    /// overrides and nothing else, rather than keeping an override that now matches by luck.</summary>
    [Fact]
    public async Task Going_back_to_inherit_drops_the_override_entirely()
    {
        var (rules, own, _) = await OpenAsync();

        var row = rules.Options.Single(o => o.Name == "Ignore case");
        row.Choice = RuleOptionRowViewModel.On;
        rules.Options.Single(o => o.Name == "Ignore case").Choice = RuleOptionRowViewModel.Inherit;

        Assert.Null(own.Comparison);
    }

    // ---- Removing ------------------------------------------------------------------------------

    /// <summary>The one that matters: an inherited rule is stopped HERE, never edited where it was
    /// written.</summary>
    [Fact]
    public async Task Removing_an_inherited_ignore_writes_a_removal_at_this_level()
    {
        var (rules, own, _) = await OpenAsync(c => c.Inherited = new ComparisonSettings
        {
            IgnoredPaths = new InheritedPaths { Add = ["$.meta.requestId"] },
        });

        rules.RemoveIgnoredPathCommand.Execute(rules.IgnoredPaths.Single());

        Assert.Equal(["$.meta.requestId"], own.Comparison?.IgnoredPaths?.Remove);
        Assert.Empty(own.Comparison?.IgnoredPaths?.Add ?? []);
        Assert.Empty(rules.IgnoredPaths);
    }

    [Fact]
    public async Task Removing_a_local_ignore_just_deletes_it()
    {
        var (rules, own, _) = await OpenAsync();

        rules.NewIgnoredPath = "$.debug";
        rules.AddIgnoredPathCommand.Execute(null);
        await rules.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.True(rules.IgnoredPaths.Single().IsLocal);

        rules.RemoveIgnoredPathCommand.Execute(rules.IgnoredPaths.Single());

        // Deleted, NOT turned into a removal of something nothing above ever added.
        Assert.Null(own.Comparison);
    }

    /// <summary>Re-adding what this level had stopped inheriting un-does the removal rather than
    /// leaving the file saying both "remove $.id" and "add $.id".</summary>
    [Fact]
    public async Task Re_adding_a_stopped_rule_removes_the_stop()
    {
        var (rules, own, _) = await OpenAsync(c => c.Inherited = new ComparisonSettings
        {
            IgnoredPaths = new InheritedPaths { Add = ["$.meta.requestId"] },
        });

        rules.RemoveIgnoredPathCommand.Execute(rules.IgnoredPaths.Single());

        rules.NewIgnoredPath = "$.meta.requestId";
        rules.AddIgnoredPathCommand.Execute(null);

        Assert.Empty(own.Comparison?.IgnoredPaths?.Remove ?? []);
        Assert.Equal(["$.meta.requestId"], own.Comparison?.IgnoredPaths?.Add);
    }

    // ---- Snapshot policy -------------------------------------------------------------------------

    [Fact]
    public async Task Adding_a_redaction_writes_it_with_its_replacement()
    {
        var (rules, own, _) = await OpenAsync();

        rules.NewRedactPath = "$..token";
        rules.NewRedactAs = "<gone>";
        rules.AddRedactionCommand.Execute(null);

        var rule = own.Snapshot?.Redact?.Add.Single();

        Assert.Equal("$..token", rule?.Path);
        Assert.Equal("<gone>", rule?.As);
    }

    [Fact]
    public async Task Removing_an_inherited_redaction_writes_a_removal_at_this_level()
    {
        var (rules, own, _) = await OpenAsync(c => c.InheritedSnapshot = new SnapshotPolicy
        {
            Redact = new InheritedRules { Add = [new SnapshotRule("$..token", "<redacted>")] },
        });

        rules.RemoveRedactionCommand.Execute(rules.Redactions.Single());

        Assert.Equal(["$..token"], own.Snapshot?.Redact?.Remove);
    }

    /// <summary>A snapshot belongs to the endpoint, and its policy is resolved without the case. The
    /// tab says so instead of offering controls whose value would be dropped on save.</summary>
    [Fact]
    public async Task A_case_shows_the_snapshot_policy_and_cannot_change_it()
    {
        var (rules, own, _) = await OpenAsync(
            c => c.InheritedSnapshot = new SnapshotPolicy
            {
                Normalize = new InheritedRules { Add = [new SnapshotRule("$.at", "<timestamp>")] },
            },
            asCase: true);

        Assert.False(rules.SupportsSnapshotPolicy);
        Assert.Equal("$.at", rules.Normalisations.Single().Path);

        rules.NewRedactPath = "$..token";
        rules.AddRedactionCommand.Execute(null);

        Assert.Null(own.Snapshot);
        Assert.Equal(0, own.Changes);
    }

    [Fact]
    public async Task Headers_kept_says_which_level_chose_them()
    {
        var (rules, _, _) = await OpenAsync(
            c => c.InheritedSnapshot = new SnapshotPolicy { Headers = ["Content-Type"] });

        Assert.Contains("Content-Type", rules.SnapshotHeaders, StringComparison.Ordinal);
        Assert.Contains(Folder, rules.SnapshotHeaders, StringComparison.Ordinal);
    }

    // ---- Tolerances ------------------------------------------------------------------------------

    [Fact]
    public async Task Adding_a_tolerance_writes_the_kind_that_was_chosen()
    {
        var (rules, own, _) = await OpenAsync();

        rules.NewTolerancePath = "$.total";
        rules.NewToleranceKind = ToleranceKind.Numeric;
        rules.NewToleranceValue = "0.01";
        rules.AddToleranceCommand.Execute(null);

        var tolerance = own.Tolerances?.Single();

        Assert.Equal("$.total", tolerance?.Path);
        Assert.Equal(0.01, tolerance?.Numeric);
        Assert.Equal(ToleranceKind.Numeric, tolerance?.Kind);
    }

    /// <summary>Refused rather than written half-formed: a tolerance stating no rule forgives nothing,
    /// so a silently-empty one would look like an allowance and behave like none.</summary>
    [Fact]
    public async Task A_tolerance_with_no_usable_value_is_refused()
    {
        var (rules, own, _) = await OpenAsync();

        rules.NewTolerancePath = "$.total";
        rules.NewToleranceKind = ToleranceKind.Numeric;
        rules.NewToleranceValue = "about a penny";
        rules.AddToleranceCommand.Execute(null);

        Assert.Null(own.Tolerances);
    }

    /// <summary>Per path, closest wins (spec §4.4) - so restating one replaces this level's rule for
    /// it rather than leaving two the resolver would have to choose between.</summary>
    [Fact]
    public async Task Restating_a_tolerance_replaces_this_levels_rule_for_that_path()
    {
        var (rules, own, _) = await OpenAsync();

        rules.NewTolerancePath = "$.total";
        rules.NewToleranceValue = "0.01";
        rules.AddToleranceCommand.Execute(null);

        rules.NewTolerancePath = "$.total";
        rules.NewToleranceValue = "5";
        rules.AddToleranceCommand.Execute(null);

        Assert.Equal(5, own.Tolerances?.Single().Numeric);
    }

    /// <summary>Tolerances are per-path closest-wins rather than add/remove, so there is nothing to
    /// write at this level that means "allow nothing here".</summary>
    [Fact]
    public async Task An_inherited_tolerance_cannot_be_removed_from_here()
    {
        var (rules, own, _) = await OpenAsync(
            c => c.InheritedTolerances.Add(new Tolerance { Path = "$.total", Numeric = 0.01 }));

        rules.RemoveToleranceCommand.Execute(rules.Tolerances.Single());

        Assert.Null(own.Tolerances);
        Assert.Single(rules.Tolerances);
    }

    // ---- Staying true ----------------------------------------------------------------------------

    /// <summary>An edit here can change what an inherited rule RESOLVES to, so the whole chain is
    /// re-read rather than the one row being patched - otherwise a restated tolerance would keep
    /// claiming the folder wrote it.</summary>
    [Fact]
    public async Task An_edit_re_reads_the_whole_chain()
    {
        var (rules, _, chain) = await OpenAsync();

        Assert.Equal(1, chain.Resolutions);

        rules.Options[0].Choice = RuleOptionRowViewModel.On;

        Assert.Equal(2, chain.Resolutions);
    }

    [Fact]
    public async Task Restating_an_inherited_rule_makes_the_row_say_this_level()
    {
        var (rules, _, _) = await OpenAsync(
            c => c.InheritedTolerances.Add(new Tolerance { Path = "$.total", Numeric = 0.01 }));

        Assert.Equal(Folder, rules.Tolerances.Single().SourceName);

        rules.NewTolerancePath = "$.total";
        rules.NewToleranceValue = "5";
        rules.AddToleranceCommand.Execute(null);
        await rules.RefreshAsync(TestContext.Current.CancellationToken);

        var row = rules.Tolerances.Single();

        Assert.Equal("Request", row.SourceName);
        Assert.True(row.IsLocal);
        Assert.Equal("within 5", row.Detail);
    }
    // ---- A folder is a level too ---------------------------------------------------------------

    /// <summary>
    /// Every folder in the chain carries <c>ComparisonScope.Folder</c>, so the level has to say WHICH
    /// folder it is.
    /// </summary>
    /// <remarks>
    /// Matching on scope alone would call a grandparent's rules this folder's own - and then offer to
    /// delete them here, which is exactly what "an inherited rule is never edited in place" forbids.
    /// </remarks>
    [Fact]
    public void A_folder_tells_its_own_rules_from_an_ancestors()
    {
        var own = new RuleEntryRowViewModel(
            "$.a", "never reported", ComparisonScope.Folder, "Folder: orders",
            ComparisonScope.Folder, "Folder: orders");

        var ancestors = new RuleEntryRowViewModel(
            "$.b", "never reported", ComparisonScope.Folder, "Folder: Workspace Root",
            ComparisonScope.Folder, "Folder: orders");

        Assert.True(own.IsLocal);
        Assert.True(ancestors.IsInherited);
    }

    /// <summary>A level that gives no name matches on scope alone, which is right for the levels
    /// where there is only ever one of them - a request, a case.</summary>
    [Fact]
    public void A_level_with_no_name_matches_on_scope_alone()
    {
        var row = new RuleEntryRowViewModel(
            "$.a", "never reported", ComparisonScope.Request, "Request", ComparisonScope.Request);

        Assert.True(row.IsLocal);
    }
}
