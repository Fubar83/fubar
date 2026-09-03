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
        Name = step.Name;
    }

    public RunStep Step { get; }

    public int Order { get; }

    public string Name { get; }

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
    public bool IsPassed => Status == StepStatus.Passed && !IsUnexpectedStatus;

    public bool IsFailed => Status == StepStatus.Failed;

    public bool IsErrored => Status == StepStatus.Errored;

    public bool IsSkipped => Status == StepStatus.Skipped;

    public bool IsPending => Status is null && !IsRunning;

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
        IsUnexpectedStatus = false;
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
        IsRunning = false;
        Status = report.Status;
        IsUnexpectedStatus = report.IsUnexpectedStatus && report.Assertions.Count == 0;

        StatusText = report.Status switch
        {
            StepStatus.Skipped => "skipped",
            StepStatus.Errored => "error",
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

    private void RaiseClassFlags()
    {
        OnPropertyChanged(nameof(IsPassed));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(IsErrored));
        OnPropertyChanged(nameof(IsSkipped));
        OnPropertyChanged(nameof(IsPending));
    }

    partial void OnIsRunningChanged(bool value) => RaiseClassFlags();

    partial void OnIsUnexpectedStatusChanged(bool value) => OnPropertyChanged(nameof(IsPassed));
}
