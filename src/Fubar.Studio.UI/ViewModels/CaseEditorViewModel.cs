using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Studio.Core.Json;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Workspaces;
using Fubar.Studio.UI.Services;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>
/// One <c>cases/&lt;name&gt;.json</c> in the main canvas.
/// </summary>
/// <remarks>
/// <para>Deliberately NOT a second request editor. A case says what is different about one call -
/// its path parameters, its query, what it expects back - and showing it the method, the URL and the
/// auth again would invite editing them here, where the edit would either be lost or would silently
/// fork the endpoint. Those are shown, greyed, as context: <see cref="EndpointSummary"/>.</para>
/// <para>What the case leaves alone it inherits, and every grid here is an override on top of the
/// endpoint's - see <c>CaseMerge</c>, which is where that folding actually happens.</para>
/// </remarks>
public partial class CaseEditorViewModel : ViewModelBase, ISaveableEditor
{
    private readonly IEndpointStore _endpoints;
    private readonly StatusLogViewModel _statusLog;
    private readonly string _id;

    public CaseEditorViewModel(
        EndpointCase endpointCase,
        RequestModel endpoint,
        string filePath,
        Workspace workspace,
        IEndpointStore endpoints,
        IFilePickerService filePickerService,
        Core.Json.IJsonSchemaValidator schemaValidator,
        StatusLogViewModel statusLog)
    {
        ArgumentNullException.ThrowIfNull(endpointCase);
        ArgumentNullException.ThrowIfNull(endpoint);

        _endpoints = endpoints;
        _statusLog = statusLog;
        _id = endpointCase.Id;

        FilePath = filePath;
        Workspace = workspace;
        Name = endpointCase.Name;
        Description = endpointCase.Description ?? "";
        EndpointName = endpoint.Name;
        EndpointSummary = $"{endpoint.Method} {endpoint.Url}";

        // Seeded from the endpoint's URL, so a case opens with the questions it actually has to
        // answer rather than an empty grid and a URL to squint at.
        foreach (var parameter in CaseMerge.PathParamNames(endpoint.Url))
        {
            PathParams.Add(new KeyValueRowViewModel
            {
                Key = parameter,
                Value = endpointCase.PathParams.GetValueOrDefault(parameter, ""),
                Enabled = true,
            });
        }

        // Anything the case sets that the URL no longer mentions is KEPT and shown, rather than
        // dropped on load: silently discarding it would make renaming a placeholder lose the value
        // without saying so.
        foreach (var (key, value) in endpointCase.PathParams)
        {
            if (!PathParams.Any(p => string.Equals(p.Key, key, StringComparison.Ordinal)))
            {
                PathParams.Add(new KeyValueRowViewModel { Key = key, Value = value, Enabled = true });
            }
        }

        QueryParams = new KeyValueGridViewModel(endpointCase.QueryParams);
        Headers = new KeyValueGridViewModel(endpointCase.Headers);

        OverridesBody = endpointCase.Body is not null;
        Body = RequestBodyViewModel.FromModel(
            endpointCase.Body ?? endpoint.Body, filePickerService, schemaValidator);

        Tests = new RequestTestsViewModel(new RequestModel
        {
            Name = endpointCase.Name,
            Assertions = endpointCase.Assertions,
            Captures = endpointCase.Captures,
        })
        {
            // A case has no timeout of its own - it inherits the endpoint's. Showing the control
            // would promise an override the format does not have, and the value typed into it would
            // vanish on save.
            ShowTimeout = false,
        };

        QueryParams.Changed += MarkDirty;
        Headers.Changed += MarkDirty;
        Body.Changed += MarkDirty;
        Tests.Changed += MarkDirty;

        foreach (var row in PathParams)
        {
            row.PropertyChanged += (_, _) => MarkDirty();
        }
    }

    public string FilePath { get; private set; }

    public Workspace Workspace { get; }

    /// <summary>The endpoint this is a case of - shown as context, not editable here.</summary>
    public string EndpointName { get; }

    /// <summary>"GET {{baseUrl}}/orders/{orderId}" - the operation, so the placeholders below have
    /// something to be read against.</summary>
    public string EndpointSummary { get; }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string Description { get; set; }

    /// <summary>Values for the endpoint's <c>{param}</c> placeholders.</summary>
    public ObservableCollection<KeyValueRowViewModel> PathParams { get; } = [];

    public KeyValueGridViewModel QueryParams { get; }

    public KeyValueGridViewModel Headers { get; }

    public RequestBodyViewModel Body { get; }

    public RequestTestsViewModel Tests { get; }

    /// <summary>
    /// Whether this case sends its own body or the endpoint's.
    /// </summary>
    /// <remarks>
    /// A checkbox rather than "an empty body means inherit", because "send nothing" is a decision a
    /// case legitimately makes and there would otherwise be no way to write it down.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BodyHint))]
    public partial bool OverridesBody { get; set; }

    public string BodyHint => OverridesBody
        ? "This case sends its own body."
        : "This case sends the endpoint's body. Tick to send something else.";

    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    partial void OnNameChanged(string value) => MarkDirty();

    partial void OnDescriptionChanged(string value) => MarkDirty();

    partial void OnOverridesBodyChanged(bool value) => MarkDirty();

    private void MarkDirty() => IsDirty = true;

    /// <summary>Raised after a successful save, so the tree can refresh.</summary>
    public event Action? Saved;

    [RelayCommand]
    public async Task SaveAsync()
    {
        try
        {
            await _endpoints.SaveCaseAsync(FilePath, ToModel());
            IsDirty = false;
            _statusLog.Log($"Saved case \"{Name}\".");
            Saved?.Invoke();
        }
        catch (Exception ex)
        {
            _statusLog.LogError($"Could not save \"{Name}\": {ex.Message}");
        }
    }

    public EndpointCase ToModel()
    {
        var model = new EndpointCase
        {
            Id = _id,
            Name = Name,
            Description = string.IsNullOrWhiteSpace(Description) ? null : Description,
            QueryParams = QueryParams.ToModel(),
            Headers = Headers.ToModel(),
            Body = OverridesBody ? Body.ToModel() : null,
            Assertions = Tests.AssertionsToModel(),
            Captures = Tests.CapturesToModel(),
        };

        foreach (var row in PathParams)
        {
            if (!string.IsNullOrWhiteSpace(row.Key))
            {
                model.PathParams[row.Key] = row.Value ?? "";
            }
        }

        return model;
    }
}
