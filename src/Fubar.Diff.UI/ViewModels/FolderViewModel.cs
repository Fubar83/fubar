using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Diff.Application.Folders;
using Fubar.Diff.Core.Folders;
using Fubar.Diff.Core.Settings;
using Fubar.Diff.UI.Services;

namespace Fubar.Diff.UI.ViewModels;

/// <summary>
/// One folder comparison: pick two directories, walk them, and open the files that differ.
///
/// Its own window and its own view model, like the merge, and for the same reason: it asks a different
/// question from a file comparison and answers it with a tree rather than two panes. Where it MEETS
/// the rest of the app is one event - <see cref="CompareRequested"/> - which the shell turns into an
/// ordinary comparison tab. A folder comparison that could not open a file would be a listing, not a
/// diff tool.
/// </summary>
public partial class FolderViewModel : ViewModelBase
{
    private readonly IFolderComparisonService _service;
    private readonly IFilePickerService _filePicker;
    private readonly IFileCopier? _copier;
    private readonly IConfirmationService? _confirmation;

    private FolderComparison _comparison = FolderComparison.Empty;

    /// <summary>Cancels a walk in progress - the one operation here long enough to want abandoning.</summary>
    private CancellationTokenSource? _cancellation;

    public FolderViewModel(
        IFolderComparisonService service,
        IFilePickerService filePicker,
        ThemeManagerViewModel themeManager,
        IFileCopier? copier = null,
        IConfirmationService? confirmation = null)
    {
        _service = service;
        _filePicker = filePicker;
        ThemeManager = themeManager;

        // Both optional, and copying is offered only when BOTH are present. A host that wired up the
        // copier but no way to ask the user would otherwise get a folder window that overwrites files
        // without confirming, which is the one outcome this feature must not have.
        _copier = copier;
        _confirmation = confirmation;
    }

    public ThemeManagerViewModel ThemeManager { get; }

    /// <summary>
    /// Raised when the user opens a file pair. Carries absolute paths, because that is what a
    /// comparison tab takes and this view model is the only thing that knows the roots.
    /// </summary>
    public event EventHandler<FileComparisonRequest>? CompareRequested;

    /// <summary>
    /// Raised when a remembered option changes, so the shell can persist it. The same division the
    /// comparison tabs use: this view model does not own the settings file.
    /// </summary>
    public event EventHandler? OptionsChanged;

    // ---- Folder selection -----------------------------------------------------------------------

    [ObservableProperty]
    public partial string LeftPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RightPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Choose two folders to compare.";

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>The file currently being compared, so a long walk shows it is doing something.</summary>
    [ObservableProperty]
    public partial string ProgressText { get; set; } = string.Empty;

    // ---- Results --------------------------------------------------------------------------------

    /// <summary>The visible tree, already filtered.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<FolderEntryViewModel> Entries { get; set; } = [];

    /// <summary>The row the user has selected, if any.</summary>
    [ObservableProperty]
    public partial FolderEntryViewModel? SelectedEntry { get; set; }

    /// <summary>Everything currently selected, in the order it was selected.</summary>
    private IReadOnlyList<FolderEntryViewModel> _selection = [];

    /// <summary>
    /// Pushed up by the view, because multiple selection is something the control owns and the view
    /// model only needs the answer to.
    /// </summary>
    public void SetSelection(IReadOnlyList<FolderEntryViewModel> rows)
    {
        _selection = rows;
        SelectedEntry = rows.Count > 0 ? rows[0] : null;

        OnPropertyChanged(nameof(CanComparePair));
        OnPropertyChanged(nameof(PairDescription));
        RaiseCopyState();
    }

    /// <summary>
    /// The copy buttons describe what they would do to the CURRENT selection, so they all change
    /// together whenever it does - including after a copy, when the rows involved have just become
    /// identical and there is nothing left to copy.
    /// </summary>
    private void RaiseCopyState()
    {
        OnPropertyChanged(nameof(CanCopy));
        OnPropertyChanged(nameof(CanCopyToRight));
        OnPropertyChanged(nameof(CanCopyToLeft));
        OnPropertyChanged(nameof(CopyToRightDescription));
        OnPropertyChanged(nameof(CopyToLeftDescription));
    }

