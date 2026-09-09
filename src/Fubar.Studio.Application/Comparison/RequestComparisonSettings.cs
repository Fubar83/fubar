using Fubar.Studio.Core.Snapshots;
using Fubar.Studio.Core.Comparison;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Settings;
using Fubar.Studio.Core.Workspaces;

namespace Fubar.Studio.Application.Comparison;

/// <summary>
/// The comparison rules in force for one request: global, then its folders, then its own overrides.
/// </summary>
/// <remarks>
/// One implementation of the chain, shared by everything that judges. The request editor's dialog
/// built its own, the environment-comparison window built a second, and the runner would have been a
/// third - three places that must agree about what applies to a request, kept in step by nothing.
/// </remarks>
public interface IRequestComparisonSettings
{
    /// <summary>Everything the chain says about judging this request: what counts as a difference,
    /// and which differences are allowed.</summary>
    /// <param name="casePath">The <c>cases/&lt;name&gt;.json</c> being run, when there is one. The
    /// innermost level, applied after the endpoint's own - null in the requests format and for an
    /// endpoint sent as it stands.</param>
    /// <param name="overlay">The batch's own rules, applied AFTER the chain resolves. A batch is not
    /// a level of the hierarchy - it cuts across the tree, so folding it in would make an endpoint's
    /// settings depend on which list happened to name it.</param>
    Task<ResolvedRequestRules> ResolveRulesAsync(
        Workspace workspace,
        string requestPath,
        string? casePath = null,
        BatchOverlay? overlay = null,
        CancellationToken cancellationToken = default);

    /// <summary>Just the comparison options, for the panes that render them and have no verdict to
    /// reach.</summary>
    async Task<ResolvedComparisonSettings> ResolveAsync(
        Workspace workspace,
        string requestPath,
        CancellationToken cancellationToken = default) =>
        (await ResolveRulesAsync(workspace, requestPath, null, null, cancellationToken).ConfigureAwait(false))
        .Comparison;
}

/// <summary>
/// Everything the chain says about judging one request, resolved in a single walk - and together,
/// because a caller that got one part without the others would judge with a fraction of the rules.
/// </summary>
/// <param name="Snapshot">
/// What to redact and normalise. Needed on the READ side as well as the write side: a snapshot is
/// stored with <c>"generatedAt": "&lt;timestamp&gt;"</c>, so a live response carrying the real value
/// differs from it on that field every single run unless the same rules are applied to both. That
/// makes normalisation useless and is exactly the bug it exists to prevent.
/// </param>
public sealed record ResolvedRequestRules(
    ResolvedComparisonSettings Comparison,
    IReadOnlyList<ResolvedTolerance> Tolerances,
    ResolvedSnapshotPolicy Snapshot);

/// <inheritdoc cref="IRequestComparisonSettings"/>
public sealed class RequestComparisonSettings : IRequestComparisonSettings
{
    private readonly IAppSettingsService _appSettings;
    private readonly IInheritanceResolver _inheritance;
    private readonly IRequestStore _requests;
    private readonly IEndpointStore _endpoints;

    public RequestComparisonSettings(
        IAppSettingsService appSettings,
        IInheritanceResolver inheritance,
        IRequestStore requests,
        IEndpointStore endpoints)
    {
        _appSettings = appSettings;
        _inheritance = inheritance;
        _requests = requests;
        _endpoints = endpoints;
    }

    public async Task<ResolvedRequestRules> ResolveRulesAsync(
        Workspace workspace,
        string requestPath,
        string? casePath = null,
        BatchOverlay? overlay = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var layers = new List<ComparisonSettingsLayer>();
        var tolerances = new List<ToleranceLayer>();
        var snapshot = new List<SnapshotPolicyLayer>();

        // Read fresh rather than cached: this runs once per comparison, not per keystroke, and another
        // window may have changed the global defaults since the run started.
        var app = await _appSettings.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (app.Comparison is { } global)
        {
            layers.Add(new ComparisonSettingsLayer(global, ComparisonScope.Global, "Global"));
        }

        if (app.Tolerances is { Count: > 0 } globalTolerances)
        {
            tolerances.Add(new ToleranceLayer(globalTolerances, ComparisonScope.Global, "Global"));
        }

        if (app.Snapshot is { } globalSnapshot)
        {
            snapshot.Add(new SnapshotPolicyLayer(globalSnapshot, ComparisonScope.Global, "Global"));
        }

        var chain = await _inheritance
            .GetInheritanceChainAsync(workspace.RootPath, requestPath, cancellationToken)
            .ConfigureAwait(false);

        layers.AddRange(chain.ComparisonLayers);
        tolerances.AddRange(chain.ToleranceLayers ?? []);
        snapshot.AddRange(chain.SnapshotLayers ?? []);

        var request = await _requests.LoadRequestAsync(requestPath, cancellationToken).ConfigureAwait(false);
        if (request.Comparison is { } own)
        {
            layers.Add(new ComparisonSettingsLayer(own, ComparisonScope.Request, "Request"));
        }

        if (request.Tolerances is { Count: > 0 } ownTolerances)
        {
            tolerances.Add(new ToleranceLayer(ownTolerances, ComparisonScope.Request, "Request"));
        }

        if (request.Snapshot is { } ownSnapshot)
        {
            snapshot.Add(new SnapshotPolicyLayer(ownSnapshot, ComparisonScope.Request, "Request"));
        }

        if (casePath is { Length: > 0 }
            && await TryLoadCaseAsync(casePath, cancellationToken).ConfigureAwait(false) is { } endpointCase)
        {
            var sourceName = $"Case: {endpointCase.Name}";

            if (endpointCase.Comparison is { } caseComparison)
            {
                layers.Add(new ComparisonSettingsLayer(caseComparison, ComparisonScope.Case, sourceName));
            }

            if (endpointCase.Tolerances is { Count: > 0 } caseTolerances)
            {
                tolerances.Add(new ToleranceLayer(caseTolerances, ComparisonScope.Case, sourceName));
            }
        }

        // Last, and outside the chain: the occasion's rules beat every containment level, and read as
        // "Batch: smoke" wherever provenance is shown.
        if (overlay is { IsEmpty: false })
        {
            var sourceName = "Batch";

            if (overlay.Comparison is { } batchComparison)
            {
                layers.Add(new ComparisonSettingsLayer(batchComparison, ComparisonScope.Batch, sourceName));
            }

            if (overlay.Tolerances is { Count: > 0 } batchTolerances)
            {
                tolerances.Add(new ToleranceLayer(batchTolerances, ComparisonScope.Batch, sourceName));
            }
        }

        return new ResolvedRequestRules(
            ComparisonSettingsResolver.Resolve(layers),
            ToleranceResolver.Resolve(tolerances),
            SnapshotPolicyResolver.Resolve(snapshot));
    }

    /// <summary>
    /// The case at <paramref name="casePath"/>, or null when there is no file there yet.
    /// </summary>
    /// <remarks>
    /// A case that has not been saved is a legitimate state - the Rules tab opens on one the moment
    /// "New case" is chosen - and it simply contributes no rules of its own; everything above it still
    /// applies. Only "not there" is forgiven: a file that exists and cannot be read still throws,
    /// because that is a real problem and silently judging with a fraction of the rules is the failure
    /// this whole type exists to prevent.
    /// </remarks>
    private async Task<EndpointCase?> TryLoadCaseAsync(string casePath, CancellationToken cancellationToken)
    {
        try
        {
            return await _endpoints.LoadCaseAsync(casePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }
}
