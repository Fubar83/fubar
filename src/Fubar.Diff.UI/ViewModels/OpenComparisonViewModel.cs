using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Settings;
using Fubar.Diff.UI.Services;

namespace Fubar.Diff.UI.ViewModels;

/// <summary>Which half of the dialog something applies to.</summary>
public enum OpenSide
{
    Left,
    Right,
}

/// <summary>What the dialog was asked to open.</summary>
/// <param name="Left">The left path, or the single folder in linked mode.</param>
/// <param name="Right">The right path. Empty in linked mode.</param>
/// <param name="Kind">Files, folders, or one folder paired against itself.</param>
/// <param name="Options">
/// The comparison options as the dialog left them - the saved defaults, plus whatever was overridden
/// for this one comparison. Only meaningful for a file comparison; the folder window carries its own.
/// </param>
public sealed record OpenComparisonRequest(
    string Left,
    string Right,
    ComparisonTargetKind Kind,
    ComparisonOptions Options);

/// <summary>
/// The open dialog: choose two files or two folders, swap them, check the settings that will apply,
/// and go.
///
/// This replaces a bare file picker, and the reason is that a file picker can only answer one question
/// ("which files") when opening a comparison actually involves four: which two things, whether they
/// are files or folders, which way round they go, and under what rules. WinMerge has had this dialog
/// for twenty years and it is the right shape - every one of those is visible and changeable before
/// anything is read from disk, rather than discovered afterwards by comparing the wrong pair with the
/// wrong options and starting again.
///
/// The rule for what a pair of paths MEANS lives in <see cref="ComparisonTargets"/>, not here. The
/// same question decides whether Compare is enabled and what it opens, and two answers that could
/// disagree is how a button ends up enabled for something that then fails.
/// </summary>
public sealed partial class OpenComparisonViewModel : ViewModelBase
{
    private readonly IFilePickerService _picker;

    /// <summary>
    /// Existence checks, injected so the dialog is testable without a disk. Defaults to the real
    /// thing, since every caller outside a test wants that.
    /// </summary>
    private readonly Func<string, bool> _fileExists;

    private readonly Func<string, bool> _folderExists;

    public OpenComparisonViewModel(
        IFilePickerService picker,
        Func<string, bool>? fileExists = null,
        Func<string, bool>? folderExists = null)
    {
        _picker = picker;
        _fileExists = fileExists ?? File.Exists;
        _folderExists = folderExists ?? Directory.Exists;
    }

    // ---- The two sides ---------------------------------------------------------------------------

    [ObservableProperty]
    public partial string LeftPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RightPath { get; set; } = string.Empty;

    partial void OnLeftPathChanged(string value) => Revalidate();

    partial void OnRightPathChanged(string value) => Revalidate();

