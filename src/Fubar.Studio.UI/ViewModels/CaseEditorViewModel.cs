using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Studio.Core.Json;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Running;
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
    private readonly Fubar.Studio.Application.Running.ICollectionRunService _runs;
    private readonly EnvironmentManagerViewModel _environments;
    private readonly string _id;
    private CancellationTokenSource? _sending;

    /// <summary>
    /// This case's own rules, carried through a save whether or not the Rules tab was opened.
    /// </summary>
    /// <remarks>
    /// These used to be dropped: <c>ToModel</c> built a fresh <see cref="EndpointCase"/> and never
    /// copied them, so a case carrying a tolerance lost it the first time anyone pressed Ctrl+S -
    /// silently, and in a file whose whole job is to say what may differ.
    /// </remarks>
    private ComparisonSettings? _comparison;
    private List<Core.Comparison.Tolerance>? _tolerances;

    public CaseEditorViewModel(
        EndpointCase endpointCase,
        RequestModel endpoint,
        string filePath,
        Workspace workspace,
        IEndpointStore endpoints,
        IFilePickerService filePickerService,
        Core.Json.IJsonSchemaValidator schemaValidator,
        StatusLogViewModel statusLog,
        Fubar.Studio.Application.Running.ICollectionRunService runs,
        EnvironmentManagerViewModel environments,
        ResponsePanelViewModel response,
        Fubar.Studio.Application.Comparison.IRequestComparisonSettings comparisonSettings)
    {
        ArgumentNullException.ThrowIfNull(endpointCase);
        ArgumentNullException.ThrowIfNull(endpoint);

        _endpoints = endpoints;
        _statusLog = statusLog;
        _runs = runs;
        _environments = environments;
        _id = endpointCase.Id;
        _comparison = endpointCase.Comparison;
        _tolerances = endpointCase.Tolerances;

        Response = response;
        EndpointPath = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(filePath))!,
            IEndpointStore.EndpointFileName);

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

        // A case is the innermost level for comparison rules and tolerances, and no level at all for
        // snapshot policy - a snapshot belongs to the endpoint. The tab says so rather than offering
        // a control whose value would be dropped on save.
        Rules = new RulesViewModel(
            new RuleLevel
            {
                Scope = Core.Comparison.ComparisonScope.Case,
                LevelName = "this case",
                GetComparison = () => _comparison,
                SetComparison = value => _comparison = value,
                GetTolerances = () => _tolerances,
                SetTolerances = value => _tolerances = value,
                Changed = MarkDirty,
            },
            workspace,
            EndpointPath,
            filePath,
            comparisonSettings,
            statusLog);

        _ = Rules.RefreshAsync();
    }

    /// <summary>Every rule that applies to this case, and where each came from.</summary>
    public RulesViewModel Rules { get; }

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

    /// <summary>The endpoint's own file - what a run of this case reads for the method, URL and auth.</summary>
    public string EndpointPath { get; }

    /// <summary>What came back, for the response the last Send produced.</summary>
    /// <remarks>
    /// Headers are not filled in here. A single-step run reports a body, a status and a time; the
    /// response HEADERS are not on <c>StepReport</c>, so the tab would show an empty list rather than
    /// nothing - see <see cref="ResponseNote"/>, which says so on screen rather than leaving it to be
    /// discovered.
    /// </remarks>
    public ResponsePanelViewModel Response { get; }

    [ObservableProperty]
    public partial bool IsSending { get; set; }

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

    /// <summary>
    /// Sends this one case and shows what came back.
    /// </summary>
    /// <remarks>
    /// <para>Through the ORDINARY run pipeline, as a plan of one step. The spec's own rule (§2): "the
    /// pipeline is the same for one case and for a batch of two hundred; there is no separate run-a-
    /// single-request path, which is what stops the two from drifting." Auth resolution, variable
    /// resolution, captures and assertions are then identical to what CI will do, by construction
    /// rather than by two implementations agreeing.</para>
    /// <para>It SAVES first, because the runner reads from disk. That is the honest behaviour for
    /// something whose whole purpose is to be repeatable - and it is the same thing the run window
    /// says about a collection - but it does mean pressing Send commits the edit.</para>
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        if (IsDirty)
        {
            await SaveAsync();
            if (IsDirty)
            {
                return;
            }
        }

        IsSending = true;
        SendCommand.NotifyCanExecuteChanged();
        CancelSendCommand.NotifyCanExecuteChanged();

        _sending = new CancellationTokenSource();
        try
        {
            var step = new RunStep(1, EndpointName, EndpointPath, System.IO.Path.GetDirectoryName(EndpointPath)!, Name, FilePath);

            var report = await _runs.RunAsync(
                new Fubar.Studio.Application.Running.CollectionRun(
                    new RunPlan([step]),
                    Workspace,
                    _environments.ActiveEnvironment,
                    RunOptions.Default with { CaptureResponseBodies = true, RecordHistory = true }),
                progress: null,
                _sending.Token);

            Apply(report.Steps.Count > 0 ? report.Steps[0] : null);
        }
        catch (OperationCanceledException)
        {
            _statusLog.Log($"Cancelled sending \"{Name}\".");
        }
        catch (Exception ex)
        {
            _statusLog.LogError($"Could not send \"{Name}\": {ex.Message}");
        }
        finally
        {
            _sending?.Dispose();
            _sending = null;
            IsSending = false;
            SendCommand.NotifyCanExecuteChanged();
            CancelSendCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanSend() => !IsSending;

    [RelayCommand(CanExecute = nameof(CanCancelSend))]
    private void CancelSend() => _sending?.Cancel();

    private bool CanCancelSend() => IsSending;

    /// <summary>What the response pane shows about a send that has not happened, or a limit of this
    /// screen. Said on screen rather than left to be discovered.</summary>
    [ObservableProperty]
    public partial string? ResponseNote { get; set; }

    private void Apply(StepReport? step)
    {
        if (step is null)
        {
            ResponseNote = "Nothing was sent.";
            return;
        }

        var environment = _environments.ActiveEnvironment?.Name;
        Response.SourceLabel = environment is null
            ? $"{EndpointName}#{Name}"
            : $"{EndpointName}#{Name} · {environment}";

        Response.HasResponse = true;
        Response.ElapsedMilliseconds = step.ElapsedMilliseconds;
        Response.SizeBytes = step.SizeBytes;
        Response.ContentType = step.ContentType;

        if (step.Error is { Length: > 0 } error)
        {
            Response.StatusCode = 0;
            Response.StatusText = "Error";
            Response.LoadBody(error, []);
            _statusLog.LogError($"{EndpointName}#{Name}: {error}");
        }
        else
        {
            Response.StatusCode = step.StatusCode ?? 0;
            Response.StatusText = step.ReasonPhrase ?? "";
            Response.LoadBody(Pretty(step.ResponseBody ?? ""), []);
            _statusLog.Log(
                $"{EndpointName}#{Name}: {step.StatusCode} {step.ReasonPhrase} - {step.ElapsedMilliseconds} ms");
        }

        Response.SetTestResults(step.Assertions);

        // Named, never valued - the same rule the run report and the status log already follow.
        foreach (var capture in step.Captures)
        {
            if (capture.Ok)
            {
                _statusLog.Log($"Captured {{{{{capture.VariableName}}}}} → {capture.Scope}");
            }
            else
            {
                _statusLog.LogWarning($"Capture \"{capture.VariableName}\" failed: {capture.Error}");
            }
        }

        ResponseNote = step.BodyTooLargeToCompare
            ? "The response was too large to keep, so only its status and timing are shown."
            : "Response headers are not shown here - open the endpoint to send it with the full response pane.";
    }

    private static string Pretty(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return body;
        }

        try
        {
            return System.Text.Json.Nodes.JsonNode.Parse(body)
                ?.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) ?? body;
        }
        catch (System.Text.Json.JsonException)
        {
            return body;
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
            Comparison = _comparison,
            Tolerances = _tolerances,
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
