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

    private FolderComparison _comparison = FolderComparison.Empty;

    /// <summary>Cancels a walk in progress - the one operation here long enough to want abandoning.</summary>
    private CancellationTokenSource? _cancellation;

    public FolderViewModel(IFolderComparisonService service, IFilePickerService filePicker, ThemeManagerViewModel themeManager)
    {
        _service = service;
        _filePicker = filePicker;
        ThemeManager = themeManager;
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
