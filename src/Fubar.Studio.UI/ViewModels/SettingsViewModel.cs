using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Settings;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>
/// API Studio's settings.
///
/// <para>There was no settings window at all. The theme was reachable only after it was bound to a
/// control, the eight comparison defaults only by opening a response comparison and choosing to save
/// at "global" level - the top of a three-level hierarchy, reachable from inside a dialog about one
/// request - and the request/history limits not at all, being constants.</para>
///
/// <para>Grouped to match the model. Applied on Save rather than as each control changes: several of
/// these govern what a request sends, and half-applied settings during a send is a class of bug worth
/// not having.</para>
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly IAppSettingsService _settings;
    private readonly IMachinePolicyService? _policy;

    /// <summary>The policy is optional: without one nothing is imposed, which is what every
    /// installation without a policy file gets.</summary>
    public SettingsViewModel(IAppSettingsService settings, IMachinePolicyService? policy = null)
    {
        _settings = settings;
        _policy = policy;

        var current = settings.Load();

        Theme = current.Appearance.Theme;
        DefaultTimeoutSeconds = current.Requests.DefaultTimeoutSeconds;
        MaxResponseMegabytes = current.Requests.MaxResponseMegabytes;
        HistoryEnabled = current.History.Enabled;
        MaxHistoryEntries = current.History.MaxEntriesPerRequest;
        MaxHistoryBodyKilobytes = current.History.MaxResponseBodyKilobytes;

        var comparison = current.Comparison ?? new ComparisonSettings();
        IgnoreWhitespace = comparison.IgnoreWhitespace ?? false;
        IgnoreCase = comparison.IgnoreCase ?? false;
        NormalizeStructure = comparison.NormalizeStructure ?? false;
        ReportPropertyOrder = comparison.ReportPropertyOrder ?? false;
        MatchArraysByPosition = comparison.MatchArraysByPosition ?? false;
        IgnoreNullVsMissing = comparison.IgnoreNullVsMissing ?? false;
    }

    // --- appearance ---------------------------------------------------------------------------

    [ObservableProperty]
    public partial string Theme { get; set; }

    public static string[] ThemeOptions { get; } = ["System", "Dark", "Light"];

    // --- requests -----------------------------------------------------------------------------

    [ObservableProperty]
    public partial int DefaultTimeoutSeconds { get; set; }

    [ObservableProperty]
    public partial int MaxResponseMegabytes { get; set; }

    // --- history ------------------------------------------------------------------------------

    [ObservableProperty]
    public partial bool HistoryEnabled { get; set; }

    [ObservableProperty]
    public partial int MaxHistoryEntries { get; set; }

    [ObservableProperty]
    public partial int MaxHistoryBodyKilobytes { get; set; }

    /// <summary>
    /// Named so someone can see WHY a limit is not theirs to change - the same "say where this value
    /// came from" the comparison tooltips already do. Null when no policy is in force.
    /// </summary>
    public string? PolicyNote =>
        _policy?.Current.MaxHistoryEntries is { } capped
            ? $"This machine's Fubar policy caps history at {capped} entries."
            : null;

    public bool HasPolicyNote => PolicyNote is not null;

    // --- comparison defaults ------------------------------------------------------------------
    //
    // The top of the global -> folder -> request hierarchy. A workspace, folder or request can still
    // override any one of these and keep inheriting the rest.

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

    /// <summary>Raised after a successful save, so the shell can close the window and re-apply.</summary>
    public event System.Action? Saved;

    [RelayCommand]
    private async Task SaveAsync()
    {
        // Load-merge-save, never a fresh AppSettings: the session state (which workspaces were open)
        // lives in this same file and a bare new object would wipe it.
        var settings = _settings.Load();

        settings.Appearance.Theme = Theme;

        // Clamped rather than validated-and-refused: a timeout of zero or a negative cap is a slip, and
        // refusing to save the whole page over one is a worse answer than quietly using the nearest
        // sane value.
        settings.Requests.DefaultTimeoutSeconds = System.Math.Clamp(DefaultTimeoutSeconds, 1, 3600);
        settings.Requests.MaxResponseMegabytes = System.Math.Clamp(MaxResponseMegabytes, 1, 2048);

        settings.History.Enabled = HistoryEnabled;
        settings.History.MaxEntriesPerRequest = System.Math.Clamp(MaxHistoryEntries, 1, 10_000);
        // Zero is meaningful here and is NOT clamped away: it keeps the timing and status of every
        // execution and no payloads at all, which is the option someone who does not want response
        // bodies on disk actually needs.
        settings.History.MaxResponseBodyKilobytes = System.Math.Clamp(MaxHistoryBodyKilobytes, 0, 65_536);

        var comparison = new ComparisonSettings
        {
            IgnoreWhitespace = IgnoreWhitespace ? true : null,
            IgnoreCase = IgnoreCase ? true : null,
            NormalizeStructure = NormalizeStructure ? true : null,
            ReportPropertyOrder = ReportPropertyOrder ? true : null,
            MatchArraysByPosition = MatchArraysByPosition ? true : null,
            IgnoreNullVsMissing = IgnoreNullVsMissing ? true : null,
            IgnoredPaths = settings.Comparison?.IgnoredPaths,
            ArrayKeyOverrides = settings.Comparison?.ArrayKeyOverrides,
        };

        // Null rather than an object full of nulls, so a file shows what is actually set. An unticked
        // box means "no global opinion", which is what lets a folder or request decide instead - it is
        // not the same as globally setting it to false.
        settings.Comparison = comparison.IsEmpty ? null : comparison;

        await _settings.SaveAsync(settings);
        Saved?.Invoke();
    }
}
