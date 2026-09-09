using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Studio.Application.Comparison;
using Fubar.Studio.Core.Comparison;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Snapshots;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>
/// The document a Rules tab writes to, as the editor that owns it can reach it.
/// </summary>
/// <remarks>
/// Delegates rather than an interface over the model, because the two levels store their rules in
/// different documents that their own editors already load, mutate and save. A Rules tab that loaded
/// and saved the file itself would be a second writer racing the editor's own Save over one file.
/// </remarks>
public sealed class RuleLevel
{
    /// <summary>Which level this is. A rule whose scope matches is LOCAL - written here, editable
    /// here; everything else is inherited from somewhere a click here must not change.</summary>
    public required ComparisonScope Scope { get; init; }

    /// <summary>How this level reads in a sentence: "stop ignoring $.id in this case".</summary>
    public required string LevelName { get; init; }

    public required Func<ComparisonSettings?> GetComparison { get; init; }

    public required Action<ComparisonSettings?> SetComparison { get; init; }

    public required Func<List<Tolerance>?> GetTolerances { get; init; }

    public required Action<List<Tolerance>?> SetTolerances { get; init; }

    /// <summary>Null when this level cannot hold a snapshot policy at all - a case cannot, because a
    /// snapshot is written for the endpoint and its policy is resolved without the case (spec §4).
    /// The tab then shows what applies and says it is not editable here, rather than offering a
    /// control whose value would be dropped on save.</summary>
    public Func<SnapshotPolicy?>? GetSnapshot { get; init; }

    public Action<SnapshotPolicy?>? SetSnapshot { get; init; }

    /// <summary>Tells the owning editor it has unsaved changes.</summary>
    public required Action Changed { get; init; }

    public bool SupportsSnapshotPolicy => GetSnapshot is not null && SetSnapshot is not null;
}

/// <summary>One inherited-or-local rule, as a row: what it says, and where it came from.</summary>
public sealed partial class RuleEntryRowViewModel : ViewModelBase
{
    public RuleEntryRowViewModel(
        string path, string detail, ComparisonScope scope, string sourceName, ComparisonScope level)
    {
        Path = path;
        Detail = detail;
        Scope = scope;
        SourceName = sourceName;
        IsLocal = scope == level;
    }

    public string Path { get; }

    /// <summary>What the rule does, beside the path - "→ &lt;redacted&gt;", "within 0.01".</summary>
    public string Detail { get; }

    public ComparisonScope Scope { get; }

    public string SourceName { get; }

    public bool IsLocal { get; }

    public bool IsInherited => !IsLocal;
}

/// <summary>
/// One comparison option: its effective value, where that came from, and whether this level says
/// anything about it.
/// </summary>
/// <remarks>
/// Three states, not a checkbox. "Inherit" is a real answer and the commonest one - a two-state
/// control would make every option this level had never mentioned look deliberately set, and turning
/// one off and back on would leave a local override behind that keeps overriding forever.
/// </remarks>
public sealed partial class RuleOptionRowViewModel : ViewModelBase
{
    private readonly Action<bool?> _write;
    private bool _quiet;

    public RuleOptionRowViewModel(
        string name,
        string explanation,
        Resolved<bool> resolved,
        bool? own,
        Action<bool?> write)
    {
        Name = name;
        Explanation = explanation;
        _write = write;

        EffectiveValue = resolved.Value;
        SourceName = resolved.SourceName;
        Scope = resolved.Scope;

        _quiet = true;
        Choice = own switch { true => On, false => Off, null => Inherit };
        _quiet = false;
    }

    public const string Inherit = "Inherit";
    public const string On = "On";
    public const string Off = "Off";

    public static IReadOnlyList<string> Choices { get; } = [Inherit, On, Off];

    public string Name { get; }

    public string Explanation { get; }

    public bool EffectiveValue { get; }

    public ComparisonScope Scope { get; }

    public string SourceName { get; }

    /// <summary>What it currently resolves to, said in words, because "Inherit" alone does not tell
    /// you what you are inheriting.</summary>
    public string EffectiveDescription =>
        $"{(EffectiveValue ? "on" : "off")} · from {SourceName}";

    [ObservableProperty]
    public partial string Choice { get; set; }

    partial void OnChoiceChanged(string value)
    {
        if (!_quiet)
        {
            _write(value switch { On => true, Off => false, _ => null });
        }
    }
}

