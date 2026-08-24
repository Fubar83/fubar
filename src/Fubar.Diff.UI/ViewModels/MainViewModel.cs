using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Files;
using Fubar.Diff.UI.Services;

namespace Fubar.Diff.UI.ViewModels;

/// <summary>
/// The shell view model: pick two files, compare them, page through the changes.
///
/// It holds no diff logic of its own - alignment belongs to <see cref="IFileComparisonService"/> and
/// the next/previous rules to <see cref="HunkNavigator"/> in Core. What lives here is the UI state
/// those produce: the row collection, the toggles, and the status line.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly IFileComparisonService _comparisonService;
    private readonly IFilePickerService _filePicker;

    private FileComparison _comparison = FileComparison.Empty;

    public MainViewModel(
        IFileComparisonService comparisonService,
        IFilePickerService filePicker,
        ThemeManagerViewModel themeManager,
        StartupFiles startupFiles)
    {
        _comparisonService = comparisonService;
        _filePicker = filePicker;
        ThemeManager = themeManager;

        LeftPath = startupFiles.Left ?? string.Empty;
        RightPath = startupFiles.Right ?? string.Empty;
        _compareOnLoad = startupFiles.HasBoth;
    }

    /// <summary>
    /// Whether files came from the command line. The comparison itself is kicked off by
    /// <see cref="InitializeAsync"/> once the window exists - doing file I/O in a constructor would
    /// block the UI thread before the first frame and give errors nowhere to be shown.
    /// </summary>
    private readonly bool _compareOnLoad;

    /// <summary>Runs the startup comparison, if two files were named on the command line.</summary>
    public async Task InitializeAsync()
    {
        if (_compareOnLoad)
        {
            await CompareAsync().ConfigureAwait(true);
        }
    }

    public ThemeManagerViewModel ThemeManager { get; }

    /// <summary>The aligned rows currently on screen.</summary>
    public ObservableCollection<DiffRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    public partial string LeftPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RightPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Choose two files to compare.";

    /// <summary>True while a comparison is running, so the view can disable the toolbar.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Set when the last attempt failed; the view shows it as an error banner.</summary>
    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    // Each option re-runs the comparison from the already-loaded documents rather than re-reading
    // from disk - see IFileComparisonService.Recompare.
    [ObservableProperty]
    public partial bool IgnoreWhitespace { get; set; }

    [ObservableProperty]
    public partial bool IgnoreCase { get; set; }

    [ObservableProperty]
    public partial bool NormalizeStructure { get; set; }

    /// <summary>Index into the current hunk list, or -1 when nothing is selected yet.</summary>
    [ObservableProperty]
    public partial int CurrentHunk { get; set; } = -1;

    /// <summary>
    /// Row to bring into view. The view watches this and scrolls; -1 means "no request pending".
    /// </summary>
    [ObservableProperty]
    public partial int ScrollToRow { get; set; } = -1;

    public bool HasChanges => _comparison.Result.Hunks.Count > 0;

    partial void OnIgnoreWhitespaceChanged(bool value) => Recompare();
    partial void OnIgnoreCaseChanged(bool value) => Recompare();
    partial void OnNormalizeStructureChanged(bool value) => Recompare();

    [RelayCommand]
    private async Task BrowseLeftAsync()
    {
        if (await _filePicker.PickFileAsync("Choose the left-hand file").ConfigureAwait(true) is { } path)
        {
            LeftPath = path;
            await CompareAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task BrowseRightAsync()
    {
        if (await _filePicker.PickFileAsync("Choose the right-hand file").ConfigureAwait(true) is { } path)
        {
            RightPath = path;
            await CompareAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Runs the comparison. A no-op until both sides have been chosen.</summary>
    [RelayCommand]
    public async Task CompareAsync()
    {
        if (string.IsNullOrWhiteSpace(LeftPath) || string.IsNullOrWhiteSpace(RightPath))
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            _comparison = await _comparisonService
                .CompareFilesAsync(LeftPath, RightPath, CurrentOptions())
                .ConfigureAwait(true);

            Refresh();
        }
        catch (TextFileReadException ex)
        {
            // The domain phrases these for a user already, so show the reason as-is.
            Reset();
            ErrorMessage = ex.Message;
            StatusMessage = "Comparison failed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void NextChange() => MoveTo(HunkNavigator.Next(_comparison.Result.Hunks, CurrentHunk));

    [RelayCommand]
    private void PreviousChange() => MoveTo(HunkNavigator.Previous(_comparison.Result.Hunks, CurrentHunk));

    private ComparisonOptions CurrentOptions() => new()
    {
        IgnoreWhitespace = IgnoreWhitespace,
        IgnoreCase = IgnoreCase,
        NormalizeStructure = NormalizeStructure,
    };

    /// <summary>
    /// Re-runs against the loaded documents when an option changes. Silent when nothing is loaded -
    /// toggling a checkbox before choosing files is not an error.
    /// </summary>
    private void Recompare()
    {
        if (!_comparison.HasBothSides)
        {
            return;
        }

        _comparison = _comparisonService.Recompare(_comparison, CurrentOptions());
        Refresh();
    }

    private void Refresh()
    {
        Rows.Clear();
        foreach (var line in _comparison.Result.Lines)
        {
            Rows.Add(new DiffRowViewModel(line));
        }

        CurrentHunk = -1;
        ScrollToRow = -1;
        OnPropertyChanged(nameof(HasChanges));

        var result = _comparison.Result;
        StatusMessage = result.AreIdentical
            ? "The files are identical."
            : $"{result.Hunks.Count} change(s) — +{result.Inserted} −{result.Deleted} ~{result.Modified}";
    }

    private void Reset()
    {
        _comparison = FileComparison.Empty;
        Rows.Clear();
        CurrentHunk = -1;
        ScrollToRow = -1;
        OnPropertyChanged(nameof(HasChanges));
    }

    private void MoveTo(int? hunkIndex)
    {
        if (hunkIndex is not { } index)
        {
            return;
        }

        CurrentHunk = index;
        ScrollToRow = _comparison.Result.Hunks[index].StartIndex;
        StatusMessage = $"Change {index + 1} of {_comparison.Result.Hunks.Count}";
    }
}
