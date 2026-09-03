namespace Fubar.Studio.Core.Running;

/// <summary>
/// Reported as a run proceeds, so a UI can fill a list in rather than showing a spinner for two minutes.
///
/// <para>Two events per step - <see cref="Starting"/> before the send and <see cref="Finished"/> after -
/// because a request that hangs is exactly the one a reader most wants named, and a progress model that
/// only reports completions shows nothing at all for the whole time it is stuck.</para>
/// </summary>
public sealed record RunProgress(RunStep Step, int Total, StepReport? Report)
{
    public bool IsStarting => Report is null;

    public static RunProgress Starting(RunStep step, int total) => new(step, total, null);

    public static RunProgress Finished(StepReport report, int total) => new(report.Step, total, report);
}
