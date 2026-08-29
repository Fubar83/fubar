using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Diff.Application.Comparison;
using Fubar.Diff.Controls.ViewModels;
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

    public DiffPreviewViewModel(IFileComparisonService comparison)
    {
        _comparison = comparison;
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
    private void Override(System.Action<ComparisonSettings> set)
    {
        if (_applyingResolved)
        {
            return;
        }

        set(_draft);
        SettingsDirty = true;
        _ = RecompareAsync();
    }

    /// <summary>Human-readable origin for each setting, e.g. "Folder: users" - bound as a hint.</summary>
    public string IgnoreWhitespaceSource => Describe(Resolved.IgnoreWhitespace.Scope, Resolved.IgnoreWhitespace.SourceName);

    public string IgnoreCaseSource => Describe(Resolved.IgnoreCase.Scope, Resolved.IgnoreCase.SourceName);

    public string NormalizeStructureSource => Describe(Resolved.NormalizeStructure.Scope, Resolved.NormalizeStructure.SourceName);

    public string ReportPropertyOrderSource => Describe(Resolved.ReportPropertyOrder.Scope, Resolved.ReportPropertyOrder.SourceName);

    public string MatchArraysByPositionSource => Describe(Resolved.MatchArraysByPosition.Scope, Resolved.MatchArraysByPosition.SourceName);

    public string IgnoreNullVsMissingSource => Describe(Resolved.IgnoreNullVsMissing.Scope, Resolved.IgnoreNullVsMissing.SourceName);

    public string IgnoredPathsSource => Describe(Resolved.IgnoredPaths.Scope, Resolved.IgnoredPaths.SourceName);

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

    /// <summary>True while this request overrides anything, so "Reset" can be offered only when it does.</summary>
    public bool HasOverrides => !_draft.IsEmpty;

    // ---- Ignore rules ---------------------------------------------------------------------------

    /// <summary>Rules in force for this comparison, newest last. Bound as removable chips.</summary>
    public ObservableCollection<string> IgnoredPaths { get; } = [];

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
    private async Task IgnorePathAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || IgnoredPaths.Contains(path))
        {
            return;
        }

        IgnoredPaths.Add(path);
        CaptureIgnoredPaths();

        await RecompareAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RemoveIgnoreAsync(string? path)
    {
        if (path is null || !IgnoredPaths.Remove(path))
        {
            return;
        }

        CaptureIgnoredPaths();
        await RecompareAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Promotes the visible rule list into a request-level override. Editing the list at all is an
    /// override even when it ends up matching what was inherited - the alternative is a list that
    /// silently changes underfoot when the folder's rules change.
    /// </summary>
    private void CaptureIgnoredPaths()
    {
        _draft.IgnoredPaths = [.. IgnoredPaths];
        SettingsDirty = true;
    }

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
        DiffSettingsContext? settings = null)
    {
        Title = title;
        LeftLabel = leftLabel;
        RightLabel = rightLabel;

        _leftText = leftText;
        _rightText = rightText;
        _settingsContext = settings;
        _draft = settings?.RequestOverrides?.Clone() ?? new ComparisonSettings();

        SettingsDirty = false;

        // Hides the "ignore" affordance in the tree for a comparison with nowhere to put a rule.
        Pane.IgnorePathCommand = settings is null
            ? null
            : new RelayCommand<string>(path => _ = IgnorePathAsync(path));

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
                .CompareTextAsync(_leftText, _rightText, ComparisonSettingsMapper.ToOptions(Resolved), LeftLabel, RightLabel)
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

            // Only reseeded when the user is not the one editing it, so removing a chip does not
            // immediately reappear from the inherited list.
            if (_draft.IgnoredPaths is null && !IgnoredPaths.SequenceEqual(Resolved.IgnoredPaths.Value))
            {
                IgnoredPaths.Clear();
                foreach (var path in Resolved.IgnoredPaths.Value)
                {
                    IgnoredPaths.Add(path);
                }
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
        OnPropertyChanged(nameof(IgnoredPathsSource));
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