/// <summary>
/// The Rules tab: every rule that applies here, with where each came from.
/// </summary>
/// <remarks>
/// <para>The settings hierarchy was legible only by opening four files and folding them in your head.
/// This is that fold, shown - and it is where the rules that had no editor at all (tolerances, snapshot
/// redaction and normalisation) are written.</para>
/// <para>Inherited rules are shown and are never edited in place: removing one writes a REMOVAL at this
/// level (spec §4.3, §9.4). A click in an endpoint's window must not change what forty other endpoints
/// do.</para>
/// </remarks>
public sealed partial class RulesViewModel : ViewModelBase
{
    private readonly RuleLevel _level;
    private readonly Workspace _workspace;
    private readonly string _requestPath;
    private readonly string? _casePath;
    private readonly IRequestComparisonSettings _settings;
    private readonly StatusLogViewModel _statusLog;

    public RulesViewModel(
        RuleLevel level,
        Workspace workspace,
        string requestPath,
        string? casePath,
        IRequestComparisonSettings settings,
        StatusLogViewModel statusLog)
    {
        _level = level;
        _workspace = workspace;
        _requestPath = requestPath;
        _casePath = casePath;
        _settings = settings;
        _statusLog = statusLog;
    }

    public string LevelName => _level.LevelName;

    public bool SupportsSnapshotPolicy => _level.SupportsSnapshotPolicy;

    /// <summary>Said rather than left to be inferred from controls that quietly do nothing.</summary>
    public string SnapshotNote => SupportsSnapshotPolicy
        ? "What is replaced on the way into a snapshot file, and again on the live response before it is compared."
        : "A snapshot belongs to the endpoint, so its policy is set there. What applies here is shown, and is not editable from a case.";

    public ObservableCollection<RuleOptionRowViewModel> Options { get; } = [];

    public ObservableCollection<RuleEntryRowViewModel> IgnoredPaths { get; } = [];

    public ObservableCollection<RuleEntryRowViewModel> ArrayKeys { get; } = [];

    public ObservableCollection<RuleEntryRowViewModel> Redactions { get; } = [];

    public ObservableCollection<RuleEntryRowViewModel> Normalisations { get; } = [];

    public ObservableCollection<RuleEntryRowViewModel> Tolerances { get; } = [];

    /// <summary>Response headers kept in a snapshot. A plain list, closest level wins - there is no
    /// "add one to what the folder keeps", so this says which level decided and what it chose.</summary>
    [ObservableProperty]
    public partial string SnapshotHeaders { get; set; } = "";

    public bool HasNoIgnoredPaths => IgnoredPaths.Count == 0;

    public bool HasNoArrayKeys => ArrayKeys.Count == 0;

    public bool HasNoRedactions => Redactions.Count == 0;

    public bool HasNoNormalisations => Normalisations.Count == 0;

    public bool HasNoTolerances => Tolerances.Count == 0;

    // ---- What is being typed into the add rows ---------------------------------------------------

    [ObservableProperty]
    public partial string NewIgnoredPath { get; set; } = "";

    [ObservableProperty]
    public partial string NewRedactPath { get; set; } = "";

    [ObservableProperty]
    public partial string NewRedactAs { get; set; } = "<redacted>";

    [ObservableProperty]
    public partial string NewNormalisePath { get; set; } = "";

    [ObservableProperty]
    public partial string NewNormaliseAs { get; set; } = "<timestamp>";

