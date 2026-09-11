using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Fubar.Studio.Core.Running;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>
/// One request's row in a run.
///
/// <para>The row exists from the moment the plan is built, before anything is sent, and is filled in as
/// the run reaches it. That is what lets the window show the whole plan up front - a list that grew a
/// row at a time would answer "how much is left?" only by finishing.</para>
/// </summary>
public sealed partial class RunStepRowViewModel : ViewModelBase
{
    public RunStepRowViewModel(RunStep step)
    {
        Step = step;
        Order = step.Order;
        Name = step.QualifiedName;
    }

    public RunStep Step { get; }

    public int Order { get; }

    public string Name { get; }

    /// <summary>Cleanup rather than test. Marked on the row because a failing cleanup step is not a
    /// failing test, and a reader scanning a red run has to tell the two apart at a glance.</summary>
    public bool IsTeardown => Step.IsTeardown;

    /// <summary>The finished report for this step, once it has one. Held so the row can be OPENED -
    /// a difference count is where the question starts, not where it ends.</summary>
    public StepReport? Report { get; private set; }

    /// <summary>Whether there are two bodies to show. False for a step that never answered, and for a
    /// run whose bodies were dropped for being too large to compare.</summary>
    public bool CanShowDifferences =>
        Report is { ResponseBody: not null, ComparedBody: not null };

    /// <summary>
    /// Whether there is an answer to show on its own.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="CanShowDifferences"/>, and true more often: a run judged by assertions
    /// has no other side, so there is nothing to diff and the response was previously unreachable from
    /// this window. False for a step that never answered, and for a body dropped for being too large.
    /// </remarks>
    public bool CanShowResponse => Report is { ResponseBody: not null };

    [ObservableProperty]
    public partial StepStatus? Status { get; set; }

    /// <summary>True while this request is in flight. Its own flag rather than "no status yet", because
    /// a queued row and a running row are the two things a reader is trying to tell apart while waiting.</summary>
    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    public partial string? StatusText { get; set; }

