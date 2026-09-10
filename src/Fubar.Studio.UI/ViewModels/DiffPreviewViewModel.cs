using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Controls.ViewModels;
using Fubar.Diff.Core.Comparison;
using Fubar.Diff.Core.Json;
using Fubar.Studio.Core.Comparison;
using Fubar.Studio.Core.Models;
using Fubar.Studio.UI.Services;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>
/// Hosts the shared diff widget for API Studio's own comparisons: an existing request against the one
/// an OpenAPI spec would import, two HTTP responses, or a response against a previous run.
///
/// Everything API Studio compares is already in memory, so it goes through
/// <see cref="IFileComparisonService.CompareTextAsync"/> rather than the file path Fubar Diff uses.
/// The pane and every renderer behind it are identical either way.
///
/// Comparison options come from a hierarchy (global → folder → request) rather than being fixed here:
/// see <see cref="DiffSettingsContext"/> and <see cref="ComparisonSettingsResolver"/>. Toggling one in
/// this dialog edits a REQUEST-level working copy, which is what makes every control here an override
/// of whatever it inherited.
/// </summary>
public partial class DiffPreviewViewModel : ViewModelBase
{
    private readonly IFileComparisonService _comparison;

    /// <summary>The content being compared, kept so changing a setting can re-run the comparison.</summary>
    private string _leftText = string.Empty;
    private string _rightText = string.Empty;

    private DiffSettingsContext? _settingsContext;

    /// <summary>
    /// The request level's overrides as the user is editing them - a clone of what was persisted, so
    /// closing without saving leaves the file alone. Stacked on top of the inherited layers on every
    /// resolve.
    /// </summary>
    private ComparisonSettings _draft = new();

    /// <summary>Suppresses re-comparing while the toggles are being seeded from a resolve.</summary>
    private bool _applyingResolved;

    /// <summary>Asks for an array key the menu could not offer. Null in tests and wherever no window
    /// is available, which simply leaves "Match by another field…" doing nothing.</summary>
    private readonly Fubar.Controls.IConfirmationService? _confirmation;

    public DiffPreviewViewModel(
        IFileComparisonService comparison,
        Fubar.Controls.IConfirmationService? confirmation = null)
    {
        _comparison = comparison;
        _confirmation = confirmation;

        // The tree's "Compare this list" menu is part of the shared widget and renders in API Studio
        // exactly as it does in Fubar Diff - but the widget only ASKS, because the host owns the
        // options. Nothing here listened, so the menu recorded a choice nobody applied: the check mark
        // never moved and the comparison never changed.
        Pane.ArrayKeyChosen += (_, option) => _ = ApplyArrayMatchAsync(option.Path, option.Mode, option.Key);
        Pane.CustomArrayKeyRequested += (_, path) => _ = AskForArrayKeyAsync(path);
    }


    /// <summary>The diff widget itself.</summary>
    public DiffPaneViewModel Pane { get; } = new();

    // ---- Effective settings ---------------------------------------------------------------------

    /// <summary>
    /// Every setting's effective value and origin. Recomputed on every change rather than cached in
    /// pieces, so the "inherited from" labels can never disagree with what the comparison just ran.
    /// </summary>
    public ResolvedComparisonSettings Resolved { get; private set; } =
        ComparisonSettingsResolver.Resolve([]);

    [ObservableProperty]
    public partial bool IgnoreWhitespace { get; set; }

    [ObservableProperty]
    public partial bool IgnoreCase { get; set; }

    [ObservableProperty]
    public partial bool NormalizeStructure { get; set; }

    [ObservableProperty]
    public partial bool ReportPropertyOrder { get; set; }

    [ObservableProperty]
    public partial bool MatchArraysByPosition { get; set; }

    [ObservableProperty]
    public partial bool IgnoreNullVsMissing { get; set; }

    partial void OnIgnoreWhitespaceChanged(bool value) => Override(s => s.IgnoreWhitespace = value);

    partial void OnIgnoreCaseChanged(bool value) => Override(s => s.IgnoreCase = value);

