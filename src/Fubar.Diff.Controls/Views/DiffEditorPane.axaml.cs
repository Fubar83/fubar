using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.Search;
using AvaloniaEdit.TextMate;
using Fubar.Diff.Core.Rendering;
using Fubar.Diff.Controls.Rendering;
using TextMateSharp.Grammars;

namespace Fubar.Diff.Controls.Views;

/// <summary>
/// One side of the side-by-side view: a read-only <see cref="TextEditor"/> wired to the three diff
/// renderers (line tint, character spans, source-number gutter).
///
/// Exists as its own control so the two panes are provably symmetrical - left and right differ only in
/// which <see cref="AlignedDocument"/> they are given - and so <c>DiffView</c> stays about layout and
/// scroll sync rather than rendering detail.
/// </summary>
public partial class DiffEditorPane : UserControl
{
    /// <summary>The flattened document for this side. Setting it re-renders the pane.</summary>
    public static readonly StyledProperty<AlignedDocument?> DocumentProperty =
        AvaloniaProperty.Register<DiffEditorPane, AlignedDocument?>(nameof(Document));

    /// <summary>
    /// True when this pane is a close-up (<c>DiffDetailPane</c>) rather than one of the main
    /// side-by-side panes - see <see cref="DiffLineColors.LineBackground"/> for why that changes the
    /// tint's intensity.
    /// </summary>
    public static readonly StyledProperty<bool> EmphasizedProperty =
        AvaloniaProperty.Register<DiffEditorPane, bool>(nameof(Emphasized));

    /// <summary>Whether to mark invisible characters - see <see cref="InvisibleCharacterGenerator"/>.</summary>
    public static readonly StyledProperty<bool> ShowInvisiblesProperty =
        AvaloniaProperty.Register<DiffEditorPane, bool>(nameof(ShowInvisibles));

    /// <summary>
    /// The file extension whose grammar this side should be highlighted with (<c>.cs</c>, <c>.ts</c>),
    /// or null for none.
    ///
    /// An extension rather than a <c>SourceLanguage</c> on purpose: the highlighter and the COMPARISON
    /// know different amounts. The comparison only claims a language when it has real scanning rules
    /// for it, which is a short list; highlighting works for anything TextMate ships a grammar for,
    /// which is a long one - and a Python file gets nothing from the code-comparison rules but is still
    /// far easier to read coloured. Tying the two together would mean either colouring nothing outside
    /// the short list, or claiming to compare languages we cannot scan.
    /// </summary>
    public static readonly StyledProperty<string?> SyntaxExtensionProperty =
        AvaloniaProperty.Register<DiffEditorPane, string?>(nameof(SyntaxExtension));

    /// <summary>Whether syntax highlighting is wanted at all. On by default.</summary>
    public static readonly StyledProperty<bool> SyntaxHighlightingProperty =
        AvaloniaProperty.Register<DiffEditorPane, bool>(nameof(SyntaxHighlighting), defaultValue: true);

    /// <summary>
    /// The grammar and theme registry, shared by every pane in the process.
    ///
    /// One instance, deliberately: constructing it reads TextMateSharp's grammar and theme resources,
    /// and there are at least four panes on screen (two main, two in the close-up) plus one per open
    /// tab. Per-pane instances would repeat that work for identical data. It is only created when a
    /// pane is actually asked to highlight something, so a session comparing nothing but text files
    /// never pays for it at all.
    /// </summary>
    private static RegistryOptions? _registry;

    private static RegistryOptions Registry => _registry ??= new RegistryOptions(ThemeName.DarkPlus);

    private TextMate.Installation? _textMate;

    /// <summary>The scope currently installed, so re-applying the same grammar is free.</summary>
    private string? _grammarScope;

    private readonly ChangeLineBackgroundRenderer _backgroundRenderer;
    private readonly CharSpanColorizer _colorizer;
    private readonly SourceLineNumberMargin _lineNumbers;
    private readonly CurrentHunkRenderer _currentHunk;
    private readonly InvisibleCharacterGenerator _invisibles;
    private readonly SearchPanel _searchPanel;

