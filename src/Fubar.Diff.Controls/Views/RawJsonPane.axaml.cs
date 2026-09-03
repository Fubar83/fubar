using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Fubar.Diff.Controls.Rendering;
using Fubar.Diff.Core.Json;
using Fubar.Diff.Core.Models;

namespace Fubar.Diff.Controls.Views;

/// <summary>
/// One side of the Json view. Deliberately much simpler than <see cref="DiffEditorPane"/>: there is
/// no alignment and no filler rows - just the document as it was parsed, with each change's own
/// <see cref="SourceSpan"/> marked directly.
///
/// Three renderers, and which of them matter depends on <see cref="Emphasized"/>. In the main panes
/// <see cref="JsonChangeSpanColorizer"/> tints EVERY change quietly and the current one strongly,
/// while <see cref="CurrentHunkRenderer"/> bands and brackets the current change's lines (a
/// full-width band needs no remapping here - an unaligned document's own line numbers already ARE the
/// numbers a span refers to). The Json close-up (<c>JsonDetailPane</c>) shows one change and nothing
/// else, so it drops both in favour of <see cref="SpanTextColorizer"/> over the exact characters.
/// </summary>
public partial class RawJsonPane : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<RawJsonPane, string?>(nameof(Text));

    public static readonly StyledProperty<SourceSpan?> HighlightSpanProperty =
        AvaloniaProperty.Register<RawJsonPane, SourceSpan?>(nameof(HighlightSpan));

    /// <summary>
    /// Every change in this document, so the ones the user is not currently on are still visible.
    ///
    /// Optional: the close-up leaves it unset, and so does any host that only wants the current
    /// change marked. The spans must address the text this pane is SHOWING - see
    /// <c>DiffPaneViewModel.SemanticChanges</c>, which is the list whose spans point into the raw
    /// text rather than into the canonicalized copy the aligner used.
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<JsonChange>?> ChangesProperty =
        AvaloniaProperty.Register<RawJsonPane, IReadOnlyList<JsonChange>?>(nameof(Changes));

    /// <summary>
    /// Which side of the comparison this pane is showing.
    ///
    /// Only used to colour a MODIFIED change, which by definition exists on both sides: the left
    /// document lost that value and the right gained one, so the two panes paint it in the removal
    /// and addition colours respectively rather than in one shared "something changed" colour.
    /// </summary>
    public static readonly StyledProperty<DiffSide> SideProperty =
        AvaloniaProperty.Register<RawJsonPane, DiffSide>(nameof(Side));

    /// <summary>
    /// True in the Json view's close-up (<c>JsonDetailPane</c>) - see the class comment for what this
    /// switches between.
    /// </summary>
    public static readonly StyledProperty<bool> EmphasizedProperty =
        AvaloniaProperty.Register<RawJsonPane, bool>(nameof(Emphasized));

    private readonly CurrentHunkRenderer _highlight;
    private readonly SpanTextColorizer _textHighlight;
    private readonly JsonChangeSpanColorizer _changeSpans;

    public RawJsonPane()
    {
        InitializeComponent();

        _highlight = new CurrentHunkRenderer(this);
        _textHighlight = new SpanTextColorizer(this);
        _changeSpans = new JsonChangeSpanColorizer(this);

        Editor.TextArea.TextView.BackgroundRenderers.Add(_highlight);

        // Every change first, the close-up's single strong highlight second: line transformers run in
        // order and the later one wins on text they both cover, so the close-up can never end up
        // showing its own change in the quiet colour.
        Editor.TextArea.TextView.LineTransformers.Add(_changeSpans);
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

    public IReadOnlyList<JsonChange>? Changes
    {
        get => GetValue(ChangesProperty);
        set => SetValue(ChangesProperty, value);
    }

    public DiffSide Side
    {
        get => GetValue(SideProperty);
        set => SetValue(SideProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty)
        {
            // Every mark addresses lines in the DOCUMENT THAT JUST LEFT - keeping them would paint
            // over unrelated text for one frame, or throw if the new document is shorter. The change
            // list is cleared for the same reason and re-applied below, since Text and Changes arrive
            // as two separate property assignments however close together the host sets them.
            _highlight.SetRange(-1, -1);
            _textHighlight.SetSpan(null);
            _changeSpans.SetChanges([], Side);

            Editor.Document.Text = change.GetNewValue<string?>() ?? string.Empty;
            Editor.ScrollToHome();

            ApplyChanges();
        }
        else if (change.Property == HighlightSpanProperty)
        {
            ApplyHighlight(change.GetNewValue<SourceSpan?>());
        }
        else if (change.Property == ChangesProperty || change.Property == SideProperty)
        {
            ApplyChanges();
        }
        else if (change.Property == EmphasizedProperty)
        {
            // Which renderer owns the highlight depends on this flag, so re-derive everything from
            // scratch rather than trying to hand the current span from one to the other.
            ApplyChanges();
            ApplyHighlight(HighlightSpan);
        }
    }

    /// <summary>
    /// Scrolls sideways so the highlighted characters are actually on screen.
    ///
    /// <para>Vertical centring alone is not enough here, and this pane is where that shows worst: an
    /// unaligned Json document is regularly MINIFIED, so the change is one line down and two hundred
    /// characters across. Centring found the line and left the reader looking at the start of it - the
    /// close-up would show a wall of text with its highlight somewhere off the right edge.</para>
    ///
    /// <para>Posted, because the visual line for a row that was just scrolled to does not exist until
    /// the next layout pass, and asking for a column position before then finds nothing and scrolls
    /// nowhere - silently, which is how this went unnoticed in the main panes' version until it was
    /// tried on a minified file.</para>
    /// </summary>
    private void RevealHorizontally(SourceSpan span)
    {
        Dispatcher.UIThread.Post(
            () => EditorScroll.RevealColumns(
                Editor, Editor.TextArea.TextView, span.StartLine, span.StartColumn, span.EndColumn),
            DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Hands the quiet all-changes layer what it should mark.
    ///
    /// Nothing at all in the close-up: that pane shows an EXCERPT, whose lines are renumbered from 1,
    /// so spans addressing the whole document would land on whatever text happened to be at those
    /// line numbers in the excerpt - which is worse than not drawing them.
    /// </summary>
    private void ApplyChanges()
    {
        _changeSpans.SetChanges(Emphasized ? [] : Changes ?? [], Side);
        _changeSpans.SetCurrent(Emphasized ? null : HighlightSpan);

        Editor.TextArea.TextView.Redraw();
    }

    private void ApplyHighlight(SourceSpan? span)
    {
        // The quiet layer paints the current change at full strength too, so it has to be told which
        // one that is every time it moves.
        _changeSpans.SetCurrent(Emphasized ? null : span);

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
            RevealHorizontally(known);
        }
    }
}
