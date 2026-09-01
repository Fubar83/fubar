using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Application.Merge;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Merge;
using Fubar.Diff.Core.Models;
using Fubar.Diff.Core.Settings;
using Fubar.Diff.UI.Services;
using Fubar.Diff.UI.ViewModels;

namespace Fubar.Diff.UI.Tests;

/// <summary>
/// The merge tab's own behaviour, with the services faked.
///
/// What is under test here is the part the domain deliberately does NOT own: resolving a region moves
/// on to the next one, saving past an unresolved conflict says so, and a re-merge cannot leave a stale
/// decision pointing at a region that no longer exists. The merge algorithm itself is covered in Core.
/// </summary>
public class MergeViewModelTests
{
    /// <summary>Returns a merge built from three in-memory documents, without touching a disk.</summary>
    private sealed class StubComparisonService : IThreeWayComparisonService
    {
        private readonly Func<ThreeWayResult> _result;

        public StubComparisonService(Func<ThreeWayResult> result) => _result = result;

        public int Calls { get; private set; }

        public Task<ThreeWayComparison> CompareFilesAsync(
            string ancestorPath,
            string leftPath,
            string rightPath,
            ComparisonOptions options,
            CancellationToken cancellationToken = default)
        {
            Calls++;

            return Task.FromResult(new ThreeWayComparison(
                new TextDocument(ancestorPath, [], TextFormat.Default),
                new TextDocument(leftPath, [], TextFormat.Default),
                new TextDocument(rightPath, [], TextFormat.Default),
                options,
                _result()));
        }

        public Task<ThreeWayComparison> RecompareAsync(
            ThreeWayComparison comparison,
            ComparisonOptions options,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(comparison);
    }

    /// <summary>Fails every read, for the error path.</summary>
    private sealed class FailingComparisonService : IThreeWayComparisonService
    {
        public Task<ThreeWayComparison> CompareFilesAsync(
            string ancestorPath, string leftPath, string rightPath,
            ComparisonOptions options, CancellationToken cancellationToken = default) =>
            throw new TextFileReadException(ancestorPath, "the file does not exist.");

        public Task<ThreeWayComparison> RecompareAsync(
            ThreeWayComparison comparison, ComparisonOptions options,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(comparison);
    }

    private sealed class RecordingMergeService : IMergeService
    {
        public ThreeWayMergeState? State { get; private set; }

        public MergeSide? Destination { get; private set; }

        public string? TargetPath { get; private set; }

        public Task<string> SaveThreeWayAsync(
            ThreeWayComparison comparison,
            ThreeWayMergeState state,
            MergeSide destination,
            string? targetPath = null,
            CancellationToken cancellationToken = default)
        {
            State = state;
            Destination = destination;
            TargetPath = targetPath;

            return Task.FromResult(targetPath ?? comparison.DocumentFor(destination).Path);
        }

        /// <summary>What a hand-edited merge is saved through - see <see cref="SavedLines"/>.</summary>
        public IReadOnlyList<string>? SavedLines { get; private set; }

        public Task<string> SaveThreeWayTextAsync(
            ThreeWayComparison comparison,
            MergeSide destination,
            IReadOnlyList<string> lines,
            string? targetPath = null,
            CancellationToken cancellationToken = default)
        {
            SavedLines = lines;
            Destination = destination;
            TargetPath = targetPath;

            return Task.FromResult(targetPath ?? comparison.DocumentFor(destination).Path);
        }

        /// <summary>The merged text, which the output pane is filled from.</summary>
        public string MergedText { get; set; } = string.Empty;

        public string PreviewThreeWay(ThreeWayComparison comparison, ThreeWayMergeState state, MergeSide destination) =>
            MergedText;

        // Not exercised here - the two-way half of the port belongs to ComparisonViewModel.
        public Task<string> SaveAsync(
            FileComparison comparison, MergeState state, DiffSide baseSide,
            string? targetPath = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty);

        public string Preview(FileComparison comparison, MergeState state, DiffSide baseSide) => string.Empty;
    }

    private sealed class StubPicker(string? file = null, string? save = null) : IFilePickerService
    {
        public Task<string?> PickFileAsync(string title) => Task.FromResult(file);

        public Task<IReadOnlyList<string>> PickFilesAsync(string title) =>
            Task.FromResult<IReadOnlyList<string>>(file is null ? [] : [file]);

