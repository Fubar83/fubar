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

    /// <summary>
    /// Whether this row can show a side-by-side preview.
    ///
    /// Only an UPDATE of a request has two sides to compare: an ADD has nothing in the workspace yet,
    /// a REMOVE has nothing in the spec, and a variable is a single value the Detail column already
    /// shows in full. Offering the button anywhere else would be a promise the dialog cannot keep.
    /// </summary>
    public bool CanPreview =>
        Change == ImportChange.Update
        && Model is RequestDiff { Planned: not null, ExistingPath: not null and not "" };

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
