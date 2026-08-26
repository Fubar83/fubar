using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Controls.ViewModels;
using Fubar.Diff.Core.Comparison;
using Fubar.Studio.UI.Services;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>
/// Hosts the shared diff widget for API Studio's own comparisons: an existing request against the one
/// an OpenAPI spec would import, two HTTP responses, or a response against a previous run.
///
/// Everything API Studio compares is already in memory, so it goes through
/// <see cref="IFileComparisonService.CompareTextAsync"/> rather than the file path Fubar Diff uses.
/// The pane and every renderer behind it are identical either way.
/// </summary>
public partial class DiffPreviewViewModel : ViewModelBase
{
    private readonly IFileComparisonService _comparison;

    /// <summary>The content being compared, kept so ignoring a path can re-run the comparison.</summary>
    private string _leftText = string.Empty;
    private string _rightText = string.Empty;

    private DiffIgnoreContext? _ignoreContext;

    // The ignore command is set in LoadAsync, once it is known whether this comparison has a host
    // that can hold a rule.
    public DiffPreviewViewModel(IFileComparisonService comparison)
    {
        _comparison = comparison;
    }

    /// <summary>The diff widget itself.</summary>
    public DiffPaneViewModel Pane { get; } = new();

    // ---- Ignore rules ---------------------------------------------------------------------------

    /// <summary>Rules in force for this comparison, newest last. Bound as removable chips.</summary>
    public ObservableCollection<string> IgnoredPaths { get; } = [];

    /// <summary>True once the set differs from what was persisted, which is what enables Save.</summary>
    [ObservableProperty]
    public partial bool IgnoreRulesDirty { get; set; }

    /// <summary>Whether this comparison can persist its rules at all.</summary>
    public bool CanSaveIgnoreRules => _ignoreContext?.SaveAsync is not null;

    /// <summary>Whether to show the rules strip - hidden entirely for a comparison that has no host.</summary>
    public bool ShowIgnoreRules => _ignoreContext is not null;

    /// <summary>
    /// Adds a rule and re-compares immediately, so the effect is visible rather than promised. The
    /// rule is session-only until saved - ignoring is often exploratory, and silently rewriting
    /// request.json on a click inside a diff window is a side effect nobody asked for.
    /// </summary>
    private async Task IgnorePathAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || IgnoredPaths.Contains(path))
        {
            return;
        }

        IgnoredPaths.Add(path);
        IgnoreRulesDirty = true;

        await RecompareAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RemoveIgnoreAsync(string? path)
    {
        if (path is null || !IgnoredPaths.Remove(path))
        {
            return;
        }

        IgnoreRulesDirty = true;
        await RecompareAsync().ConfigureAwait(true);
    }

    /// <summary>Persists the rules onto whatever owns this comparison, via the host's callback.</summary>
    [RelayCommand]
    private async Task SaveIgnoreRulesAsync()
    {
        if (_ignoreContext?.SaveAsync is not { } save)
        {
            return;
        }

        await save(IgnoredPaths.ToList()).ConfigureAwait(true);

        IgnoreRulesDirty = false;
        StatusMessage = $"Saved {IgnoredPaths.Count} ignore rule(s) to the request.";
    }

    /// <summary>Dialog title, e.g. "GET /api/users — existing vs spec".</summary>
    [ObservableProperty]
    public partial string Title { get; set; } = "Compare";

    /// <summary>Label above the left pane.</summary>
    [ObservableProperty]
    public partial string LeftLabel { get; set; } = "Left";

    /// <summary>Label above the right pane.</summary>
    [ObservableProperty]
    public partial string RightLabel { get; set; } = "Right";

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>
    /// Compares two pieces of text and shows the result.
    ///
    /// Defaults to <see cref="ComparisonMode.Auto"/>, so anything that parses as JSON - which most of
    /// what API Studio compares does - is compared semantically. That is the difference between
    /// "these two responses differ" and "these two responses differ only in key order".
    /// </summary>
    public async Task LoadAsync(
        string leftText,
        string rightText,
        string leftLabel,
        string rightLabel,
        string title,
        DiffIgnoreContext? ignore = null)
    {
        Title = title;
        LeftLabel = leftLabel;
        RightLabel = rightLabel;

        _leftText = leftText;
        _rightText = rightText;
        _ignoreContext = ignore;

        IgnoredPaths.Clear();
        foreach (var path in ignore?.Paths ?? [])
        {
            IgnoredPaths.Add(path);
        }

        IgnoreRulesDirty = false;

        // Hides the "ignore" affordance in the tree for a comparison with nowhere to put a rule.
        Pane.IgnorePathCommand = ignore is null
            ? null
            : new RelayCommand<string>(path => _ = IgnorePathAsync(path));

        OnPropertyChanged(nameof(ShowIgnoreRules));
        OnPropertyChanged(nameof(CanSaveIgnoreRules));

        await RecompareAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Runs the comparison against the current rule set. Called on load and again after every rule
    /// change - the rules are a comparison OPTION, so the only way to apply them is to compare again.
    /// </summary>
    private async Task RecompareAsync()
    {
        IsBusy = true;

        try
        {
            var options = ComparisonOptions.Default with
            {
                Json = ComparisonOptions.Default.Json with { IgnoredPaths = IgnoredPaths.ToList() },
            };

            var result = await _comparison
                .CompareTextAsync(_leftText, _rightText, options, LeftLabel, RightLabel)
                .ConfigureAwait(true);

            Pane.Show(
                result.Result,
                result.IsSemantic,
                result.SemanticChanges,
                result.OriginalLeftText,
                result.OriginalRightText,
                result.OriginalSemanticChanges);

            StatusMessage = Describe(result);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string Describe(FileComparison comparison)
    {
        var result = comparison.Result;

        // Ignored changes form no hunk and are drawn only as a faint band, so they are counted
        // separately - reporting them among the changes would contradict what the view shows.
        var ignored = comparison.SemanticChanges.Count(c => c.IsIgnored);
        var counted = comparison.SemanticChanges.Count - ignored;
        var suffix = ignored > 0 ? $"   ·   {ignored} ignored" : string.Empty;

        if (result.AreIdentical)
        {
            if (!comparison.IsSemantic)
            {
                return "Identical.";
            }

            // Worth distinguishing: "nothing differs" and "everything that differs is ignored" look
            // the same on screen, and only one of them means the responses actually match.
            return ignored > 0
                ? $"No differences outside the ignored paths.{suffix}"
                : "No semantic differences - these differ only in formatting or ordering.";
        }

        return comparison.IsSemantic
            ? $"semantic: {counted} change(s) across {result.Hunks.Count} region(s){suffix}"
            : $"{result.Hunks.Count} change(s) - {result.Inserted} added, {result.Deleted} removed, "
              + $"{result.Modified} changed";
    }
}