    partial void OnNormalizeStructureChanged(bool value) => Override(s => s.NormalizeStructure = value);

    partial void OnReportPropertyOrderChanged(bool value) => Override(s => s.ReportPropertyOrder = value);

    partial void OnMatchArraysByPositionChanged(bool value) => Override(s => s.MatchArraysByPosition = value);

    partial void OnIgnoreNullVsMissingChanged(bool value) => Override(s => s.IgnoreNullVsMissing = value);

    /// <summary>
    /// Records a request-level override and re-compares. Skipped while
    /// <see cref="ApplyResolved"/> is seeding the toggles, which would otherwise turn every inherited
    /// value into an explicit override the moment the dialog opened.
    /// </summary>
    private void Override(System.Action<ComparisonSettings> set) => _ = OverrideAsync(set);

    /// <summary>
    /// The same thing, awaitable. A toggle is fire-and-forget - nobody is waiting on a checkbox - but
    /// a menu click that the user is watching for a result has something to wait for, and so does a
    /// test.
    /// </summary>
    private Task OverrideAsync(System.Action<ComparisonSettings> set)
    {
        if (_applyingResolved)
        {
            return Task.CompletedTask;
        }

        set(_draft);
        SettingsDirty = true;
        return RecompareAsync();
    }

    /// <summary>Human-readable origin for each setting, e.g. "Folder: users" - bound as a hint.</summary>
    public string IgnoreWhitespaceSource => Describe(Resolved.IgnoreWhitespace.Scope, Resolved.IgnoreWhitespace.SourceName);

    public string IgnoreCaseSource => Describe(Resolved.IgnoreCase.Scope, Resolved.IgnoreCase.SourceName);

    public string NormalizeStructureSource => Describe(Resolved.NormalizeStructure.Scope, Resolved.NormalizeStructure.SourceName);

    public string ReportPropertyOrderSource => Describe(Resolved.ReportPropertyOrder.Scope, Resolved.ReportPropertyOrder.SourceName);

    public string MatchArraysByPositionSource => Describe(Resolved.MatchArraysByPosition.Scope, Resolved.MatchArraysByPosition.SourceName);

    public string IgnoreNullVsMissingSource => Describe(Resolved.IgnoreNullVsMissing.Scope, Resolved.IgnoreNullVsMissing.SourceName);


    private static string Describe(ComparisonScope scope, string sourceName) => scope switch
    {
        ComparisonScope.Default => "default",
        ComparisonScope.Request => "set here",
        _ => $"from {sourceName}",
    };

    /// <summary>Drops every request-level override, falling back to whatever is inherited.</summary>
    [RelayCommand]
    private async Task ResetOverridesAsync()
    {
        _draft = new ComparisonSettings();
        SettingsDirty = true;
        await RecompareAsync().ConfigureAwait(true);
    }

    // ---- How this comparison is read ------------------------------------------------------------

    /// <summary>
    /// Text or structure. <see cref="ComparisonMode.Auto"/> compares anything that parses as JSON
    /// semantically, which is most of what API Studio compares.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT part of <see cref="ComparisonSettings"/> and never saved. The settings say
    /// what COUNTS as a difference and belong to the request for everyone who clones the repository;
    /// this says how the person in front of the window wants to read this response right now - usually
    /// "show me the raw text, I do not trust the parse". Persisting it would make one reader's glance
    /// at the bytes everybody's default.
    /// </remarks>
    [ObservableProperty]
    public partial ComparisonMode CompareMode { get; set; } = ComparisonMode.Auto;

    partial void OnCompareModeChanged(ComparisonMode value)
    {
        OnPropertyChanged(nameof(IsModeAuto));
        OnPropertyChanged(nameof(IsModeText));
        OnPropertyChanged(nameof(IsModeJson));

        if (!_applyingResolved)
        {
            _ = RecompareAsync();
        }
    }

    public bool IsModeAuto => CompareMode == ComparisonMode.Auto;

