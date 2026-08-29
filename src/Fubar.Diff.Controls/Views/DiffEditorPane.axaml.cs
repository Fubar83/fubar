using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.Search;
using Fubar.Diff.Core.Rendering;
using Fubar.Diff.Controls.Rendering;

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
        else if (change.Property == ForegroundProperty || change.Property == FontSizeProperty)
        {
            ApplyGutterStyle();
        }
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
