using System;
using System.Collections.Generic;
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
        if (string.IsNullOrWhiteSpace(LeftPath) || string.IsNullOrWhiteSpace(RightPath))
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

            _comparison = await _service
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

        CompareRequested?.Invoke(this, new FileComparisonRequest(
            System.IO.Path.Combine(_comparison.LeftRoot, Normalize(row.Entry.LeftRelativePath!)),
            System.IO.Path.Combine(_comparison.RightRoot, Normalize(row.Entry.RightRelativePath!))));
    }

    /// <summary>Entries carry '/'-separated relative paths; the filesystem may spell them differently.</summary>
    private static string Normalize(string relativePath) =>
        relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar);

    /// <summary>Seeds from the persisted defaults.</summary>
    public void ApplyDefaults(AppSettings settings)
    {
        ShowIdentical = settings.FolderShowIdentical;

        if (settings.FolderExclude.Count > 0)
        {
            ExcludeList = string.Join(", ", settings.FolderExclude);
        }
    }

    /// <summary>The current values, for the shell to persist.</summary>
    public AppSettings CaptureOptions(AppSettings settings) => settings with
    {
        FolderShowIdentical = ShowIdentical,
        FolderExclude = ParseExclusions(),
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

    private string Describe()
    {
        if (_comparison.AreIdentical)
        {
            return $"The folders match - {_comparison.SameCount} file(s), all identical.";
        }

        var hidden = ShowIdentical || _comparison.SameCount == 0
            ? string.Empty
            : $"   ·   {_comparison.SameCount} identical file(s) hidden";

        return $"{_comparison.DifferentCount} changed   ·   {_comparison.LeftOnlyCount} only on the left"
               + $"   ·   {_comparison.RightOnlyCount} only on the right{hidden}";
    }
}

/// <summary>A pair of absolute file paths to open as a comparison.</summary>
/// <param name="LeftPath">The left file.</param>
/// <param name="RightPath">The right file.</param>
public sealed record FileComparisonRequest(string LeftPath, string RightPath);