    /// <summary>The pair the current selection resolves to, or null when it does not resolve to one.</summary>
    private (FolderEntryViewModel Left, FolderEntryViewModel Right)? SelectedPair => ResolvePair(_selection);

    /// <summary>Whether two selected rows can be compared against each other.</summary>
    public bool CanComparePair => SelectedPair is not null;

    /// <summary>Names the pair, so the button says what it will actually open.</summary>
    public string PairDescription => SelectedPair is { } pair
        ? $"Compare {pair.Left.Name} ↔ {pair.Right.Name}"
        : "Compare selected";

    /// <summary>
    /// Works out which of two selected rows supplies which side.
    ///
    /// This exists for renames, which a folder comparison cannot detect and should not guess at: a
    /// file renamed between two trees appears as one left-only row and one right-only row, neither of
    /// which can be opened on its own, and pairing them is a judgement only the user can make. Rather
    /// than inventing a similarity heuristic that is wrong on the cases that matter, the tool lets them
    /// say so.
    ///
    /// The rule reads selection ORDER first and falls back to whichever assignment is possible: two
    /// files that each exist on one side only have exactly one sensible pairing regardless of the order
    /// they were clicked in, while two files that exist on both sides are genuinely ambiguous and the
    /// first one selected becomes the left.
    /// </summary>
    private static (FolderEntryViewModel Left, FolderEntryViewModel Right)? ResolvePair(
        IReadOnlyList<FolderEntryViewModel> rows)
    {
        if (rows.Count != 2 || rows[0].IsDirectory || rows[1].IsDirectory)
        {
            return null;
        }

        var (first, second) = (rows[0], rows[1]);

        if (first.HasLeft && second.HasRight)
        {
            return (first, second);
        }

        // Selected the other way round: the right-hand file first.
        if (first.HasRight && second.HasLeft)
        {
            return (second, first);
        }

        // Both on the same side only - there is no pairing to make.
        return null;
    }

    /// <summary>
    /// Show files that are identical on both sides.
    ///
    /// OFF by default, which is the whole reason this is usable on a real pair of checkouts: the answer
    /// to "what differs" should not arrive buried in ten thousand files that do not. The count of
    /// hidden files is still reported, so nothing is silently missing.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowIdentical { get; set; }

