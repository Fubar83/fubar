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

    /// <summary>
    /// Everything the chain says at a FOLDER - the global defaults and every folder from the
    /// workspace root down to and including this one. Nothing below it contributes.
    /// </summary>
    Task<ResolvedRequestRules> ResolveFolderRulesAsync(
        Workspace workspace,
        string folderPath,
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

    /// <summary>
    /// Everything the chain says at a FOLDER - the global defaults and every folder from the
    /// workspace root down to and including this one.
    /// </summary>
    /// <remarks>
    /// The chain is anchored on the folder's own <c>_folder.json</c> because
    /// <c>GetInheritanceChainAsync</c> walks up from a file's PARENT: naming the file inside the
    /// folder is what makes the folder itself the innermost level rather than the one above it.
    /// Nothing below a folder contributes - a request's own rules are not in force "at" the folder.
    /// </remarks>
    public async Task<ResolvedRequestRules> ResolveFolderRulesAsync(
        Workspace workspace,
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var (layers, tolerances, snapshot) = await ChainAsync(
            workspace,
            Path.Combine(folderPath, FolderConfigFileName),
            cancellationToken).ConfigureAwait(false);

        return new ResolvedRequestRules(
            ComparisonSettingsResolver.Resolve(layers),
            ToleranceResolver.Resolve(tolerances),
            SnapshotPolicyResolver.Resolve(snapshot));
    }

    /// <summary>The file a folder's own settings live in - the anchor a folder's chain is walked
    /// from.</summary>
    public const string FolderConfigFileName = "_folder.json";

    /// <summary>The global defaults plus every folder above <paramref name="anchorPath"/>, which is
    /// the part both entry points share.</summary>
    private async Task<(List<ComparisonSettingsLayer> Comparison, List<ToleranceLayer> Tolerances,
        List<SnapshotPolicyLayer> Snapshot)> ChainAsync(
        Workspace workspace, string anchorPath, CancellationToken cancellationToken)
    {
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
            .GetInheritanceChainAsync(workspace.RootPath, anchorPath, cancellationToken)
            .ConfigureAwait(false);

        layers.AddRange(chain.ComparisonLayers);
        tolerances.AddRange(chain.ToleranceLayers ?? []);
        snapshot.AddRange(chain.SnapshotLayers ?? []);

        return (layers, tolerances, snapshot);
    }

    public async Task<ResolvedRequestRules> ResolveRulesAsync(
        Workspace workspace,
        string requestPath,
        string? casePath = null,
        BatchOverlay? overlay = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var (layers, tolerances, snapshot) =
            await ChainAsync(workspace, requestPath, cancellationToken).ConfigureAwait(false);

        // A request that has not been saved yet contributes nothing of its own - see TryLoadAsync.
        var request = await TryLoadAsync(
            () => _requests.LoadRequestAsync(requestPath, cancellationToken)).ConfigureAwait(false);

        if (request?.Comparison is { } own)
        {
            layers.Add(new ComparisonSettingsLayer(own, ComparisonScope.Request, "Request"));
        }

        if (request?.Tolerances is { Count: > 0 } ownTolerances)
        {
            tolerances.Add(new ToleranceLayer(ownTolerances, ComparisonScope.Request, "Request"));
        }

        if (request?.Snapshot is { } ownSnapshot)
        {
            snapshot.Add(new SnapshotPolicyLayer(ownSnapshot, ComparisonScope.Request, "Request"));
        }

        if (casePath is { Length: > 0 }
            && await TryLoadAsync(() => _endpoints.LoadCaseAsync(casePath, cancellationToken))
                .ConfigureAwait(false) is { } endpointCase)
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
    /// The document, or null when there is no file there yet.
    /// </summary>
    /// <remarks>
    /// A request or case that has not been saved is a legitimate state - the Rules tab opens on one
    /// the moment "New endpoint" or "New case" is chosen - and it simply contributes no rules of its
    /// own; everything above it still applies. Only "not there" is forgiven: a file that exists and
    /// cannot be read still throws, because that is a real problem and silently judging with a
    /// fraction of the rules is the failure this whole type exists to prevent.
    /// </remarks>
    private static async Task<T?> TryLoadAsync<T>(Func<Task<T>> load)
        where T : class
    {
        try
        {
            return await load().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }
}
