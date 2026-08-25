using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Studio.Application.Requests;
using Fubar.Studio.Core.Auth;
using Fubar.Studio.Core.History;
using Fubar.Studio.Core.Http;
using Fubar.Studio.Core.Import;
using Fubar.Studio.Core.Json;
using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Protocols;
using Fubar.Studio.Core.Testing;
using Fubar.Studio.Core.Variables;
using Fubar.Studio.Core.Workspaces;
using Fubar.Studio.UI.Controls;
using Fubar.Studio.UI.Services;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>
/// The single active request canvas (RequestEditorPane.md §1): Address Bar state (with
/// bidirectional URL/Params sync - §3), the Params/Headers/Body/Auth/History builder tabs, and
/// <see cref="IsDirty"/> tracking for the Left Pane's unsaved-changes dot.
/// <see cref="MainViewModel.ActiveRequest"/> holds exactly one of these at a time - there are no
/// request tabs. <see cref="Provider"/> is resolved once via <c>IProtocolRegistry</c> at open time
/// and supplies the Address Bar's method list; <see cref="SendAsync"/> resolves its
/// <c>IRequestExecutor</c> via <c>IExecutorRegistry</c> the same way - nothing here is hardcoded to
/// HTTP, so a GraphQL/WebSocket request document works identically once those protocols register
/// their own provider + executor.
/// </summary>
public partial class RequestEditorViewModel : ViewModelBase, IDisposable
{
    private readonly Workspace _workspace;
    private readonly EnvironmentManagerViewModel _environmentManager;
    private readonly IRequestStore _requestStore;
    private readonly IAuthProfileStore _authProfileStore;
    private readonly IInheritanceResolver _inheritanceResolver;
    private readonly IRequestExecutionService _requestExecution;
    private readonly IHistoryService _historyService;
    private readonly IDiffPreviewService _diffPreview;
    private readonly ICurlExportService _curlExport;
    private readonly IClipboardService _clipboardService;
    private readonly IVariableResolver _variableResolver;
    private readonly IAuthProvider _authProvider;
    private readonly StatusLogViewModel _statusLog;
    private readonly RequestModel _original;

    /// <summary>Cancels the in-flight Send (the Send button becomes Cancel while a request is running).</summary>
    private CancellationTokenSource? _sendCts;

    private bool _syncingUrlAndParams;
    private bool _loadingWorkspaceContext;
    private InheritanceChain? _inheritanceChain;
    private List<AuthProfile> _authProfiles = [];

    /// <summary>True only during the constructor's initial population below - guards
    /// <see cref="MarkDirty"/> so setting <see cref="Method"/>/<see cref="Url"/> (and the
    /// <see cref="SyncParamsFromUrl"/> that <see cref="OnUrlChanged"/> triggers) from the loaded
    /// <see cref="RequestModel"/> never itself flags a freshly-opened request as unsaved.</summary>
    private bool _populating = true;

