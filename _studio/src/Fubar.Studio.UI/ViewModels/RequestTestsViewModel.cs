using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Studio.Core.Models;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>
/// Backs the request editor's "Tests" tab: a per-request send timeout, a set of post-response capture
/// rules (extract a value into a session/environment variable), and a set of declarative assertions.
/// Purely an editor over <see cref="RequestModel.TimeoutSeconds"/> / <see cref="RequestModel.Captures"/>
/// / <see cref="RequestModel.Assertions"/>; evaluation lives in the infrastructure test service.
/// </summary>
public partial class RequestTestsViewModel : ViewModelBase
{
    public RequestTestsViewModel(RequestModel request)
    {
        TimeoutSeconds = request.TimeoutSeconds;

        foreach (var c in request.Captures)
        {
            Captures.Add(new CaptureRowViewModel(c));
        }

        foreach (var a in request.Assertions)
        {
            Assertions.Add(new AssertionRowViewModel(a));
        }

        Captures.CollectionChanged += (_, _) => RaiseChanged();
        Assertions.CollectionChanged += (_, _) => RaiseChanged();
        foreach (var row in Captures)
        {
            row.PropertyChanged += OnRowChanged;
        }

        foreach (var row in Assertions)
        {
            row.PropertyChanged += OnRowChanged;
        }
    }

    /// <summary>Raised on any edit so the editor can mark the request dirty.</summary>
    public event Action? Changed;

    private void RaiseChanged() => Changed?.Invoke();

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e) => RaiseChanged();

    [ObservableProperty]
    public partial int? TimeoutSeconds { get; set; }

    partial void OnTimeoutSecondsChanged(int? value)
    {
        OnPropertyChanged(nameof(TimeoutValue));
        RaiseChanged();
    }

    /// <summary>Decimal view of <see cref="TimeoutSeconds"/> for the NumericUpDown (whose Value is
    /// <c>decimal?</c>); blank clears the per-request override.</summary>
    public decimal? TimeoutValue
    {
        get => TimeoutSeconds;
        set => TimeoutSeconds = value is null ? null : (int)value;
    }

    public ObservableCollection<CaptureRowViewModel> Captures { get; } = [];

    public ObservableCollection<AssertionRowViewModel> Assertions { get; } = [];

    public IReadOnlyList<ResponseField> SourceOptions { get; } = Enum.GetValues<ResponseField>();

    public IReadOnlyList<CaptureScope> ScopeOptions { get; } = Enum.GetValues<CaptureScope>();

    public IReadOnlyList<AssertionOperator> OperatorOptions { get; } = Enum.GetValues<AssertionOperator>();

    [RelayCommand]
    private void AddCapture()
    {
        var row = new CaptureRowViewModel(new CaptureRule());
        row.PropertyChanged += OnRowChanged;
        Captures.Add(row);
    }

    [RelayCommand]
    private void RemoveCapture(CaptureRowViewModel? row)
    {
        if (row is not null)
        {
            row.PropertyChanged -= OnRowChanged;
            Captures.Remove(row);
        }
    }

    [RelayCommand]
    private void AddAssertion()
    {
        var row = new AssertionRowViewModel(new Assertion());
        row.PropertyChanged += OnRowChanged;
        Assertions.Add(row);
    }

    [RelayCommand]
    private void RemoveAssertion(AssertionRowViewModel? row)
    {
        if (row is not null)
        {
            row.PropertyChanged -= OnRowChanged;
            Assertions.Remove(row);
        }
    }

    public List<CaptureRule> CapturesToModel() => Captures.Select(c => c.ToModel()).ToList();

    public List<Assertion> AssertionsToModel() => Assertions.Select(a => a.ToModel()).ToList();
}

/// <summary>One editable row in the Captures grid.</summary>
public partial class CaptureRowViewModel : ObservableObject
{
    public CaptureRowViewModel(CaptureRule model)
    {
        Enabled = model.Enabled;
        VariableName = model.VariableName;
        Source = model.Source;
        Expression = model.Expression;
        Scope = model.Scope;
    }

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    [ObservableProperty]
    public partial string VariableName { get; set; } = "";

    [ObservableProperty]
    public partial ResponseField Source { get; set; }

    [ObservableProperty]
    public partial string Expression { get; set; } = "";

    [ObservableProperty]
    public partial CaptureScope Scope { get; set; }

    /// <summary>Whether the Expression cell applies (a JSONPath / header name is only meaningful for
    /// body/header sources, not status/time).</summary>
    public bool NeedsExpression => Source is ResponseField.JsonBody or ResponseField.Header;

    partial void OnSourceChanged(ResponseField value) => OnPropertyChanged(nameof(NeedsExpression));

    public CaptureRule ToModel() => new()
    {
        Enabled = Enabled,
        VariableName = VariableName,
        Source = Source,
        Expression = Expression,
        Scope = Scope,
    };
}

/// <summary>One editable row in the Assertions grid.</summary>
public partial class AssertionRowViewModel : ObservableObject
{
    public AssertionRowViewModel(Assertion model)
    {
        Enabled = model.Enabled;
        Source = model.Source;
        Target = model.Target;
        Operator = model.Operator;
        Expected = model.Expected;
    }

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    [ObservableProperty]
    public partial ResponseField Source { get; set; }

    [ObservableProperty]
    public partial string Target { get; set; } = "";

    [ObservableProperty]
    public partial AssertionOperator Operator { get; set; }

    [ObservableProperty]
    public partial string Expected { get; set; } = "";

    public bool NeedsTarget => Source is ResponseField.JsonBody or ResponseField.Header;

    public bool NeedsExpected => Operator is not (AssertionOperator.Exists or AssertionOperator.NotExists);

    partial void OnSourceChanged(ResponseField value) => OnPropertyChanged(nameof(NeedsTarget));

    partial void OnOperatorChanged(AssertionOperator value) => OnPropertyChanged(nameof(NeedsExpected));

    public Assertion ToModel() => new()
    {
        Enabled = Enabled,
        Source = Source,
        Target = Target,
        Operator = Operator,
        Expected = Expected,
    };
}
