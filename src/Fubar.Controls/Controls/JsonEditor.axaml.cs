using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Search;
using AvaloniaEdit.TextMate;
using TextMateSharp.Grammars;

namespace Fubar.Controls;

/// <summary>
/// Reusable pretty-printed JSON editor for request/response bodies: line numbers, JSON syntax
/// highlighting (TextMate), brace folding, and Ctrl+F search - all via <see cref="Text"/>, a
/// plain bindable string property, so callers never touch AvaloniaEdit directly.
/// </summary>
public partial class JsonEditor : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<JsonEditor, string>(nameof(Text), string.Empty, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        AvaloniaProperty.Register<JsonEditor, bool>(nameof(IsReadOnly));

    /// <summary>An optional JSON Schema (as text) for the body. When set, the editor offers schema-aware
    /// completion (property names, enum/boolean values) on typing <c>"</c> or Ctrl+Space.</summary>
    public static readonly StyledProperty<string?> SchemaJsonProperty =
        AvaloniaProperty.Register<JsonEditor, string?>(nameof(SchemaJson));

    private readonly TextMate.Installation _textMateInstallation;
    private readonly FoldingManager _foldingManager;
    private bool _suppressTextCallback;
    private JsonNode? _schemaRoot;
    private CompletionWindow? _completionWindow;

    public JsonEditor()
    {
        InitializeComponent();

        var registryOptions = new RegistryOptions(ThemeName.DarkPlus);
        _textMateInstallation = Editor.InstallTextMate(registryOptions);
        _textMateInstallation.SetGrammar(registryOptions.GetScopeByExtension(".json"));

        // FoldingManager.Install already adds a FoldingMargin (the fold-marker gutter) to the
        // TextArea's LeftMargins - adding another one by hand rendered a SECOND gutter, so every
        // foldable node showed two expand/collapse buttons. Install alone is enough.
        _foldingManager = FoldingManager.Install(Editor.TextArea);

        SearchPanel.Install(Editor);

        Editor.TextChanged += (_, _) =>
        {
            _suppressTextCallback = true;
            SetValue(TextProperty, Editor.Document.Text);
            _suppressTextCallback = false;
            JsonFoldingStrategy.UpdateFoldings(_foldingManager, Editor.Document);
        };

        // Belt-and-suspenders: a click anywhere in this UserControl (e.g. empty space below the
        // last line, before any text has been typed) should still land the caret in the editor,
        // in case it doesn't already have keyboard focus for some other reason.
        PointerPressed += (_, _) => Editor.Focus();

        // Schema completion: unobtrusive - only pops on typing a quote or Ctrl+Space, and only when a
        // schema is set and there's something useful to suggest.
        Editor.TextArea.TextEntered += (_, e) =>
        {
            if (e.Text == "\"")
            {
                ShowSchemaCompletion();
            }
        };
        Editor.TextArea.KeyDown += (_, e) =>
        {
            if (e is { Key: Key.Space, KeyModifiers: KeyModifiers.Control })
            {
                ShowSchemaCompletion();
                e.Handled = true;
            }
        };
    }

    public string? SchemaJson
    {
        get => GetValue(SchemaJsonProperty);
        set => SetValue(SchemaJsonProperty, value);
    }

    private void ShowSchemaCompletion()
    {
        if (_schemaRoot is null || IsReadOnly)
        {
            return;
        }

        var result = JsonSchemaCompletion.Compute(Editor.Document.Text, Editor.CaretOffset, _schemaRoot);
        if (result is null)
        {
            return;
        }

        _completionWindow?.Close();
        var window = new CompletionWindow(Editor.TextArea) { StartOffset = result.StartOffset };
        foreach (var candidate in result.Candidates)
        {
            window.CompletionList.CompletionData.Add(new JsonCompletionData(candidate));
        }

        window.Closed += (_, _) => _completionWindow = null;
        _completionWindow = window;
        window.Show();
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty && !_suppressTextCallback)
        {
            var newText = change.GetNewValue<string>() ?? string.Empty;
            if (Editor.Document.Text != newText)
            {
                Editor.Document.Text = newText;
                JsonFoldingStrategy.UpdateFoldings(_foldingManager, Editor.Document);
            }
        }
        else if (change.Property == IsReadOnlyProperty)
        {
            Editor.IsReadOnly = change.GetNewValue<bool>();
        }
        else if (change.Property == SchemaJsonProperty)
        {
            var schema = change.GetNewValue<string?>();
            try
            {
                _schemaRoot = string.IsNullOrWhiteSpace(schema) ? null : JsonNode.Parse(schema);
            }
            catch (System.Text.Json.JsonException)
            {
                _schemaRoot = null;
            }
        }
    }
}