        public Task<string?> PickSaveFileAsync(string title) => Task.FromResult(save);

        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    /// <summary>One auto-merged region, then a conflict, separated by a line all three agree on.</summary>
    private static ThreeWayResult OneAutoOneConflict() => ThreeWayResult.Create(
    [
        new ThreeWayLine(1, "a", 1, "A", 1, "a", MergeKind.LeftOnly, 0),
        new ThreeWayLine(2, "keep", 2, "keep", 2, "keep", MergeKind.Unchanged, -1),
        new ThreeWayLine(3, "b", 3, "L", 3, "R", MergeKind.Conflict, 1),
    ]);

    private static ThreeWayResult Clean() => ThreeWayResult.Create(
    [
        new ThreeWayLine(1, "a", 1, "a", 1, "a", MergeKind.Unchanged, -1),
    ]);

    /// <summary>Answers <see cref="IConfirmationService.ChooseAsync"/> with a fixed index.</summary>
    private sealed class StubConfirmation(int choice) : IConfirmationService
    {
        public int Asked { get; private set; }

        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel) =>
            Task.FromResult(false);

        public Task<int> ChooseAsync(string title, string message, IReadOnlyList<string> choices)
        {
            Asked++;

            return Task.FromResult(choice);
        }

        public Task<string?> AskForTextAsync(string title, string message, string initial = "") =>
            Task.FromResult<string?>(null);
    }

    private static (MergeViewModel Merge, RecordingMergeService Writer) Build(
        Func<ThreeWayResult>? result = null,
        IThreeWayComparisonService? comparison = null,
        IFilePickerService? picker = null,
        IConfirmationService? confirmation = null)
    {
        var writer = new RecordingMergeService();

        var merge = new MergeViewModel(
            comparison ?? new StubComparisonService(result ?? OneAutoOneConflict),
            writer,
            picker ?? new StubPicker(),
            new ThemeManagerViewModel(),
            confirmation)
        {
            BasePath = "base.cs",
            LeftPath = "left.cs",
            RightPath = "right.cs",
        };

        return (merge, writer);
    }

    // ---- Merging --------------------------------------------------------------------------------

    [Fact]
    public async Task Merging_shows_the_result_and_collapses_the_pickers()
    {
        var (merge, _) = Build();

        await merge.MergeAsync();

        Assert.Equal(3, merge.Pane.TotalLines);
        Assert.False(merge.IsFileRowExpanded);
        Assert.True(merge.CanSave);
    }

    [Fact]
    public async Task Merging_lands_on_the_first_conflict_rather_than_nowhere()
    {
        // Otherwise the user has to press Next just to find out whether anything needs them.
        var (merge, _) = Build();

        await merge.MergeAsync();

        Assert.Equal(1, merge.Pane.CurrentRegion);
        Assert.True(merge.Pane.SelectedRegion!.IsConflict);
    }