    public RequestEditorViewModel(
        RequestModel request,
        string filePath,
        IProtocolProvider provider,
        Workspace workspace,
        EnvironmentManagerViewModel environmentManager,
        IRequestStore requestStore,
        IAuthProfileStore authProfileStore,
        IInheritanceResolver inheritanceResolver,
        IRequestExecutionService requestExecution,
        IHistoryService historyService,
        ICurlExportService curlExport,
        IJsonSchemaValidator schemaValidator,
        IJsonPathEvaluator jsonPathEvaluator,
        IVariableResolver variableResolver,
        IAuthProvider authProvider,
        IClipboardService clipboardService,
        IFilePickerService filePickerService,
        StatusLogViewModel statusLog,
        IDiffPreviewService diffPreview)
    {
        _original = request;
        _workspace = workspace;
        _environmentManager = environmentManager;
        _requestStore = requestStore;
        _authProfileStore = authProfileStore;
        _inheritanceResolver = inheritanceResolver;
        _requestExecution = requestExecution;
        _historyService = historyService;
        _curlExport = curlExport;
        _clipboardService = clipboardService;
        _variableResolver = variableResolver;
        _authProvider = authProvider;
        _statusLog = statusLog;
        _diffPreview = diffPreview;

        FilePath = filePath;
        Provider = provider;
        Name = request.Name;

        // Constructed before Method/Url are set below: setting Url triggers OnUrlChanged, which
        // calls SyncParamsFromUrl - that dereferences Params.Rows, so Params must already exist.
        Params = new KeyValueGridViewModel(request.QueryParams);
        Headers = new HeadersTabViewModel(request.Headers, request.SuppressedInheritedHeaderKeys);
        Tests = new RequestTestsViewModel(request);

        // Schema-aware key hints for the Params/Headers editors: the OpenAPI importer stashes the
        // operation's declared query/header parameter names under Settings.fubarOpenApi.parameters.
        // Params suggestions are null (no autocomplete, plain textbox) unless the spec declared some;
        // Header suggestions always include the common HTTP request headers, with spec-declared ones first.
        var schemaParams = request.Settings?["fubarOpenApi"]?["parameters"];
        var schemaQuery = ReadNames(schemaParams?["query"]);
        QueryParamSuggestions = schemaQuery.Count > 0 ? schemaQuery : null;
        HeaderNameSuggestions = ReadNames(schemaParams?["header"])
            .Concat(HttpHeaderNames.Common)
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .ToList();
        // A body JSON Schema stashed by the OpenAPI importer (Settings.fubarOpenApi.bodySchema) powers
        // the Body editor's schema validation.
        var bodySchema = request.Settings?["fubarOpenApi"]?["bodySchema"]?.ToJsonString();
        Body = RequestBodyViewModel.FromModel(request.Body, filePickerService, schemaValidator, bodySchema);
        Auth = new RequestAuthViewModel(new TokenRequestEditorViewModel(filePickerService, schemaValidator));
        Auth.LoadFrom(request.Auth);
        Response = new ResponsePanelViewModel(clipboardService, filePickerService, statusLog, schemaValidator, jsonPathEvaluator);
        // A success-response JSON Schema stashed by the OpenAPI importer lets the Response pane validate
        // what actually comes back against what the spec promised.
        Response.SetResponseSchema(request.Settings?["fubarOpenApi"]?["responseSchema"]?.ToJsonString());

        Method = request.Method;
        Url = request.Url;

        // Wired up only after the population above, so loading a request from disk never itself
        // flips IsDirty - only a subsequent user edit does. MarkDirty's own _populating/
        // _loadingWorkspaceContext guards cover the OnMethodChanged/OnUrlChanged hooks above and
        // the later async LoadWorkspaceContextAsync population the same way.
        Params.Changed += () => { SyncUrlFromParams(); MarkDirty(); };
        Headers.Changed += MarkDirty;
        Tests.Changed += MarkDirty;
        Body.PropertyChanged += (_, _) => MarkDirty();
        Body.Changed += MarkDirty;

        // Recomputing on every Auth change (not just at load) - a directly-selected profile, an
        // inline Bearer/API key, or a folder-inherited profile - is what makes a profile's header
        // show up (read-only, still toggleable) on every request using it, including live while
        // the Auth tab is being edited, not just on next open.
        Auth.PropertyChanged += (_, _) => { MarkDirty(); RecomputeAuthHeaders(); };

        // Edits inside the OAuth2 token-request editor (URL/headers/body/captures/token variable) also
        // dirty the request and can change the derived Authorization header (it references the access-token
        // variable), so recompute the same way.
        Auth.OAuth2.Changed += () => { MarkDirty(); RecomputeAuthHeaders(); };

        // The Auth tab's Test button acquires an OAuth token via the provider against the live
        // workspace/environment, storing it in session variables; Verify previews the request.
        Auth.OAuth2.TestAuthHandler = async config => (await _authProvider.PrepareAsync(config, _workspace, _environmentManager.ActiveEnvironment)).Outcome;
        Auth.OAuth2.PreviewHandler = config => _authProvider.PreviewTokenRequest(config, _workspace, _environmentManager.ActiveEnvironment);
        Auth.OAuth2.VariableContext = VariableContext;

        // The active environment/secrets-reveal choice can change while this request stays open -
        // re-raise VariableContext's own change so every bound TextBox's tooltip/border re-evaluates.
        _environmentManager.PropertyChanged += OnEnvironmentManagerPropertyChanged;

        _populating = false;
        _ = LoadWorkspaceContextAsync();
    }

