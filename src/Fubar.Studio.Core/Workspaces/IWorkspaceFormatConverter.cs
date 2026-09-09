using Fubar.Studio.Core.Models;

namespace Fubar.Studio.Core.Workspaces;

/// <param name="RequestPath">The <c>&lt;name&gt;.json</c> that will be split.</param>
/// <param name="EndpointDirectory">Where it will land.</param>
/// <param name="CaseName">The single case its invocation half becomes.</param>
/// <param name="MovesSnapshots">Whether it also has snapshots to carry across.</param>
public sealed record ConversionStep(
    string RequestPath,
    string EndpointDirectory,
    string CaseName,
    bool MovesSnapshots);

/// <param name="Blockers">Reasons the conversion cannot run at all. Non-empty means nothing is
/// touched: a half-converted workspace is worse than either format.</param>
public sealed record ConversionPlan(
    IReadOnlyList<ConversionStep> Steps,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Blockers)
{
    public bool CanRun => Blockers.Count == 0 && Steps.Count > 0;
}

/// <param name="BackupPath">Where the originals were copied before anything was moved.</param>
public sealed record ConversionResult(
    int EndpointsCreated,
    string BackupPath,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Converts a workspace from the requests format to endpoints and cases.
/// </summary>
/// <remarks>
/// <para>An explicit act, never automatic on open (spec §10.4). It rewrites committed files, and
/// springing that on someone about to read their diff is not a thing to do quietly.</para>
/// <para>It ships WITH the format rather than after it. Without it the two halves of the product
/// never converge: an old workspace would get no cases, no batches and no snapshots, with nothing but
/// hand-editing to close the gap.</para>
/// </remarks>
public interface IWorkspaceFormatConverter
{
    /// <summary>What would happen, without doing it. Shown before anything is written, because the
    /// answer to "is this what you meant" has to come before the rewrite, not after.</summary>
    ConversionPlan Preview(Workspace workspace);

    /// <summary>Backs the workspace up, splits every request, and stamps the new
    /// <see cref="WorkspaceFormat"/> into <c>fubar.json</c> last - so an interrupted conversion leaves
    /// a workspace that still opens in the format it was in.</summary>
    Task<ConversionResult> ConvertAsync(Workspace workspace, CancellationToken cancellationToken = default);
}
