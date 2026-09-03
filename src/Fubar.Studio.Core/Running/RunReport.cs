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

    public int Total => Steps.Count;

    public int Passed => Steps.Count(s => s.Status == StepStatus.Passed);

    public int Failed => Steps.Count(s => s.Status == StepStatus.Failed);

    public int Errored => Steps.Count(s => s.Status == StepStatus.Errored);

    public int Skipped => Steps.Count(s => s.Status == StepStatus.Skipped);

    public int AssertionsPassed => Steps.Sum(s => s.AssertionsPassed);

    public int AssertionsFailed => Steps.Sum(s => s.AssertionsFailed);

    /// <summary>Requests that answered with a non-2xx and had no assertion to judge it. Reported, never
    /// counted against the verdict - see the type remarks.</summary>
    public IReadOnlyList<StepReport> UnexpectedStatuses =>
        [.. Steps.Where(s => s.IsUnexpectedStatus && s.Assertions.Count == 0)];

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
    public bool Ok => Total > 0 && Failed == 0 && Errored == 0 && !WasCancelled && Skipped == 0;

    /// <summary>One line for a status bar or a CI log.</summary>
    public string Summary()
    {
        if (Total == 0)
        {
            return "Nothing to run.";
        }

        var parts = new List<string> { $"{Passed}/{Total} passed" };
        if (Failed > 0) parts.Add($"{Failed} failed");
        if (Errored > 0) parts.Add($"{Errored} errored");
        if (Skipped > 0) parts.Add($"{Skipped} skipped");
        if (AssertionsFailed > 0) parts.Add($"{AssertionsFailed} assertion{(AssertionsFailed == 1 ? "" : "s")} failed");

        var suffix = WasCancelled ? " (cancelled)" : "";
        return $"{string.Join(", ", parts)} in {ElapsedMilliseconds:N0} ms{suffix}";
    }
}
