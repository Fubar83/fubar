using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Workspaces;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>
/// Backs the Request Editor's Headers tab (RequestEditorPane.md §5): inherited rows from ancestor
/// folders / the resolved auth profile, layered with the request's own direct headers. Inherited
/// rows are populated asynchronously by <see cref="LoadInheritedAsync"/> once the owning
/// <c>RequestEditorViewModel</c> can reach <c>IWorkspaceService.GetInheritanceChainAsync</c> - the
/// tab starts direct-only and gains its inherited rows a moment later.
/// </summary>
public partial class HeadersTabViewModel : ViewModelBase
{
    public ObservableCollection<HeaderRowViewModel> Rows { get; } = [];

    /// <summary>Raised whenever a direct row is added, removed, edited, or an inherited row's
    /// Enabled toggle changes - used for <c>RequestEditorViewModel</c>'s dirty tracking.</summary>
    public event Action? Changed;

    public HeadersTabViewModel(IEnumerable<KeyValueItem> direct, IReadOnlyCollection<string> suppressedInheritedKeys)
    {
        _suppressedInheritedKeys = [.. suppressedInheritedKeys];

        foreach (var item in direct)
        {
            AddRowInternal(new HeaderRowViewModel { Key = item.Key, Value = item.Value, Enabled = item.Enabled, SourceName = "Direct" });
        }
    }

    private readonly HashSet<string> _suppressedInheritedKeys;

    /// <summary>Prepends folder-inherited rows (root-most ancestor first, so a closer folder's
    /// same-key header visually wins by appearing later/closer to the direct rows) ahead of the
    /// direct rows already loaded from the constructor. Called once, right after construction -
    /// unlike <see cref="RefreshAuthHeaders"/>, a request's folder chain can't change while its
    /// editor is open.</summary>
    public void LoadInherited(InheritanceChain chain)
    {
        for (var i = chain.Headers.Count - 1; i >= 0; i--)
        {
            var h = chain.Headers[i];
            var row = new HeaderRowViewModel
            {
                IsInherited = true,
                SourceName = h.SourceName,
                Key = h.Item.Key,
                Value = h.Item.Value,
                Enabled = !_suppressedInheritedKeys.Contains(h.Item.Key),
            };
            row.PropertyChanged += (_, _) => Changed?.Invoke();
            Rows.Insert(0, row);
        }
    }

    /// <summary>
    /// Replaces whichever header(s) the currently-active auth implies - a directly-selected profile,
    /// an inline Bearer/API key, or (absent either) a folder-inherited profile - so a profile's
    /// header shows up, read-only but toggleable, on every request that uses it (RequestEditorPane.md
    /// §5). Re-callable any time the Auth tab changes, since unlike the folder chain this can change
    /// while the editor is open; suppression is still tracked by header Key, so toggling a row off
    /// stays off even if switching auth produces the same key again.
    /// </summary>
    public void RefreshAuthHeaders(IReadOnlyList<KeyValueItem> headers, string? sourceName)
    {
        for (var i = Rows.Count - 1; i >= 0; i--)
        {
            if (Rows[i].IsAuthDerived)
            {
                Rows.RemoveAt(i);
            }
        }

        for (var i = headers.Count - 1; i >= 0; i--)
        {
            var item = headers[i];
            var row = new HeaderRowViewModel
            {
                IsInherited = true,
                IsAuthDerived = true,
                SourceName = sourceName ?? "Auth",
                Key = item.Key,
                Value = item.Value,
                Enabled = !_suppressedInheritedKeys.Contains(item.Key),
            };
            row.PropertyChanged += (_, _) => Changed?.Invoke();
            Rows.Insert(0, row);
        }
    }

    [RelayCommand]
    private void AddRow() => AddRowInternal(new HeaderRowViewModel { SourceName = "Direct" });

    [RelayCommand]
    private void RemoveRow(HeaderRowViewModel? row)
    {
        if (row is { IsInherited: false } && Rows.Remove(row))
        {
            Changed?.Invoke();
        }
    }

    private void AddRowInternal(HeaderRowViewModel row)
    {
        row.PropertyChanged += (_, _) => Changed?.Invoke();
        Rows.Add(row);
    }

    /// <summary>The request's own direct headers, for <c>RequestModel.Headers</c>.</summary>
    public List<KeyValueItem> DirectToModel() =>
        Rows.Where(r => !r.IsInherited)
            .Select(r => new KeyValueItem { Key = r.Key, Value = r.Value, Enabled = r.Enabled })
            .ToList();

    /// <summary>Headers to actually send: enabled <b>direct + folder-inherited</b> rows. Auth-derived rows
    /// are excluded - the execution pipeline's auth prestep injects the resolved credential itself (the
    /// auth-derived rows here are a read-only preview). Used by the Send path.</summary>
    public List<KeyValueItem> SendableToModel() =>
        Rows.Where(r => r.Enabled && !r.IsAuthDerived && !string.IsNullOrWhiteSpace(r.Key))
            .Select(r => new KeyValueItem { Key = r.Key, Value = r.Value, Enabled = true })
            .ToList();

    /// <summary>Inherited rows the user toggled off, for <c>RequestModel.SuppressedInheritedHeaderKeys</c>.</summary>
    public List<string> SuppressedInheritedKeys() =>
        Rows.Where(r => r.IsInherited && !r.Enabled).Select(r => r.Key).ToList();
}