    partial void OnShowIdenticalChanged(bool value)
    {
        RebuildTree();
        OptionsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Compare file contents rather than only their sizes.</summary>
    [ObservableProperty]
    public partial bool CompareContents { get; set; } = true;

    /// <summary>Descend into subdirectories.</summary>
    [ObservableProperty]
    public partial bool Recursive { get; set; } = true;

    /// <summary>
    /// Pair files against each other inside ONE folder, by name, instead of comparing two folders.
    ///
    /// For snapshot testing: a run leaves <c>Thing.received.json</c> beside <c>Thing.verified.json</c>
    /// in the same directory, and reviewing it means diffing the two halves of every such pair. There
    /// is no second folder involved at all, which is why this is a mode rather than an option.
    /// </summary>
    [ObservableProperty]
    public partial bool LinkedMode { get; set; }

    partial void OnLinkedModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsTwoFolderMode));
        OnPropertyChanged(nameof(LeftFolderHeader));
        OnPropertyChanged(nameof(CanCompare));
        OptionsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The right-hand picker only means something when there are two folders.</summary>
    public bool IsTwoFolderMode => !LinkedMode;

    /// <summary>The left picker's label, since in linked mode it is not the "left" of anything.</summary>
    public string LeftFolderHeader => LinkedMode ? "Folder" : "Left folder";

    /// <summary>
    /// The link rules, one per line, as <c>left = right</c>. Editable because conventions vary - a
    /// codebase using something other than Verify or ApprovalTests should not need a new build.
    /// </summary>
    [ObservableProperty]
    public partial string LinkRuleText { get; set; } =
        string.Join(Environment.NewLine, LinkRule.Defaults);

    partial void OnLinkRuleTextChanged(string value) => OptionsChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Whether a comparison can run at all - one folder in linked mode, two otherwise.</summary>
    public bool CanCompare => !string.IsNullOrWhiteSpace(LeftPath)
                              && (LinkedMode || !string.IsNullOrWhiteSpace(RightPath));

    partial void OnLeftPathChanged(string value) => OnPropertyChanged(nameof(CanCompare));

    partial void OnRightPathChanged(string value) => OnPropertyChanged(nameof(CanCompare));

    /// <summary>Names never compared or descended into, as a comma-separated list for editing.</summary>
    [ObservableProperty]
    public partial string ExcludeList { get; set; } =
        string.Join(", ", FolderComparisonOptions.Default.Exclude);

    /// <summary>True once a comparison has produced something.</summary>
    public bool HasResults => _comparison.Entries.Count > 0 || _comparison.SameCount > 0;

    /// <summary>Whether anything at all differs, so the view can say "identical" rather than show nothing.</summary>
    public bool AreIdentical => HasResults && _comparison.AreIdentical;

    // ---- Commands -------------------------------------------------------------------------------

    [RelayCommand]
    private Task BrowseLeftAsync() => PickInto(path => LeftPath = path, "Choose the left-hand folder");

    [RelayCommand]
    private Task BrowseRightAsync() => PickInto(path => RightPath = path, "Choose the right-hand folder");

    private async Task PickInto(Action<string> assign, string title)
    {
        if (await _filePicker.PickFolderAsync(title).ConfigureAwait(true) is { } path)
        {
            assign(path);
            await CompareAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Walks both trees. A no-op until both folders have been chosen.</summary>
    [RelayCommand]
    public async Task CompareAsync()
    {
        if (!CanCompare)
        {
            return;
        }

        // Supersede any walk already running rather than queueing behind it - the user has just asked
        // a different question, and the old answer is no longer wanted.
        var previous = _cancellation;
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;

        previous?.Cancel();
        previous?.Dispose();

        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = "Comparing…";

        try
        {
            var progress = new Progress<string>(path => ProgressText = path);

            _comparison = LinkedMode
                ? await _service
                    .CompareLinkedAsync(LeftPath, CurrentOptions(), FileLinker.Parse(LinkRuleText), progress, cancellation.Token)
                    .ConfigureAwait(true)
                : await _service
                    .CompareAsync(LeftPath, RightPath, CurrentOptions(), progress, cancellation.Token)
                    .ConfigureAwait(true);

            RebuildTree();
            StatusMessage = Describe();
        }
        catch (OperationCanceledException)
        {
            // Superseded or cancelled - the newer walk owns the status line now.
        }
        finally
        {
            if (ReferenceEquals(_cancellation, cancellation))
            {
                IsBusy = false;
                ProgressText = string.Empty;
            }
        }
    }

    /// <summary>Abandons a walk in progress.</summary>
    [RelayCommand]
    private void Cancel()
    {
        _cancellation?.Cancel();
        IsBusy = false;
        ProgressText = string.Empty;
        StatusMessage = "Comparison cancelled.";
    }

    /// <summary>
    /// Opens the selected pair as an ordinary file comparison. Only meaningful for a file both trees
    /// have - there is nothing to diff a file against its own absence.
    /// </summary>
    [RelayCommand]
    public void Open()
    {
        if (SelectedEntry is not { CanCompare: true } row)
        {
            return;
        }

        Request(row, row);
    }

    /// <summary>
    /// Opens two DIFFERENT files against each other - the answer to a rename, where the old name is
    /// left-only and the new one is right-only and neither can be opened by itself.
    /// </summary>
    [RelayCommand]
    public void ComparePair()
    {
        if (SelectedPair is { } pair)
        {
            Request(pair.Left, pair.Right);
        }
    }

    // ---- Copying --------------------------------------------------------------------------------

    /// <summary>
    /// Whether copying is available at all - both the copier and a way to ask the user must be wired
    /// up, and there has to be something selected that a copy would mean anything for.
    /// </summary>
    public bool CanCopy => _copier is not null && _confirmation is not null && SelectedEntry is not null;

    /// <summary>How many files a left-to-right copy of the selection would write, and what it would replace.</summary>
    public string CopyToRightDescription => Describe(Plan(CopyDirection.ToRight), "right");

    /// <summary>The same for the other direction.</summary>
    public string CopyToLeftDescription => Describe(Plan(CopyDirection.ToLeft), "left");

    /// <summary>Whether a left-to-right copy would do anything.</summary>
    public bool CanCopyToRight => CanCopy && Plan(CopyDirection.ToRight).Count > 0;

    /// <summary>Whether a right-to-left copy would do anything.</summary>
    public bool CanCopyToLeft => CanCopy && Plan(CopyDirection.ToLeft).Count > 0;

    /// <summary>
    /// The copies the current selection implies.
    ///
    /// A directory row means everything under it, which is why this returns a list rather than one
    /// copy - and why the confirmation counts them: "copy 34 files, replacing 12" is a very different
    /// proposition from the single file the user thought they had selected.
    /// </summary>
    private IReadOnlyList<FileCopy> Plan(CopyDirection direction) =>
        SelectedEntry is { } row
            ? FileCopyPlanner.PlanAll(row.Entry, _comparison.LeftRoot, _comparison.RightRoot, direction)
            : [];

    private static string Describe(IReadOnlyList<FileCopy> copies, string side)
    {
        if (copies.Count == 0)
        {
            return $"Nothing to copy to the {side}";
        }

        var replacing = copies.Count(c => c.Overwrites);

        var what = copies.Count == 1 ? "1 file" : $"{copies.Count} files";
        var overwriting = replacing == 0
            ? string.Empty
            : replacing == copies.Count
                ? ", replacing all of them"
                : $", replacing {replacing}";

        return $"Copy {what} to the {side}{overwriting}";
    }

    [RelayCommand]
    private Task CopyToRightAsync() => CopyAsync(CopyDirection.ToRight, "right");

    [RelayCommand]
    private Task CopyToLeftAsync() => CopyAsync(CopyDirection.ToLeft, "left");

    /// <summary>
    /// Copies the selection, after confirming.
    ///
    /// The confirmation is not a formality and is not skippable. This is the only thing in the app
    /// that writes a file the user did not explicitly name, and it can replace one they have not
    /// looked at - so it says how many, how many it replaces, and, for a single file, exactly which
    /// path is about to be overwritten.
    /// </summary>
    private async Task CopyAsync(CopyDirection direction, string side)
    {
        if (_copier is null || _confirmation is null)
        {
            return;
        }

        var copies = Plan(direction);
        if (copies.Count == 0)
        {
            return;
        }

        var replacing = copies.Count(c => c.Overwrites);

        var detail = copies.Count == 1
            ? $"{copies[0].SourcePath}\n\nwill be written to\n\n{copies[0].DestinationPath}"
            : $"{copies.Count} files will be written under {(direction == CopyDirection.ToRight ? _comparison.RightRoot : _comparison.LeftRoot)}.";

        var warning = replacing == 0
            ? "\n\nNothing existing will be replaced."
            : $"\n\n{(replacing == 1 ? "1 existing file" : $"{replacing} existing files")} will be replaced. This cannot be undone.";

        var confirmed = await _confirmation
            .ConfirmAsync(
                replacing > 0 ? $"Replace files on the {side}?" : $"Copy to the {side}?",
                detail + warning,
                replacing > 0 ? "Replace" : "Copy")
            .ConfigureAwait(true);

        if (!confirmed)
        {
            StatusMessage = "Copy cancelled.";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        var written = 0;
        string? failure = null;

        try
        {
            foreach (var copy in copies)
            {
                await _copier.CopyAsync(copy.SourcePath, copy.DestinationPath).ConfigureAwait(true);
                written++;
            }
        }
        catch (FileCopyException ex)
        {
            // Stop at the first failure rather than pressing on: the rest of the batch probably shares
            // the same permission or the same disk, and a partial copy the user was not told about is
            // worse than one that stopped and named the file.
            failure = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }

        // Re-walk BEFORE reporting, so the tree reflects what is now on disk - without it the rows the
        // user has just made identical would still show as different, which reads as the copy having
        // failed. Reporting after it, rather than before, because a comparison clears the error and
        // status for its own run: say it first and the one thing the user needs to read is wiped by
        // the refresh that follows.
        await CompareAsync().ConfigureAwait(true);

        ErrorMessage = failure;

        StatusMessage = failure is null
            ? written == 1 ? "Copied 1 file." : $"Copied {written} files."
            : written == 0
                ? "Nothing was copied."
                : $"Copied {written} file(s), then stopped.";
    }

    /// <summary>Raises the request, taking each side's path from the row that supplies that side.</summary>
    private void Request(FolderEntryViewModel left, FolderEntryViewModel right)
    {
        if (left.Entry.LeftRelativePath is not { } leftPath || right.Entry.RightRelativePath is not { } rightPath)
        {
            return;
        }

        CompareRequested?.Invoke(this, new FileComparisonRequest(
            System.IO.Path.Combine(_comparison.LeftRoot, Normalize(leftPath)),
            System.IO.Path.Combine(_comparison.RightRoot, Normalize(rightPath))));
    }

    /// <summary>Entries carry '/'-separated relative paths; the filesystem may spell them differently.</summary>
    private static string Normalize(string relativePath) =>
        relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar);

    /// <summary>Seeds from the persisted defaults.</summary>
    public void ApplyDefaults(AppSettings settings)
    {
        ShowIdentical = settings.FolderShowIdentical;
        LinkedMode = settings.FolderLinkedMode;

        if (settings.FolderExclude.Count > 0)
        {
            ExcludeList = string.Join(", ", settings.FolderExclude);
        }

        // Empty means "never customised", which keeps the built-in conventions rather than leaving a
        // settings file written before this existed with no rules at all.
        if (settings.FolderLinkRules.Count > 0)
        {
            LinkRuleText = string.Join(Environment.NewLine, settings.FolderLinkRules);
        }
    }

    /// <summary>The current values, for the shell to persist.</summary>
    public AppSettings CaptureOptions(AppSettings settings) => settings with
    {
        FolderShowIdentical = ShowIdentical,
        FolderLinkedMode = LinkedMode,
        FolderExclude = ParseExclusions(),
        FolderLinkRules = [.. FileLinker.Parse(LinkRuleText).Select(rule => rule.ToString())],
    };

    // ---- Internals ------------------------------------------------------------------------------

    private FolderComparisonOptions CurrentOptions() => new()
    {
        Recursive = Recursive,
        CompareContents = CompareContents,
        Exclude = ParseExclusions(),
    };

    /// <summary>
    /// Splits the exclusion box. Commas or whitespace, because both are what people type, and an empty
    /// entry is dropped rather than becoming a pattern that matches nothing and confuses the reader.
    /// </summary>
    private IReadOnlyList<string> ParseExclusions() =>
        ExcludeList.Split([',', ';', ' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private void RebuildTree()
    {
        Entries = FolderEntryViewModel.Build(_comparison.Entries, ShowIdentical);

        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(AreIdentical));

        // The tree is new, so the previous selection points at rows that no longer exist - including
        // the rows a copy has just made identical, which must stop offering to be copied again.
        SetSelection([]);
    }

    /// <summary>
    /// The status line, phrased for the mode.
    ///
    /// "Only on the left" is meaningless in linked mode - there is one folder. What those counts mean
    /// there is worth saying plainly instead: a baseline with no output beside it is a snapshot nothing
    /// produces any more, and output with no baseline is a new one waiting to be accepted. Those are
    /// the two things a reviewer is looking for, and neither is "a file that is only on one side".
    /// </summary>
    private string Describe()
    {
        if (_comparison.AreIdentical)
        {
            return LinkedMode
                ? $"Nothing to review - {_comparison.SameCount} pair(s), all matching."
                : $"The folders match - {_comparison.SameCount} file(s), all identical.";
        }

        var hidden = ShowIdentical || _comparison.SameCount == 0
            ? string.Empty
            : $"   ·   {_comparison.SameCount} {(LinkedMode ? "matching pair(s)" : "identical file(s)")} hidden";

        return LinkedMode
            ? $"{_comparison.DifferentCount} changed   ·   {_comparison.RightOnlyCount} new"
              + $"   ·   {_comparison.LeftOnlyCount} with no new output{hidden}"
            : $"{_comparison.DifferentCount} changed   ·   {_comparison.LeftOnlyCount} only on the left"
              + $"   ·   {_comparison.RightOnlyCount} only on the right{hidden}";
    }
}

/// <summary>A pair of absolute file paths to open as a comparison.</summary>
/// <param name="LeftPath">The left file.</param>
/// <param name="RightPath">The right file.</param>
public sealed record FileComparisonRequest(string LeftPath, string RightPath);
