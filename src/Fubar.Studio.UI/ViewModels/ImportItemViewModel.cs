using CommunityToolkit.Mvvm.ComponentModel;
using Fubar.Studio.Core.Import;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>One tickable row in the import diff (a request or an environment variable): its change kind,
/// a label, and whether the user has chosen to apply it. Wraps the underlying <see cref="RequestDiff"/>
/// or <see cref="VariableDiff"/> so the dialog can bind a checkbox without the diff records needing
/// mutable UI state.</summary>
public partial class ImportItemViewModel : ViewModelBase
{
    public ImportItemViewModel(object model, ImportChange change, string label, string? detail)
    {
        Model = model;
        Change = change;
        Label = label;
        Detail = detail;
        // Sensible defaults: apply adds/updates, don't touch unchanged, don't delete unless opted in.
        IsSelected = change is ImportChange.Add or ImportChange.Update;
        CanSelect = change != ImportChange.Unchanged;
    }

    public object Model { get; }

    public ImportChange Change { get; }

    public string ChangeLabel => Change switch
    {
        ImportChange.Add => "ADD",
        ImportChange.Update => "UPDATE",
        ImportChange.Remove => "REMOVE",
        _ => "UNCHANGED",
    };

    public string Label { get; }

    public string? Detail { get; }

    public bool CanSelect { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
