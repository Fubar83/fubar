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
    Task<ResolvedComparisonSettings> ResolveAsync(
        Workspace workspace,
        string requestPath,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IRequestComparisonSettings"/>
public sealed class RequestComparisonSettings : IRequestComparisonSettings
{
    private readonly IAppSettingsService _appSettings;
    private readonly IInheritanceResolver _inheritance;
    private readonly IRequestStore _requests;

    public RequestComparisonSettings(
        IAppSettingsService appSettings,
        IInheritanceResolver inheritance,
        IRequestStore requests)
    {
        _appSettings = appSettings;
        _inheritance = inheritance;
        _requests = requests;
    }

    public async Task<ResolvedComparisonSettings> ResolveAsync(
        Workspace workspace,
        string requestPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var layers = new List<ComparisonSettingsLayer>();

        // Read fresh rather than cached: this runs once per comparison, not per keystroke, and another
        // window may have changed the global defaults since the run started.
        var app = await _appSettings.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (app.Comparison is { } global)
        {
            layers.Add(new ComparisonSettingsLayer(global, ComparisonScope.Global, "Global"));
        }

        var chain = await _inheritance
            .GetInheritanceChainAsync(workspace.RootPath, requestPath, cancellationToken)
            .ConfigureAwait(false);

        layers.AddRange(chain.ComparisonLayers);

        var request = await _requests.LoadRequestAsync(requestPath, cancellationToken).ConfigureAwait(false);
        if (request.Comparison is { } own)
        {
            layers.Add(new ComparisonSettingsLayer(own, ComparisonScope.Request, "Request"));
        }

        return ComparisonSettingsResolver.Resolve(layers);
    }
}
