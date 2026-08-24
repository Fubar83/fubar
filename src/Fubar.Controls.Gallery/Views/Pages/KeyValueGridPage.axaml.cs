using System.Collections.ObjectModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Fubar.Controls.Gallery.Views.Pages;

public partial class KeyValueGridPage : UserControl
{
    public KeyValueGridPage()
    {
        InitializeComponent();
        DataContext = new KeyValueGridPageViewModel();
    }
}

/// <summary>A row for the gallery's KeyValueGrid demo - exposes the Enabled/Key/Value/Description
/// members the grid's default cells bind to.</summary>
public partial class DemoKvRow : ObservableObject
{
    [ObservableProperty] public partial bool Enabled { get; set; } = true;
    [ObservableProperty] public partial string Key { get; set; } = string.Empty;
    [ObservableProperty] public partial string Value { get; set; } = string.Empty;
    [ObservableProperty] public partial string Description { get; set; } = string.Empty;
}

public partial class KeyValueGridPageViewModel : ObservableObject
{
    public ObservableCollection<DemoKvRow> Rows { get; } =
    [
        new() { Key = "Accept", Value = "application/json", Description = "Response format" },
        new() { Key = "Authorization", Value = "Bearer {{token}}", Description = "Auth header" },
        new() { Enabled = false, Key = "X-Debug", Value = "1", Description = "Disabled row" },
    ];

    [RelayCommand]
    private void AddRow() => Rows.Add(new DemoKvRow());

    [RelayCommand]
    private void RemoveRow(DemoKvRow? row)
    {
        if (row is not null)
        {
            Rows.Remove(row);
        }
    }
}
