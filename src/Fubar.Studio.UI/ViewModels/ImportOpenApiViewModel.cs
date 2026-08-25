using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Studio.Core.Import;
using Fubar.Studio.UI.Services;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>
/// Backs the OpenAPI/Swagger import dialog. The user parses a source (file path or URL) into a plan,
/// which is diffed against the current workspace; the diff is shown as tickable request/variable rows
/// (add / update / unchanged / remove) so the user chooses exactly what to apply - manual edits they
/// didn't tick are left alone. Nothing is written here; the caller applies the returned selection.
/// </summary>
public partial class ImportOpenApiViewModel : ViewModelBase
{
    private readonly IOpenApiImportService _import;
    private readonly IFilePickerService _filePicker;
    private readonly string _workspaceRoot;

    public ImportOpenApiViewModel(IOpenApiImportService import, IFilePickerService filePicker, string workspaceRoot)
    {
        _import = import;
        _filePicker = filePicker;
        _workspaceRoot = workspaceRoot;
    }

    [ObservableProperty]
    public partial string Source { get; set; } = "";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    public partial bool CreateAuthProfiles { get; set; } = true;

    [ObservableProperty]
    public partial OpenApiImportDiff? Diff { get; set; }

    public ObservableCollection<ImportItemViewModel> RequestItems { get; } = [];
    public ObservableCollection<ImportItemViewModel> VariableItems { get; } = [];
    public ObservableCollection<string> Warnings { get; } = [];

    private OpenApiImportPlan? _plan;

    public bool HasDiff => Diff is not null;
    public bool HasWarnings => Warnings.Count > 0;

    public string Summary => Diff is null
        ? ""
        : $"{Diff.ApiTitle}  —  {Count(RequestItems, ImportChange.Add)} new · {Count(RequestItems, ImportChange.Update)} changed · " +
          $"{Count(RequestItems, ImportChange.Unchanged)} unchanged · {Count(RequestItems, ImportChange.Remove)} removed requests";

    private static int Count(IEnumerable<ImportItemViewModel> items, ImportChange change) => items.Count(i => i.Change == change);

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var path = await _filePicker.PickOpenFileAsync("Choose an OpenAPI / Swagger spec (JSON or YAML)");
        if (path is not null)
        {
            Source = path;
        }
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (string.IsNullOrWhiteSpace(Source))
        {
            StatusMessage = "Choose a file or enter a URL first.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Reading, parsing and comparing...";
        try
        {
            _plan = await _import.ParseAsync(Source.Trim());
            Diff = await _import.DiffAsync(_plan, _workspaceRoot);
            PopulateFromDiff(Diff);
            StatusMessage = null;
        }
        catch (Exception ex)
        {
            _plan = null;
            Diff = null;
            RequestItems.Clear();
            VariableItems.Clear();
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasDiff));
            OnPropertyChanged(nameof(Summary));
        }
    }

    private void PopulateFromDiff(OpenApiImportDiff diff)
    {
        RequestItems.Clear();
        foreach (var request in diff.Requests.OrderBy(r => r.Change).ThenBy(r => r.DisplayName))
        {
            var detail = request.FolderName.Length > 0 ? $"{request.Method} · {request.FolderName}/" : request.Method;
            RequestItems.Add(new ImportItemViewModel(request, request.Change, request.DisplayName, detail));
        }

        VariableItems.Clear();
        foreach (var variable in diff.Variables.OrderBy(v => v.EnvironmentName).ThenBy(v => v.Key))
        {
            var detail = DescribeVariableChange(variable);
            VariableItems.Add(new ImportItemViewModel(variable, variable.Change, $"{variable.EnvironmentName} · {variable.Key}", detail));
        }

        Warnings.Clear();
        foreach (var warning in diff.Warnings)
        {
            Warnings.Add(warning);
        }

        OnPropertyChanged(nameof(HasWarnings));
    }

    private static string DescribeVariableChange(VariableDiff variable)
    {
        if (variable.IsSecret)
        {
            return "secret";
        }

        return variable.Change switch
        {
            ImportChange.Update => $"\"{variable.ExistingValue}\" → \"{variable.NewValue}\"",
            ImportChange.Remove => $"was \"{variable.ExistingValue}\"",
            _ => $"\"{variable.NewValue}\"",
        };
    }

    [RelayCommand]
    private void SelectAllRequests() => SetAll(RequestItems, true);

    [RelayCommand]
    private void SelectNoRequests() => SetAll(RequestItems, false);

    [RelayCommand]
    private void SelectAllVariables() => SetAll(VariableItems, true);

    [RelayCommand]
    private void SelectNoVariables() => SetAll(VariableItems, false);

    private static void SetAll(IEnumerable<ImportItemViewModel> items, bool selected)
    {
        foreach (var item in items.Where(i => i.CanSelect))
        {
            item.IsSelected = selected;
        }
    }

    public event Action<ImportDialogResult?>? CloseRequested;

    [RelayCommand]
    private void Import()
    {
        if (_plan is null)
        {
            return;
        }

        var selectedRequests = RequestItems.Where(i => i.IsSelected).Select(i => (RequestDiff)i.Model).ToList();
        var selectedVariables = VariableItems.Where(i => i.IsSelected).Select(i => (VariableDiff)i.Model).ToList();
        var options = new OpenApiImportOptions { CreateAuthProfiles = CreateAuthProfiles };

        CloseRequested?.Invoke(new ImportDialogResult(_plan, selectedRequests, selectedVariables, options));
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(null);
}
