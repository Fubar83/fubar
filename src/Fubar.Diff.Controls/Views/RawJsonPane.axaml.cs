using Avalonia;
using Avalonia.Controls;
using Fubar.Diff.Controls.Rendering;
using Fubar.Diff.Core.Json;

namespace Fubar.Diff.Controls.Views;

/// <summary>
/// One side of the Hybrid view. Deliberately much simpler than <see cref="DiffEditorPane"/>: there is
/// no alignment and no filler rows - just the document as it was parsed, with the current change's own
/// <see cref="SourceSpan"/> marked directly. Two different renderers share that job depending on
/// <see cref="Emphasized"/>: the main panes use <see cref="CurrentHunkRenderer"/> (a full-width band -
/// an unaligned document's own line numbers already ARE the numbers a span refers to, so there is
/// nothing to remap), while the Json close-up (<c>JsonDetailPane</c>) uses <see cref="SpanTextColorizer"/>
/// instead, highlighting only the exact characters the span covers.
/// </summary>
public partial class RawJsonPane : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<RawJsonPane, string?>(nameof(Text));

    public static readonly StyledProperty<SourceSpan?> HighlightSpanProperty =
        AvaloniaProperty.Register<RawJsonPane, SourceSpan?>(nameof(HighlightSpan));

    /// <summary>
    /// True in the Json view's close-up (<c>JsonDetailPane</c>) - see the class comment for what this
    /// switches between.
    /// </summary>
    public static readonly StyledProperty<bool> EmphasizedProperty =
        AvaloniaProperty.Register<RawJsonPane, bool>(nameof(Emphasized));

    private readonly CurrentHunkRenderer _highlight;
    private readonly SpanTextColorizer _textHighlight;

    public RawJsonPane()
    {
        InitializeComponent();

        _highlight = new CurrentHunkRenderer(this);
        _textHighlight = new SpanTextColorizer(this);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_highlight);
        Editor.TextArea.TextView.LineTransformers.Add(_textHighlight);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public SourceSpan? HighlightSpan
    {
        get => GetValue(HighlightSpanProperty);
        set => SetValue(HighlightSpanProperty, value);
    }

    public bool Emphasized
    {
        get => GetValue(EmphasizedProperty);
        set => SetValue(EmphasizedProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty)
        {
            // The old highlight addresses lines in the DOCUMENT THAT JUST LEFT - keeping it would
            // paint the marker over unrelated text for one frame, or throw if the new document is
            // shorter.
            _highlight.SetRange(-1, -1);
            _textHighlight.SetSpan(null);
            Editor.Document.Text = change.GetNewValue<string?>() ?? string.Empty;
            Editor.ScrollToHome();
        }
        else if (change.Property == HighlightSpanProperty)
        {
            ApplyHighlight(change.GetNewValue<SourceSpan?>());
        }
        else if (change.Property == EmphasizedProperty)
        {
            // Which renderer owns the highlight depends on this flag, so re-derive both from scratch
            // rather than trying to hand the current span from one to the other.
            ApplyHighlight(HighlightSpan);
        }
    }

    private void ApplyHighlight(SourceSpan? span)
    {
        if (span is not { IsKnown: true } known)
        {
            _highlight.SetRange(-1, -1);
            _textHighlight.SetSpan(null);
            Editor.TextArea.TextView.Redraw();
            return;
        }

        if (Emphasized)
        {
            _highlight.SetRange(-1, -1);
            _textHighlight.SetSpan(known);
        }
        else
        {
            _textHighlight.SetSpan(null);

            // SourceSpan is 1-based inclusive; CurrentHunkRenderer takes 0-based row indices, matching
            // how it is fed from the aligned view elsewhere.
            _highlight.SetRange(known.StartLine - 1, known.EndLine - 1);
        }

        Editor.TextArea.TextView.Redraw();

        // Centred, not merely visible - a highlight tucked against the viewport's edge is easy to miss
        // when it becomes current.
        if (known.StartLine >= 1 && known.StartLine <= Editor.Document.LineCount)
        {
            EditorScroll.CenterOnLine(Editor, Editor.TextArea.TextView, known.StartLine);
        }
    }
}
