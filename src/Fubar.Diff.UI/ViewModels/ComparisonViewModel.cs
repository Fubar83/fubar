using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Application.Merge;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Editing;
using Fubar.Diff.Core.Files;
using Fubar.Diff.Core.Json;
using Fubar.Diff.Core.Languages;
using Fubar.Diff.Core.Merge;
using Fubar.Diff.Core.Models;
using Fubar.Diff.Core.Patch;
using Fubar.Diff.Core.Rendering;
using Fubar.Diff.Controls.ViewModels;
using Fubar.Diff.Core.Settings;
using Fubar.Diff.UI.Services;

namespace Fubar.Diff.UI.ViewModels;

/// <summary>
/// One comparison: pick two files, compare them, page through the changes, resolve hunks, save.
///
/// This is the per-TAB view model - the window may hold several, each with its own files, options,
/// merge decisions and scroll position. Shared concerns (the theme, the settings file) are owned by
/// <see cref="ShellViewModel"/> and passed in.
///
/// It holds no diff or merge logic of its own - alignment belongs to <see cref="IFileComparisonService"/>,
/// the next/previous rules to <see cref="HunkNavigator"/>, and what gets written to
/// <see cref="MergedDocument"/>, all in Core. What lives here is the UI state those produce.
/// </summary>
public partial class ComparisonViewModel : ViewModelBase, IDisposable
{
    private readonly IFileComparisonService _comparisonService;
    private readonly IMergeService _mergeService;
    private readonly IFilePickerService _filePicker;
    private readonly IFileChangeWatcher _watcher;
    private readonly IClipboardService _clipboard;
    private readonly ITextFileWriter _patchWriter;

    private FileComparison _comparison = FileComparison.Empty;
    private MergeState _mergeState = MergeState.Empty;

    /// <summary>
    /// When this tab last wrote to one of its own files, so the watcher can tell our save from
    /// somebody else's edit.
    ///
    /// A timestamp rather than a flag held across the save, because the watcher announces changes only
    /// after a quiet period: by the time it speaks, a flag cleared in a `finally` is long gone and our
    /// own write arrives looking exactly like an external one. Saving already re-reads the file
    /// deliberately, so acting on it again would be a wasted comparison at best and a "files changed
    /// on disk" notice about the user's own save at worst.
    /// </summary>
    private DateTime _lastSelfWrite = DateTime.MinValue;

    /// <summary>
    /// How long after our own write a change is assumed to be ours. Comfortably longer than the
    /// watcher's quiet period, and far shorter than anyone can save twice.
    /// </summary>
    private static readonly TimeSpan SelfWriteWindow = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Suppresses persistence while defaults are being applied - otherwise each property assignment
    /// would fire its own change handler and write the settings file back.
    /// </summary>
    private bool _loadingSettings;

    private readonly IConfirmationService? _confirmation;
    private readonly IProjectConfigStore? _projectConfig;

    /// <summary>
    /// The rules the repository being compared supplies - see <see cref="ProjectConfig"/>. Resolved
    /// per FILE, because a rule can name the files it applies to and the two sides need not be alike.
    /// </summary>
    private ProjectRule _leftRules = new();
    private ProjectRule _rightRules = new();

