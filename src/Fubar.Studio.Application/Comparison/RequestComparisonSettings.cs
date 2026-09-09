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
    Task<ResolvedRequestRules> ResolveRulesAsync(
        Workspace workspace,
        string requestPath,
        string? casePath = null,
        CancellationToken cancellationToken = default);

    /// <summary>Just the comparison options, for the panes that render them and have no verdict to
    /// reach.</summary>
    async Task<ResolvedComparisonSettings> ResolveAsync(
        Workspace workspace,
        string requestPath,
        CancellationToken cancellationToken = default) =>
        (await ResolveRulesAsync(workspace, requestPath, null, cancellationToken).ConfigureAwait(false))
        .Comparison;
}

/// <summary>
/// The two halves of a verdict, resolved together because they come from the same walk down the same
/// chain - and because a caller that got one without the other would compare with half the rules.
/// </summary>
public sealed record ResolvedRequestRules(
    ResolvedComparisonSettings Comparison,
    IReadOnlyList<ResolvedTolerance> Tolerances);

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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var layers = new List<ComparisonSettingsLayer>();
        var tolerances = new List<ToleranceLayer>();

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

        var chain = await _inheritance
            .GetInheritanceChainAsync(workspace.RootPath, requestPath, cancellationToken)
            .ConfigureAwait(false);

        layers.AddRange(chain.ComparisonLayers);
        tolerances.AddRange(chain.ToleranceLayers ?? []);

        var request = await _requests.LoadRequestAsync(requestPath, cancellationToken).ConfigureAwait(false);
        if (request.Comparison is { } own)
        {
            layers.Add(new ComparisonSettingsLayer(own, ComparisonScope.Request, "Request"));
        }

        if (request.Tolerances is { Count: > 0 } ownTolerances)
        {
            tolerances.Add(new ToleranceLayer(ownTolerances, ComparisonScope.Request, "Request"));
        }

        if (casePath is { Length: > 0 })
        {
            var endpointCase = await _endpoints.LoadCaseAsync(casePath, cancellationToken).ConfigureAwait(false);
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

        return new ResolvedRequestRules(
            ComparisonSettingsResolver.Resolve(layers),
            ToleranceResolver.Resolve(tolerances));
    }
}
