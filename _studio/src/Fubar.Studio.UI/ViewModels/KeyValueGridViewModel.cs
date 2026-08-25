using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Studio.Core.Models;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>
/// Backs the reusable Params key-value-description grid on <c>RequestEditorViewModel</c> (the
/// Headers tab has its own <see cref="HeadersTabViewModel"/> now, for inheritance display).
/// </summary>
public partial class KeyValueGridViewModel : ViewModelBase
{
    public KeyValueGridViewModel()
    {
    }

    public KeyValueGridViewModel(IEnumerable<KeyValueItem> items)
    {
        foreach (var item in items)
        {
            AddRowInternal(KeyValueRowViewModel.FromModel(item));
        }
    }

    public ObservableCollection<KeyValueRowViewModel> Rows { get; } = [];

    /// <summary>
    /// Raised whenever a row is added, removed, or edited - <c>RequestEditorViewModel</c> wires
    /// this into its <c>IsDirty</c> tracking. Deliberately not raised by the constructor's initial
    /// population (see <see cref="AddRowInternal"/>), so loading a request from disk never itself
    /// marks it dirty.
    /// </summary>
    public event Action? Changed;

    [RelayCommand]
    private void AddRow()
    {
        AddRowInternal(new KeyValueRowViewModel());
        Changed?.Invoke();
    }

    [RelayCommand]
    private void RemoveRow(KeyValueRowViewModel? row)
    {
        if (row is not null && Rows.Remove(row))
        {
            Changed?.Invoke();
        }
    }

    private void AddRowInternal(KeyValueRowViewModel row)
    {
        row.PropertyChanged += (_, _) => Changed?.Invoke();
        Rows.Add(row);
    }

    /// <summary>
    /// Adds a row without raising <see cref="Changed"/> for the add itself (though the row's own
    /// future edits still will, same as any other row) - for a caller synchronizing this grid from
    /// another source of truth (the Params tab's bidirectional URL sync, RequestEditorPane.md §3),
    /// where re-raising <see cref="Changed"/> here would feed back into that same sync.
    /// </summary>
    public void AddRowQuietly(KeyValueRowViewModel row) => AddRowInternal(row);

    /// <summary>Removes a row without raising <see cref="Changed"/> - see <see cref="AddRowQuietly"/>.</summary>
    public void RemoveRowQuietly(KeyValueRowViewModel row) => Rows.Remove(row);

    public List<KeyValueItem> ToModel() => Rows.Select(r => r.ToModel()).ToList();
}