    public bool IsModeText => CompareMode == ComparisonMode.Text;

    public bool IsModeJson => CompareMode == ComparisonMode.Json;

    [RelayCommand]
    private void SetCompareMode(ComparisonMode mode) => CompareMode = mode;

    [RelayCommand]
    private void SetSideBySide() => Pane.ViewMode = DiffViewMode.SideBySide;

    [RelayCommand]
    private void SetUnified() => Pane.ViewMode = DiffViewMode.Unified;

    /// <summary>True while this request overrides anything, so "Reset" can be offered only when it does.</summary>
    public bool HasOverrides => !_draft.IsEmpty;

    /// <summary>The working copy this dialog is editing, before anything is saved - what a Save at
    /// request level would write. Exposed so a caller can see what WOULD be written rather than
    /// inferring it from the resolved list, which is the confusion this whole change was about.</summary>
    public ComparisonSettings PendingOverrides => _draft;

    // ---- Ignore rules ---------------------------------------------------------------------------

    /// <summary>
    /// Rules in force for this comparison, newest last, each with the level it came from. Bound as
    /// removable chips.
    ///
    /// <para>Reseeded wholesale from the resolve after every comparison, which it can be because the
    /// working draft is one of the layers being resolved (see <see cref="RecompareAsync"/>) - so what
    /// the user just added is already in the resolved answer rather than something the list has to
    /// remember on its own.</para>
    /// </summary>
    public ObservableCollection<IgnoredPathViewModel> IgnoredPaths { get; } = [];

    // ---- How each array is matched --------------------------------------------------------------

    /// <summary>
    /// The arrays this comparison has been told how to match, each with where the instruction came
    /// from. Bound as removable chips beside the ignore rules.
    /// </summary>
    /// <remarks>
    /// Not decoration. Choosing "Ignore order" on an array usually removes every row that array had
    /// from the tree - that is the point - and the menu that set the rule lives on those rows, so
    /// without a chip the instruction becomes unreachable the moment it works. Seeded from the resolve
    /// like <see cref="IgnoredPaths"/>, for the same reason.
    /// </remarks>
    public ObservableCollection<ArrayRuleViewModel> ArrayRules { get; } = [];

    /// <summary>
    /// Records how one array is matched and re-compares.
    /// </summary>
    /// <remarks>
    /// <para>The three lists are written TOGETHER, seeded from what is currently in force. They
    /// replace rather than merge what they inherit, so writing only the array just chosen would
    /// silently drop every rule an ancestor folder states about the others.</para>
    /// <para>And they are kept mutually exclusive: an array can only be matched one way, so a stale
    /// entry in another list would make the menu's check mark lie and hand the differ a contradiction
    /// it has to break with a precedence rule the user never saw.</para>
    /// </remarks>
    public Task ApplyArrayMatchAsync(string path, ArrayMatchMode mode, string? key = null)
    {
        var keys = new Dictionary<string, string>(Resolved.ArrayKeyOverrides.Value, System.StringComparer.Ordinal);
        var unordered = Resolved.UnorderedArrays.Value.Where(NotThisPath).ToList();
        var positional = Resolved.PositionalArrays.Value.Where(NotThisPath).ToList();
        keys.Remove(path);

        switch (mode)
        {
            case ArrayMatchMode.Key when key is { Length: > 0 }:
                keys[path] = key;
                break;

            case ArrayMatchMode.Unordered:
                unordered.Add(path);
                break;

            default:
                positional.Add(path);
                break;
        }

        // Written even when empty. An empty non-null list is a real override meaning "nothing here",
        // which is what stops a folder's rule coming back to contradict the choice just made - the
        // same reading the ignore lists already have.
        return OverrideAsync(s =>
        {
            s.ArrayKeyOverrides = keys;
            s.UnorderedArrays = unordered;
            s.PositionalArrays = positional;
        });

        bool NotThisPath(string p) => !string.Equals(p, path, System.StringComparison.Ordinal);
    }

