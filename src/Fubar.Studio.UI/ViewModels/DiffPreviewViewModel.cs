using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Controls.ViewModels;
using Fubar.Diff.Core.Comparison;

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

    public DiffPreviewViewModel(IFileComparisonService comparison) => _comparison = comparison;

    /// <summary>The diff widget itself.</summary>
    public DiffPaneViewModel Pane { get; } = new();

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
        string title)
    {
        Title = title;
        LeftLabel = leftLabel;
        RightLabel = rightLabel;
        IsBusy = true;

        try
        {
            var result = await _comparison
                .CompareTextAsync(leftText, rightText, ComparisonOptions.Default, leftLabel, rightLabel)
                .ConfigureAwait(true);

            Pane.Show(result.Result, result.IsSemantic, result.SemanticChanges);

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

        if (result.AreIdentical)
        {
            return comparison.IsSemantic
                ? "No semantic differences - these differ only in formatting or ordering."
                : "Identical.";
        }

        return comparison.IsSemantic
            ? $"semantic: {comparison.SemanticChanges.Count} change(s) across {result.Hunks.Count} region(s)"
            : $"{result.Hunks.Count} change(s) - {result.Inserted} added, {result.Deleted} removed, "
              + $"{result.Modified} changed";
    }
}