    public DiffEditorPane()
    {
        InitializeComponent();

        _backgroundRenderer = new ChangeLineBackgroundRenderer(this);
        _colorizer = new CharSpanColorizer(this);
        _lineNumbers = new SourceLineNumberMargin();
        _currentHunk = new CurrentHunkRenderer(this);
        _invisibles = new InvisibleCharacterGenerator();

        Editor.TextArea.TextView.BackgroundRenderers.Add(_backgroundRenderer);
        // AFTER the change tint, deliberately: same layer, and background renderers paint in
        // registration order, so the current-hunk marker must be added second to land on top.
        Editor.TextArea.TextView.BackgroundRenderers.Add(_currentHunk);
        Editor.TextArea.TextView.LineTransformers.Add(_colorizer);
        Editor.TextArea.TextView.ElementGenerators.Add(_invisibles);
        Editor.TextArea.LeftMargins.Add(_lineNumbers);

        // Ctrl+F within a pane. AvaloniaEdit brings its own search panel, so this is one line rather
        // than a find bar of our own - and it searches the pane the caret is in, which is what a user
        // pressing Ctrl+F in a two-pane view means.
        _searchPanel = SearchPanel.Install(Editor);

        // Per pane rather than routed from the window, unlike the gutter below: DiffView only forwards
        // theme changes to the two MAIN panes, so the close-up's editors would keep dark-theme token
        // colours on a light background after a switch.
        ActualThemeVariantChanged += (_, _) => ApplyEditorTheme();

        ApplyGutterStyle();
    }

    public AlignedDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public bool Emphasized
    {
        get => GetValue(EmphasizedProperty);
        set => SetValue(EmphasizedProperty, value);
    }

    public bool ShowInvisibles
    {
        get => GetValue(ShowInvisiblesProperty);
        set => SetValue(ShowInvisiblesProperty, value);
    }

    public string? SyntaxExtension
    {
        get => GetValue(SyntaxExtensionProperty);
        set => SetValue(SyntaxExtensionProperty, value);
    }

    public bool SyntaxHighlighting
    {
        get => GetValue(SyntaxHighlightingProperty);
        set => SetValue(SyntaxHighlightingProperty, value);
    }

    /// <summary>The underlying editor, for the parent view to wire scroll sync and caret moves.</summary>
    internal TextEditor TextEditor => Editor;

    /// <summary>Opens the find bar for this pane.</summary>
    internal void OpenSearch()
    {
        Editor.Focus();
        _searchPanel.Open();
    }

    /// <summary>The text view, which owns the scroll offset the two panes keep in step.</summary>
    internal TextView TextView => Editor.TextArea.TextView;

