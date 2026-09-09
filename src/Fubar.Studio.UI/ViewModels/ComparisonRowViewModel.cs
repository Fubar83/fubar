using CommunityToolkit.Mvvm.ComponentModel;
using Fubar.Studio.Core.Running;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>
/// What one row of an environment COMPARISON has concluded about a request.
///
/// <para>Distinct from <c>Core.Running.ComparisonVerdict</c>, which is a run step&apos;s verdict against
/// its oracle. Both used to be called ComparisonVerdict, and in a view model file the nearer one
/// silently won - so a run row wrote a paired-comparison verdict and the compiler agreed.</para>
/// </summary>
public enum PairVerdict
{
    /// <summary>Not run, or only one side is in so far.</summary>
    Pending,

    /// <summary>Both sides answered and the comparison found nothing - once this request's own ignore
    /// rules and array keys had been applied, which is the whole point.</summary>
    Same,

    /// <summary>Both sides answered and the bodies differ.</summary>
    Differs,

    /// <summary>The two status codes disagree. Reported ahead of any body difference: it is the thing
    /// nobody needs a diff to care about, and usually explains the body difference underneath it.</summary>
    StatusDiffers,

    /// <summary>One side did not answer, or its body was too big to carry. There is nothing to compare
    /// and saying "differs" would be a guess.</summary>
    NotComparable,
}

/// <summary>
/// One request's row in an environment comparison: how each side answered, and what comparing them
/// concluded.
///
/// <para>Like <see cref="RunStepRowViewModel"/>, the row exists from the moment the plan is built rather
/// than appearing when its turn comes - a list that grows can only answer "how much is left?" by
/// finishing. Unlike it, each row has two sides that arrive separately, so the left can be filled in
/// while the right is still in flight.</para>
/// </summary>
public sealed partial class ComparisonRowViewModel : ViewModelBase
{
    public ComparisonRowViewModel(RunStep step)
    {
        Step = step;
        Order = step.Order;
        Name = step.Name;
    }

    public RunStep Step { get; }

    public int Order { get; }

    public string Name { get; }

    /// <summary>The completed pair, once both sides are in. What the diff pane is loaded from.</summary>
    public StepPair? Pair { get; private set; }

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    /// <summary>The row the diff pane is showing. A flag on the row rather than a converter over the
    /// parent's selection, so the list keeps the one-bool-per-style-class convention.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial string? LeftStatus { get; set; }

    [ObservableProperty]
    public partial string? RightStatus { get; set; }

    [ObservableProperty]
    public partial PairVerdict Verdict { get; set; } = PairVerdict.Pending;

    /// <summary>Why this row cannot be compared, in the user's words - shown instead of a difference
    /// count, because "not comparable" without a reason reads as a bug in the tool.</summary>
    [ObservableProperty]
    public partial string? Note { get; set; }

    /// <summary>Number of differences the comparison found. Null until it has run.</summary>
    [ObservableProperty]
    public partial int? DifferenceCount { get; set; }

    partial void OnVerdictChanged(PairVerdict value)
    {
        OnPropertyChanged(nameof(IsSame));
        OnPropertyChanged(nameof(IsDifferent));
        OnPropertyChanged(nameof(IsStatusMismatch));
        OnPropertyChanged(nameof(IsNotComparable));
        OnPropertyChanged(nameof(IsPending));
        OnPropertyChanged(nameof(Summary));
    }

    partial void OnDifferenceCountChanged(int? value) => OnPropertyChanged(nameof(Summary));

    partial void OnNoteChanged(string? value) => OnPropertyChanged(nameof(Summary));

    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(IsPending));

    // Style-class flags. Avalonia's Classes is not bindable, so a view model exposes a bool per class
    // rather than a class-name string (see CLAUDE.md, Conventions).
    public bool IsSame => Verdict == PairVerdict.Same;

    public bool IsDifferent => Verdict == PairVerdict.Differs;

    public bool IsStatusMismatch => Verdict == PairVerdict.StatusDiffers;

    public bool IsNotComparable => Verdict == PairVerdict.NotComparable;

    public bool IsPending => Verdict == PairVerdict.Pending && !IsRunning;

    /// <summary>The one-line verdict, so the row says its answer in words rather than only in colour.</summary>
    public string Summary => Verdict switch
    {
        PairVerdict.Same => "Same",
        PairVerdict.StatusDiffers => "Status differs",
        PairVerdict.Differs when DifferenceCount is { } n => n == 1 ? "1 difference" : $"{n} differences",
        PairVerdict.Differs => "Differs",
        PairVerdict.NotComparable => Note ?? "Not comparable",
        _ => IsRunning ? "Running…" : "",
    };

    public void Reset()
    {
        Pair = null;
        IsRunning = false;
        LeftStatus = null;
        RightStatus = null;
        Note = null;
        DifferenceCount = null;
        Verdict = PairVerdict.Pending;
    }

    public void Starting() => IsRunning = true;

    /// <summary>The left side landed; the right is still in flight.</summary>
    public void ApplyLeft(StepReport left) => LeftStatus = Describe(left);

    /// <summary>
    /// Both sides are in. The verdict is everything that can be settled WITHOUT running a comparison -
    /// a status mismatch, a side that did not answer, identical text. Anything else is left
    /// <see cref="PairVerdict.Pending"/> for the caller to resolve with the comparison engine,
    /// because only that knows this request's ignore rules and array keys.
    /// </summary>
    public void ApplyPair(StepPair pair)
    {
        Pair = pair;
        IsRunning = false;
        LeftStatus = Describe(pair.Left);
        RightStatus = Describe(pair.Right);

        if (pair.Left.Status == StepStatus.Skipped && pair.Right.Status == StepStatus.Skipped)
        {
            Verdict = PairVerdict.NotComparable;
            Note = "Skipped";
            return;
        }

        if (pair.StatusDiffers)
        {
            Verdict = PairVerdict.StatusDiffers;
            return;
        }

        if (pair.NotComparable)
        {
            Verdict = PairVerdict.NotComparable;
            Note = Reason(pair);
            return;
        }

        if (pair.BodiesIdentical)
        {
            Verdict = PairVerdict.Same;
            DifferenceCount = 0;
        }
    }

    /// <summary>The comparison engine's answer, for a pair that identical-text could not settle.</summary>
    public void ApplyComparison(int differences)
    {
        DifferenceCount = differences;
        Verdict = differences == 0 ? PairVerdict.Same : PairVerdict.Differs;
    }

    private static string Reason(StepPair pair)
    {
        if (pair.Left.BodyTooLargeToCompare || pair.Right.BodyTooLargeToCompare)
        {
            return "Response too large to compare";
        }

        var side = pair.Left.ResponseBody is null ? pair.Left : pair.Right;
        return side.Error is { Length: > 0 } error ? error : "No response body";
    }

    private static string Describe(StepReport report) =>
        report.Status == StepStatus.Skipped ? "—"
            : report.StatusCode is { } code ? $"{code} · {report.ElapsedMilliseconds:N0} ms"
            : report.Error is { Length: > 0 } error ? error
            : "no response";
}
