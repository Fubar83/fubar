using CommunityToolkit.Mvvm.ComponentModel;
using Fubar.Studio.Core.Models;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>One editable row in a Params/Headers/Variables key-value-description grid.</summary>
public partial class KeyValueRowViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial bool Enabled { get; set; } = true;

    [ObservableProperty]
    public partial string Key { get; set; } = "";

    [ObservableProperty]
    public partial string Value { get; set; } = "";

    [ObservableProperty]
    public partial string Description { get; set; } = "";

    /// <summary>Text, or File for a multipart upload - in which case <see cref="Value"/> is a path.
    /// Only meaningful in a FormData body; the Params and Headers grids leave it Text.</summary>
    [ObservableProperty]
    public partial FieldKind Kind { get; set; } = FieldKind.Text;

    /// <summary>Drives the row's file-picker button, so it appears only where a file part is legal.</summary>
    public bool IsFile => Kind == FieldKind.File;

    partial void OnKindChanged(FieldKind value) => OnPropertyChanged(nameof(IsFile));

    public KeyValueItem ToModel() => new()
    {
        Key = Key,
        Value = Value,
        Description = string.IsNullOrEmpty(Description) ? null : Description,
        Enabled = Enabled,
        Kind = Kind,
    };

    public static KeyValueRowViewModel FromModel(KeyValueItem item) => new()
    {
        Key = item.Key,
        Value = item.Value,
        Description = item.Description ?? "",
        Enabled = item.Enabled,
        Kind = item.Kind,
    };
}