    /// <summary>
    /// Marks a row range as the current difference, or clears it with a negative start. Redraws
    /// immediately - this is driven by navigation, where the whole point is instant feedback.
    ///
    /// Also tells the background tint and character-span colourizer which rows are "current", so rows
    /// outside the range can fade - see <see cref="ChangeLineBackgroundRenderer.SetCurrentRange"/>.
    /// </summary>
    internal void SetCurrentHunk(int startIndex, int endIndex)
    {
        _currentHunk.SetRange(startIndex, endIndex);
        _backgroundRenderer.SetCurrentRange(startIndex, endIndex);
        _colorizer.SetCurrentRange(startIndex, endIndex);
        Editor.TextArea.TextView.Redraw();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == DocumentProperty)
        {
            Apply(change.GetNewValue<AlignedDocument?>());
        }
        else if (change.Property == EmphasizedProperty)
        {
            var emphasized = change.GetNewValue<bool>();
            _backgroundRenderer.SetEmphasized(emphasized);
            _colorizer.SetEmphasized(emphasized);
            Editor.TextArea.TextView.Redraw();
        }
        else if (change.Property == ShowInvisiblesProperty)
        {
            // Redraw, not re-render: the generator is consulted per visual line, so invalidating the
            // visual lines is what makes it run again.
            _invisibles.SetEnabled(change.GetNewValue<bool>());
            Editor.TextArea.TextView.Redraw();
        }
        else if (change.Property == SyntaxExtensionProperty || change.Property == SyntaxHighlightingProperty)
        {
            ApplySyntax();
        }
        else if (change.Property == ForegroundProperty || change.Property == FontSizeProperty)
        {
            ApplyGutterStyle();
        }
    }

    /// <summary>
    /// Installs (or clears) the grammar for the current extension.
    ///
    /// TextMate is installed on FIRST USE rather than in the constructor: installing adds a line
    /// transformer and pulls in the registry, and the majority of comparisons - JSON, logs, config,
    /// plain text - never need one. A pane that is never asked to highlight stays exactly as cheap as
    /// it was before this existed.
    /// </summary>
    private void ApplySyntax()
    {
        var scope = SyntaxHighlighting ? ScopeFor(SyntaxExtension) : null;

        if (scope is null)
        {
            // Not uninstalled, just blanked: an installation that has already run has already added its
            // transformer, and removing it mid-session is more surface than turning the grammar off.
            // Only when there IS one to clear - the library resolves the scope name through its
            // registry, and handing it a null to resolve is not a documented no-op.
            if (_grammarScope is not null)
            {
                Clear();
            }

            return;
        }

        if (_textMate is null)
        {
            _textMate = Editor.InstallTextMate(Registry);
            ApplyEditorTheme();
        }

        if (_grammarScope == scope)
        {
            return;
        }

        _textMate.SetGrammar(scope);
        _grammarScope = scope;
    }

    /// <summary>Turns the highlighter off, leaving the pane's own diff rendering untouched.</summary>
    private void Clear()
    {
        try
        {
            _textMate?.SetGrammar(null);
        }
        catch (Exception)
        {
            // See ScopeFor: a highlighter problem must never be the reason a diff cannot be read.
        }

        _grammarScope = null;
    }

    /// <summary>
    /// The TextMate scope for a file extension, or null when there is no grammar for it - which is a
    /// perfectly ordinary outcome, not an error: the file is simply shown uncoloured.
    ///
    /// Broadly caught, deliberately. This is a reading aid layered on top of the thing the user
    /// actually opened the app for; a grammar that fails to resolve has to degrade to plain text, not
    /// take the pane - and with it the diff - down with it.
    /// </summary>
    private static string? ScopeFor(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        var normalized = extension.StartsWith('.') ? extension : "." + extension;

        try
        {
            var scope = Registry.GetScopeByExtension(normalized);

            return string.IsNullOrEmpty(scope) ? null : scope;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Points the highlighter at the theme the app is actually wearing. Token colours chosen for a dark
    /// background are close to invisible on a light one, so this is not cosmetic.
    /// </summary>
    private void ApplyEditorTheme()
    {
        if (_textMate is null)
        {
            return;
        }

        var dark = ActualThemeVariant == ThemeVariant.Dark;

        _textMate.SetTheme(Registry.LoadTheme(dark ? ThemeName.DarkPlus : ThemeName.LightPlus));
    }

    private void Apply(AlignedDocument? document)
    {
        var lines = document?.Lines ?? [];

        // Metadata BEFORE text: setting the text triggers a render pass, and a renderer holding the
        // previous comparison's line list would paint the old tints for one frame.
        _backgroundRenderer.SetLines(lines);
        _colorizer.SetLines(lines);
        _lineNumbers.SetLines(lines);

        // A row range from the previous comparison addresses rows this document may not have.
        _currentHunk.SetRange(-1, -1);
        _backgroundRenderer.SetCurrentRange(-1, -1);
        _colorizer.SetCurrentRange(-1, -1);

        Editor.Document.Text = document?.Text ?? string.Empty;

        // Scroll home: the previous offset means nothing in a document that has just been replaced.
        Editor.ScrollToHome();
        Editor.TextArea.TextView.Redraw();
    }

    private void ApplyGutterStyle()
    {
        var foreground = this.TryFindResource("TextSecondary", out var brush) && brush is IBrush found
            ? found
            : Brushes.Gray;

        _lineNumbers.SetTextStyle(
            new Typeface(Editor.FontFamily),
            Editor.FontSize,
            foreground);
    }

    /// <summary>
    /// Re-resolves palette-derived colours after a theme switch. The renderers look their brushes up
    /// per pass, so this only needs to restyle the gutter and force a repaint.
    /// </summary>
    internal void OnThemeChanged()
    {
        ApplyGutterStyle();
        Editor.TextArea.TextView.Redraw();
    }
}
