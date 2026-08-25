using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fubar.Studio.Core.Json;
using Fubar.Studio.Core.Models;
using Fubar.Studio.UI.Services;

namespace Fubar.Studio.UI.ViewModels;

/// <summary>
/// Backs the Body tab: type selector plus whichever editor that type needs - the raw JSON/text
/// editor (Json/RawText), a key-value grid (FormData/UrlEncoded, reusing the same
/// <see cref="KeyValueGridViewModel"/> as the Params tab), or a file picker (BinaryFile). Previously
/// only Json/RawText were wired all the way through - <c>HttpRequestExecutor</c> silently sent no
/// body at all for the others.
/// </summary>
public partial class RequestBodyViewModel : ViewModelBase
{
    private readonly IFilePickerService _filePickerService;
    private readonly IJsonSchemaValidator _schemaValidator;

    public static IReadOnlyList<BodyType> TypeOptions { get; } = Enum.GetValues<BodyType>();

    public RequestBodyViewModel(IFilePickerService filePickerService, IJsonSchemaValidator schemaValidator)
    {
        _filePickerService = filePickerService;
        _schemaValidator = schemaValidator;
        FormData.Changed += () => Changed?.Invoke();
        UrlEncoded.Changed += () => Changed?.Invoke();
    }

    [ObservableProperty]
    public partial BodyType Type { get; set; } = BodyType.None;

    [ObservableProperty]
    public partial string Raw { get; set; } = "";

    public KeyValueGridViewModel FormData { get; } = new();

    public KeyValueGridViewModel UrlEncoded { get; } = new();

    [ObservableProperty]
    public partial string? BinaryFilePath { get; set; }

    public bool IsRawEditorVisible => Type is BodyType.Json or BodyType.RawText;

    public bool IsFormDataVisible => Type == BodyType.FormData;

    public bool IsUrlEncodedVisible => Type == BodyType.UrlEncoded;

    public bool IsBinaryFileVisible => Type == BodyType.BinaryFile;

    /// <summary>True only for the JSON body type - gates the validity indicator + Pretty button.</summary>
    public bool IsJsonType => Type == BodyType.Json;

    // JSON validity of the current Raw text: null = nothing to validate (empty), true = parses,
    // false = malformed. Recomputed whenever Raw or Type changes; drives the glyph/brush/tooltip below.
    [ObservableProperty]
    public partial bool? IsJsonValid { get; set; }

    /// <summary>The operation's request-body JSON Schema (self-contained, from an OpenAPI import), or
    /// null. When set, the JSON body is validated against it and mismatches surface in
    /// <see cref="SchemaProblems"/> and the validity indicator.</summary>
    [ObservableProperty]
    public partial string? SchemaJson { get; set; }

    /// <summary>Schema-validation problems for the current body ("&lt;location&gt;: &lt;message&gt;"),
    /// shown under the editor to help the user enter correctly-shaped data.</summary>
    public ObservableCollection<string> SchemaProblems { get; } = [];

    public bool HasSchema => !string.IsNullOrEmpty(SchemaJson);

    public bool HasSchemaProblems => SchemaProblems.Count > 0;

    /// <summary>When on, the body editor area shows the (read-only) schema instead. Toggled by the
    /// "Schema" button, which is disabled when there's no schema.</summary>
    [ObservableProperty]
    public partial bool ShowSchema { get; set; }

    /// <summary>The schema pretty-printed for the read-only schema view.</summary>
    public string SchemaPretty { get; private set; } = "";

    public bool IsSchemaViewVisible => ShowSchema && IsJsonType && HasSchema;

    partial void OnShowSchemaChanged(bool value) => OnPropertyChanged(nameof(IsSchemaViewVisible));

    // ✕ malformed JSON; ⚠ valid JSON that doesn't match the schema; ✓ valid (and matching if a schema).
    public string JsonValidityGlyph =>
        IsJsonValid == false ? "✕" : HasSchemaProblems ? "⚠" : IsJsonValid == true ? "✓" : "";

    public string JsonValidityTooltip =>
        IsJsonValid == false ? "Invalid JSON"
        : HasSchemaProblems ? $"Doesn't match schema ({SchemaProblems.Count} issue(s))"
        : IsJsonValid == true ? (HasSchema ? "Valid JSON, matches schema" : "Valid JSON")
        : "";

