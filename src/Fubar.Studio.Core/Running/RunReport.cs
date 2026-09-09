using Fubar.Studio.Core.Testing;

namespace Fubar.Studio.Core.Running;

/// <summary>Why a step is not simply "sent and answered".</summary>
public enum StepStatus
{
    /// <summary>A response arrived and every assertion on it passed (including the case of none).</summary>
    Passed,

    /// <summary>A response arrived and at least one assertion failed.</summary>
    Failed,

    /// <summary>No response: a transport error, a bad URL, a timeout, or auth that could not be acquired.</summary>
    Errored,

    /// <summary>Never sent - the run stopped before reaching it, or was cancelled.</summary>
    Skipped,
}

/// <summary>What comparing a step's response concluded, when something was comparing.</summary>
public enum ComparisonVerdict
{
    /// <summary>No oracle, or the step never answered. Not a judgement.</summary>
    NotCompared,

    /// <summary>Compared, and nothing survived this request's rules.</summary>
    Same,

    /// <summary>Compared, and something did.</summary>
    Differs,

    /// <summary>There was meant to be something to compare against and there was not - no snapshot
    /// recorded, most often. Reported as its own outcome and never as a pass.</summary>
    Unavailable,
}

/// <summary>What one request did during a run.</summary>
/// <param name="StatusCode">Null when no response arrived.</param>
/// <param name="Error">The transport/auth failure, or the reason a capture could not be applied.</param>
public sealed record StepReport(
    RunStep Step,
    StepStatus Status,
    int? StatusCode,
    string? ReasonPhrase,
    long ElapsedMilliseconds,
    long SizeBytes,
    IReadOnlyList<AssertionResult> Assertions,
    IReadOnlyList<CaptureResult> Captures,
    string? Error)
{
    /// <summary>
    /// The largest response this will carry for comparison, in characters (~4 MB of UTF-16).
    ///
    /// <para>A body over the cap is dropped and <see cref="BodyTooLargeToCompare"/> is set, rather than
    /// truncated. Truncating would be worse than useless here: two responses cut at the same length
    /// compare as identical past the cut, and two cut at different lengths differ at the cut - so a
    /// truncated body produces a confident answer to a question it cannot see the whole of.</para>
    /// </summary>
    public const int MaxComparableBodyChars = 4 * 1024 * 1024;

    /// <summary>
    /// The response body, kept only when <see cref="RunOptions.CaptureResponseBodies"/> asked for it -
    /// otherwise null, because an ordinary run has no use for thirty bodies held in memory for as long
    /// as its report lives.
    /// </summary>
    public string? ResponseBody { get; init; }

    /// <summary>The response's Content-Type, so a reader knows what it is looking at without sniffing
    /// the body.</summary>
    public string? ContentType { get; init; }

    /// <summary>The response arrived but was too big to carry (see <see cref="MaxComparableBodyChars"/>).
    /// Distinct from a null body with this false, which means nothing asked for the body at all.</summary>
    public bool BodyTooLargeToCompare { get; init; }

    /// <summary>
    /// What comparing this response against the oracle's other side concluded.
    ///
    /// <para>A SECOND axis, deliberately, rather than more values on <see cref="StepStatus"/>. "The
    /// request ran and its assertions passed" and "the answer matches what it should" are different
    /// questions with different answers, and a step can legitimately pass every assertion while
    /// differing from its snapshot. One enum would have to pick a winner and lose the other.</para>
    /// </summary>
    public ComparisonVerdict Comparison { get; init; } = ComparisonVerdict.NotCompared;

    /// <summary>Differences the rules did not excuse. Zero unless <see cref="Comparison"/> is
    /// <see cref="ComparisonVerdict.Differs"/>.</summary>
    public int DifferenceCount { get; init; }

    /// <summary>Which other side this was judged against - "snapshots/staging.json", "Production".
    /// Reported on every step: a run that switched from the shared snapshot to a per-environment one
    /// someone recorded last week is a run whose green means something different.</summary>
    public string? ComparedAgainst { get; init; }

    /// <summary>Why there was nothing to compare against. Never a reason to pass.</summary>
    public string? ComparisonUnavailableReason { get; init; }

    /// <summary>Differences a tolerance forgave. Reported rather than folded into the green: a step
    /// that matched and a step that was within tolerance in six fields are different facts, and the
    /// second is the one worth looking at when a tolerance turns out to be too generous.</summary>
    public int ToleratedCount { get; init; }

    /// <summary>Rules that could not be applied - a tolerance stating no allowance, or one that met a
    /// text comparison with no fields to name. A rule that silently does nothing reads as a check.</summary>
    public IReadOnlyList<string> ComparisonWarnings { get; init; } = [];

    public int AssertionsPassed => Assertions.Count(a => a.Passed);

    public int AssertionsFailed => Assertions.Count(a => !a.Passed);

    /// <summary>A response that arrived but was not a 2xx. Never on its own a failure (see
    /// <see cref="RunReport"/>), but always worth showing: it is the commonest thing a reader wants to
    /// spot in a list of thirty green rows.</summary>
    public bool IsUnexpectedStatus => StatusCode is { } code && (code < 200 || code >= 300);

    public static StepReport SkippedStep(RunStep step) =>
        new(step, StepStatus.Skipped, null, null, 0, 0, [], [], null);
}