    public ComparisonViewModel(
        IFileComparisonService comparisonService,
        IMergeService mergeService,
        IFilePickerService filePicker,
        IFileChangeWatcher watcher,
        IClipboardService clipboard,
        ITextFileWriter patchWriter,
        ThemeManagerViewModel themeManager,
        IConfirmationService? confirmation = null,
        IProjectConfigStore? projectConfig = null)
    {
        _projectConfig = projectConfig;
        _comparisonService = comparisonService;
        _mergeService = mergeService;
        _filePicker = filePicker;
        _watcher = watcher;
        _clipboard = clipboard;
        _patchWriter = patchWriter;
        ThemeManager = themeManager;

        // Optional so the many tests that never prompt are not made to supply one. Without it the
        // conflicts below fall back to the SAFE answer - keep the user's changes, refuse to close -
        // rather than to silence, because a prompt that cannot be shown must never be read as a yes.
        _confirmation = confirmation;

        Pane.Navigated += OnPaneNavigated;
        Pane.SideEdited += OnSideEdited;

        // Whether the Pretty buttons are offered is decided per comparison, in Refresh: this host has
        // a JSON formatter behind them and no YAML one.
        Pane.FormattingChanged += (_, _) => Refresh();

        // The pane asks; this owns the comparison options, so it answers and re-compares.
        Pane.ArrayKeyChosen += (_, option) => _ = ApplyArrayKeyAsync(option.Path, option.Key);
        Pane.CustomArrayKeyRequested += (_, path) => _ = AskForArrayKeyAsync(path);

        // Editing is offered only in the side-by-side view, so the toggle has to appear and disappear
        // with it rather than only when a comparison is re-run.
        Pane.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DiffPaneViewModel.IsSideBySideViewVisible))
            {
                OnPropertyChanged(nameof(CanEdit));
            }

            // Stepping through a JSON comparison reports where it has got to in the status bar, the
            // same way stepping through a text one does (OnPaneNavigated). The Json view used to say
            // this in a strip of its own; that strip is gone in this app, and the status bar is where
            // the answer to "which difference am I on" already lives.
            //
            // Only once something IS selected: the caption's other form ("5 difference(s) - none
            // selected") is raised whenever a comparison loads, and would overwrite the summary that
            // has just been written there.
            if (e.PropertyName == nameof(DiffPaneViewModel.JsonCaption)
                && Pane.IsJsonViewVisible
                && Pane.CurrentSemanticChange is not null)
            {
                StatusMessage = Pane.JsonCaption;
            }
        };

        _watcher.Changed += OnFilesChangedOnDisk;
    }

    // ---- Editing ---------------------------------------------------------------------------------

    /// <summary>
    /// How long to wait after the last keystroke before re-diffing.
    ///
    /// Long enough that typing a word costs one comparison rather than five, short enough that the
    /// diff feels live. The work behind it is affordable at this rate - a 60,000-line pair takes about
    /// 90 ms to align and 60 ms to read back, and both happen off the UI thread.
    /// </summary>
    private static readonly TimeSpan EditSettleTime = TimeSpan.FromMilliseconds(300);

    private Avalonia.Threading.DispatcherTimer? _editTimer;

    private DiffSide _editedSide = DiffSide.Right;

    /// <summary>
    /// Whether the panes accept typing. Off by default: a diff tool is a reading tool most of the
    /// time, and a caret blinking in someone's source file is an invitation to change it by accident.
    /// </summary>
    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    partial void OnIsEditingChanged(bool value)
    {
        // Never for a byte comparison. The panes are showing a hex dump, which is a view OF the file
        // rather than the file, and there is nothing sensible to write back from it.
        Pane.IsEditable = value && !IsBinaryComparison;

        DisplayOptionChanged();
    }

    // ---- Manual alignment ------------------------------------------------------------------------

    /// <summary>
    /// Pairings the user made by hand - see <see cref="AlignmentAnchor"/>.
    ///
    /// Per comparison and never persisted: "line 40 here is line 62 there" is a statement about two
    /// particular files, and carrying it to the next pair would be carrying nonsense.
    /// </summary>
    public IReadOnlyList<AlignmentAnchor> Alignments { get; private set; } = [];

    /// <summary>How many pairings are in force, for the status bar.</summary>
    public int AlignmentCount => Alignments.Count;

    public bool HasAlignments => Alignments.Count > 0;

    /// <summary>
    /// Pairs the line the left caret is on with the line the right caret is on, and re-compares.
    ///
    /// The one thing no option can express. Every "ignore whitespace, ignore case, ignore comments"
    /// switch changes what COUNTS as a difference; none of them changes which lines correspond, and
    /// when an aligner pairs the wrong two - a rewritten block, a reordered config, a file of
    /// boilerplate that matches everywhere - there is nothing else the user can do about it.
    ///
    /// Both carets, rather than a selection: a pairing is two positions, and the two panes already
    /// have one caret each. Refused when either caret is on a filler row, because there is no line
    /// there to pair with.
    /// </summary>
    [RelayCommand]
    private async Task AlignCaretsAsync()
    {
        if (Pane.CaretLineReader is not { } caret)
        {
            return;
        }

        if (caret(DiffSide.Left) is not { } left || caret(DiffSide.Right) is not { } right)
        {
            StatusMessage = "Put the caret on a real line in each pane to align them.";

            return;
        }

        Alignments = AlignmentAnchors.Add(Alignments, new AlignmentAnchor(left, right));
        RaiseAlignmentState();

        StatusMessage = $"Aligned left line {left} with right line {right}.";

        await RecompareAsync().ConfigureAwait(true);
    }

    /// <summary>Drops every hand-made pairing and re-compares, back to what the aligner thinks.</summary>
    [RelayCommand]
    private async Task ClearAlignmentsAsync()
    {
        if (Alignments.Count == 0)
        {
            return;
        }

        Alignments = [];
        RaiseAlignmentState();

        StatusMessage = "Manual alignment cleared.";

        await RecompareAsync().ConfigureAwait(true);
    }

    private void RaiseAlignmentState()
    {
        OnPropertyChanged(nameof(Alignments));
        OnPropertyChanged(nameof(AlignmentCount));
        OnPropertyChanged(nameof(HasAlignments));
    }

    // ---- Array matching --------------------------------------------------------------------------

    /// <summary>
    /// Arrays the user has asked to compare by position, by path - the per-array counterpart of the
    /// global <see cref="MatchArraysByPosition"/> switch.
    /// </summary>
    public ObservableCollection<string> PositionalArrays { get; } = [];

    /// <summary>
    /// Records how one array should be matched and re-compares.
    ///
    /// A null key means "by position". The two lists are kept mutually exclusive rather than layered,
    /// because an array cannot both be keyed and positional, and leaving a stale entry in the other
    /// list would make the menu's check marks lie about what is happening.
    /// </summary>
    public async Task ApplyArrayKeyAsync(string path, string? key)
    {
        var existing = ArrayKeyOverrides.FirstOrDefault(o => string.Equals(o.Path, path, StringComparison.Ordinal));
        if (existing is not null)
        {
            ArrayKeyOverrides.Remove(existing);
        }

        while (PositionalArrays.Remove(path))
        {
            // Remove every copy, not just the first - a repeated entry would survive as a rule the
            // user cannot see and cannot undo from the menu.
        }

        if (key is null)
        {
            PositionalArrays.Add(path);
        }
        else
        {
            ArrayKeyOverrides.Add(new ArrayKeyOverrideEntry(path, key));
        }

        StatusMessage = key is null
            ? $"{path}: comparing by position."
            : $"{path}: matching elements by {key}.";

        if (_loadingSettings)
        {
            return;
        }

        OptionsChanged?.Invoke(this, System.EventArgs.Empty);

        // Awaited rather than fired and forgotten, unlike an option checkbox: the caller here is a
        // menu click that the user is watching for a result, and a test has something to assert once
        // it lands.
        await RecompareAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Asks for a field the menu did not offer - one only some elements carry today, or nested deeper
    /// than the scanner looks. A dotted path works, so <c>meta.id</c> is as valid as <c>id</c>.
    /// </summary>
    private async Task AskForArrayKeyAsync(string path)
    {
        if (_confirmation is null)
        {
            return;
        }

        var key = await _confirmation
            .AskForTextAsync(
                "Match elements by",
                $"Which field identifies the elements of {path}?\n\nA name, or a dotted path into each element - for example meta.id.")
            .ConfigureAwait(true);

        if (key is not null)
        {
            await ApplyArrayKeyAsync(path, key).ConfigureAwait(true);
        }
    }

    // ---- Json formatting -------------------------------------------------------------------------

    /// <summary>Spaces per level when a Json document is pretty-printed for reading.</summary>
    [ObservableProperty]
    public partial int JsonIndentSize { get; set; } = 2;

    /// <summary>Indent with tabs instead of spaces.</summary>
    [ObservableProperty]
    public partial bool JsonUseTabs { get; set; }

    /// <summary>Keep an object or array of only scalars on one line - see <see cref="JsonFormatOptions"/>.</summary>
    [ObservableProperty]
    public partial bool JsonInlineSimpleContainers { get; set; } = true;

    /// <summary>Order properties by name when pretty-printing.</summary>
    [ObservableProperty]
    public partial bool JsonSortProperties { get; set; }

    private JsonFormatOptions CurrentFormatOptions() => new()
    {
        IndentSize = JsonIndentSize,
        UseTabs = JsonUseTabs,
        InlineSimpleContainers = JsonInlineSimpleContainers,
        SortProperties = JsonSortProperties,
    };

    // Formatting changes what is on screen and nothing about what was found, so it re-renders rather
    // than re-comparing - and only matters at all while a side is being shown pretty-printed.
    partial void OnJsonIndentSizeChanged(int value) => ReformatIfShowing();

    partial void OnJsonUseTabsChanged(bool value) => ReformatIfShowing();

    partial void OnJsonInlineSimpleContainersChanged(bool value) => ReformatIfShowing();

    partial void OnJsonSortPropertiesChanged(bool value) => ReformatIfShowing();

    private void ReformatIfShowing()
    {
        DisplayOptionChanged();

        if (Pane.PrettyLeft || Pane.PrettyRight)
        {
            Refresh();
        }
    }

    /// <summary>
    /// Whether each side holds changes that are not on disk. Tracked per SIDE because both panes are
    /// editable and the two files are saved independently - a session that fixed something on the left
    /// and something else on the right has two files to write, and writing one of them is not "saved".
    /// </summary>
    [ObservableProperty]
    public partial bool HasUnsavedLeft { get; set; }

    [ObservableProperty]
    public partial bool HasUnsavedRight { get; set; }

    /// <summary>True when either side holds changes that are not on disk.</summary>
    public bool HasUnsavedEdits => HasUnsavedLeft || HasUnsavedRight;

    partial void OnHasUnsavedLeftChanged(bool value) => RaiseUnsavedState();

    partial void OnHasUnsavedRightChanged(bool value) => RaiseUnsavedState();

    private void RaiseUnsavedState()
    {
        OnPropertyChanged(nameof(HasUnsavedEdits));
        OnPropertyChanged(nameof(HasUnsavedMerge));
        OnPropertyChanged(nameof(UnsavedDescription));
    }

    /// <summary>
    /// Asks whether it is alright to throw this tab's unsaved changes away, saving them first if the
    /// user wants. Returns false to mean "do not close".
    ///
    /// Called before closing a tab and before closing the window. Clean tabs return true immediately,
    /// which is nearly all of them - the prompt is for the case where something would actually be
    /// lost, not a ceremony on the way out.
    /// </summary>
    public async Task<bool> ConfirmDiscardAsync()
    {
        if (!HasUnsavedEdits)
        {
            return true;
        }

        if (_confirmation is null)
        {
            // Nothing to ask with. Refusing to close is the only safe answer: losing work silently is
            // the outcome this whole prompt exists to prevent, and a tab that will not close is a
            // nuisance the user can see and act on.
            return false;
        }

        var choice = await _confirmation
            .ChooseAsync(
                "Save changes?",
                $"{UnsavedDescription}\n\nClosing now discards them.",
                ["Save and close", "Close without saving"])
            .ConfigureAwait(true);

        return choice switch
        {
            0 => await SaveDirtySidesAsync().ConfigureAwait(true),
            1 => true,

            // Cancelled, or the dialog was dismissed. "Went away" is never agreement to discard.
            _ => false,
        };
    }

    /// <summary>Names what is unsaved, for a prompt that has to be specific about what would be lost.</summary>
    public string UnsavedDescription => (HasUnsavedLeft, HasUnsavedRight) switch
    {
        (true, true) => $"{_comparison.Left.DisplayName} and {_comparison.Right.DisplayName} have unsaved changes.",
        (true, false) => $"{_comparison.Left.DisplayName} has unsaved changes.",
        (false, true) => $"{_comparison.Right.DisplayName} has unsaved changes.",
        _ => string.Empty,
    };

    /// <summary>Marks one side dirty.</summary>
    private void MarkDirty(DiffSide side)
    {
        if (side == DiffSide.Left)
        {
            HasUnsavedLeft = true;
        }
        else
        {
            HasUnsavedRight = true;
        }
    }

    private void MarkClean()
    {
        HasUnsavedLeft = false;
        HasUnsavedRight = false;
    }

    /// <summary>
    /// Whether editing can be offered at all: only for a text comparison shown side by side.
    ///
    /// Hidden rather than disabled elsewhere, like everything else in this toolbar - a permanently
    /// grey box in the Json or hex view is a question the user has to rule out for themselves.
    /// </summary>
    public bool CanEdit => !IsBinaryComparison && Pane.IsSideBySideViewVisible;

    /// <summary>
    /// A pane was typed into. Restarts the settle timer rather than comparing now - the user is very
    /// likely still typing, and re-diffing per keystroke would spend the whole budget on answers
    /// nobody reads.
    /// </summary>
    private void OnSideEdited(object? sender, DiffSide side)
    {
        _editedSide = side;
        MarkDirty(side);

        // Said out loud either way. Between the keystroke and the re-diff, every count, tint and hunk
        // boundary on screen describes the text as it was BEFORE the keystroke - and a diff tool
        // showing a stale answer without saying so is exactly the failure this whole thing exists to
        // prevent. With LiveDiff on that gap is a fraction of a second; with it off it lasts until F5.
        IsDiffStale = true;

        if (!LiveDiff)
        {
            return;
        }

        _editTimer ??= CreateEditTimer();
        _editTimer.Stop();
        _editTimer.Start();
    }

    /// <summary>
    /// Whether typing re-runs the comparison on its own, shortly after the typing stops.
    ///
    /// On by default, and for most files it is the right answer - the diff keeps up, and F5 is only
    /// ever needed to skip the wait. Off is for the case the default cannot serve: on a pair big
    /// enough that a comparison is measured in seconds, re-running one per pause turns typing into a
    /// series of freezes, and the honest alternative is to let the diff go stale, SAY that it has, and
    /// re-run it when the user asks. Which is what makes this a switch rather than a hidden threshold:
    /// where "big enough" falls depends on the machine and the file, and guessing it wrong is either a
    /// stuttering editor or a refresh key nobody knew they needed.
    /// </summary>
    [ObservableProperty]
    public partial bool LiveDiff { get; set; } = true;

    partial void OnLiveDiffChanged(bool value)
    {
        if (!value)
        {
            // Turned off mid-edit: the pending re-diff was still the old promise. Anything already
            // typed stays marked stale, which is now true until F5.
            _editTimer?.Stop();
        }
        else if (IsDiffStale)
        {
            // Turned on with an edit outstanding: honour it now rather than waiting for the next
            // keystroke to start the timer.
            _ = RefreshDiffAsync();
        }

        DisplayOptionChanged();
    }

    /// <summary>
    /// Whether the comparison on screen describes what the panes now hold.
    ///
    /// False for the whole of an ordinary reading session: it goes true only when a pane is typed
    /// into, and back to false as soon as the re-diff behind it lands. The status bar shows it, and
    /// F5 (<see cref="RefreshDiffCommand"/>) resolves it without waiting for the timer.
    /// </summary>
    [ObservableProperty]
    public partial bool IsDiffStale { get; set; }

    /// <summary>
    /// F5: bring the comparison up to date, now.
    ///
    /// Two different things, and picking between them is the whole point of the command. With typed
    /// changes in a pane, "up to date" means re-diffing WHAT IS ON SCREEN - going back to disk there
    /// would quietly throw the user's edits away, which is not something a refresh key may do. With
    /// nothing typed, it means re-reading both files, which is what someone pressing F5 after a build
    /// or a checkout is asking for.
    /// </summary>
    [RelayCommand]
    public async Task RefreshDiffAsync()
    {
        // Whatever the timer was about to do, this is doing now - leaving it running would re-diff a
        // second time for nothing.
        _editTimer?.Stop();

        if (HasUnsavedEdits)
        {
            await ReDiffAfterEditAsync().ConfigureAwait(true);
            return;
        }

        FilesChangedOnDisk = false;
        await CompareAsync().ConfigureAwait(true);
    }

    private Avalonia.Threading.DispatcherTimer CreateEditTimer()
    {
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = EditSettleTime };

        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            await ReDiffAfterEditAsync().ConfigureAwait(true);
        };

        return timer;
    }

    /// <summary>
    /// Re-runs the comparison over what the panes now hold.
    ///
    /// The edited side's text comes from the PANE rather than from disk or from the previous
    /// comparison - it is the only place the user's typing exists. The other side is whatever it
    /// already was.
    /// </summary>
    private async Task ReDiffAfterEditAsync()
    {
        if (Pane.FileLinesReader is not { } read || !_comparison.HasBothSides || _comparison.IsBinary)
        {
            return;
        }

        var edited = read(_editedSide);

        var left = _editedSide == DiffSide.Left ? _comparison.Left with { Lines = edited } : _comparison.Left;
        var right = _editedSide == DiffSide.Right ? _comparison.Right with { Lines = edited } : _comparison.Right;

        try
        {
            _comparison = await _comparisonService
                .CompareDocumentsAsync(left, right, CurrentOptions())
                .ConfigureAwait(true);

            IsDiffStale = false;
            Refresh();
        }
        catch (TextFileReadException ex)
        {
            // Left stale on purpose: the comparison on screen is still the pre-edit one, and saying so
            // is more use than an error message the user has to remember the meaning of.
            ErrorMessage = ex.Message;
        }
    }

    /// <summary>Stops watching. Called when the tab closes; a watcher holds OS handles.</summary>
    public void Dispose()
    {
        _watcher.Changed -= OnFilesChangedOnDisk;
        _watcher.Dispose();

        // Decoded bitmaps hold unmanaged buffers, and an image comparison holds two of them for as
        // long as the tab is open.
        Images.Dispose();
    }

    /// <summary>
    /// Raised when an option changes, so the shell can persist it as the default for new tabs. The tab
    /// deliberately does not write the settings file itself - several tabs doing so independently
    /// would race, and the last one to finish would win regardless of which the user touched.
    /// </summary>
    public event System.EventHandler? OptionsChanged;

    /// <summary>
    /// Raised after a comparison reads both files successfully, so the shell can add the pair to the
    /// recent list. The list is shared across tabs, so the shell owns it.
    /// </summary>
    public event System.EventHandler? ComparisonSucceeded;

    /// <summary>The label shown on this tab.</summary>
    public string Title => _comparison.HasBothSides
        ? $"{_comparison.Left.DisplayName} ↔ {_comparison.Right.DisplayName}"
        : "New comparison";

    /// <summary>
    /// Seeds this tab's options from the persisted defaults, without triggering a save or a
    /// re-comparison. Options are per-tab on purpose - a JSON comparison and a log comparison want
    /// different settings - and the persisted values are only the starting point for a new one.
    /// </summary>
    public void ApplyDefaults(AppSettings settings)
    {
        _loadingSettings = true;
        try
        {
            IgnoreWhitespace = settings.IgnoreWhitespace;
            IgnoreCase = settings.IgnoreCase;
            NormalizeStructure = settings.NormalizeStructure;
            NormalizeUnicode = settings.NormalizeUnicode;
            ShowInvisibles = settings.ShowInvisibles;
            CollapseUnchanged = settings.CollapseUnchanged;
            WordWrap = settings.WordWrap;
            IsEditing = settings.Editing;
            JsonIndentSize = settings.JsonIndentSize;
            JsonUseTabs = settings.JsonUseTabs;
            JsonInlineSimpleContainers = settings.JsonInlineSimpleContainers;
            JsonSortProperties = settings.JsonSortProperties;
            AutoRefresh = settings.AutoRefresh;
            LiveDiff = settings.LiveDiff;
            IgnoreComments = settings.IgnoreComments;
            IgnoreBlankLines = settings.IgnoreBlankLines;
            SyntaxHighlighting = settings.SyntaxHighlighting;
            ReportPropertyOrder = settings.ReportPropertyOrder;
            MatchArraysByPosition = settings.MatchArraysByPosition;
            IgnoreNullVsMissing = settings.IgnoreNullVsMissing;
            Mode = settings.Mode;

            ArrayKeyOverrides.Clear();
            foreach (var (path, key) in settings.ArrayKeyOverrides)
            {
                ArrayKeyOverrides.Add(new ArrayKeyOverrideEntry(path, key));
            }

            IgnoredLinePatterns.Clear();
            foreach (var pattern in settings.IgnoredLinePatterns)
            {
                IgnoredLinePatterns.Add(pattern);
            }

            IgnoredPaths.Clear();
            foreach (var path in settings.IgnoredPaths)
            {
                IgnoredPaths.Add(path);
            }
        }
        finally
        {
            _loadingSettings = false;
        }
    }

    /// <summary>The current option values, for the shell to persist as the defaults.</summary>
    public AppSettings CaptureOptions(AppSettings settings) => settings with
    {
        IgnoreWhitespace = IgnoreWhitespace,
        IgnoreCase = IgnoreCase,
        NormalizeStructure = NormalizeStructure,
        NormalizeUnicode = NormalizeUnicode,
        ShowInvisibles = ShowInvisibles,
        CollapseUnchanged = CollapseUnchanged,
        WordWrap = WordWrap,
        Editing = IsEditing,
        JsonIndentSize = JsonIndentSize,
        JsonUseTabs = JsonUseTabs,
        JsonInlineSimpleContainers = JsonInlineSimpleContainers,
        JsonSortProperties = JsonSortProperties,
        AutoRefresh = AutoRefresh,
        LiveDiff = LiveDiff,
        IgnoreComments = IgnoreComments,
        IgnoreBlankLines = IgnoreBlankLines,
        SyntaxHighlighting = SyntaxHighlighting,
        ReportPropertyOrder = ReportPropertyOrder,
        MatchArraysByPosition = MatchArraysByPosition,
        IgnoreNullVsMissing = IgnoreNullVsMissing,
        Mode = Mode,
        ArrayKeyOverrides = ArrayKeyOverrides.ToDictionary(e => e.Path, e => e.Key),
        IgnoredLinePatterns = [.. IgnoredLinePatterns],
        IgnoredPaths = [.. IgnoredPaths],
    };

    public ThemeManagerViewModel ThemeManager { get; }

    /// <summary>
    /// Loads and compares a pair of files. Kept out of the constructor: file I/O there would block the
    /// UI thread before the first frame, and errors would have nowhere to be shown.
    /// </summary>
    public async Task InitializeAsync(string? left, string? right)
    {
        LeftPath = left ?? string.Empty;
        RightPath = right ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(LeftPath) && !string.IsNullOrWhiteSpace(RightPath))
        {
            await CompareAsync().ConfigureAwait(true);
        }
    }

    // ---- The diff pane ---------------------------------------------------------------------

    /// <summary>
    /// Everything about DISPLAYING the comparison - the two documents, hunks, navigation and the
    /// semantic tree. Held rather than inherited so the same widget can be hosted by API Studio,
    /// which produces its comparisons from memory rather than from files.
    /// </summary>
    public DiffPaneViewModel Pane { get; } = new();

    /// <summary>
    /// The two pictures, when the compared files turned out to be images. Empty otherwise, and hidden.
    ///
    /// Beside <see cref="Pane"/> rather than inside it: the diff pane is about aligned rows and knows
    /// nothing about bitmaps, and a comparison of two PNGs still HAS a row view - the hex one - which
    /// this sits above rather than replaces.
    /// </summary>
    public ImagePairViewModel Images { get; } = new();

    // ---- File selection ----------------------------------------------------------------------

    [ObservableProperty]
    public partial string LeftPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RightPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Choose two files to compare.";

    /// <summary>
    /// What is loaded, for the empty state and the Open menu: neither side, one side, or both.
    ///
    /// There is no picker row in the window any more - one Open button in the toolbar replaced two
    /// text boxes, two Browse buttons, a Compare button and the collapsed summary line that used to
    /// stand in for all of them (IsFileRowExpanded, now gone). What replaced the SUMMARY is the tab
    /// title in the title bar, which named both files all along.
    ///
    /// Which leaves one thing the row did that a button cannot: show a half-finished choice. Opening
    /// one file is a real state - it is how "compare this against something" starts - so it gets said
    /// here, on the empty state, rather than leaving a window that looks like nothing happened.
    /// </summary>
    public bool HasOneSide => !_comparison.HasBothSides
        && (!string.IsNullOrWhiteSpace(LeftPath) || !string.IsNullOrWhiteSpace(RightPath));

    /// <summary>
    /// What the empty state says below its title: how to start, or what is still missing.
    ///
    /// The half-finished case is the one worth spelling out. Opening a single file leaves a window
    /// that looks exactly like a window where nothing happened, and "pick two files" is then both
    /// wrong and unhelpful - one of them is already picked.
    /// </summary>
    public string EmptyStateDescription
    {
        get
        {
            if (!HasOneSide)
            {
                return "Open two files, or drop them onto this window.";
            }

            var chosen = System.IO.Path.GetFileName(
                string.IsNullOrWhiteSpace(LeftPath) ? RightPath : LeftPath);

            return $"{chosen} is loaded. Choose the file to compare it with.";
        }
    }

    partial void OnLeftPathChanged(string value) => OnPathChanged();

    partial void OnRightPathChanged(string value) => OnPathChanged();

    private void OnPathChanged()
    {
        RaiseSideState();
        ResolveProjectRules();

        // A hand-made pairing is a statement about two particular files - "line 40 here is line 62
        // there" - so it cannot survive one of them being replaced. Dropped silently rather than
        // re-checked: a comparison always follows a path change, and an anchor that happened to fall
        // inside the new file would be honoured while meaning nothing.
        if (Alignments.Count > 0)
        {
            Alignments = [];
            RaiseAlignmentState();
        }
    }

    private void RaiseSideState()
    {
        OnPropertyChanged(nameof(HasOneSide));
        OnPropertyChanged(nameof(EmptyStateDescription));
        OnPropertyChanged(nameof(CanSwapSides));
    }

    /// <summary>
    /// Names how the two files' encodings/BOM/line endings differ, or empty when they do not.
    ///
    /// Named in the status bar in its own right, not only inside the status message, because when it
    /// is the ONLY difference the panes are identical and there is nothing else on screen to notice.
    /// </summary>
    public string FormatDifferenceDetail =>
        TextFormatComparer.Describe(_comparison.Left.Format, _comparison.Right.Format);

    public bool HasFormatDifference => _comparison.FormatDifference.Any;

    /// <summary>True while a comparison or save is running, so the view can disable the toolbar.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>
    /// Re-run the comparison when the files change on disk.
    ///
    /// On by default. The workflow it serves is the ordinary one - the diff open beside the editor
    /// doing the editing - and a stale diff that looks current is the one state in which the tool
    /// actively misleads.
    /// </summary>
    [ObservableProperty]
    public partial bool AutoRefresh { get; set; } = true;

    partial void OnAutoRefreshChanged(bool value)
    {
        if (!value)
        {
            FilesChangedOnDisk = false;
        }

        ApplyWatch();
        DisplayOptionChanged();
    }

    /// <summary>
    /// Set when the files changed but the comparison was NOT re-run, so the status bar can say so and
    /// offer a Reload button. That happens for one reason: unsaved merge decisions. Refreshing would silently discard
    /// them, since decisions are keyed by hunk index and a new comparison renumbers the hunks - so the
    /// choice belongs to the user, not to a file-system event.
    /// </summary>
    [ObservableProperty]
    public partial bool FilesChangedOnDisk { get; set; }

    /// <summary>Re-runs the comparison after an on-disk change, discarding any pending decisions.</summary>
    [RelayCommand]
    private async Task ReloadAsync()
    {
        FilesChangedOnDisk = false;
        await CompareAsync().ConfigureAwait(true);
    }

    /// <summary>Set when the last attempt failed; the view shows it as an error banner.</summary>
    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    // ---- Comparison options -------------------------------------------------------------------

    [ObservableProperty]
    public partial bool IgnoreWhitespace { get; set; }

    [ObservableProperty]
    public partial bool IgnoreCase { get; set; }

    [ObservableProperty]
    public partial bool NormalizeStructure { get; set; }

    /// <summary>Compare in Unicode normal form C - see <see cref="ComparisonOptions.NormalizeUnicode"/>.</summary>
    [ObservableProperty]
    public partial bool NormalizeUnicode { get; set; }

    /// <summary>
    /// Reveal invisible characters in the panes. A DISPLAY setting, not a comparison one - it changes
    /// nothing about the result, so it does not re-run the diff, it just stops the answer looking
    /// wrong.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowInvisibles { get; set; }

    partial void OnShowInvisiblesChanged(bool value)
    {
        Pane.ShowInvisibles = value;
        DisplayOptionChanged();
    }

    /// <summary>
    /// Colour the panes by the file's own grammar. A DISPLAY setting like <see cref="ShowInvisibles"/>,
    /// so it is pushed to the pane and remembered, but never re-runs the comparison - the diff is the
    /// same diff either way.
    /// </summary>
    [ObservableProperty]
    public partial bool SyntaxHighlighting { get; set; } = true;

    partial void OnSyntaxHighlightingChanged(bool value)
    {
        Pane.SyntaxHighlighting = value;
        DisplayOptionChanged();
    }

    /// <summary>
    /// Hide long stretches of unchanged context. A display setting: it changes what is on SCREEN, never
    /// what the comparison found, so it re-folds rather than re-comparing.
    /// </summary>
    [ObservableProperty]
    public partial bool CollapseUnchanged { get; set; } = true;

    partial void OnCollapseUnchangedChanged(bool value)
    {
        Pane.CollapseUnchanged = value;
        DisplayOptionChanged();
    }

    /// <summary>
    /// Wrap long lines in the unified view. A display setting like the two above, and one the unified
    /// view alone can offer - see <c>DiffPaneViewModel.WordWrap</c>.
    /// </summary>
    [ObservableProperty]
    public partial bool WordWrap { get; set; }

    partial void OnWordWrapChanged(bool value)
    {
        Pane.WordWrap = value;
        DisplayOptionChanged();
    }

    /// <summary>
    /// Treat comments as absent. Only has an effect for files in a language the scanner knows - see
    /// <see cref="CodeLanguageDescription"/>, which says so on screen rather than leaving the user to
    /// wonder why a box did nothing.
    /// </summary>
    [ObservableProperty]
    public partial bool IgnoreComments { get; set; }

    /// <summary>Treat added or removed blank lines as noise. Same language caveat as <see cref="IgnoreComments"/>.</summary>
    [ObservableProperty]
    public partial bool IgnoreBlankLines { get; set; }

    /// <summary>
    /// Names the language the code rules are being read with, for the settings window.
    ///
    /// Worth saying out loud: both code options are silently inert for a pair the scanner cannot read,
    /// and a checkbox that does nothing with no explanation is indistinguishable from a broken one.
    /// </summary>
    public string CodeLanguageDescription => _comparison.Language switch
    {
        SourceLanguage.CSharp => "Detected language: C#",
        SourceLanguage.JavaScript => "Detected language: JavaScript",
        SourceLanguage.TypeScript => "Detected language: TypeScript",
        SourceLanguage.Java => "Detected language: Java",
        SourceLanguage.Go => "Detected language: Go",
        SourceLanguage.C => "Detected language: C",
        SourceLanguage.Cpp => "Detected language: C++",
        SourceLanguage.Python => "Detected language: Python",
        _ when !_comparison.HasBothSides =>
            "These apply to C#, JavaScript, TypeScript, Java, Go, C, C++ and Python files.",
        _ => "No source language detected for this pair - these have no effect here.",
    };

    /// <summary>
    /// Report a property that only moved. Off by default because JSON objects are unordered, so
    /// reporting order produces noise on files nobody meaningfully edited.
    /// </summary>
    [ObservableProperty]
    public partial bool ReportPropertyOrder { get; set; }

    /// <summary>Compare arrays by position instead of by identity key.</summary>
    [ObservableProperty]
    public partial bool MatchArraysByPosition { get; set; }

    /// <summary>Treat an explicit JSON <c>null</c> and an absent property as the same thing.</summary>
    [ObservableProperty]
    public partial bool IgnoreNullVsMissing { get; set; }

    /// <summary>Text or semantic comparison; <see cref="ComparisonMode.Auto"/> decides per file.</summary>
    [ObservableProperty]
    public partial ComparisonMode Mode { get; set; } = ComparisonMode.Auto;

    /// <summary>The values offered by the mode selector.</summary>
    public static IReadOnlyList<ComparisonMode> ModeOptions { get; } = Enum.GetValues<ComparisonMode>();

    /// <summary>
    /// Sets the comparison mode, for the View menu's three radio entries.
    ///
    /// A command rather than a two-way bound selector because the mode now lives in a menu: a menu
    /// item reports "I was clicked", and the check marks are read back off <see cref="Mode"/> so the
    /// menu cannot disagree with the comparison about which mode is in force.
    /// </summary>
    [RelayCommand]
    private void SetCompareMode(ComparisonMode mode) => Mode = mode;

    /// <summary>Which entry the View menu ticks. Read-only, so the tick always follows the comparison.</summary>
    public bool IsModeAuto => Mode == ComparisonMode.Auto;

    public bool IsModeText => Mode == ComparisonMode.Text;

    public bool IsModeJson => Mode == ComparisonMode.Json;

    public bool IsModeYaml => Mode == ComparisonMode.Yaml;

    /// <summary>Layout, the other half of the View menu. Side by side or unified - text views only.</summary>
    [RelayCommand]
    private void SetSideBySide() => Pane.ViewMode = DiffViewMode.SideBySide;

    [RelayCommand]
    private void SetUnified() => Pane.ViewMode = DiffViewMode.Unified;

    /// <summary>
    /// Identity-key overrides for specific arrays, shown and edited as a list rather than the
    /// dictionary <see cref="JsonComparisonOptions"/> actually wants - see <see cref="CurrentOptions"/>
    /// for the conversion. Entries are replaced wholesale (remove then re-add), never edited in place,
    /// which is why <see cref="ArrayKeyOverrideEntry"/> is a plain immutable record.
    /// </summary>
    public ObservableCollection<ArrayKeyOverrideEntry> ArrayKeyOverrides { get; } = [];

    [ObservableProperty]
    public partial string NewOverridePath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewOverrideKey { get; set; } = string.Empty;

    [RelayCommand(CanExecute = nameof(CanAddArrayKeyOverride))]
    private void AddArrayKeyOverride()
    {
        ArrayKeyOverrides.Add(new ArrayKeyOverrideEntry(NewOverridePath.Trim(), NewOverrideKey.Trim()));
        NewOverridePath = string.Empty;
        NewOverrideKey = string.Empty;
        OptionChanged();
    }

    private bool CanAddArrayKeyOverride() =>
        !string.IsNullOrWhiteSpace(NewOverridePath) && !string.IsNullOrWhiteSpace(NewOverrideKey);

    partial void OnNewOverridePathChanged(string value) => AddArrayKeyOverrideCommand.NotifyCanExecuteChanged();

    partial void OnNewOverrideKeyChanged(string value) => AddArrayKeyOverrideCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private void RemoveArrayKeyOverride(ArrayKeyOverrideEntry entry)
    {
        ArrayKeyOverrides.Remove(entry);
        OptionChanged();
    }

    /// <summary>
    /// Regular expressions whose matches are ignored when comparing any text - see
    /// <see cref="LinePatternMask"/>. Unlike <see cref="IgnoredPaths"/>, which is JSON-only, these
    /// apply to every file.
    /// </summary>
    public ObservableCollection<string> IgnoredLinePatterns { get; } = [];

    [ObservableProperty]
    public partial string NewLinePattern { get; set; } = string.Empty;

    [RelayCommand(CanExecute = nameof(CanAddLinePattern))]
    private void AddLinePattern()
    {
        IgnoredLinePatterns.Add(NewLinePattern.Trim());
        NewLinePattern = string.Empty;
        OptionChanged();
    }

    /// <summary>
    /// Refuses a pattern that will not compile, rather than storing one that silently does nothing.
    /// The rule is validated here, once, instead of per line inside the comparison.
    /// </summary>
    private bool CanAddLinePattern() =>
        !string.IsNullOrWhiteSpace(NewLinePattern)
        && LinePatternMask.Create([NewLinePattern.Trim()]) is not null;

    partial void OnNewLinePatternChanged(string value) => AddLinePatternCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private void RemoveLinePattern(string pattern)
    {
        IgnoredLinePatterns.Remove(pattern);
        OptionChanged();
    }

    /// <summary>
    /// JSON paths whose differences are never reported - see <see cref="JsonPathPattern"/> for the
    /// syntax. Same add/remove-only shape as <see cref="ArrayKeyOverrides"/>, for the same reason.
    /// </summary>
    public ObservableCollection<string> IgnoredPaths { get; } = [];

    [ObservableProperty]
    public partial string NewIgnoredPath { get; set; } = string.Empty;

    [RelayCommand(CanExecute = nameof(CanAddIgnoredPath))]
    private void AddIgnoredPath()
    {
        IgnoredPaths.Add(NewIgnoredPath.Trim());
        NewIgnoredPath = string.Empty;
        OptionChanged();
    }

    private bool CanAddIgnoredPath() => !string.IsNullOrWhiteSpace(NewIgnoredPath);

    partial void OnNewIgnoredPathChanged(string value) => AddIgnoredPathCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private void RemoveIgnoredPath(string path)
    {
        IgnoredPaths.Remove(path);
        OptionChanged();
    }

    partial void OnIgnoreWhitespaceChanged(bool value) => OptionChanged();

    partial void OnIgnoreCaseChanged(bool value) => OptionChanged();

    partial void OnNormalizeStructureChanged(bool value) => OptionChanged();

    partial void OnNormalizeUnicodeChanged(bool value) => OptionChanged();

    partial void OnReportPropertyOrderChanged(bool value) => OptionChanged();

    partial void OnMatchArraysByPositionChanged(bool value) => OptionChanged();

    partial void OnIgnoreNullVsMissingChanged(bool value) => OptionChanged();

    partial void OnModeChanged(ComparisonMode value)
    {
        // The View menu's tick marks are computed from Mode, so they have to be told.
        OnPropertyChanged(nameof(IsModeAuto));
        OnPropertyChanged(nameof(IsModeText));
        OnPropertyChanged(nameof(IsModeJson));
        OnPropertyChanged(nameof(IsModeYaml));

        OptionChanged();
    }

    partial void OnIgnoreCommentsChanged(bool value) => OptionChanged();

    partial void OnIgnoreBlankLinesChanged(bool value) => OptionChanged();

    /// <summary>
    /// An option was toggled: re-run the comparison and remember the choice. Both are skipped while
    /// settings are being applied at startup, which would otherwise re-save what was just loaded.
    /// </summary>
    private void OptionChanged()
    {
        if (_loadingSettings)
        {
            return;
        }

        Recompare();
        OptionsChanged?.Invoke(this, System.EventArgs.Empty);
    }

    /// <summary>
    /// A DISPLAY setting was toggled: remember it, but do not re-run anything. These change how the
    /// result is drawn, not what it is, and re-comparing a large pair to change a colour would be a
    /// visible pause for no reason.
    /// </summary>
    private void DisplayOptionChanged()
    {
        if (!_loadingSettings)
        {
            OptionsChanged?.Invoke(this, System.EventArgs.Empty);
        }
    }

    // ---- Merge --------------------------------------------------------------------------------

    /// <summary>
    /// True when there is something to save.
    ///
    /// Now just an alias for <see cref="HasUnsavedEdits"/>. It used to mean "at least one hunk has a
    /// pending decision", which was a separate idea from editing; taking a side is an edit now, so
    /// there is only one kind of unsaved change and only one thing to ask about.
    /// </summary>
    public bool HasUnsavedMerge => HasUnsavedEdits;

    /// <summary>
    /// Which document the merge is written into. Right by convention: left is the original / theirs,
    /// right is the current / mine.
    /// </summary>
    [ObservableProperty]
    public partial DiffSide MergeTarget { get; set; } = DiffSide.Right;

    // ---- Commands -----------------------------------------------------------------------------

    /// <summary>
    /// The toolbar's Open button: one dialog, both files.
    ///
    /// This is the whole file-opening UI now, and it is one control because opening files is one
    /// question. Picking two at once is a shift-click for the common case (a pair in the same folder);
    /// picking one fills whichever side is free, which is how "compare this against what I have open"
    /// works. The per-side entries in the same menu (<see cref="BrowseLeftCommand"/>,
    /// <see cref="BrowseRightCommand"/>) cover replacing ONE side of an existing comparison, which is
    /// the only thing the old two-text-box row could do that this cannot.
    /// </summary>
    [RelayCommand]
    private async Task OpenFilesDialogAsync()
    {
        var picked = await _filePicker.PickFilesAsync("Choose two files to compare").ConfigureAwait(true);

        if (picked.Count > 0)
        {
            await OpenFilesAsync(picked).ConfigureAwait(true);
        }
    }

    /// <summary>Whether there is a pair to swap. Both sides, or there is nothing to exchange.</summary>
    public bool CanSwapSides =>
        !string.IsNullOrWhiteSpace(LeftPath) && !string.IsNullOrWhiteSpace(RightPath);

    /// <summary>
    /// Exchanges the two sides and re-compares.
    ///
    /// The picker reports a multi-selection in the platform's order, not the order they were clicked,
    /// so a pair can land the wrong way round through no fault of the user's. Everything about a diff
    /// reverses with the sides - added becomes removed - so getting it back is one click rather than
    /// two trips through a dialog.
    ///
    /// Refuses to throw away typed changes: with unsaved edits the panes hold text that exists nowhere
    /// else, and re-comparing reloads both sides from disk.
    /// </summary>
    [RelayCommand]
    private async Task SwapSidesAsync()
    {
        if (!CanSwapSides || HasUnsavedEdits)
        {
            if (HasUnsavedEdits)
            {
                StatusMessage = "Save or undo your changes before swapping sides.";
            }

            return;
        }

        (LeftPath, RightPath) = (RightPath, LeftPath);

        await CompareAsync().ConfigureAwait(true);
    }

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

    /// <summary>
    /// Rules the repository supplies for these two files, and whether any were found.
    ///
    /// Resolved when the FILES change rather than on every comparison: the config is read from disk,
    /// and re-reading it because someone ticked a checkbox would be work for nothing. Toggling an
    /// option the config also sets is allowed and wins for the session - the file is the team's
    /// starting point, not a lock.
    /// </summary>
    private void ResolveProjectRules()
    {
        if (_projectConfig is null)
        {
            return;
        }

        var config = _projectConfig.Find(LeftPath, out var problem)
                     is { IsEmpty: false } found
            ? found
            : _projectConfig.Find(RightPath, out problem);

        _leftRules = config.For(LeftPath);
        _rightRules = config.For(RightPath);

        ProjectRulesApply = !_leftRules.IsEmpty || !_rightRules.IsEmpty;
        OnPropertyChanged(nameof(ProjectRulesApply));

        // A config with a typo in it is reported and then ignored. Refusing to compare two files
        // because a rules file has a trailing comma would be the wrong trade every time - but so
        // would leaving the user to wonder why their rules stopped working.
        if (problem is not null)
        {
            StatusMessage = problem;
        }
    }

    /// <summary>True when a .fubardiff.json is in force, so the status bar can say so.</summary>
    public bool ProjectRulesApply { get; private set; }

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

            // A fresh pair of files is a fresh start: whatever was typed into the previous one is
            // either saved or gone, and the panes are about to be replaced wholesale anyway.
            _mergeState = MergeState.Empty;
            MarkClean();
            FilesChangedOnDisk = false;

            // Whatever was out of date is now the thing on screen.
            IsDiffStale = false;
            Refresh();

            ApplyWatch();

            RaiseSideState();

            // Announced only after a successful read - a path that could not be opened is not
            // something worth offering to reopen. The shell owns the recent list, since it is shared
            // across tabs.
            ComparisonSucceeded?.Invoke(this, System.EventArgs.Empty);
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

    /// <summary>Resolves the current hunk in favour of the left side, by rewriting the right file.</summary>
    [RelayCommand]
    private Task TakeLeftAsync() => ResolveAsync(HunkResolution.TakeLeft);

    /// <summary>Resolves the current hunk in favour of the right side, by rewriting the left file.</summary>
    [RelayCommand]
    private Task TakeRightAsync() => ResolveAsync(HunkResolution.TakeRight);

    /// <summary>
    /// Kept so the toolbar binding and the shortcut still resolve, but there is nothing pending to
    /// reset any more - it points at undo instead.
    /// </summary>
    [RelayCommand]
    private Task ResetHunkAsync() => ResolveAsync(HunkResolution.Unresolved);

    // ---- Patch --------------------------------------------------------------------------------

    /// <summary>
    /// The comparison as a unified diff - the format git, patch and every review tool understands.
    ///
    /// Built from the CURRENT result, so every comparison option is already reflected in it: a patch
    /// exported with "ignore comments" on describes the changes the user was actually looking at.
    /// </summary>
    private string BuildPatch() => UnifiedPatch.Create(
        _comparison.Result,
        "a/" + _comparison.Left.DisplayName,
        "b/" + _comparison.Right.DisplayName);

    /// <summary>Whether there is anything to export. A patch of no changes is not worth a file.</summary>
    public bool HasPatch => _comparison.Result.Hunks.Count > 0;

    [RelayCommand]
    private async Task CopyPatchAsync()
    {
        if (!HasPatch)
        {
            return;
        }

        await _clipboard.SetTextAsync(BuildPatch()).ConfigureAwait(true);
        StatusMessage = "Patch copied to the clipboard.";
    }

    [RelayCommand]
    private async Task ExportPatchAsync()
    {
        if (!HasPatch || await _filePicker.PickSaveFileAsync("Save patch").ConfigureAwait(true) is not { } path)
        {
            return;
        }

        try
        {
            // Written through the same port as a merge, so a patch is subject to the same failure
            // reporting rather than throwing out of a command.
            await _patchWriter.WriteAsync(path, SplitLines(BuildPatch()), TextFormat.Default).ConfigureAwait(true);
            StatusMessage = $"Patch saved to {path}";
        }
        catch (TextFileWriteException ex)
        {
            ErrorMessage = ex.Message;
            StatusMessage = "Could not save the patch.";
        }
    }

    /// <summary>
    /// A patch is built with '\n' terminators throughout, which is what patch tools expect regardless
    /// of the platform, so it is split back apart for the writer rather than being handed over whole.
    /// </summary>
    private static IReadOnlyList<string> SplitLines(string patch) =>
        patch.TrimEnd('\n').Split('\n');

    /// <summary>Ctrl+S: writes every side that has unsaved changes.</summary>
    [RelayCommand]
    private Task SaveAsync() => SaveDirtySidesAsync();

    /// <summary>Writes just the left file.</summary>
    [RelayCommand]
    private async Task SaveLeftAsync()
    {
        if (await SaveSideAsync(DiffSide.Left, null).ConfigureAwait(true))
        {
            await CompareAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Writes just the right file.</summary>
    [RelayCommand]
    private async Task SaveRightAsync()
    {
        if (await SaveSideAsync(DiffSide.Right, null).ConfigureAwait(true))
        {
            await CompareAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private Task SaveLeftAsAsync() => SaveAsAsync(DiffSide.Left);

    [RelayCommand]
    private Task SaveRightAsAsync() => SaveAsAsync(DiffSide.Right);

    /// <summary>
    /// Writes one side to a file the user picks. Deliberately does NOT clear that side's unsaved flag -
    /// see <see cref="SaveSideAsync"/>.
    /// </summary>
    private async Task SaveAsAsync(DiffSide side)
    {
        var name = side == DiffSide.Left ? _comparison.Left.DisplayName : _comparison.Right.DisplayName;

        if (await _filePicker.PickSaveFileAsync($"Save {name} as").ConfigureAwait(true) is { } path)
        {
            await SaveSideAsync(side, path).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Loads a pair of files, e.g. from a drag and drop. Compares immediately when both are given;
    /// a single file fills whichever side is empty, so dropping two files one at a time works.
    /// </summary>
    public async Task OpenFilesAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }

        if (paths.Count >= 2)
        {
            LeftPath = paths[0];
            RightPath = paths[1];
        }
        else if (string.IsNullOrWhiteSpace(LeftPath))
        {
            LeftPath = paths[0];
        }
        else
        {
            // Left is taken, so a single dropped file becomes the right-hand side - which is what
            // "compare this against what I already have open" means.
            RightPath = paths[0];
        }

        await CompareAsync().ConfigureAwait(true);
    }

    // ---- Internals ----------------------------------------------------------------------------

    /// <summary>
    /// The options this comparison runs with: the session's, with the repository's rules laid over
    /// them.
    ///
    /// The two sides' rules are applied one after the other, so a rule naming <c>*.json</c> and one
    /// naming <c>*.yaml</c> both take effect on a pair that has one of each. Order does not matter
    /// for the list settings (they add up) and barely can for the rest: two rules disagreeing about
    /// the mode of a two-file comparison is a config saying something contradictory about the same
    /// comparison, and the second file's answer is as good as the first's.
    /// </summary>
    private ComparisonOptions CurrentOptions() =>
        _rightRules.ApplyTo(_leftRules.ApplyTo(SessionOptions()));

    private ComparisonOptions SessionOptions() => new()
    {
        IgnoreWhitespace = IgnoreWhitespace,
        IgnoreCase = IgnoreCase,
        NormalizeStructure = NormalizeStructure,
        NormalizeUnicode = NormalizeUnicode,
        Mode = Mode,
        IgnoredLinePatterns = [.. IgnoredLinePatterns],
        Alignments = Alignments,
        Code = new CodeComparisonOptions
        {
            IgnoreComments = IgnoreComments,
            IgnoreBlankLines = IgnoreBlankLines,
        },
        Json = new JsonComparisonOptions
        {
            ReportPropertyOrder = ReportPropertyOrder,
            MatchArraysByPosition = MatchArraysByPosition,
            IgnoreNullVsMissing = IgnoreNullVsMissing,
            ArrayKeyOverrides = ArrayKeyOverrides.ToDictionary(e => e.Path, e => e.Key),
            PositionalArrays = [.. PositionalArrays],
            IgnoredPaths = [.. IgnoredPaths],
        },
    };

    private async Task ResolveAsync(HunkResolution resolution)
    {
        if (Pane.CurrentHunk < 0 || Pane.CurrentHunk >= _comparison.Result.Hunks.Count)
        {
            return;
        }

        if (resolution == HunkResolution.Unresolved)
        {
            // There is nothing pending to reset any more - taking a side rewrote the file, and undoing
            // that is what Ctrl+Z is for, along with everything else the user has done.
            StatusMessage = "Use undo (Ctrl+Z) to take back a change.";
            return;
        }

        var take = resolution == HunkResolution.TakeLeft ? DiffSide.Left : DiffSide.Right;
        var target = take == DiffSide.Left ? DiffSide.Right : DiffSide.Left;

        var hunk = Pane.CurrentHunk;
        var range = _comparison.Result.Hunks[hunk];

        if (IsEditing && Pane.RowReplacer is { } replace)
        {
            // Through the DOCUMENT, so the merge lands on the editor's undo stack next to everything
            // the user typed. The rest follows on its own: the pane reports the edit, the comparison
            // re-runs, and the difference disappears.
            replace(
                target,
                range.StartIndex,
                range.EndIndex,
                TextIn(_comparison.Result, range, take));
        }
        else
        {
            // No editable pane to write through - the toolbar hides the merge controls in that case,
            // but a keyboard shortcut still reaches here. Rewrite the file directly instead; the only
            // thing lost is Ctrl+Z, which is not available outside edit mode anyway.
            await ApplyEditAsync(
                target,
                HunkEdit.Resolve(_comparison.Result, range, take, target, DocumentFor(target).Lines))
                .ConfigureAwait(true);
        }

        StatusMessage = take == DiffSide.Left
            ? $"Change {hunk + 1}: took the left version."
            : $"Change {hunk + 1}: took the right version.";
    }

    /// <summary>One side's text across a hunk's rows, skipping the rows that side has no line for.</summary>
    private static IReadOnlyList<string> TextIn(DiffResult result, DiffHunk hunk, DiffSide side)
    {
        var lines = new List<string>();

        for (var i = hunk.StartIndex; i <= hunk.EndIndex && i < result.Lines.Count; i++)
        {
            if ((side == DiffSide.Left ? result.Lines[i].LeftText : result.Lines[i].RightText) is { } text)
            {
                lines.Add(text);
            }
        }

        return lines;
    }

    /// <summary>
    /// Rewrites one side and re-compares, which is how every change to a file happens now - whether it
    /// was typed or came from taking a side.
    ///
    /// Going through one path is the point. Taking a side used to record a decision that stayed
    /// invisible until save, was keyed by hunk INDEX so a fresh comparison silently renumbered it, and
    /// could not be undone because nothing in the document had changed. As an edit it has none of
    /// those problems, and the diff shrinks in front of the user as they work.
    /// </summary>
    private async Task ApplyEditAsync(DiffSide side, IReadOnlyList<string> lines)
    {
        var left = side == DiffSide.Left ? _comparison.Left with { Lines = lines } : _comparison.Left;
        var right = side == DiffSide.Right ? _comparison.Right with { Lines = lines } : _comparison.Right;

        // CompareDocumentsAsync rather than Recompare: the side's content has changed, so the
        // "original text" the semantic pass highlights from has to be re-derived from it. Recompare
        // threads the PREVIOUS original text through on purpose, which is right when only an option
        // changed and wrong the moment the document did.
        _comparison = await _comparisonService
            .CompareDocumentsAsync(left, right, CurrentOptions())
            .ConfigureAwait(true);

        MarkDirty(side);
        Refresh();
    }

    private TextDocument DocumentFor(DiffSide side) =>
        side == DiffSide.Left ? _comparison.Left : _comparison.Right;

    /// <summary>
    /// Writes every side that has unsaved changes, and reports what happened.
    ///
    /// Both sides, because both are editable: a session that fixed something on the left and something
    /// else on the right has two files to write, and writing one of them is not "saved". Returns false
    /// when anything failed, so a caller closing the tab knows not to.
    /// </summary>
    public async Task<bool> SaveDirtySidesAsync()
    {
        var saved = new List<string>();

        if (HasUnsavedLeft && !await SaveSideAsync(DiffSide.Left, null, saved).ConfigureAwait(true))
        {
            return false;
        }

        if (HasUnsavedRight && !await SaveSideAsync(DiffSide.Right, null, saved).ConfigureAwait(true))
        {
            return false;
        }

        if (saved.Count == 0)
        {
            return true;
        }

        StatusMessage = $"Saved {string.Join(" and ", saved)}";

        // Re-read once, after everything is written, rather than per side: the view should reflect
        // what is now on disk, and doing it between two writes would compare a half-saved pair.
        await CompareAsync().ConfigureAwait(true);

        return true;
    }

    private async Task<bool> SaveSideAsync(DiffSide side, string? targetPath, List<string>? saved = null)
    {
        if (!_comparison.HasBothSides)
        {
            return false;
        }

        // The guard that keeps editing from being destructive. A binary comparison carries EMPTY text
        // documents - the bytes live on Binary, not on Left/Right - so a save would build a document of
        // no lines and write it cheerfully over the user's file. The toolbar hides these controls and
        // CanMerge says no, but a save that erases a PNG is not a thing to leave resting on a binding.
        if (IsBinaryComparison)
        {
            StatusMessage = "Binary files cannot be merged.";
            return false;
        }

        IsBusy = true;
        ErrorMessage = null;
        _lastSelfWrite = DateTime.UtcNow;

        try
        {
            // The merge state is always empty now, which is what makes this write the document exactly
            // as the pane holds it - see MergedDocument.Build.
            var path = await _mergeService
                .SaveAsync(_comparison, _mergeState, side, targetPath)
                .ConfigureAwait(true);

            // Only an in-place save makes the side clean. A Save As writes a copy somewhere else and
            // leaves the compared file exactly as unsaved as it was, which is the one thing about
            // Save As that is easy to get wrong and expensive when it is.
            if (targetPath is null)
            {
                if (side == DiffSide.Left)
                {
                    HasUnsavedLeft = false;
                }
                else
                {
                    HasUnsavedRight = false;
                }
            }

            saved?.Add(System.IO.Path.GetFileName(path));

            if (saved is null)
            {
                StatusMessage = $"Saved {path}";
            }

            return true;
        }
        catch (TextFileWriteException ex)
        {
            ErrorMessage = ex.Message;
            StatusMessage = "Save failed.";

            return false;
        }
        finally
        {
            IsBusy = false;

            // Stamped again on the way out: the re-read can take longer than the watcher's quiet
            // period, and the window has to still be open when the event finally arrives.
            _lastSelfWrite = DateTime.UtcNow;
        }
    }

    private void Refresh()
    {
        // A binary comparison has no text rows of its own, so the pane is given the HEX rows instead -
        // an ordinary DiffResult, which is what lets the editors, scroll sync, tints, diff map,
        // navigation and folds all work on bytes without knowing they are bytes. See HexDiff.
        var result = _comparison.Binary is { } binary
            ? HexDiff.Build(binary)
            : _comparison.Result;

        // Nothing to remap any more: there are no pending decisions keyed by hunk index, because
        // taking a side rewrites the document there and then. The merge state is kept only because
        // MergeService still takes one, and it is always empty - which makes MergedDocument.Build
        // round-trip the base side exactly, i.e. save what the pane holds.
        _mergeState = MergeState.Empty;

        // The Pretty button re-lays-out JSON, and there is no YAML emitter behind it - so it is
        // offered exactly where it would do something, rather than being a control that quietly does
        // nothing on a manifest.
        Pane.CanReformat = _comparison.IsJsonPair;

        // Before Show, like the renderer metadata it resembles: Show replaces both documents, and a
        // pane that learned its grammar afterwards would repaint the new content with the previous
        // comparison's colours for a frame.
        //
        // No grammar at all for hex: the extension belongs to the file, and colouring a dump of its
        // bytes as if it were a PNG - or worse, as C# - would be actively misleading.
        Pane.LeftSyntaxExtension = _comparison.IsBinary ? null : System.IO.Path.GetExtension(_comparison.Left.Path);
        Pane.RightSyntaxExtension = _comparison.IsBinary ? null : System.IO.Path.GetExtension(_comparison.Right.Path);
        Pane.SyntaxHighlighting = SyntaxHighlighting;
        Pane.CollapseUnchanged = CollapseUnchanged;
        Pane.WordWrap = WordWrap;

        // Before Show: the tree is annotated with these as it is built, so a right-click on an array
        // row has its options from the first frame rather than after the next comparison.
        Pane.ArrayKeys = _comparison.ArrayKeys;

        // Re-asserted per comparison rather than only when the toggle moves: a pair that turns out to
        // be binary must not stay editable just because the previous one was.
        Pane.IsEditable = IsEditing && !_comparison.IsBinary;

        // Before Show for the same reason the grammar is: this replaces both bitmaps, and the previous
        // comparison's pictures must not be on screen next to the new one's hex for a frame.
        Images.Show(_comparison.Binary);

        // The Json view's text and the spans into it are produced together, because a change carries
        // offsets into one specific string - reformatting a side without re-deriving them would leave
        // every highlight pointing at the line a value used to be on.
        var json = _comparisonService.FormatJsonForDisplay(
            _comparison,
            Pane.PrettyLeft,
            Pane.PrettyRight,
            CurrentFormatOptions());

        Pane.Show(
            result,
            _comparison.IsSemantic,
            _comparison.SemanticChanges,
            json.LeftText,
            json.RightText,
            json.Changes);

        // A skipped semantic pass is only worth mentioning when the user explicitly asked for JSON;
        // the service decides that and leaves the reason null otherwise.
        ErrorMessage = _comparison.SemanticFallbackReason;

        RaiseTitle();

        OnPropertyChanged(nameof(HasFormatDifference));
        OnPropertyChanged(nameof(FormatDifferenceDetail));
        OnPropertyChanged(nameof(CodeLanguageDescription));
        OnPropertyChanged(nameof(HasPatch));
        OnPropertyChanged(nameof(Binary));
        OnPropertyChanged(nameof(IsBinaryComparison));
        OnPropertyChanged(nameof(ShowsImages));
        OnPropertyChanged(nameof(CanMerge));
        OnPropertyChanged(nameof(ShowsMergeControls));
        OnPropertyChanged(nameof(CanEdit));

        StatusMessage = _comparison.Binary is { } summary ? BuildBinaryStatus(summary) : BuildStatus(result);
    }

    /// <summary>The byte comparison, when the files turned out not to be text. Null otherwise.</summary>
    public BinaryComparison? Binary => _comparison.Binary;

    /// <summary>True when this tab is comparing bytes rather than text.</summary>
    public bool IsBinaryComparison => _comparison.IsBinary;

    /// <summary>True when both sides are images the app can put on screen next to each other.</summary>
    public bool ShowsImages => _comparison.Binary?.BothAreImages ?? false;

    /// <summary>
    /// Whether taking a side and saving is offered at all.
    ///
    /// False for a binary comparison, and this is the one guard in the feature that MATTERS. The pane
    /// is showing hex rows, which are an ordinary <c>DiffResult</c> as far as everything downstream is
    /// concerned - including the merge, which would happily write those hex STRINGS over the user's
    /// PNG. The view hides the merge controls (see MainWindow), and the commands refuse as well, so
    /// neither a keyboard shortcut nor a binding that outlives a refactor can reach the write path.
    /// </summary>
    public bool CanMerge => !IsBinaryComparison;

    /// <summary>
    /// Whether the take-left/take-right group belongs in the toolbar right now.
    ///
    /// A hex comparison HAS hunks - that is the point of expressing it as a diff result - so the
    /// group's usual condition is satisfied and it would appear, offering to merge two files it must
    /// not touch.
    /// </summary>
    public bool ShowsMergeControls => CanMerge && Pane.HasCurrentHunk;

    /// <summary>
    /// The status line for a byte comparison.
    ///
    /// Says what a byte comparison can honestly say and no more. There is no "3 changes" here: for
    /// anything compressed, one altered pixel moves every byte after it, and a count would read as a
    /// total rewrite of a file that changed in one place.
    /// </summary>
    private static string BuildBinaryStatus(BinaryComparison binary)
    {
        if (binary.AreIdentical)
        {
            return $"Binary - the files are identical ({Bytes(binary.LeftLength)}).";
        }

        var sizes = binary.LengthsDiffer
            ? $"{Bytes(binary.LeftLength)} vs {Bytes(binary.RightLength)}"
            : $"both {Bytes(binary.LeftLength)}";

        var at = binary.FirstDifference is { } offset
            ? $", first differ at byte 0x{offset:x} ({offset:N0})"
            : string.Empty;

        return $"Binary - {sizes}{at}.";
    }

    /// <summary>A byte count in the unit a person would use for it.</summary>
    private static string Bytes(int count) => count switch
    {
        < 1024 => $"{count:N0} B",
        < 1024 * 1024 => $"{count / 1024.0:N1} KB",
        _ => $"{count / (1024.0 * 1024.0):N1} MB",
    };

    /// <summary>
    /// The status line. When a semantic comparison finds nothing, it says so explicitly rather than
    /// just "identical" - the two files may well differ as text, and claiming otherwise would look
    /// like a bug to anyone who can see that they do.
    /// </summary>
    private string BuildStatus(DiffResult result)
    {
        if (result.AreIdentical)
        {
            // A format-only difference is still a difference: these two files are not interchangeable
            // on disk, and saying "identical" about them is how a diff tool loses someone's trust
            // right after their version control told them otherwise.
            if (_comparison.FormatDifference.Any)
            {
                return $"Same content, different file format - {FormatDifferenceDetail}";
            }

            return Pane.IsSemantic
                ? "No semantic differences - the files differ only in formatting or ordering."
                : "The files are identical.";
        }

        if (!Pane.IsSemantic)
        {
            // Moved blocks are named separately rather than deducted: they are still counted among the
            // added and removed rows, because that is what they are on disk and what the patch will
            // say. What the extra clause buys is the reader knowing how much of the total they can
            // stop reading.
            var moved = result.Moved > 0 ? $", {result.Moved} block(s) moved" : string.Empty;

            return $"{result.Hunks.Count} change(s) - {result.Inserted} added, "
                   + $"{result.Deleted} removed, {result.Modified} changed{moved}";
        }

        // Count SEMANTIC changes, not rows. The two genuinely differ - a value that changed and also
        // moved shows up as a deleted row and an inserted row, so the row count would say "2 changes"
        // where the tree shows one. Reporting the row count next to a tree that disagrees with it just
        // looks like a bug.
        // Ignored changes are excluded: they form no hunk and are drawn only as a faint band, so
        // counting them here would contradict both the tree and the region count beside it.
        var count = _comparison.SemanticChanges.Count(c => !c.IsIgnored);
        var ignored = _comparison.SemanticChanges.Count - count;
        var hunks = result.Hunks.Count;

        var suffix = ignored > 0 ? $"   ·   {ignored} ignored" : string.Empty;
        return $"semantic: {count} change(s) across {hunks} region(s){suffix}";
    }

    private void Reset()
    {
        _comparison = FileComparison.Empty;
        _mergeState = MergeState.Empty;
        MarkClean();
        Pane.Clear();

        RaiseTitle();
    }

    /// <summary>
    /// The tab label is computed from the loaded documents, so it has to be raised by hand whenever
    /// the comparison is replaced - it has no setter for the generator to hook.
    /// </summary>
    private void RaiseTitle() => OnPropertyChanged(nameof(Title));

    /// <summary>
    /// Cancels the in-flight re-comparison, if any. Toggling several options quickly would otherwise
    /// queue a diff per keystroke and apply them out of order.
    /// </summary>
    private System.Threading.CancellationTokenSource? _recompareCancellation;

    /// <summary>
    /// Re-runs against the loaded documents when an option changes. Silent when nothing is loaded -
    /// toggling a checkbox before choosing files is not an error.
    /// </summary>
    private async void Recompare() => await RecompareAsync().ConfigureAwait(true);

    /// <summary>
    /// <see cref="Recompare"/>, awaitable.
    ///
    /// Split out because the fire-and-forget form is right for a property setter - a checkbox cannot
    /// await anything - and wrong for a caller that has something to do afterwards, including a test
    /// that wants to assert on the result rather than sleep and hope.
    /// </summary>
    public async Task RecompareAsync()
    {
        if (!_comparison.HasBothSides)
        {
            return;
        }

        var previous = _recompareCancellation;
        var cancellation = new System.Threading.CancellationTokenSource();
        _recompareCancellation = cancellation;

        previous?.Cancel();
        previous?.Dispose();

        try
        {
            var result = await _comparisonService
                .RecompareAsync(_comparison, CurrentOptions(), cancellation.Token)
                .ConfigureAwait(true);

            // A newer toggle superseded this one while it ran; applying it now would show a result for
            // options the user has already moved on from.
            if (!cancellation.IsCancellationRequested)
            {
                _comparison = result;
                Refresh();
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer toggle - nothing to report.
        }
    }

    /// <summary>Reports the position after the pane navigates. Navigation itself lives on the pane.</summary>
    private void OnPaneNavigated(object? sender, EventArgs e)
    {
        StatusMessage = _comparison.IsBinary
            ? $"Difference {Pane.CurrentHunk + 1} of {Pane.Hunks.Count} in the bytes"
            : $"Change {Pane.CurrentHunk + 1} of {Pane.Hunks.Count}";

        OnPropertyChanged(nameof(ShowsMergeControls));
    }

    /// <summary>Starts or stops watching, to match the current files and the setting.</summary>
    private void ApplyWatch()
    {
        if (AutoRefresh && _comparison.HasBothSides)
        {
            _watcher.Watch([_comparison.Left.Path, _comparison.Right.Path]);
        }
        else
        {
            _watcher.Stop();
        }
    }

    /// <summary>
    /// The files changed underneath us.
    ///
    /// Arrives on a background thread, so everything real happens back on the UI one. With unsaved
    /// decisions we refuse to reload and say so in the status bar instead: a new comparison renumbers the hunks
    /// those decisions are keyed by, so refreshing would either discard them or, worse, apply them to
    /// different changes.
    /// </summary>
    private void OnFilesChangedOnDisk(object? sender, EventArgs e) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!_comparison.HasBothSides || DateTime.UtcNow - _lastSelfWrite < SelfWriteWindow)
            {
                return;
            }

            if (HasUnsavedEdits)
            {
                // A genuine conflict: the file moved under changes the user has not saved, and only
                // they can say which version wins. The status bar keeps saying so as well as the prompt,
                // so dismissing the dialog does not leave the situation unmarked.
                FilesChangedOnDisk = true;
                _ = PromptForConflictAsync();

                return;
            }

            if (!AutoRefresh)
            {
                // Auto-refresh off used to mean nothing happened at all - the file changed and the
                // user went on reading a stale comparison with no sign of it. Saying so is not the
                // same as reloading behind their back.
                FilesChangedOnDisk = true;
                return;
            }

            _ = RefreshFromDiskAsync();
        });

    /// <summary>
    /// Asks what to do when the files changed on disk under unsaved changes.
    ///
    /// The safe answer is first and is what a dismissed dialog gives: keeping what the user typed
    /// costs them a manual reload, while reloading over it costs them their work. Saving first is
    /// offered because it is usually what they meant - they were finished, and something else touched
    /// the file while they were not looking.
    /// </summary>
    private async Task PromptForConflictAsync()
    {
        if (_confirmation is null || _promptingConflict)
        {
            return;
        }

        _promptingConflict = true;

        try
        {
            var choice = await _confirmation
                .ChooseAsync(
                    "The files changed on disk",
                    $"{UnsavedDescription}\n\nSomething else has written to these files since they were opened.",
                    ["Keep my changes", "Save mine over what changed", "Reload and discard my changes"])
                .ConfigureAwait(true);

            switch (choice)
            {
                case 1:
                    await SaveDirtySidesAsync().ConfigureAwait(true);
                    break;

                case 2:
                    MarkClean();
                    await RefreshFromDiskAsync().ConfigureAwait(true);
                    break;

                default:
                    // Keep saying so: they chose to keep their work, and the files are still out of
                    // date. The status bar's Reload button is how they change their mind.
                    StatusMessage = "Kept your changes. The files on disk are newer.";
                    break;
            }
        }
        finally
        {
            _promptingConflict = false;
        }
    }

    /// <summary>
    /// Guards against a second dialog while one is open. Editors save by writing a temporary file and
    /// renaming it, which can produce several events in a row - and stacking modal prompts on top of
    /// each other is how a window becomes impossible to close.
    /// </summary>
    private bool _promptingConflict;

    /// <summary>
    /// Re-runs the comparison against the files as they are now.
    ///
    /// A read failure is swallowed rather than shown, because this was not asked for: an editor that
    /// saves by replacing the file leaves it missing for a moment, and turning that instant into an
    /// error banner over a diff that was fine would be worse than waiting for the next event. The
    /// status bar offers a manual reload if the file really has gone.
    /// </summary>
    private async Task RefreshFromDiskAsync()
    {
        try
        {
            await CompareAsync().ConfigureAwait(true);
        }
        catch (TextFileReadException)
        {
            FilesChangedOnDisk = true;
        }
    }
}
