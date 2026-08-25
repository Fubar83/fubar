using CommunityToolkit.Mvvm.ComponentModel;
using Fubar.Studio.Core.Models;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>One editable row in the Environment Editor's variable grid (Key/Value/Type/Description).</summary>
public partial class EnvironmentVariableRowViewModel : ViewModelBase
{
    public static IReadOnlyList<VariableKind> KindOptions { get; } = Enum.GetValues<VariableKind>();

    [ObservableProperty]
    public partial string Key { get; set; } = "";

    [ObservableProperty]
    public partial string Value { get; set; } = "";

    [ObservableProperty]
    public partial VariableKind Kind { get; set; } = VariableKind.Normal;

    [ObservableProperty]
    public partial string Description { get; set; } = "";

    /// <summary>Only Secret values are masked in the grid; Session values show in the clear (for now).</summary>
    public bool IsSecretKind => Kind == VariableKind.Secret;

    partial void OnKindChanged(VariableKind value) => OnPropertyChanged(nameof(IsSecretKind));
}