    [ObservableProperty]
    public partial string NewTolerancePath { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToleranceValueHint))]
    public partial ToleranceKind NewToleranceKind { get; set; } = ToleranceKind.Numeric;

    [ObservableProperty]
    public partial string NewToleranceValue { get; set; } = "";

    public IReadOnlyList<ToleranceKind> ToleranceKinds { get; } =
    [
        ToleranceKind.Numeric,
        ToleranceKind.WithinSeconds,
        ToleranceKind.Matches,
        ToleranceKind.LengthWithinPercent,
        ToleranceKind.OneOf,
    ];

    public string ToleranceValueHint => NewToleranceKind switch
    {
        ToleranceKind.Numeric => "How far apart the two numbers may be - 0.01",
        ToleranceKind.WithinSeconds => "How many seconds apart the two timestamps may be - 30",
        ToleranceKind.Matches => "A regular expression both sides must match - ^[0-9a-f]{32}$",
        ToleranceKind.LengthWithinPercent => "How much the array's length may change, as a percent - 10",
        _ => "The allowed values, separated by commas - queued, running",
    };

    // ---- Loading ---------------------------------------------------------------------------------

    /// <summary>
    /// Re-resolves the whole chain and rebuilds every row.
    /// </summary>
    /// <remarks>
    /// After every edit as well as on open. An edit at this level can change what an inherited rule
    /// resolves to - restating a tolerance for a path replaces the ancestor's, and the row then has to
    /// say "this endpoint" rather than still claiming the folder - so re-reading is the only way the
    /// tab stays true.
    /// </remarks>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var rules = await _settings
                .ResolveRulesAsync(_workspace, _requestPath, _casePath, null, cancellationToken)
                .ConfigureAwait(true);

            Build(rules);
        }
        catch (Exception ex)
        {
            _statusLog.LogError($"Could not work out which rules apply here: {ex.Message}");
        }
    }

    private void Build(ResolvedRequestRules rules)
    {
        var own = _level.GetComparison();

        Options.Clear();
        Options.Add(Option("Ignore whitespace", "Leading and trailing whitespace does not count as a difference. Text comparisons only.", rules.Comparison.IgnoreWhitespace, own?.IgnoreWhitespace, v => Write(c => c.IgnoreWhitespace = v)));
        Options.Add(Option("Ignore case", "Upper and lower case do not count as a difference. Text comparisons only.", rules.Comparison.IgnoreCase, own?.IgnoreCase, v => Write(c => c.IgnoreCase = v)));
        Options.Add(Option("Reformat for display", "Pretty-print JSON and XML before showing it. Never rewrites the response.", rules.Comparison.NormalizeStructure, own?.NormalizeStructure, v => Write(c => c.NormalizeStructure = v)));
        Options.Add(Option("Report property order", "A JSON property that only moved counts as a difference.", rules.Comparison.ReportPropertyOrder, own?.ReportPropertyOrder, v => Write(c => c.ReportPropertyOrder = v)));
        Options.Add(Option("Match arrays by position", "Compare array elements by index rather than by an identity key.", rules.Comparison.MatchArraysByPosition, own?.MatchArraysByPosition, v => Write(c => c.MatchArraysByPosition = v)));
        Options.Add(Option("Null is the same as missing", "An explicit null and an absent property are the same thing.", rules.Comparison.IgnoreNullVsMissing, own?.IgnoreNullVsMissing, v => Write(c => c.IgnoreNullVsMissing = v)));

        Fill(IgnoredPaths, rules.Comparison.IgnoredPaths.Select(
            p => new RuleEntryRowViewModel(p.Path, "never reported", p.Scope, p.SourceName, _level.Scope)));

        Fill(ArrayKeys, rules.Comparison.ArrayKeyOverrides.Value.Select(
            pair => new RuleEntryRowViewModel(
                pair.Key,
                $"matched by {pair.Value}",
                rules.Comparison.ArrayKeyOverrides.Scope,
                rules.Comparison.ArrayKeyOverrides.SourceName,
                _level.Scope)));

        Fill(Redactions, rules.Snapshot.Redact.Select(
            r => new RuleEntryRowViewModel(r.Path, $"→ {r.As}", r.Scope, r.SourceName, _level.Scope)));

        Fill(Normalisations, rules.Snapshot.Normalize.Select(
            r => new RuleEntryRowViewModel(r.Path, $"→ {r.As}", r.Scope, r.SourceName, _level.Scope)));

        Fill(Tolerances, rules.Tolerances.Select(
            t => new RuleEntryRowViewModel(
                t.Tolerance.Path, Describe(t.Tolerance), t.Scope, t.SourceName, _level.Scope)));

        SnapshotHeaders = rules.Snapshot.Headers.Value is { Count: > 0 } headers
            ? $"{string.Join(", ", headers)} · from {rules.Snapshot.Headers.SourceName}"
            : "None. A snapshot records the status and the body unless a level names headers to keep.";

        OnPropertyChanged(nameof(HasNoIgnoredPaths));
        OnPropertyChanged(nameof(HasNoArrayKeys));
        OnPropertyChanged(nameof(HasNoRedactions));
        OnPropertyChanged(nameof(HasNoNormalisations));
        OnPropertyChanged(nameof(HasNoTolerances));
    }

    private static void Fill(
        ObservableCollection<RuleEntryRowViewModel> target, IEnumerable<RuleEntryRowViewModel> rows)
    {
        target.Clear();
        foreach (var row in rows)
        {
            target.Add(row);
        }
    }

    private RuleOptionRowViewModel Option(
        string name, string explanation, Resolved<bool> resolved, bool? own, Action<bool?> write) =>
        new(name, explanation, resolved, own, write);

    /// <summary>What a tolerance allows, in words.</summary>
    public static string Describe(Tolerance tolerance)
    {
        ArgumentNullException.ThrowIfNull(tolerance);

        return tolerance.Kind switch
        {
            ToleranceKind.Numeric => $"within {Number(tolerance.Numeric)}",
            ToleranceKind.WithinSeconds => $"within {Number(tolerance.WithinSeconds)} s",
            ToleranceKind.Matches => $"matches {tolerance.Matches}",
            ToleranceKind.LengthWithinPercent => $"length within {Number(tolerance.LengthWithinPercent)}%",
            ToleranceKind.OneOf => $"one of {string.Join(", ", tolerance.OneOf ?? [])}",

            // Reported rather than guessed at: a rule stating none or several is not a rule, and it
            // forgives nothing at run time either (see ToleranceEvaluator).
            _ => "states no rule, or more than one - it allows nothing",
        };
    }

    private static string Number(double? value) =>
        (value ?? 0).ToString("0.####", CultureInfo.InvariantCulture);

    // ---- Editing ---------------------------------------------------------------------------------

    /// <summary>Gets this level's own comparison section, creating it on first write. Cleared again
    /// when the last override goes, so the file says what it overrides and nothing else.</summary>
    private void Write(Action<ComparisonSettings> edit)
    {
        var settings = _level.GetComparison() ?? new ComparisonSettings();
        edit(settings);

        // A list that adds and removes nothing is dropped rather than written empty: a level that says
        // nothing must LOOK like one, or the next reader takes it for a deliberate choice.
        if (settings.IgnoredPaths is { IsEmpty: true })
        {
            settings.IgnoredPaths = null;
        }

        _level.SetComparison(settings.IsEmpty ? null : settings);
        _level.Changed();
        _ = RefreshAsync();
    }

    [RelayCommand]
    private void AddIgnoredPath()
    {
        if (Clean(NewIgnoredPath) is not { } path)
        {
            return;
        }

        Write(c =>
        {
            var paths = c.IgnoredPaths ??= new InheritedPaths();

            // A path this level had previously stopped inheriting is un-removed rather than added
            // twice - otherwise the level would say both "remove $.id" and "add $.id".
            paths.Remove.RemoveAll(p => string.Equals(p, path, StringComparison.Ordinal));

            if (!paths.Add.Contains(path, StringComparer.Ordinal))
            {
                paths.Add.Add(path);
            }
        });

        NewIgnoredPath = "";
    }

    [RelayCommand]
    private void RemoveIgnoredPath(RuleEntryRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        Write(c =>
        {
            var paths = c.IgnoredPaths ??= new InheritedPaths();
            paths.Add.RemoveAll(p => string.Equals(p, row.Path, StringComparison.Ordinal));

            // Inherited: stopped HERE, by writing a removal, rather than by editing the level that
            // added it - which forty other endpoints are also reading.
            if (row.IsInherited && !paths.Remove.Contains(row.Path, StringComparer.Ordinal))
            {
                paths.Remove.Add(row.Path);
            }
        });
    }

    [RelayCommand]
    private void AddRedaction() => AddSnapshotRule(NewRedactPath, NewRedactAs, redact: true);

    [RelayCommand]
    private void AddNormalisation() => AddSnapshotRule(NewNormalisePath, NewNormaliseAs, redact: false);

    private void AddSnapshotRule(string path, string replacement, bool redact)
    {
        if (!SupportsSnapshotPolicy || Clean(path) is not { } cleaned)
        {
            return;
        }

        var stand = Clean(replacement) ?? (redact ? "<redacted>" : "<normalised>");

        WriteSnapshot(policy =>
        {
            var rules = redact ? policy.Redact ??= new InheritedRules() : policy.Normalize ??= new InheritedRules();

            rules.Remove.RemoveAll(p => string.Equals(p, cleaned, StringComparison.Ordinal));
            rules.Add.RemoveAll(r => string.Equals(r.Path, cleaned, StringComparison.Ordinal));
            rules.Add.Add(new SnapshotRule(cleaned, stand));
        });

        if (redact)
        {
            NewRedactPath = "";
        }
        else
        {
            NewNormalisePath = "";
        }
    }

    [RelayCommand]
    private void RemoveRedaction(RuleEntryRowViewModel? row) => RemoveSnapshotRule(row, redact: true);

    [RelayCommand]
    private void RemoveNormalisation(RuleEntryRowViewModel? row) => RemoveSnapshotRule(row, redact: false);

    private void RemoveSnapshotRule(RuleEntryRowViewModel? row, bool redact)
    {
        if (row is null || !SupportsSnapshotPolicy)
        {
            return;
        }

        WriteSnapshot(policy =>
        {
            var rules = redact ? policy.Redact ??= new InheritedRules() : policy.Normalize ??= new InheritedRules();

            rules.Add.RemoveAll(r => string.Equals(r.Path, row.Path, StringComparison.Ordinal));

            if (row.IsInherited && !rules.Remove.Contains(row.Path, StringComparer.Ordinal))
            {
                rules.Remove.Add(row.Path);
            }
        });
    }

    private void WriteSnapshot(Action<SnapshotPolicy> edit)
    {
        if (_level.GetSnapshot is not { } get || _level.SetSnapshot is not { } set)
        {
            return;
        }

        var policy = get() ?? new SnapshotPolicy();
        edit(policy);

        if (policy.Redact is { IsEmpty: true })
        {
            policy.Redact = null;
        }

        if (policy.Normalize is { IsEmpty: true })
        {
            policy.Normalize = null;
        }

        set(policy.IsEmpty ? null : policy);
        _level.Changed();
        _ = RefreshAsync();
    }

    [RelayCommand]
    private void AddTolerance()
    {
        if (Clean(NewTolerancePath) is not { } path)
        {
            return;
        }

        var tolerance = new Tolerance { Path = path };
        var value = NewToleranceValue.Trim();

        switch (NewToleranceKind)
        {
            case ToleranceKind.Numeric when Parse(value) is { } numeric:
                tolerance.Numeric = numeric;
                break;

            case ToleranceKind.WithinSeconds when Parse(value) is { } seconds:
                tolerance.WithinSeconds = seconds;
                break;

            case ToleranceKind.LengthWithinPercent when Parse(value) is { } percent:
                tolerance.LengthWithinPercent = percent;
                break;

            case ToleranceKind.Matches when value.Length > 0:
                tolerance.Matches = value;
                break;

            case ToleranceKind.OneOf when value.Length > 0:
                tolerance.OneOf = [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
                break;

            default:
                // Refused rather than written half-formed. A tolerance stating no rule forgives
                // nothing, so a silently-empty one would look like an allowance and behave like none.
                _statusLog.LogError($"\"{value}\" is not a value for that kind of tolerance.");
                return;
        }

        var tolerances = _level.GetTolerances() ?? [];

        // Per path, closest wins (spec §4.4) - so restating a path replaces this level's rule for it
        // rather than leaving two the resolver would have to choose between.
        tolerances.RemoveAll(t => string.Equals(t.Path, path, StringComparison.Ordinal));
        tolerances.Add(tolerance);

        _level.SetTolerances(tolerances);
        _level.Changed();

        NewTolerancePath = "";
        NewToleranceValue = "";
        _ = RefreshAsync();
    }

    /// <summary>
    /// Removes a tolerance written HERE. An inherited one cannot be removed at this level: tolerances
    /// are per-path closest-wins rather than add/remove, so there is nothing to write that means
    /// "allow nothing here" - and the honest answer is to change it where it was written.
    /// </summary>
    [RelayCommand]
    private void RemoveTolerance(RuleEntryRowViewModel? row)
    {
        if (row is null || row.IsInherited || _level.GetTolerances() is not { } tolerances)
        {
            return;
        }

        tolerances.RemoveAll(t => string.Equals(t.Path, row.Path, StringComparison.Ordinal));

        _level.SetTolerances(tolerances.Count == 0 ? null : tolerances);
        _level.Changed();
        _ = RefreshAsync();
    }

    private static string? Clean(string value) =>
        value.Trim() is { Length: > 0 } trimmed ? trimmed : null;

    private static double? Parse(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}