    [Fact]
    public async Task Nothing_happens_until_all_three_files_are_chosen()
    {
        var service = new StubComparisonService(OneAutoOneConflict);
        var merge = new MergeViewModel(service, new RecordingMergeService(), new StubPicker(), new ThemeManagerViewModel())
        {
            BasePath = "base.cs",
            LeftPath = "left.cs",
        };

        await merge.MergeAsync();

        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task A_read_failure_is_reported_and_leaves_nothing_half_loaded()
    {
        var (merge, _) = Build(comparison: new FailingComparisonService());

        await merge.MergeAsync();

        Assert.NotNull(merge.ErrorMessage);
        Assert.False(merge.CanSave);
        Assert.Equal(0, merge.Pane.TotalLines);
    }

    [Fact]
    public async Task A_clean_merge_says_so_rather_than_reporting_zero_conflicts()
    {
        var (merge, _) = Build(Clean);

        await merge.MergeAsync();

        Assert.Contains("Nothing to merge", merge.StatusMessage, StringComparison.Ordinal);
        Assert.False(merge.HasUnresolvedConflicts);
    }

    // ---- Resolving ------------------------------------------------------------------------------

    [Fact]
    public async Task Resolving_a_conflict_moves_on_to_the_next_thing_needing_attention()
    {
        // The difference between a merge tool that is used and one that is tolerated: with only one
        // conflict, "next" wraps back to it, but the decision is recorded and the count drops.
        var (merge, _) = Build();
        await merge.MergeAsync();

        Assert.Equal(1, merge.UnresolvedConflicts);

        merge.TakeLeftCommand.Execute(null);

        Assert.Equal(0, merge.UnresolvedConflicts);
        Assert.False(merge.HasUnresolvedConflicts);
    }

    [Fact]
    public async Task Resetting_a_region_puts_it_back_to_unresolved()
    {
        var (merge, _) = Build();
        await merge.MergeAsync();

        merge.TakeRightCommand.Execute(null);
        Assert.Equal(0, merge.UnresolvedConflicts);

        merge.Pane.CurrentRegion = 1;
        merge.ResetRegionCommand.Execute(null);

        Assert.Equal(1, merge.UnresolvedConflicts);
    }

    [Fact]
    public async Task Resolving_with_no_region_selected_does_nothing()
    {
        var (merge, writer) = Build();
        await merge.MergeAsync();

        merge.Pane.CurrentRegion = -1;
        merge.TakeLeftCommand.Execute(null);

        await merge.SaveCommand.ExecuteAsync(null);

        Assert.Equal(0, writer.State!.ResolvedCount);
    }

    [Fact]
    public async Task An_auto_merged_region_can_be_overridden_too()
    {
        var (merge, writer) = Build();
        await merge.MergeAsync();

        merge.Pane.CurrentRegion = 0;
        merge.TakeBaseCommand.Execute(null);

        await merge.SaveCommand.ExecuteAsync(null);

        Assert.Equal(MergeChoice.TakeBase, writer.State!.For(0));
    }

    // ---- Saving ---------------------------------------------------------------------------------

    [Fact]
    public async Task Saving_hands_the_decisions_and_the_destination_to_the_service()
    {
        var (merge, writer) = Build();
        await merge.MergeAsync();

        merge.TakeLeftCommand.Execute(null);
        await merge.SaveCommand.ExecuteAsync(null);

        Assert.Equal(MergeSide.Right, writer.Destination);
        Assert.Null(writer.TargetPath);
        Assert.Equal(MergeChoice.TakeLeft, writer.State!.For(1));
    }

    [Fact]
    public async Task Saving_past_an_unresolved_conflict_says_what_it_did()
    {
        // The fallback keeps the ancestor's text. That is allowed; being unaware of having done it is
        // not, which is why this string is asserted rather than left to chance.
        var (merge, _) = Build();
        await merge.MergeAsync();

        await merge.SaveCommand.ExecuteAsync(null);

        Assert.Contains("unresolved", merge.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("base version", merge.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_fully_resolved_save_reports_plainly()
    {
        var (merge, _) = Build();
        await merge.MergeAsync();

        merge.TakeLeftCommand.Execute(null);
        await merge.SaveCommand.ExecuteAsync(null);

        Assert.DoesNotContain("unresolved", merge.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Saved", merge.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Save_as_writes_to_the_chosen_path()
    {
        var (merge, writer) = Build(picker: new StubPicker(save: "merged.cs"));
        await merge.MergeAsync();

        await merge.SaveAsCommand.ExecuteAsync(null);

        Assert.Equal("merged.cs", writer.TargetPath);
    }

    [Fact]
    public async Task A_cancelled_save_as_writes_nothing()
    {
        var (merge, writer) = Build(picker: new StubPicker(save: null));
        await merge.MergeAsync();

        await merge.SaveAsCommand.ExecuteAsync(null);

        Assert.Null(writer.State);
    }

    [Fact]
    public async Task Saving_before_anything_is_loaded_does_nothing()
    {
        var (merge, writer) = Build();

        await merge.SaveCommand.ExecuteAsync(null);

        Assert.Null(writer.State);
    }

    // ---- Settings -------------------------------------------------------------------------------

    [Fact]
    public void Display_preferences_are_seeded_from_the_persisted_defaults()
    {
        var (merge, _) = Build();

        merge.ApplyDefaults(AppSettings.Default with { ShowInvisibles = true, SyntaxHighlighting = false });

        Assert.True(merge.Pane.ShowInvisibles);
        Assert.False(merge.Pane.SyntaxHighlighting);
    }

    [Fact]
    public async Task Re_merging_a_fresh_set_of_files_drops_the_previous_decisions()
    {
        // Decisions are keyed by region index, and a stale one would silently resolve a different
        // region of a different file.
        var (merge, writer) = Build();
        await merge.MergeAsync();

        merge.TakeLeftCommand.Execute(null);
        Assert.Equal(0, merge.UnresolvedConflicts);

        await merge.MergeAsync();

        Assert.Equal(1, merge.UnresolvedConflicts);
    }

    // ---- The editable merged output -------------------------------------------------------------

    [Fact]
    public async Task Merging_fills_the_output_pane_with_the_result()
    {
        var (merge, writer) = Build();
        writer.MergedText = "one\ntwo\n";

        await merge.MergeAsync();

        Assert.Equal("one\ntwo", merge.Pane.OutputDocument!.Text);
        Assert.False(merge.HasHandEdits);
    }

    [Fact]
    public async Task Resolving_a_region_rewrites_the_output()
    {
        // What makes the pane a live result rather than a preview someone has to refresh.
        var (merge, writer) = Build();
        writer.MergedText = "before";
        await merge.MergeAsync();

        writer.MergedText = "after";
        merge.TakeLeftCommand.Execute(null);

        Assert.Equal("after", merge.Pane.OutputDocument!.Text);
    }

    [Fact]
    public async Task A_hand_edited_result_is_saved_as_TEXT_rather_than_rebuilt_from_the_decisions()
    {
        // The whole point of the pane being editable: at this moment the decisions and the document
        // disagree, and the document is the one the user is looking at.
        var (merge, writer) = Build();
        await merge.MergeAsync();

        merge.Pane.OutputLinesReader = () => ["hand", "written"];
        merge.Pane.ReportOutputEdit();

        Assert.True(merge.HasHandEdits);

        await merge.SaveCommand.ExecuteAsync(null);

        Assert.Equal(["hand", "written"], writer.SavedLines);
        Assert.Null(writer.State); // never through the decisions
    }

    [Fact]
    public async Task An_untouched_result_still_saves_through_the_decisions()
    {
        var (merge, writer) = Build();
        await merge.MergeAsync();

        merge.Pane.OutputLinesReader = () => ["should not be used"];

        await merge.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(writer.State);
        Assert.Null(writer.SavedLines);
    }

    [Fact]
    public async Task Resolving_after_a_hand_edit_asks_first_and_keeps_the_edits_when_refused()
    {
        var confirmation = new StubConfirmation(0); // "Keep my edits"
        var (merge, writer) = Build(confirmation: confirmation);
        writer.MergedText = "before";
        await merge.MergeAsync();

        merge.Pane.ReportOutputEdit();
        writer.MergedText = "rebuilt";

        merge.TakeLeftCommand.Execute(null);

        Assert.Equal(1, confirmation.Asked);
        Assert.True(merge.HasHandEdits);
        Assert.Equal(1, merge.UnresolvedConflicts); // the decision was not taken
        Assert.Equal("before", merge.Pane.OutputDocument!.Text);
    }

    [Fact]
    public async Task Resolving_after_a_hand_edit_rebuilds_when_agreed()
    {
        var confirmation = new StubConfirmation(1); // "Rebuild and discard them"
        var (merge, writer) = Build(confirmation: confirmation);
        writer.MergedText = "before";
        await merge.MergeAsync();

        merge.Pane.ReportOutputEdit();
        writer.MergedText = "rebuilt";

        merge.TakeLeftCommand.Execute(null);

        Assert.False(merge.HasHandEdits);
        Assert.Equal(0, merge.UnresolvedConflicts);
        Assert.Equal("rebuilt", merge.Pane.OutputDocument!.Text);
    }

    [Fact]
    public async Task A_dismissed_prompt_keeps_the_edits()
    {
        // -1 is "none of these", which every caller in this codebase treats as the safe answer -
        // here, the one that cannot lose the only copy of what was typed.
        var (merge, writer) = Build(confirmation: new StubConfirmation(-1));
        writer.MergedText = "before";
        await merge.MergeAsync();

        merge.Pane.ReportOutputEdit();
        merge.TakeLeftCommand.Execute(null);

        Assert.True(merge.HasHandEdits);
        Assert.Equal(1, merge.UnresolvedConflicts);
    }

    [Fact]
    public async Task With_no_way_to_ask_a_hand_edit_stops_the_resolve_buttons_rather_than_losing_work()
    {
        var (merge, writer) = Build(); // no confirmation service at all
        writer.MergedText = "before";
        await merge.MergeAsync();

        merge.Pane.ReportOutputEdit();
        merge.TakeLeftCommand.Execute(null);

        Assert.True(merge.HasHandEdits);
        Assert.Equal(1, merge.UnresolvedConflicts);
        Assert.Contains("Undo", merge.StatusMessage);
    }

    [Fact]
    public async Task Re_merging_drops_the_hand_edits_with_the_document_they_belonged_to()
    {
        var (merge, writer) = Build();
        await merge.MergeAsync();

        merge.Pane.ReportOutputEdit();
        Assert.True(merge.HasHandEdits);

        await merge.MergeAsync();

        Assert.False(merge.HasHandEdits);
    }
}
