using CommunityToolkit.Mvvm.ComponentModel;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>
/// One row in the Headers tab's combined grid (RequestEditorPane.md §5): either inherited from a
/// folder/auth-profile ancestor (<see cref="IsInherited"/> - Key/Value readonly, but still
/// toggleable via <see cref="Enabled"/> to suppress it for this one request) or defined directly
/// on the request (fully editable, deletable).
/// </summary>
public partial class HeaderRowViewModel : ViewModelBase
{
    /// <summary>True for a row inherited from a folder or the resolved auth profile - its Key/Value
    /// are read-only in the UI, but <see cref="Enabled"/> can still be toggled off to suppress it.</summary>
    public bool IsInherited { get; init; }

    /// <summary>True for a row synthesized from the active auth (a directly-selected profile, an
    /// inline Bearer/API key, or a folder-inherited profile) rather than a folder's own
    /// <c>_folder.json</c> headers - lets <c>HeadersTabViewModel.RefreshAuthHeaders</c> replace just
    /// these rows when the Auth tab changes, without touching the folder-inherited ones.</summary>
    public bool IsAuthDerived { get; init; }

    /// <summary>"Direct", "Folder: Users", "Auth: Admin Profile", etc. - the Headers tab's Source column.</summary>
    public string SourceName { get; init; } = "Direct";

    [ObservableProperty]
    public partial bool Enabled { get; set; } = true;

    partial void OnEnabledChanged(bool value) => OnPropertyChanged(nameof(DisplayOpacity));

    /// <summary>Key/Value dim strongest when the row is unchecked (disabled) - low enough to read as
    /// "won't be sent" at a glance, regardless of whether it's also inherited - and a lighter touch
    /// for an inherited-but-still-enabled row, same as before. A direct, enabled row stays full opacity.</summary>
    public double DisplayOpacity => !Enabled ? 0.25 : IsInherited ? 0.6 : 1.0;

    [ObservableProperty]
    public partial string Key { get; set; } = "";

    [ObservableProperty]
    public partial string Value { get; set; } = "";
}