    /// <summary>Puts one array back to whatever it inherits - the ✕ on its chip.</summary>
    [RelayCommand]
    private async Task ResetArrayMatchAsync(string? path)
    {
        if (path is not { Length: > 0 })
        {
            return;
        }

        var keys = new Dictionary<string, string>(Resolved.ArrayKeyOverrides.Value, System.StringComparer.Ordinal);
        keys.Remove(path);

        await OverrideAsync(s =>
        {
            s.ArrayKeyOverrides = keys;
            s.UnorderedArrays = [.. Resolved.UnorderedArrays.Value.Where(p => !string.Equals(p, path, System.StringComparison.Ordinal))];
            s.PositionalArrays = [.. Resolved.PositionalArrays.Value.Where(p => !string.Equals(p, path, System.StringComparison.Ordinal))];
        }).ConfigureAwait(true);
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

        if (key is { Length: > 0 })
        {
            await ApplyArrayMatchAsync(path, ArrayMatchMode.Key, key).ConfigureAwait(true);
        }
    }

    /// <summary>True once anything differs from what was persisted, which is what enables Save.</summary>
    [ObservableProperty]
    public partial bool SettingsDirty { get; set; }

    /// <summary>Whether this comparison can persist its settings at all.</summary>
    public bool CanSaveSettings => _settingsContext?.SaveAsync is not null;

    /// <summary>Whether to show the settings strip - hidden entirely for a comparison with no host.</summary>
    public bool ShowSettings => _settingsContext is not null;

    /// <summary>Whether saving to a folder is offered, i.e. the request has an ancestor folder.</summary>
    public bool CanSaveToFolder => _settingsContext?.FolderName is not null;

    /// <summary>Label for the folder save option, e.g. "Save to folder “users”".</summary>
    public string SaveToFolderLabel => _settingsContext?.FolderName is { } name
        ? $"Save to folder “{name}”"
        : "Save to folder";

    /// <summary>
    /// Adds a rule and re-compares immediately, so the effect is visible rather than promised. The
    /// rule is session-only until saved - ignoring is often exploratory, and silently rewriting
    /// request.json on a click inside a diff window is a side effect nobody asked for.
    /// </summary>
    /// <summary>How to write a difference into the recorded answer, or null when the left side is not
    /// something that can be rewritten. Drives the accept affordances.</summary>
    public Services.SnapshotAcceptContext? Accept { get; private set; }

    public bool CanAccept => Accept is not null;

    /// <summary>"Accept into staging.json" - the file is NAMED, because accepting into a shared
    /// snapshot changes what every environment compares against.</summary>
    public string AcceptAllLabel => $"Accept all into {Accept?.Description}";

