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

    public KeyValueItem ToModel() => new()
    {
        Key = Key,
        Value = Value,
        Description = string.IsNullOrEmpty(Description) ? null : Description,
        Enabled = Enabled,
    };

    public static KeyValueRowViewModel FromModel(KeyValueItem item) => new()
    {
        Key = item.Key,
        Value = item.Value,
        Description = item.Description ?? "",
        Enabled = item.Enabled,
    };
}
