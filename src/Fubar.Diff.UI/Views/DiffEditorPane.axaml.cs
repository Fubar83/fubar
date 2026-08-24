using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using Fubar.Diff.Core.Rendering;
using Fubar.Diff.UI.Rendering;

namespace Fubar.Diff.UI.Views;

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

    private readonly ChangeLineBackgroundRenderer _backgroundRenderer;
    private readonly CharSpanColorizer _colorizer;
    private readonly SourceLineNumberMargin _lineNumbers;

    public DiffEditorPane()
    {
        InitializeComponent();

        _backgroundRenderer = new ChangeLineBackgroundRenderer(this);
        _colorizer = new CharSpanColorizer(this);
        _lineNumbers = new SourceLineNumberMargin();

        Editor.TextArea.TextView.BackgroundRenderers.Add(_backgroundRenderer);
        Editor.TextArea.TextView.LineTransformers.Add(_colorizer);
        Editor.TextArea.LeftMargins.Add(_lineNumbers);

        ApplyGutterStyle();
    }

    public AlignedDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    /// <summary>The underlying editor, for the parent view to wire scroll sync and caret moves.</summary>
    internal TextEditor TextEditor => Editor;

    /// <summary>The text view, which owns the scroll offset the two panes keep in step.</summary>
    internal TextView TextView => Editor.TextArea.TextView;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == DocumentProperty)
        {
            Apply(change.GetNewValue<AlignedDocument?>());
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