    /// <summary>
    /// Writes one field, or everything, into the recorded answer, then re-compares against what was
    /// written.
    /// </summary>
    /// <remarks>
    /// Re-comparing rather than closing: accepting three of forty differences leaves thirty-seven,
    /// and those thirty-seven are the point. Closing the pane would hide the thing being worked on.
    /// </remarks>
    [RelayCommand]
    private async Task AcceptAsync(string? path)
    {
        if (Accept is not { } accept)
        {
            return;
        }

        IsBusy = true;

        try
        {
            if (await accept.AcceptAsync(path).ConfigureAwait(true) is { } written)
            {
                _leftText = written;
                await RecompareAsync().ConfigureAwait(true);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task IgnorePathAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || IgnoredPaths.Any(p => p.Path == path))
        {
            return;
        }

        var draft = Draft();
        draft.Remove.Remove(path);
        draft.Add.Add(path);
        SettingsDirty = true;

        await RecompareAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Stops a rule applying here. What that writes depends on where the rule came from: a rule this
    /// level added is simply dropped, while an INHERITED one becomes an explicit removal, because the
    /// only other way to be rid of it would be editing the folder - and a click in one request's
    /// window must not change what every other request under that folder does.
    /// </summary>
    [RelayCommand]
    private async Task RemoveIgnoreAsync(string? path)
    {
        if (path is null || IgnoredPaths.FirstOrDefault(p => p.Path == path) is not { } entry)
        {
            return;
        }

        var draft = Draft();
        if (entry.IsInherited)
        {
            draft.Remove.Add(path);
        }
        else
        {
            draft.Add.Remove(path);
        }

        SettingsDirty = true;
        await RecompareAsync().ConfigureAwait(true);
    }

    private InheritedPaths Draft() => _draft.IgnoredPaths ??= new InheritedPaths();


    /// <summary>Persists the current overrides at the given level, via the host's callback.</summary>
    [RelayCommand]
    private async Task SaveSettingsAsync(ComparisonScope scope)
    {
        if (_settingsContext?.SaveAsync is not { } save)
        {
            return;
        }

        // An empty draft clears that level rather than writing a section full of nulls.
        var toSave = _draft.IsEmpty ? null : _draft.Clone();

        await save(scope, toSave).ConfigureAwait(true);

        SettingsDirty = false;
        StatusMessage = scope switch
        {
            ComparisonScope.Global => "Saved as your global comparison defaults.",
            ComparisonScope.Folder => $"Saved to folder “{_settingsContext.FolderName}”.",
            _ => "Saved to the request.",
        };
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
    /// Comparison mode is always Auto (see <see cref="ComparisonSettingsMapper"/>), so anything that
    /// parses as JSON - which most of what API Studio compares does - is compared semantically. That is
    /// the difference between "these two responses differ" and "these two responses differ only in key
    /// order".
    /// </summary>
    public async Task LoadAsync(
        string leftText,
        string rightText,
        string leftLabel,
        string rightLabel,
        string title,
        DiffSettingsContext? settings = null,
        Services.SnapshotAcceptContext? accept = null)
    {
        Title = title;
        LeftLabel = leftLabel;
        RightLabel = rightLabel;

        _leftText = leftText;
        _rightText = rightText;
        _settingsContext = settings;
        Accept = accept;
        _draft = settings?.RequestOverrides?.Clone() ?? new ComparisonSettings();

        SettingsDirty = false;

        // Hides the "ignore" affordance in the tree for a comparison with nowhere to put a rule.
        Pane.IgnorePathCommand = settings is null
            ? null
            : new RelayCommand<string>(path => _ = IgnorePathCommand.ExecuteAsync(path));

        OnPropertyChanged(nameof(CanAccept));
        OnPropertyChanged(nameof(AcceptAllLabel));
        OnPropertyChanged(nameof(ShowSettings));
        OnPropertyChanged(nameof(CanSaveSettings));
        OnPropertyChanged(nameof(CanSaveToFolder));
        OnPropertyChanged(nameof(SaveToFolderLabel));

        await RecompareAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Re-resolves the hierarchy with the current draft on top, pushes the result into the toggles, and
    /// runs the comparison. Called on load and after every settings change - the settings ARE the
    /// comparison's options, so the only way to apply one is to compare again.
    /// </summary>
    private async Task RecompareAsync()
    {
        IsBusy = true;

        try
        {
            var layers = new List<ComparisonSettingsLayer>(_settingsContext?.InheritedLayers ?? [])
            {
                new(_draft, ComparisonScope.Request, "Request"),
            };

            Resolved = ComparisonSettingsResolver.Resolve(layers);
            ApplyResolved();

            var result = await _comparison
                .CompareTextAsync(_leftText, _rightText, ComparisonSettingsMapper.ToOptions(Resolved, CompareMode), LeftLabel, RightLabel)
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

    /// <summary>
    /// Seeds the bound toggles and the rule list from the freshly resolved values, guarded so the
    /// resulting property changes do not read back as fresh user overrides.
    /// </summary>
    private void ApplyResolved()
    {
        _applyingResolved = true;

        try
        {
            IgnoreWhitespace = Resolved.IgnoreWhitespace.Value;
            IgnoreCase = Resolved.IgnoreCase.Value;
            NormalizeStructure = Resolved.NormalizeStructure.Value;
            ReportPropertyOrder = Resolved.ReportPropertyOrder.Value;
            MatchArraysByPosition = Resolved.MatchArraysByPosition.Value;
            IgnoreNullVsMissing = Resolved.IgnoreNullVsMissing.Value;

            // Reseeded unconditionally: the draft is one of the resolved layers, so this list IS the
            // answer including whatever the user just did. It used to be skipped while the user was
            // editing, back when the list was the source of truth rather than a view of the resolve.
            IgnoredPaths.Clear();
            foreach (var entry in Resolved.IgnoredPaths)
            {
                IgnoredPaths.Add(new IgnoredPathViewModel(
                    entry.Path,
                    Describe(entry.Scope, entry.SourceName),
                    entry.Scope != ComparisonScope.Request));
            }

            ArrayRules.Clear();
            foreach (var rule in ResolvedArrayRules())
            {
                ArrayRules.Add(rule);
            }
        }
        finally
        {
            _applyingResolved = false;
        }

        OnPropertyChanged(nameof(Resolved));
        OnPropertyChanged(nameof(HasOverrides));
        OnPropertyChanged(nameof(IgnoreWhitespaceSource));
        OnPropertyChanged(nameof(IgnoreCaseSource));
        OnPropertyChanged(nameof(NormalizeStructureSource));
        OnPropertyChanged(nameof(ReportPropertyOrderSource));
        OnPropertyChanged(nameof(MatchArraysByPositionSource));
        OnPropertyChanged(nameof(IgnoreNullVsMissingSource));
    }

    /// <summary>
    /// One chip per array that has been spoken about, keyed rules first, in path order within each
    /// kind - stable, so a re-compare does not shuffle the strip under the pointer.
    /// </summary>
    private IEnumerable<ArrayRuleViewModel> ResolvedArrayRules()
    {
        foreach (var (path, key) in Resolved.ArrayKeyOverrides.Value.OrderBy(o => o.Key, System.StringComparer.Ordinal))
        {
            yield return Rule(path, $"by {key}", Resolved.ArrayKeyOverrides.Scope, Resolved.ArrayKeyOverrides.SourceName);
        }

        foreach (var path in Resolved.UnorderedArrays.Value.OrderBy(p => p, System.StringComparer.Ordinal))
        {
            yield return Rule(path, "order ignored", Resolved.UnorderedArrays.Scope, Resolved.UnorderedArrays.SourceName);
        }

        foreach (var path in Resolved.PositionalArrays.Value.OrderBy(p => p, System.StringComparer.Ordinal))
        {
            yield return Rule(path, "by position", Resolved.PositionalArrays.Scope, Resolved.PositionalArrays.SourceName);
        }

        static ArrayRuleViewModel Rule(string path, string how, ComparisonScope scope, string sourceName) =>
            new(path, how, Describe(scope, sourceName), scope != ComparisonScope.Request);
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

/// <summary>
/// One ignore rule as a chip: the path, where it came from, and whether it was inherited - which is
/// what decides whether removing it drops a local addition or writes an explicit removal.
/// </summary>
public sealed record IgnoredPathViewModel(string Path, string Source, bool IsInherited)
{
    /// <summary>What the ✕ will do, said before it is clicked.</summary>
    public string RemoveTooltip => IsInherited
        ? $"Stop ignoring {Path} here (it stays ignored where it was set - {Source})"
        : $"Stop ignoring {Path}";
}

/// <summary>
/// How one array is being matched, as a chip: the path, the rule in three words, and where it came
/// from.
/// </summary>
/// <param name="How">"order ignored", "by position", "by id" - what the differ is doing with it.</param>
public sealed record ArrayRuleViewModel(string Path, string How, string Source, bool IsInherited)
{
    /// <summary>What the ✕ will do, said before it is clicked.</summary>
    public string RemoveTooltip => IsInherited
        ? $"Match {Path} however it is inherited again (this rule is {Source})"
        : $"Stop matching {Path} this way";
}