    private void OnEnvironmentManagerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EnvironmentManagerViewModel.ActiveEnvironment) or nameof(EnvironmentManagerViewModel.SecretsRevealed))
        {
            OnPropertyChanged(nameof(VariableContext));
            Auth.OAuth2.VariableContext = VariableContext;
        }
    }

    /// <summary>Unsubscribes from <see cref="EnvironmentManagerViewModel"/> - it outlives every
    /// individual request (it's a shared singleton), so without this each replaced
    /// <see cref="MainViewModel.ActiveRequest"/> would leak forever as a dangling event handler.</summary>
    public void Dispose() => _environmentManager.PropertyChanged -= OnEnvironmentManagerPropertyChanged;

    /// <summary>The workspace this request belongs to - used by MainViewModel to clear the main
    /// canvas when that workspace's tab is closed.</summary>
    public Workspace Workspace => _workspace;

    /// <summary>Absolute path to this request's <c>request.json</c> - used by Save and to detect an already-open request.</summary>
    public string FilePath { get; }

    public IProtocolProvider Provider { get; }

    public string Name { get; }

    [ObservableProperty]
    public partial string Method { get; set; }

    [ObservableProperty]
    public partial string Url { get; set; }

    [ObservableProperty]
    public partial bool IsSending { get; set; }

    /// <summary>True once any field has been edited since load/save - drives the Left Pane's unsaved-changes dot.</summary>
    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    public string SendButtonText => IsSending ? "Sending..." : "Send";

    /// <summary>The Universal Variable Tooltip system's context for this request (RequestEditorPane.md
    /// §4) - bind <c>controls:VariableTooltip.Context</c> to this on the URL box and any header cell.
    /// Computed fresh from the live active environment/secrets-reveal state on every access - see
    /// <see cref="OnEnvironmentManagerPropertyChanged"/> for what triggers rebinding.</summary>
    public VariableTooltipContext VariableContext =>
        new(_variableResolver, _workspace, _environmentManager.ActiveEnvironment, _environmentManager.SecretsRevealed);

    partial void OnIsSendingChanged(bool value) => OnPropertyChanged(nameof(SendButtonText));

    partial void OnMethodChanged(string value) => MarkDirty();

    partial void OnUrlChanged(string value)
    {
        MarkDirty();
        SyncParamsFromUrl(value);
    }

    private void MarkDirty()
    {
        if (!_populating && !_loadingWorkspaceContext)
        {
            IsDirty = true;
        }
    }

    public KeyValueGridViewModel Params { get; }

    public HeadersTabViewModel Headers { get; }

    /// <summary>The Tests tab: per-request timeout, response-capture rules, and assertions.</summary>
    public RequestTestsViewModel Tests { get; }

    /// <summary>Candidate query-parameter names offered as key autocompletion in the Params grid, or
    /// null when the spec declared none (the grid then shows a plain key textbox). Populated from an
    /// imported OpenAPI operation's declared query parameters.</summary>
    public IReadOnlyList<string>? QueryParamSuggestions { get; }

    /// <summary>Candidate header names offered as key autocompletion in the Headers editor - the
    /// operation's schema-declared header parameters (if any) followed by common HTTP request headers.</summary>
    public IReadOnlyList<string> HeaderNameSuggestions { get; }

    /// <summary>Reads a JSON array of strings (the importer's stashed parameter names) into a list,
    /// tolerating a missing/non-array node.</summary>
    private static List<string> ReadNames(JsonNode? node) =>
        node is JsonArray arr
            ? arr.Select(n => n?.GetValue<string>()).Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).ToList()
            : [];

    public RequestBodyViewModel Body { get; }

    public RequestAuthViewModel Auth { get; }

    public ResponsePanelViewModel Response { get; }

    public ObservableCollection<HistoryEntryViewModel> ExecutionHistory { get; } = [];

    /// <summary>Raised after a successful Save - MainViewModel uses this to refresh the Left Pane
    /// tree node's method/auth badges, which otherwise only refresh on external file-system events.</summary>
    public event Action? Saved;

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            await _requestStore.SaveRequestAsync(FilePath, BuildRequestModel());
            IsDirty = false;
            _statusLog.Log($"Saved: {FilePath}");
            Saved?.Invoke();
        }
        catch (Exception ex)
        {
            _statusLog.Log($"Save failed: {ex.Message}");
        }
    }

    /// <summary>Copies the current request as a runnable curl command (variables resolved against the
    /// active environment, all enabled headers incl. auth included) to the clipboard.</summary>
    [RelayCommand]
    private async Task CopyAsCurlAsync()
    {
        try
        {
            var model = BuildRequestModel();
            // Direct + folder-inherited headers, then the real (resolved) auth credential injected the same
            // way the Send pipeline does - so cURL matches what actually goes on the wire.
            model.Headers = Headers.SendableToModel();
            var environment = _environmentManager.ActiveEnvironment;
            if (ResolveEffectiveAuth().Config is { } effectiveAuth)
            {
                model = AuthRequestMerge.Inject(model, _authProvider.Apply(effectiveAuth, _workspace, environment));
            }

            var curl = _curlExport.ToCurl(model, s => _variableResolver.Substitute(s, _workspace, environment));
            await _clipboardService.SetTextAsync(curl);
            _statusLog.Log("Copied request as curl.");
        }
        catch (Exception ex)
        {
            _statusLog.Log($"Copy as curl failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        if (IsSending)
        {
            return;
        }

        IsSending = true;
        _statusLog.Log($"Sending {Method} {Url}");

        _sendCts = new CancellationTokenSource();
        try
        {
            // Send direct + folder-inherited headers; the pipeline's auth prestep injects the resolved
            // credential (auth-derived rows in the Headers tab are only a preview).
            var model = BuildRequestModel();
            model.Headers = Headers.SendableToModel();
            var run = new RequestRun(model, _workspace, _environmentManager.ActiveEnvironment, ResolveEffectiveAuth().Config);
            ApplyRunResult(await _requestExecution.RunAsync(run, _sendCts.Token));
        }
        finally
        {
            _sendCts?.Dispose();
            _sendCts = null;
            IsSending = false;
        }
    }

    /// <summary>Cancels the in-flight Send (surfaced as a Cancel button that replaces Send while running).</summary>
    [RelayCommand]
    private void CancelSend() => _sendCts?.Cancel();

    /// <summary>Maps a completed run onto the UI: response pane, auth/capture log lines, assertion results,
    /// and a new history entry. The orchestration itself lives in <see cref="IRequestExecutionService"/>.</summary>
    private void ApplyRunResult(RequestRunResult outcome)
    {
        if (outcome.Auth is { Ok: false } auth)
        {
            _statusLog.Log($"Auth: {auth.Message}");
        }

        ApplyResultToResponse(outcome.Result);

        if (outcome.Result.IsSuccess)
        {
            foreach (var c in outcome.Captures)
            {
                _statusLog.Log(c.Ok
                    ? $"Captured {{{{{c.VariableName}}}}} = \"{Truncate(c.Value)}\" ({c.Scope})"
                    : $"Capture \"{c.VariableName}\" failed: {c.Error}");
            }

            if (outcome.Captures.Count > 0)
            {
                // A capture writing to the active environment changes what {{tokens}} resolve to.
                OnPropertyChanged(nameof(VariableContext));
            }

            Response.SetTestResults(outcome.Assertions);
        }
        else
        {
            Response.ClearTestResults();
        }

        if (outcome.HistorySnapshot is { } snapshot)
        {
            ExecutionHistory.Insert(0, new HistoryEntryViewModel(snapshot));
        }

        if (outcome.HistoryError is { } historyError)
        {
            _statusLog.Log($"Failed to record history for \"{Name}\": {historyError}");
        }
    }

    private static string Truncate(string? value) =>
        value is { Length: > 40 } ? value[..40] + "…" : value ?? "";

    /// <summary>
    /// Diffs a past response against the one on screen - "did this change, and where?", which is the
    /// question Replay leaves unanswered. The history entry is the left (older) side, matching the
    /// old-on-the-left convention the diff view uses everywhere else.
    /// </summary>
    [RelayCommand]
    private async Task CompareWithHistoryAsync(HistoryEntryViewModel? entry)
    {
        // Both guards are also enforced in the view, but a command must not depend on its binding
        // being the only caller.
        if (entry?.Snapshot.ResponseBody is not { } previous || !Response.HasResponse)
        {
            return;
        }

        await _diffPreview.ShowAsync(
            previous,
            Response.RawBody,
            entry.CompareLabel,
            "Current response",
            $"{Method} {Name} — response vs history");
    }

    /// <summary>
    /// Re-sends exactly what <paramref name="entry"/> captured (method/URL/headers/body), but
    /// re-resolving <c>{{variable}}</c> tokens against whichever environment is active *now* -
    /// RequestEditorPane.md §6's Replay Workflow. Records a brand-new history entry rather than
    /// overwriting <paramref name="entry"/>.
    /// </summary>
    [RelayCommand]
    private async Task ReplayHistoryAsync(HistoryEntryViewModel? entry)
    {
        if (entry is null || IsSending)
        {
            return;
        }

        IsSending = true;
        _statusLog.Log($"Replaying {entry.Snapshot.Method} {entry.Snapshot.Url}");

        try
        {
            var replay = new RequestModel
            {
                Id = _original.Id,
                Name = Name,
                Kind = _original.Kind,
                Method = entry.Snapshot.Method,
                Url = entry.Snapshot.Url,
                Headers = entry.Snapshot.Headers,
                Body = new RequestBody { Type = _original.Body.Type, Raw = entry.Snapshot.Body },
            };

            // Replay doesn't re-run auth or the request's tests - just re-send exactly what was captured.
            var run = new RequestRun(replay, _workspace, _environmentManager.ActiveEnvironment, EffectiveAuth: null);
            ApplyRunResult(await _requestExecution.RunAsync(run));
        }
        finally
        {
            IsSending = false;
        }
    }

    /// <summary>
    /// Loads header/auth inheritance (RequestEditorPane.md §5) and the workspace's auth profile
    /// library, plus this request's execution history - all only reachable asynchronously, so it
    /// runs right after construction rather than blocking it. Guarded by
    /// <see cref="_loadingWorkspaceContext"/> so populating <see cref="Auth"/>'s
    /// <c>AvailableProfiles</c>/<c>SelectedProfile</c> and <see cref="Headers"/>'s inherited rows
    /// doesn't itself mark the request dirty.
    /// </summary>
    private async Task LoadWorkspaceContextAsync()
    {
        _loadingWorkspaceContext = true;
        try
        {
            _inheritanceChain = await _inheritanceResolver.GetInheritanceChainAsync(_workspace.RootPath, FilePath);
            _authProfiles = [.. await _authProfileStore.LoadAuthProfilesAsync(_workspace.RootPath)];

            foreach (var profile in _authProfiles)
            {
                Auth.AvailableProfiles.Add(profile);
            }

            if (_original.AuthProfileId is { } selectedId)
            {
                Auth.SelectedProfile = _authProfiles.FirstOrDefault(p => p.Id == selectedId);
            }

            Headers.LoadInherited(_inheritanceChain);
            RecomputeAuthHeaders();

            var history = await _historyService.LoadAsync(_workspace.RootPath, _original.Id);
            foreach (var snapshot in history)
            {
                ExecutionHistory.Add(new HistoryEntryViewModel(snapshot));
            }
        }
        catch (Exception ex)
        {
            _statusLog.Log($"Failed to load header/auth inheritance for \"{Name}\": {ex.Message}");
        }
        finally
        {
            _loadingWorkspaceContext = false;
        }
    }

    /// <summary>
    /// Determines which auth (if any) implies a header, and feeds it to
    /// <see cref="HeadersTabViewModel.RefreshAuthHeaders"/>: a directly-selected profile takes
    /// priority (that's what makes a profile's header show up, read-only but toggleable, on every
    /// request that uses it), then an inline Bearer/API key set on this request, then - only when
    /// this request is set to Inherit - whatever profile the folder chain resolves to. Called both
    /// from <see cref="LoadWorkspaceContextAsync"/> (once <see cref="_inheritanceChain"/>/
    /// <see cref="_authProfiles"/> are populated) and, via the constructor's Auth.PropertyChanged
    /// wiring, live whenever the Auth tab changes afterward.
    /// </summary>
    private void RecomputeAuthHeaders()
    {
        var effective = ResolveEffectiveAuth();
        var applied = effective.Config is null ? AppliedAuth.Empty : AuthApplier.BuildPreview(effective.Config);
        Headers.RefreshAuthHeaders(applied.Headers, effective.Source);
        RefreshAuthParams(applied.QueryParams, effective.Source);
    }

    /// <summary>Read-only auth placeholder rows shown in the Params tab (API-key-in-query auth), so the
    /// user can see the query credential that will be injected at send.</summary>
    public ObservableCollection<HeaderRowViewModel> AuthParams { get; } = [];

    public bool HasAuthParams => AuthParams.Count > 0;

    private void RefreshAuthParams(IReadOnlyList<KeyValueItem> queryParams, string? source)
    {
        AuthParams.Clear();
        foreach (var p in queryParams)
        {
            AuthParams.Add(new HeaderRowViewModel
            {
                IsInherited = true,
                IsAuthDerived = true,
                SourceName = source ?? "Auth",
                Key = p.Key,
                Value = p.Value,
                Enabled = true,
            });
        }

        OnPropertyChanged(nameof(HasAuthParams));
    }

    /// <summary>The auth that actually applies to this request - the domain <see cref="EffectiveAuthResolver"/>
    /// policy applied to this editor's current auth-tab state, inheritance chain, and profile library.</summary>
    private EffectiveAuth ResolveEffectiveAuth() =>
        EffectiveAuthResolver.Resolve(Auth.Type, Auth.ToModel(), Auth.SelectedProfile, _inheritanceChain, _authProfiles);

    /// <summary>
    /// Bidirectional URL/Params Sync Engine (RequestEditorPane.md §3): reparses the URL's query
    /// string into <see cref="Params"/> rows whenever the URL changes directly. Enabled rows the
    /// URL still mentions are updated in place (preserving row identity/Description); enabled rows
    /// the URL no longer mentions are removed; disabled rows are left untouched, since they're
    /// intentionally absent from the URL ("Toggle Suppression").
    /// </summary>
    private void SyncParamsFromUrl(string url)
    {
        if (_syncingUrlAndParams)
        {
            return;
        }

        _syncingUrlAndParams = true;
        try
        {
            var pairs = QueryStringSync.ParseQuery(url);

            foreach (var (key, value) in pairs)
            {
                var row = Params.Rows.FirstOrDefault(r => r.Enabled && r.Key == key);
                if (row is not null)
                {
                    row.Value = value;
                }
                else
                {
                    Params.AddRowQuietly(new KeyValueRowViewModel { Key = key, Value = value, Enabled = true });
                }
            }

            var urlKeys = pairs.Select(p => p.Key).ToHashSet();
            foreach (var row in Params.Rows.Where(r => r.Enabled && !urlKeys.Contains(r.Key)).ToList())
            {
                Params.RemoveRowQuietly(row);
            }
        }
        finally
        {
            _syncingUrlAndParams = false;
        }
    }

    /// <summary>Rebuilds the address bar's query string from <see cref="Params"/> whenever a row is
    /// added/removed/edited/toggled directly in the Params tab - the other half of §3's sync engine.</summary>
    private void SyncUrlFromParams()
    {
        if (_syncingUrlAndParams)
        {
            return;
        }

        _syncingUrlAndParams = true;
        try
        {
            var enabled = Params.Rows.Where(r => r.Enabled).Select(r => (r.Key, r.Value));
            Url = QueryStringSync.BuildUrl(Url, enabled);
        }
        finally
        {
            _syncingUrlAndParams = false;
        }
    }

    private void ApplyResultToResponse(ExecutionResult result)
    {
        Response.HasResponse = true;
        Response.ElapsedMilliseconds = result.ElapsedMilliseconds;
        Response.SizeBytes = result.SizeBytes;
        Response.ContentType = result.ContentType;

        Response.Headers.Clear();
        foreach (var header in result.Headers)
        {
            Response.Headers.Add(KeyValueRowViewModel.FromModel(header));
        }

        if (result.ErrorMessage is not null)
        {
            Response.StatusCode = 0;
            Response.StatusText = "Error";
            Response.LoadBody(result.ErrorMessage, result.BodyBytes);
            _statusLog.Log($"Request failed: {result.ErrorMessage}");
        }
        else
        {
            Response.StatusCode = result.StatusCode;
            Response.StatusText = result.ReasonPhrase ?? "";
            Response.LoadBody(TryPrettyPrintJson(result.Body), result.BodyBytes);
            _statusLog.Log($"{result.StatusCode} {result.ReasonPhrase} - {result.ElapsedMilliseconds} ms, {result.SizeBytes} B");
        }
    }

    private static string TryPrettyPrintJson(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return body;
        }

        try
        {
            var node = JsonNode.Parse(body);
            return node?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? body;
        }
        catch (JsonException)
        {
            return body;
        }
    }

    private RequestModel BuildRequestModel()
    {
        var auth = Auth.ToModel();
        return new RequestModel
        {
            Id = _original.Id,
            Name = Name,
            Kind = _original.Kind,
            Method = Method,
            Url = Url,
            QueryParams = Params.ToModel(),
            Headers = Headers.DirectToModel(),
            Body = Body.ToModel(),
            Auth = auth,
            AuthProfileId = auth.Type == AuthType.Profile ? Auth.SelectedProfile?.Id : null,
            TimeoutSeconds = Tests.TimeoutSeconds,
            Captures = Tests.CapturesToModel(),
            Assertions = Tests.AssertionsToModel(),
            SuppressedInheritedHeaderKeys = Headers.SuppressedInheritedKeys(),
            // Local variables are obsolete (RequestModel.LocalVariables doc comment) - no UI edits
            // them anymore, so just carry the original value through unchanged on save.
            LocalVariables = _original.LocalVariables,
            Settings = _original.Settings,
        };
    }
}
