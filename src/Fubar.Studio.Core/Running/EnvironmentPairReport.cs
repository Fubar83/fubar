namespace Fubar.Studio.Core.Running;

/// <summary>One request, answered by both environments.</summary>
/// <param name="Left">The left (usually the reference) environment's answer.</param>
/// <param name="Right">The right environment's answer.</param>
public sealed record StepPair(RunStep Step, StepReport Left, StepReport Right)
{
    /// <summary>Both sides answered, so there is something to compare.</summary>
    public bool BothAnswered => Left.StatusCode is not null && Right.StatusCode is not null;

    /// <summary>The two status codes disagree - worth saying on its own, because it is the difference
    /// nobody needs a diff to care about.</summary>
    public bool StatusDiffers => Left.StatusCode != Right.StatusCode;

    /// <summary>
    /// The bodies are character-for-character the same.
    ///
    /// <para>A sound shortcut in ONE direction only: identical text is identical data, so a true here
    /// settles it without running a comparison. False settles nothing - two responses that differ only
    /// in key order, whitespace or a field the request is configured to ignore are still equal in every
    /// sense this feature cares about, and only the comparison engine can say so.</para>
    /// </summary>
    public bool BodiesIdentical =>
        Left.ResponseBody is { } left && Right.ResponseBody is { } right &&
        string.Equals(left, right, StringComparison.Ordinal);

    /// <summary>Neither side can be compared: one of them carried no body, because it errored, because
    /// the body was too large, or because the run was not asked to keep bodies.</summary>
    public bool NotComparable => Left.ResponseBody is null || Right.ResponseBody is null;
}

/// <summary>How far a paired run has got. Reported twice per request: once with the left side in and the
/// right still running, once with both.</summary>
public sealed record StepPairProgress(RunStep Step, int Total, StepReport? Left, StepReport? Right)
{
    public static StepPairProgress Starting(RunStep step, int total) => new(step, total, null, null);

    public static StepPairProgress LeftDone(RunStep step, int total, StepReport left) =>
        new(step, total, left, null);

    public static StepPairProgress Complete(StepPair pair, int total) =>
        new(pair.Step, total, pair.Left, pair.Right);

    /// <summary>The pair is finished and can be compared.</summary>
    public StepPair? Pair => Left is not null && Right is not null ? new StepPair(Step, Left, Right) : null;
}

/// <summary>
/// The result of running one collection against two environments.
///
/// <para>Each side is also available as an ordinary <see cref="RunReport"/>, so everything that already
/// reads one - the summary line, the JUnit writer, the verdict rules - works on either half without a
/// second implementation.</para>
/// </summary>
public sealed record EnvironmentPairReport(
    string LeftEnvironment,
    string RightEnvironment,
    IReadOnlyList<StepPair> Pairs,
    long ElapsedMilliseconds,
    bool WasCancelled,
    bool StoppedEarly)
{
    public static readonly EnvironmentPairReport Empty =
        new("", "", [], 0, false, false);

    public int Total => Pairs.Count;

    public RunReport Left =>
        new([.. Pairs.Select(p => p.Left)], ElapsedMilliseconds, WasCancelled, StoppedEarly);

    public RunReport Right =>
        new([.. Pairs.Select(p => p.Right)], ElapsedMilliseconds, WasCancelled, StoppedEarly);

    /// <summary>Pairs whose two sides disagree on the status code. Not the same question as whether
    /// their bodies differ, and usually the more urgent one.</summary>
    public IReadOnlyList<StepPair> StatusMismatches => [.. Pairs.Where(p => p.StatusDiffers)];
}
