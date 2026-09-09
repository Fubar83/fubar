using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Running;

namespace Fubar.Studio.Application.Running;

/// <summary>
/// Compares a response against the same call made to a second environment.
/// </summary>
/// <remarks>
/// <para>"Does staging still agree with production?" as a single run, and the reason the oracle seam
/// exists at all: a snapshot and another environment differ only in where the other side comes from,
/// so both end at the same comparer and the same verdict.</para>
/// <para>It sends the OTHER side through the ordinary runner, one step at a time, rather than
/// duplicating how a step is built. Auth acquisition, the 401 retry, variable resolution and captures
/// are then identical on both sides by construction - which matters more here than anywhere, because
/// any difference in how the two sides were sent would be reported as a difference between the two
/// environments.</para>
/// <para>Interleaved, one step at a time, rather than running the whole collection twice: the first
/// comparable pair is then complete after two sends instead of after the last request of the first
/// run.</para>
/// </remarks>
public sealed class EnvironmentOracle : IOracle
{
    private readonly ICollectionRunService _runner;
    private readonly WorkspaceEnvironment? _other;

    public EnvironmentOracle(ICollectionRunService runner, WorkspaceEnvironment? other)
    {
        _runner = runner;
        _other = other;
    }

    public OracleKind Kind => OracleKind.Environment;

    /// <summary>The environment this compares against, for the report's "compared against" column.</summary>
    public string OtherName => _other?.Name ?? "No environment";

    public async Task<OtherSide> ObtainAsync(OracleContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // A step that never answered has nothing to compare, and sending the other side anyway would
        // report a missing response as an environment difference.
        if (context.Primary.ResponseBody is null)
        {
            return OtherSide.NotApplicable;
        }

        var report = await _runner
            .RunAsync(
                // No oracle on the inner run - it is the other SIDE, not another comparison. Passing
                // one would recurse.
                new CollectionRun(
                    new RunPlan([context.Step with { Order = 1 }]),
                    context.Workspace,
                    _other,
                    RunOptions.Default with { CaptureResponseBodies = true }),
                progress: null,
                cancellationToken)
            .ConfigureAwait(false);

        if (report.Steps.Count == 0)
        {
            return OtherSide.Missing($"{OtherName} was not sent to.");
        }

        var step = report.Steps[0];

        return step.ResponseBody is { } body
            ? OtherSide.From(body, OtherName)
            : OtherSide.Missing($"{OtherName} did not answer: {step.Error ?? "no response body"}");
    }
}