    private void Revalidate()
    {
        OnPropertyChanged(nameof(Target));
        OnPropertyChanged(nameof(CanCompare));
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(HasProblem));
        OnPropertyChanged(nameof(IsFolderComparison));
        OnPropertyChanged(nameof(LeftCaption));
        OnPropertyChanged(nameof(RightCaption));
        CompareCommand.NotifyCanExecuteChanged();
    }

    /// <summary>What the two sides currently add up to.</summary>
    public ComparisonTarget Target => ComparisonTargets.Resolve(
        ComparisonTargets.Classify(LeftPath, _fileExists, _folderExists),
        ComparisonTargets.Classify(RightPath, _fileExists, _folderExists));

    public bool CanCompare => Target.CanCompare;

    /// <summary>The prompt or the complaint, whichever applies. Empty when the pair is ready.</summary>
    public string Message => Target.Problem ?? string.Empty;

    /// <summary>True when <see cref="Message"/> is a complaint rather than a prompt, so it can be red.</summary>
    public bool HasProblem => Target.Kind == ComparisonTargetKind.Invalid;

    /// <summary>True when Compare would open the folder window.</summary>
    public bool IsFolderComparison => Target.IsFolders;

    /// <summary>
    /// What each side is, under its box - "File", "Folder", "Not found". Says what the dialog
    /// understood, so a typo in a path is visible before Compare is pressed rather than after.
    /// </summary>
    public string LeftCaption => Caption(LeftPath);

    public string RightCaption => Caption(RightPath);

    private string Caption(string path) =>
        ComparisonTargets.Classify(path, _fileExists, _folderExists) switch
        {
            PathKind.File => "File",
            PathKind.Folder => "Folder",
            PathKind.Missing => "Not found",
            _ => string.Empty,
        };

    // ---- Choosing --------------------------------------------------------------------------------

    [RelayCommand]
    private async Task BrowseFile(OpenSide side)
    {
        if (await _picker.PickFileAsync($"Choose the {Name(side)} file").ConfigureAwait(true) is { } path)
        {
            Set(side, path);
        }
    }

    [RelayCommand]
    private async Task BrowseFolder(OpenSide side)
    {
        if (await _picker.PickFolderAsync($"Choose the {Name(side)} folder").ConfigureAwait(true) is { } path)
        {
            Set(side, path);
        }
    }

    [RelayCommand]
    private void Clear(OpenSide side) => Set(side, string.Empty);

    /// <summary>
    /// Swaps the two sides.
    ///
    /// Not a nicety: which file is "left" decides which way round every insertion and deletion reads,
    /// and getting it backwards is a mistake people make constantly - the two paths look alike, and
    /// the answer is inverted rather than wrong, so it is easy to misread rather than notice. One
    /// button beats retyping both.
    /// </summary>
    [RelayCommand]
    private void Swap() => (LeftPath, RightPath) = (RightPath, LeftPath);

    private static string Name(OpenSide side) => side == OpenSide.Left ? "left" : "right";

    private void Set(OpenSide side, string path)
    {
        if (side == OpenSide.Left)
        {
            LeftPath = path;
        }
        else
        {
            RightPath = path;
        }
    }

    // ---- Dropping --------------------------------------------------------------------------------

    /// <summary>
    /// Takes files or folders dropped ON A PARTICULAR SIDE.
    ///
    /// Two dropped at once fill both sides in the order they came, even when dropped on one - dragging
    /// a pair out of a file manager and letting go is the fastest way to open a comparison, and making
    /// the user aim at the correct half first would throw that away. A single one fills the side it
    /// landed on, which is the whole point of having two targets.
    /// </summary>
    public void Drop(OpenSide side, IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var usable = paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();

        if (usable.Count == 0)
        {
            return;
        }

        if (usable.Count >= 2)
        {
            LeftPath = usable[0];
            RightPath = usable[1];

            return;
        }

        Set(side, usable[0]);
    }

    /// <summary>
    /// Takes a drop on the dialog as a whole, with no side aimed at.
    ///
    /// One path fills the first EMPTY side rather than always the left, so dropping a second file
    /// after a first completes the pair instead of replacing it.
    /// </summary>
    public void Drop(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var oneAtATime = paths.Count == 1 && !string.IsNullOrWhiteSpace(LeftPath) && string.IsNullOrWhiteSpace(RightPath);

        Drop(oneAtATime ? OpenSide.Right : OpenSide.Left, paths);
    }

    // ---- Recent ----------------------------------------------------------------------------------

    /// <summary>Pairs compared before, most recent first. Filled by the host from settings.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<RecentComparison> Recent { get; set; } = [];

    public bool HasRecent => Recent.Count > 0;

    partial void OnRecentChanged(IReadOnlyList<RecentComparison> value) => OnPropertyChanged(nameof(HasRecent));

    /// <summary>
    /// Loads a previous pair into the boxes rather than opening it immediately.
    ///
    /// Deliberately not one-click-and-go: the commonest reason to reach for a recent pair in THIS
    /// dialog is to compare something against one of them, or to re-run it with a different setting.
    /// Filling the boxes leaves both of those one step away; opening straight from the list would
    /// close the dialog and undo the reason for coming here.
    /// </summary>
    [RelayCommand]
    private void UseRecent(RecentComparison? entry)
    {
        if (entry is null)
        {
            return;
        }

        LeftPath = entry.Left;
        RightPath = entry.Right;
    }

    // ---- Settings for this comparison ------------------------------------------------------------

    /// <summary>Leading and trailing whitespace stops counting as a difference.</summary>
    [ObservableProperty]
    public partial bool IgnoreWhitespace { get; set; }

    [ObservableProperty]
    public partial bool IgnoreCase { get; set; }

    /// <summary>Comments stop counting, for the languages the scanner knows.</summary>
    [ObservableProperty]
    public partial bool IgnoreComments { get; set; }

    [ObservableProperty]
    public partial bool IgnoreBlankLines { get; set; }

    /// <summary>Text, or structure where the files have one. See <see cref="ComparisonMode"/>.</summary>
    [ObservableProperty]
    public partial ComparisonMode Mode { get; set; } = ComparisonMode.Auto;

    /// <summary>The modes the dropdown offers.</summary>
    public static IReadOnlyList<ComparisonMode> Modes { get; } =
        [ComparisonMode.Auto, ComparisonMode.Text, ComparisonMode.Json, ComparisonMode.Yaml];

    /// <summary>
    /// Seeds the settings from what the app has saved, so the dialog opens showing what WOULD happen
    /// rather than a set of defaults nobody chose.
    ///
    /// That is the difference between "check settings" and "set settings": the boxes are already
    /// right for most comparisons, and the point of showing them is that the one comparison that
    /// needs something different can have it without a trip to the settings window and back.
    /// </summary>
    public void ApplyDefaults(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        IgnoreWhitespace = settings.IgnoreWhitespace;
        IgnoreCase = settings.IgnoreCase;
        IgnoreComments = settings.IgnoreComments;
        IgnoreBlankLines = settings.IgnoreBlankLines;
        Mode = settings.Mode;
        Recent = settings.Recent;
    }

    /// <summary>The options the boxes add up to.</summary>
    public ComparisonOptions CurrentOptions() => new()
    {
        IgnoreWhitespace = IgnoreWhitespace,
        IgnoreCase = IgnoreCase,
        Mode = Mode,
        Code = new CodeComparisonOptions
        {
            IgnoreComments = IgnoreComments,
            IgnoreBlankLines = IgnoreBlankLines,
        },
    };

    // ---- Going -----------------------------------------------------------------------------------

    /// <summary>Raised when Compare is pressed on a pair that can be opened.</summary>
    public event EventHandler<OpenComparisonRequest>? Accepted;

    /// <summary>Raised when the dialog should close without opening anything.</summary>
    public event EventHandler? Cancelled;

    [RelayCommand(CanExecute = nameof(CanCompare))]
    private void Compare()
    {
        var target = Target;

        if (!target.CanCompare)
        {
            return;
        }

        // Linked mode can be asked for from either box; the folder window takes one root, so whichever
        // side was filled becomes it.
        var left = target.Kind == ComparisonTargetKind.LinkedFolder && string.IsNullOrWhiteSpace(LeftPath)
            ? RightPath
            : LeftPath;

        var right = target.Kind == ComparisonTargetKind.LinkedFolder ? string.Empty : RightPath;

        Accepted?.Invoke(this, new OpenComparisonRequest(left.Trim(), right.Trim(), target.Kind, CurrentOptions()));
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);
}
