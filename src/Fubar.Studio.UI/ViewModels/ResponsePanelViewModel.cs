using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Studio.Core.Json;
using Fubar.Studio.Core.Testing;
using Fubar.Studio.UI.Services;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>
/// Backs the Response Pane (ResponsePane.md): telemetry bar (status/latency/size badges),
/// Pretty/Tree/Raw/Headers/Preview view modes, and the Tree view's JSONPath filter. Populated by
/// <c>RequestEditorViewModel.ApplyResultToResponse</c> after every Send/Replay.
/// </summary>
public partial class ResponsePanelViewModel : ViewModelBase, IDisposable
{
    private readonly IClipboardService _clipboardService;
    private readonly IFilePickerService _filePickerService;
    private readonly StatusLogViewModel _statusLog;
    private readonly IJsonSchemaValidator _schemaValidator;
    private readonly IJsonPathEvaluator _jsonPathEvaluator;
    private readonly IResponseBaselineService _baseline;
    private readonly IDiffPreviewService _diffPreview;

    public ResponsePanelViewModel(
        IClipboardService clipboardService,
        IFilePickerService filePickerService,
        StatusLogViewModel statusLog,
        IJsonSchemaValidator schemaValidator,
        IJsonPathEvaluator jsonPathEvaluator,
        IResponseBaselineService baseline,
        IDiffPreviewService diffPreview)
    {
        _clipboardService = clipboardService;
        _filePickerService = filePickerService;
        _statusLog = statusLog;
        _schemaValidator = schemaValidator;
        _jsonPathEvaluator = jsonPathEvaluator;
        _baseline = baseline;
        _diffPreview = diffPreview;

        Headers.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HeadersTabTitle));

        // The pin is app-wide, so it can change from another request's pane. Without this the button
        // here would still claim there is nothing pinned.
        _baseline.Changed += OnBaselineChanged;
    }

    /// <summary>
    /// Detaches from the app-wide pin. The baseline service outlives every request editor, so a pane
    /// that never unsubscribes keeps its whole editor alive for the rest of the session.
    /// </summary>
    public void Dispose() => _baseline.Changed -= OnBaselineChanged;

    private void OnBaselineChanged(object? sender, EventArgs e) => RaiseBaselineState();

    [ObservableProperty]
    public partial bool HasResponse { get; set; }

    [ObservableProperty]
    public partial int StatusCode { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    public string StatusIcon => StatusCode switch
    {
        >= 100 and < 200 => "ℹ", // info
        >= 200 and < 300 => "✅", // check mark
        >= 300 and < 400 => "↪", // arrow
        >= 400 and < 500 => "⚠", // warning
        >= 500 => "❌", // cross mark
        _ => "🔌", // connection failed (0 is HttpRequestExecutor's sentinel)
    };

    [ObservableProperty]
    public partial long ElapsedMilliseconds { get; set; }

    public string ElapsedTimeText => ElapsedMilliseconds >= 1000
        ? $"{ElapsedMilliseconds / 1000.0:F2} s"
        : $"{ElapsedMilliseconds} ms";

    [ObservableProperty]
    public partial long SizeBytes { get; set; }

    public string ContentSizeText => SizeBytes switch
    {
        < 1024 => $"{SizeBytes} B",
        < 1024 * 1024 => $"{SizeBytes / 1024.0:F1} KB",
        _ => $"{SizeBytes / (1024.0 * 1024.0):F2} MB",
    };

    [ObservableProperty]
    public partial string? ContentType { get; set; }

    public string ContentTypeHeader => ContentType ?? "";

    public bool HasPreview => ContentType is { } ct && ct.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    [ObservableProperty]
    public partial string RawBody { get; set; } = "";

    [ObservableProperty]
    public partial byte[] BodyBytes { get; set; } = [];

    public ObservableCollection<KeyValueRowViewModel> Headers { get; } = [];

    public string HeadersTabTitle => $"Headers ({Headers.Count})";

    public ObservableCollection<JsonTreeNodeViewModel> JsonTreeNodes { get; } = [];

    [ObservableProperty]
    public partial Bitmap? PreviewBitmapImage { get; set; }

    // View mode (ResponsePane.md §4): the Pretty/Tree/Raw/Headers/Preview switcher is a
    // SeamlessTabControl whose SelectedIndex binds here two-way. 0=Pretty 1=Tree 2=Raw 3=Headers
    // 4=Preview (Preview's tab is only present when HasPreview, but it stays the last index).
    [ObservableProperty]
    public partial int SelectedViewIndex { get; set; }

    /// <summary>True when the Tree tab is active - gates the JSONPath filter row inside that tab.</summary>
    public bool IsTreeViewSelected => SelectedViewIndex == 1;

    partial void OnSelectedViewIndexChanged(int value) => OnPropertyChanged(nameof(IsTreeViewSelected));

    // Tree view's JSONPath filter (ResponsePane.md §5, "Live Filtering Mode").
    [ObservableProperty]
    public partial string JsonPathQuery { get; set; } = "";

    [ObservableProperty]
    public partial string? JsonPathError { get; set; }

    public ObservableCollection<JsonPathMatchViewModel> JsonPathMatches { get; } = [];

    // Assertion results (the Tests tab): populated after each Send by RequestEditorViewModel.
    public ObservableCollection<AssertionResult> TestResults { get; } = [];

    [ObservableProperty]
    public partial bool HasTestResults { get; set; }

    public int TestPassedCount => TestResults.Count(r => r.Passed);

    public bool AllTestsPassed => HasTestResults && TestPassedCount == TestResults.Count;

    public string TestSummary => HasTestResults ? $"{TestPassedCount}/{TestResults.Count} passed" : "";

    public string TestsTabTitle => HasTestResults ? $"Tests ({TestPassedCount}/{TestResults.Count})" : "Tests";

    /// <summary>Replaces the assertion results shown in the Tests tab after a send.</summary>
    public void SetTestResults(IReadOnlyList<AssertionResult> results)
    {
        TestResults.Clear();
        foreach (var r in results)
        {
            TestResults.Add(r);
        }

        HasTestResults = results.Count > 0;
        NotifyTestSummary();
    }

    public void ClearTestResults()
    {
        TestResults.Clear();
        HasTestResults = false;
        NotifyTestSummary();
    }

    partial void OnHasTestResultsChanged(bool value) => NotifyTestSummary();

    // Response-body schema validation: when the request was imported with a known success-response
    // schema (Settings.fubarOpenApi.responseSchema), the response body is validated against it and a
    // small badge shows ✓ / ⚠ in the telemetry bar.
    private string? _responseSchemaJson;

    public ObservableCollection<string> SchemaProblems { get; } = [];

    private bool HasResponseSchema => !string.IsNullOrEmpty(_responseSchemaJson);

    public bool ShowSchemaStatus => HasResponse && HasResponseSchema && IsJsonResponse;

    public bool SchemaValid => SchemaProblems.Count == 0;

    public string SchemaStatusGlyph => SchemaValid ? "✓" : "⚠";

    public string SchemaStatusText => SchemaValid ? "schema ok" : $"schema: {SchemaProblems.Count}";

    public string SchemaStatusTooltip => SchemaProblems.Count == 0
        ? "Response body matches the imported schema."
        : "Response body doesn't match the schema:\n• " + string.Join("\n• ", SchemaProblems);

    public IBrush SchemaStatusBrush => SchemaValid
        ? new SolidColorBrush(Color.FromRgb(0x43, 0xA0, 0x47))  // green
        : new SolidColorBrush(Color.FromRgb(0xE5, 0x94, 0x2B)); // amber

    /// <summary>Supplies (or clears) the JSON schema to validate response bodies against - called by the
    /// request editor from the imported <c>responseSchema</c>.</summary>
    public void SetResponseSchema(string? schemaJson)
    {
        _responseSchemaJson = schemaJson;
        RevalidateSchema();
        NotifySchemaStatus();
    }

    private void RevalidateSchema()
    {
        SchemaProblems.Clear();
        if (HasResponseSchema && _parsedBody is not null)
        {
            foreach (var problem in _schemaValidator.Validate(_responseSchemaJson!, RawBody))
            {
                SchemaProblems.Add(problem);
            }
        }
    }

    private void NotifySchemaStatus()
    {
        OnPropertyChanged(nameof(ShowSchemaStatus));
        OnPropertyChanged(nameof(SchemaValid));
        OnPropertyChanged(nameof(SchemaStatusGlyph));
        OnPropertyChanged(nameof(SchemaStatusText));
        OnPropertyChanged(nameof(SchemaStatusTooltip));
        OnPropertyChanged(nameof(SchemaStatusBrush));
    }

    private void NotifyTestSummary()
    {
        OnPropertyChanged(nameof(TestPassedCount));
        OnPropertyChanged(nameof(AllTestsPassed));
        OnPropertyChanged(nameof(TestSummary));
        OnPropertyChanged(nameof(TestsTabTitle));
    }

    public bool IsJsonPathFilterActive => !string.IsNullOrWhiteSpace(JsonPathQuery);

    /// <summary>True when the response body parsed as JSON - gates the Format button / Auto-format
    /// toggle in the footer.</summary>
    public bool IsJsonResponse => _parsedBody is not null;

    /// <summary>When on, JSON responses are pretty-printed automatically as they arrive (see
    /// <see cref="LoadBody"/>). Also honoured by the manual Format button. In-memory only for now.</summary>
    [ObservableProperty]
    public partial bool AutoFormatJson { get; set; }

    // ---- Comparing two responses ----------------------------------------------------------------

    /// <summary>
    /// Names this response in a diff header - set by <c>RequestEditorViewModel</c> after each send,
    /// because the pane knows the status and the clock but not which request produced it.
    /// </summary>
    public string SourceLabel { get; set; } = "Response";

    /// <summary>Whether there is a pinned response to compare the current one against.</summary>
    public bool HasBaseline => _baseline.Pinned is not null;

    /// <summary>Both sides must exist before Compare means anything.</summary>
    public bool CanCompareWithBaseline => HasResponse && HasBaseline;

    public string BaselineTooltip => _baseline.Pinned is { } pinned
        ? $"Compare this response against the pinned one ({pinned.Label})"
        : "Pin a response first, then send again and compare";

    partial void OnHasResponseChanged(bool value) => RaiseBaselineState();

    private void RaiseBaselineState()
    {
        OnPropertyChanged(nameof(HasBaseline));
        OnPropertyChanged(nameof(CanCompareWithBaseline));
        OnPropertyChanged(nameof(BaselineTooltip));
    }

    /// <summary>
    /// Sets this response aside as the left-hand side of a later comparison. Snapshots the text
    /// rather than referencing the pane: the next send overwrites <see cref="RawBody"/>, and pinning
    /// a live reference would quietly compare the response against itself.
    /// </summary>
    [RelayCommand]
    private void PinAsBaseline()
    {
        if (!HasResponse)
        {
            return;
        }

        _baseline.Pin(new PinnedResponse(RawBody, $"{SourceLabel} · {DateTime.Now:HH:mm:ss}"));
        _statusLog.Log($"Pinned response for comparison ({RawBody.Length:N0} chars)");
    }

    /// <summary>
    /// Supplies the ignore rules for a response comparison, set by the request editor that owns this
    /// pane. Null in any host that has no request behind it, which hides the affordance.
    ///
    /// A factory rather than a value: the rules can be edited and saved from inside the diff window,
    /// so a snapshot taken when the pane was built would be stale by the second comparison.
    /// </summary>
    public Func<DiffIgnoreContext>? IgnoreContextProvider { get; set; }

    /// <summary>
    /// Diffs the pinned response (left, older) against this one (right, current).
    ///
    /// The rules are the CURRENT request's, even when the pin came from another one: it is the
    /// request on screen, and the only one whose file could remember a new rule.
    /// </summary>
    [RelayCommand]
    private async Task CompareWithBaselineAsync()
    {
        if (_baseline.Pinned is not { } pinned || !HasResponse)
        {
            return;
        }

        await _diffPreview.ShowAsync(
            pinned.Body,
            RawBody,
            pinned.Label,
            SourceLabel,
            "Compare responses",
            IgnoreContextProvider?.Invoke());
    }

    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    private JsonNode? _parsedBody;

    /// <summary>Resets and repopulates every view mode's content for a new response - called once
    /// per Send/Replay by <c>RequestEditorViewModel.ApplyResultToResponse</c>.</summary>
    public void LoadBody(string rawBody, byte[] bodyBytes)
    {
        RawBody = rawBody;
        BodyBytes = bodyBytes;

        JsonTreeNodes.Clear();
        _parsedBody = null;
        try
        {
            _parsedBody = string.IsNullOrWhiteSpace(rawBody) ? null : JsonNode.Parse(rawBody);
            if (_parsedBody is not null)
            {
                JsonTreeNodes.Add(JsonTreeNodeViewModel.Build(_parsedBody));

                if (AutoFormatJson)
                {
                    RawBody = _parsedBody.ToJsonString(IndentedJson);
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON (or malformed) - Tree view just stays empty; Pretty/Raw still show the text.
        }

        OnPropertyChanged(nameof(IsJsonResponse));

        PreviewBitmapImage?.Dispose();
        PreviewBitmapImage = null;
        if (HasPreview && bodyBytes.Length > 0)
        {
            try
            {
                using var stream = new MemoryStream(bodyBytes);
                PreviewBitmapImage = new Bitmap(stream);
            }
            catch (Exception)
            {
                // Content-Type claimed an image but the bytes didn't decode as one - leave preview empty.
            }
        }

        JsonPathQuery = "";
        JsonPathError = null;
        JsonPathMatches.Clear();

        RevalidateSchema();
        NotifySchemaStatus();
    }

    partial void OnJsonPathQueryChanged(string value)
    {
        OnPropertyChanged(nameof(IsJsonPathFilterActive));
        JsonPathMatches.Clear();
        JsonPathError = null;

        if (string.IsNullOrWhiteSpace(value) || _parsedBody is null)
        {
            return;
        }

        var result = _jsonPathEvaluator.Evaluate(_parsedBody.ToJsonString(), value);
        if (result.Error is not null)
        {
            JsonPathError = result.Error;
            return;
        }

        foreach (var match in result.Matches)
        {
            JsonPathMatches.Add(new JsonPathMatchViewModel(match.Path, match.Value));
        }

        if (JsonPathMatches.Count == 0)
        {
            JsonPathError = "No matches.";
        }
    }

    partial void OnContentTypeChanged(string? value)
    {
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(ContentTypeHeader));
    }

    /// <summary>Pretty-prints the JSON response body (2-space indent) on demand. No-op for non-JSON.</summary>
    [RelayCommand]
    private void FormatResponseJson()
    {
        if (_parsedBody is not null)
        {
            RawBody = _parsedBody.ToJsonString(IndentedJson);
        }
    }

    [RelayCommand]
    private async Task CopyResponseBodyAsync()
    {
        await _clipboardService.SetTextAsync(RawBody);
        _statusLog.Log("Copied response body to clipboard.");
    }

    [RelayCommand]
    private async Task SaveResponseToFileAsync()
    {
        var suggestedName = "response" + GuessExtension();
        var path = await _filePickerService.PickSaveFileAsync("Save Response Body", suggestedName);
        if (path is null)
        {
            return;
        }

        try
        {
            await File.WriteAllBytesAsync(path, BodyBytes);
            _statusLog.Log($"Saved response body to: {path}");
        }
        catch (Exception ex)
        {
            _statusLog.Log($"Failed to save response body: {ex.Message}");
        }
    }

    private string GuessExtension() => ContentType switch
    {
        { } ct when ct.Contains("json") => ".json",
        { } ct when ct.Contains("html") => ".html",
        { } ct when ct.Contains("xml") => ".xml",
        { } ct when ct.StartsWith("image/png") => ".png",
        { } ct when ct.StartsWith("image/jpeg") => ".jpg",
        { } ct when ct.StartsWith("image/") => "." + ContentType!["image/".Length..],
        _ => ".txt",
    };
}
