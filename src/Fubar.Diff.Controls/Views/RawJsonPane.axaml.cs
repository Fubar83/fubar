using Avalonia;
using Avalonia.Controls;
using Fubar.Diff.Controls.Rendering;
using Fubar.Diff.Core.Json;

namespace Fubar.Diff.Controls.Views;

/// <summary>
/// One side of the Hybrid view. Deliberately much simpler than <see cref="DiffEditorPane"/>: there is
/// no alignment, no filler rows, and no character-level span colouring - just the document as it was
/// parsed, with the current change's own <see cref="SourceSpan"/> marked directly. Reuses
/// <see cref="CurrentHunkRenderer"/> for that marker, since an unaligned document's own line numbers
/// already ARE the numbers a span refers to - there is nothing left to remap.
/// </summary>
public partial class RawJsonPane : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<RawJsonPane, string?>(nameof(Text));

    public static readonly StyledProperty<SourceSpan?> HighlightSpanProperty =
        AvaloniaProperty.Register<RawJsonPane, SourceSpan?>(nameof(HighlightSpan));

    private readonly CurrentHunkRenderer _highlight;

    public RawJsonPane()
    {
        InitializeComponent();

        _highlight = new CurrentHunkRenderer(this);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_highlight);
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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty)
        {
            // The old highlight addresses lines in the DOCUMENT THAT JUST LEFT - keeping it would
            // paint the marker over unrelated text for one frame, or throw if the new document is
            // shorter.
            _highlight.SetRange(-1, -1);
            Editor.Document.Text = change.GetNewValue<string?>() ?? string.Empty;
            Editor.ScrollToHome();
        }
        else if (change.Property == HighlightSpanProperty)
        {
            ApplyHighlight(change.GetNewValue<SourceSpan?>());
        }
    }

    private void ApplyHighlight(SourceSpan? span)
    {
        if (span is not { IsKnown: true } known)
        {
            _highlight.SetRange(-1, -1);
            Editor.TextArea.TextView.Redraw();
            return;
        }

        // SourceSpan is 1-based inclusive; CurrentHunkRenderer takes 0-based row indices, matching how
        // it is fed from the aligned view elsewhere.
        _highlight.SetRange(known.StartLine - 1, known.EndLine - 1);
        Editor.TextArea.TextView.Redraw();

        // ScrollToLine already centres reasonably within the viewport; a highlight that is off-screen
        // when it becomes current would defeat the entire point of marking it.
        if (known.StartLine >= 1 && known.StartLine <= Editor.Document.LineCount)
        {
            Editor.ScrollToLine(known.StartLine);
        }
    }
}