    [ObservableProperty]
    public partial string? Detail { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    /// <summary>Only the FAILED assertions are listed. A passing assertion's detail is noise in a report
    /// whose job is to point at what needs attention; the counts in <see cref="Detail"/> already say how
    /// many there were.</summary>
    public ObservableCollection<string> FailedAssertions { get; } = [];

    public bool HasFailedAssertions => FailedAssertions.Count > 0;

    // Style-class flags. Avalonia's Classes is not bindable, so a view model exposes a bool per class
    // rather than a class-name string (see CLAUDE.md, Conventions).
    /// <summary>Green only when the request passed AND its answer was accepted. A step whose
    /// snapshot differs passed every assertion it had - that is a second axis, and a row that showed
    /// green while the summary said "1 differ" would be a report that contradicts itself.</summary>
    public bool IsPassed => Status == StepStatus.Passed && !IsUnexpectedStatus && !IsDiffering && !IsUncomparable;

    /// <summary>Red is for a failing TEST. Cleanup cannot fail the run, so painting it red would put
    /// a red row in a green report - see <see cref="IsCleanupProblem"/>, which is amber.</summary>
    public bool IsFailed => Status == StepStatus.Failed && !IsTeardown;

    public bool IsErrored => Status == StepStatus.Errored && !IsTeardown;

    /// <summary>Cleanup that did not do its job: worth seeing, never a failure. A skipped one keeps
    /// the faded skipped style - it was not reached, which is a different thing from not working.</summary>
    public bool IsCleanupProblem =>
        IsTeardown && Status is StepStatus.Failed or StepStatus.Errored;

    public bool IsSkipped => Status == StepStatus.Skipped;

    public bool IsPending => Status is null && !IsRunning;

    /// <summary>The answer differs from what it was compared against - the request itself was fine.</summary>
    [ObservableProperty]
    public partial bool IsDiffering { get; set; }

    /// <summary>There was nothing to compare against. Never a pass.</summary>
    [ObservableProperty]
    public partial bool IsUncomparable { get; set; }

    /// <summary>A request that answered with a non-2xx nobody asserted on. Drawn differently from both
    /// green and red, because it is neither: the run does not fail over it (see <see cref="RunReport"/>)
    /// and it is the single most likely thing a reader is scanning for.</summary>
    [ObservableProperty]
    public partial bool IsUnexpectedStatus { get; set; }

    public void Reset()
    {
        Status = null;
        IsRunning = false;
        StatusText = null;
        Detail = null;
        Error = null;
        Report = null;
        IsUnexpectedStatus = false;
        IsDiffering = false;
        IsUncomparable = false;
        FailedAssertions.Clear();
        OnPropertyChanged(nameof(HasFailedAssertions));
    }

    public void Starting()
    {
        Reset();
        IsRunning = true;
    }

    public void Apply(StepReport report)
    {
        Report = report;
        IsRunning = false;
        Status = report.Status;
        IsUnexpectedStatus = report.IsUnexpectedStatus && report.Assertions.Count == 0;
        IsDiffering = report.Comparison == ComparisonVerdict.Differs;
        IsUncomparable = report.Comparison == ComparisonVerdict.Unavailable;

        StatusText = report switch
        {
            { Status: StepStatus.Skipped } => "skipped",
            { Status: StepStatus.Errored } => "error",
            { Comparison: ComparisonVerdict.Differs } => "differs",
            { Comparison: ComparisonVerdict.Unavailable } => "no answer",
            _ => report.StatusCode is { } code ? code.ToString() : "-",
        };

        Detail = report.Status == StepStatus.Skipped
            ? null
            : Describe(report);

        Error = report.Error;

        FailedAssertions.Clear();
        foreach (var assertion in report.Assertions.Where(a => !a.Passed))
        {
            FailedAssertions.Add(assertion.Actual is { } actual
                ? $"{assertion.Description} — got {actual}"
                : assertion.Description);
        }

        OnPropertyChanged(nameof(HasFailedAssertions));
        RaiseClassFlags();
    }

    private static string Describe(StepReport report)
    {
        var parts = new List<string> { $"{report.ElapsedMilliseconds:N0} ms" };

        if (report.Assertions.Count > 0)
        {
            parts.Add($"{report.AssertionsPassed}/{report.Assertions.Count} assertions");
        }

        // Which side it was judged against, on every compared row. A run that quietly switched from
        // the shared snapshot to a per-environment one someone recorded last week is a run whose green
        // means something different from yesterday's.
        var against = Against(report);

        parts.AddRange(report.Comparison switch
        {
            ComparisonVerdict.Differs =>
                [$"{report.DifferenceCount} difference{(report.DifferenceCount == 1 ? "" : "s")} from {against}"],

            ComparisonVerdict.Unavailable =>
                [report.ComparisonUnavailableReason ?? "nothing to compare against"],

            ComparisonVerdict.Same => report.ToleratedCount > 0
                ? [$"matches {against}", $"{report.ToleratedCount} within tolerance"]
                : new[] { $"matches {against}" },

            _ => [],
        });

        // A capture that could not be applied is shown here rather than as an error, because the request
        // itself answered. It matters because the failure it causes usually lands several requests later
        // as a {{variable}} that never resolved, by which point nothing points back at this row.
        var failedCaptures = report.Captures.Count(c => !c.Ok);
        if (failedCaptures > 0)
        {
            parts.Add($"{failedCaptures} capture{(failedCaptures == 1 ? "" : "s")} failed");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>
    /// The other side, short enough to sit on a row: the snapshot's file name, or the environment's.
    /// </summary>
    /// <remarks>
    /// The full path is what the CLI prints and what the tooltip carries. On a row it is redundant -
    /// the endpoint and case are already the row's own name, so the only part that says anything new
    /// is the last segment - and long enough to push that name off the row entirely.
    /// </remarks>
    private static string Against(StepReport report) =>
        report.ComparedAgainst is { Length: > 0 } source
            ? source[(source.LastIndexOfAny(['/', '\\']) + 1)..]
            : "nothing";

    private void RaiseClassFlags()
    {
        OnPropertyChanged(nameof(IsPassed));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(IsErrored));
        OnPropertyChanged(nameof(IsSkipped));
        OnPropertyChanged(nameof(IsPending));
        OnPropertyChanged(nameof(IsCleanupProblem));
        OnPropertyChanged(nameof(CanShowDifferences));
        OnPropertyChanged(nameof(CanShowResponse));
    }

    partial void OnIsRunningChanged(bool value) => RaiseClassFlags();

    partial void OnIsUnexpectedStatusChanged(bool value) => OnPropertyChanged(nameof(IsPassed));
}