    public IBrush JsonValidityBrush =>
        IsJsonValid == false ? new SolidColorBrush(Color.FromRgb(0xE5, 0x59, 0x39))   // red - malformed
        : HasSchemaProblems ? new SolidColorBrush(Color.FromRgb(0xE5, 0x94, 0x2B))     // amber - schema mismatch
        : IsJsonValid == true ? new SolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x50))   // green - ok
        : Brushes.Transparent;

    /// <summary>Raised whenever anything about the body changes - <c>RequestEditorViewModel</c>
    /// wires this into its <c>IsDirty</c> tracking, same as <c>HeadersTabViewModel.Changed</c>.</summary>
    public event Action? Changed;

    partial void OnTypeChanged(BodyType value)
    {
        OnPropertyChanged(nameof(IsRawEditorVisible));
        OnPropertyChanged(nameof(IsFormDataVisible));
        OnPropertyChanged(nameof(IsUrlEncodedVisible));
        OnPropertyChanged(nameof(IsBinaryFileVisible));
        OnPropertyChanged(nameof(IsJsonType));
        OnPropertyChanged(nameof(IsSchemaViewVisible));
        RecomputeJsonValidity();
    }

    partial void OnRawChanged(string value) => RecomputeJsonValidity();

    partial void OnSchemaJsonChanged(string? value)
    {
        SchemaPretty = PrettyPrintSchema(value);
        if (string.IsNullOrEmpty(value))
        {
            ShowSchema = false;
        }

        OnPropertyChanged(nameof(HasSchema));
        OnPropertyChanged(nameof(SchemaPretty));
        OnPropertyChanged(nameof(IsSchemaViewVisible));
        RecomputeJsonValidity();
    }

    private static string PrettyPrintSchema(string? schemaJson)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
        {
            return "";
        }

        try
        {
            using var document = JsonDocument.Parse(schemaJson);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            return schemaJson;
        }
    }

    partial void OnIsJsonValidChanged(bool? value) => NotifyValidity();

    private void NotifyValidity()
    {
        OnPropertyChanged(nameof(HasSchemaProblems));
        OnPropertyChanged(nameof(JsonValidityGlyph));
        OnPropertyChanged(nameof(JsonValidityTooltip));
        OnPropertyChanged(nameof(JsonValidityBrush));
    }

    private void RecomputeJsonValidity()
    {
        SchemaProblems.Clear();

        if (Type != BodyType.Json || string.IsNullOrWhiteSpace(Raw))
        {
            IsJsonValid = null;
            NotifyValidity();
            return;
        }

        try
        {
            using var _ = JsonDocument.Parse(Raw);
            IsJsonValid = true;
        }
        catch (JsonException)
        {
            IsJsonValid = false;
            NotifyValidity();
            return;
        }

        if (!string.IsNullOrEmpty(SchemaJson))
        {
            foreach (var problem in _schemaValidator.Validate(SchemaJson, Raw))
            {
                SchemaProblems.Add(problem);
            }
        }

        NotifyValidity();
    }

    /// <summary>Pretty-prints the JSON body with fixed settings (2-space indent) when it parses;
    /// leaves malformed text untouched.</summary>
    [RelayCommand]
    private void FormatJson()
    {
        if (string.IsNullOrWhiteSpace(Raw))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(Raw);
            Raw = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            // Invalid JSON - nothing to format; the validity icon already flags it.
        }
    }

    [RelayCommand]
    private async Task PickBinaryFileAsync()
    {
        var path = await _filePickerService.PickOpenFileAsync("Select File");
        if (path is not null)
        {
            BinaryFilePath = path;
        }
    }

    public RequestBody ToModel() => new()
    {
        Type = Type,
        Raw = string.IsNullOrEmpty(Raw) ? null : Raw,
        FormData = FormData.ToModel(),
        UrlEncoded = UrlEncoded.ToModel(),
        BinaryFilePath = BinaryFilePath,
    };

    public static RequestBodyViewModel FromModel(RequestBody body, IFilePickerService filePickerService, IJsonSchemaValidator schemaValidator, string? schemaJson = null)
    {
        var vm = new RequestBodyViewModel(filePickerService, schemaValidator)
        {
            Type = body.Type,
            Raw = body.Raw ?? "",
            BinaryFilePath = body.BinaryFilePath,
            SchemaJson = schemaJson,
        };

        foreach (var item in body.FormData)
        {
            vm.FormData.AddRowQuietly(KeyValueRowViewModel.FromModel(item));
        }

        foreach (var item in body.UrlEncoded)
        {
            vm.UrlEncoded.AddRowQuietly(KeyValueRowViewModel.FromModel(item));
        }

        return vm;
    }
}