/// <summary>
/// The result of a whole run.
///
/// <para><b>An HTTP status never fails a run on its own - only an assertion or a transport error does.</b>
/// This is the load-bearing decision in the whole feature and it is not the obvious one, so: this app
/// lets you assert <c>StatusCode Equals 404</c> deliberately, which a runner that also treated 4xx as
/// failure would contradict - the same response would be both the expected result and a failure, and
/// one of the two answers would have to win silently. Deciding for the user which statuses are bad is
/// exactly the job assertions exist to do explicitly, so the runner does not also do it implicitly.</para>
///
/// <para>The cost is that a request with no assertions is judged only on whether it got an answer, so a
/// collection with no assertions at all can return 500s and still pass. That is why
/// <see cref="StepReport.IsUnexpectedStatus"/> exists and why <see cref="UnexpectedStatuses"/> is
/// surfaced beside the verdict rather than folded into it: the run does not fail, and the reader is
/// still told. A verdict that quietly disagreed with the assertions would be worse than one that needs
/// a sentence of explanation.</para>
/// </summary>
public sealed record RunReport(
    IReadOnlyList<StepReport> Steps,
    long ElapsedMilliseconds,
    bool WasCancelled,
    bool StoppedEarly)
{
    public static readonly RunReport Empty = new([], 0, false, false);

    /// <summary>
    /// The steps the verdict is about - everything except cleanup.
    /// </summary>
    /// <remarks>
    /// Every count below is over these rather than over <see cref="Steps"/>, because a teardown step
    /// is not part of what was being tested (see <see cref="RunStep.IsTeardown"/>). It is still in
    /// <see cref="Steps"/>, so it appears in the report and on the console like anything else - it
    /// just cannot turn a passing run red or a failing one green.
    /// </remarks>
    public IReadOnlyList<StepReport> Judged { get; } = [.. Steps.Where(s => !s.Step.IsTeardown)];

    /// <summary>The cleanup steps, in the order they ran.</summary>
    public IReadOnlyList<StepReport> Cleanup { get; } = [.. Steps.Where(s => s.Step.IsTeardown)];

    public int Total => Judged.Count;

    public int Passed => Judged.Count(s => s.Status == StepStatus.Passed);

    public int Failed => Judged.Count(s => s.Status == StepStatus.Failed);

    public int Errored => Judged.Count(s => s.Status == StepStatus.Errored);

    public int Skipped => Judged.Count(s => s.Status == StepStatus.Skipped);

    /// <summary>Cleanup that did not do its job. Never part of the verdict, and never silent either -
    /// a leak nobody hears about is the thing teardown exists to prevent.</summary>
    public int CleanupFailed =>
        Cleanup.Count(s => s.Status is StepStatus.Failed or StepStatus.Errored or StepStatus.Skipped);

    public int AssertionsPassed => Judged.Sum(s => s.AssertionsPassed);

    public int AssertionsFailed => Judged.Sum(s => s.AssertionsFailed);

    /// <summary>Requests that answered with a non-2xx and had no assertion to judge it. Reported, never
    /// counted against the verdict - see the type remarks.</summary>
    public IReadOnlyList<StepReport> UnexpectedStatuses =>
        [.. Judged.Where(s => s.IsUnexpectedStatus && s.Assertions.Count == 0)];

    /// <summary>
    /// True when something ran, nothing failed, nothing errored, and the run actually finished.
    ///
    /// <para>Two of those clauses are there to refuse a green that would be a lie. A CANCELLED run did
    /// not answer the question that was asked, and reporting green for a run stopped half way is how a
    /// runner stops being believed. An EMPTY run is the same trap in its most familiar form - "no tests
    /// ran, so it passed" - and it is reachable here by ordinary means: a name filter with a typo in
    /// it, or a folder whose requests have not been saved yet, both produce zero steps and would
    /// otherwise report success.</para>
    /// </summary>
    /// <para>The comparison clauses are the same refusal in the oracle's terms: a step that DIFFERS
    /// from its snapshot has answered the question wrongly, and one whose snapshot is missing has not
    /// answered it at all. Neither is a pass, and "nothing to compare, therefore fine" is exactly how a
    /// suite stops testing without anyone noticing.</para>
    public bool Ok =>
        Total > 0 && Failed == 0 && Errored == 0 && !WasCancelled && Skipped == 0
        && Differing == 0 && Uncomparable == 0;

    /// <summary>Steps whose response did not match what it was compared against.</summary>
    public int Differing => Judged.Count(s => s.Comparison == ComparisonVerdict.Differs);

    /// <summary>Steps that were meant to be compared and had nothing to compare against.</summary>
    public int Uncomparable => Judged.Count(s => s.Comparison == ComparisonVerdict.Unavailable);

    /// <summary>Steps that matched only because a tolerance forgave something. Said out loud, so a
    /// rule that turned out to be too generous is visible in the green rather than only in the file.</summary>
    public int Tolerated => Judged.Count(s => s.ToleratedCount > 0);

    /// <summary>One line for a status bar or a CI log.</summary>
    public string Summary()
    {
        if (Total == 0)
        {
            return "Nothing to run.";
        }

        var parts = new List<string> { $"{Passed}/{Total} passed" };
        if (Failed > 0) parts.Add($"{Failed} failed");
        if (Differing > 0) parts.Add($"{Differing} differ");
        if (Uncomparable > 0) parts.Add($"{Uncomparable} not comparable");
        if (Tolerated > 0) parts.Add($"{Tolerated} within tolerance");
        if (Errored > 0) parts.Add($"{Errored} errored");
        if (Skipped > 0) parts.Add($"{Skipped} skipped");
        if (AssertionsFailed > 0) parts.Add($"{AssertionsFailed} assertion{(AssertionsFailed == 1 ? "" : "s")} failed");

        // Never folded into the verdict, and never left out either: cleanup that did not run is a
        // leak, and a leak nobody hears about is exactly what teardown exists to prevent.
        if (CleanupFailed > 0) parts.Add($"{CleanupFailed} cleanup step{(CleanupFailed == 1 ? "" : "s")} did not finish");

        var suffix = WasCancelled ? " (cancelled)" : "";
        return $"{string.Join(", ", parts)} in {ElapsedMilliseconds:N0} ms{suffix}";
    }
}
