using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.VisualTree;
using Fubar.Studio.Core.Variables;

namespace Fubar.Studio.UI.Controls;

/// <summary>
/// Attached-property behavior implementing the Universal Variable Tooltip system
/// (RequestEditorPane.md §4) on any <see cref="TextBox"/>: tokenizes every <c>{{name}}</c> in the
/// text, sets a <c>variable-undefined</c> or <c>variable-valid</c> style class (see Fubar.Controls'
/// <c>Palette.axaml</c> <c>VariableValidBrush</c>/<c>VariableUndefinedBrush</c> tokens) so the box's
/// border tints amber/blue, and answers the hover with whatever the pointer is actually over. Attach
/// via <c>controls:VariableTooltip.Context="{Binding SomeVariableTooltipContext}"</c> on a TextBox.
///
/// <para><b>What to SAY lives in <see cref="VariableHover"/>, in Core.</b> This class owns only the
/// part that needs a control: hit-testing the pointer to a character index, and keeping the open
/// popup up to date. The rule it feeds is testable without a pointer.</para>
///
/// <para><b>Scope note:</b> this does not recolor individual <c>{{token}}</c> substrings inline
/// within the box's own text run (the spec's per-token blue/amber pill styling) - that needs a
/// custom-rendered text presenter, a materially larger control than fits this pass. The whole box
/// gets one accent border instead (amber if anything is undefined, blue if every token resolves,
/// neutral if there are no tokens).</para>
/// </summary>
public static class VariableTooltip
{
    public static readonly AttachedProperty<VariableTooltipContext?> ContextProperty =
        AvaloniaProperty.RegisterAttached<TextBox, VariableTooltipContext?>("Context", typeof(VariableTooltip));

    public static void SetContext(TextBox element, VariableTooltipContext? value) => element.SetValue(ContextProperty, value);

    public static VariableTooltipContext? GetContext(TextBox element) => element.GetValue(ContextProperty);

    /// <summary>
    /// The tooltip's content, kept per box and MUTATED rather than replaced.
    /// </summary>
    /// <remarks>
    /// A tooltip that only caught up on the next hover would be the wrong behaviour here: the pointer
    /// moves from one token to the next within a single box with the popup already open the whole way.
    /// Handing <c>ToolTip.Tip</c> a live control and setting its Text means the open popup shows the
    /// new answer, whatever a given Avalonia version does about a changed Tip value.
    /// </remarks>
    private static readonly AttachedProperty<TextBlock?> ContentProperty =
        AvaloniaProperty.RegisterAttached<TextBox, TextBlock?>("Content", typeof(VariableTooltip));

    static VariableTooltip()
    {
        ContextProperty.Changed.AddClassHandler<TextBox>((box, _) =>
        {
            box.PropertyChanged -= OnTextBoxPropertyChanged;
            box.PropertyChanged += OnTextBoxPropertyChanged;
            box.PointerMoved -= OnPointerMoved;
            box.PointerMoved += OnPointerMoved;
            Apply(box, index: null);
        });
    }

    private static void OnTextBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is TextBox box && e.Property == TextBox.TextProperty)
        {
            Apply(box, index: null);
        }
    }

    /// <summary>Re-answers as the pointer moves within the box, so the tooltip is about the token
    /// under it rather than about the box.</summary>
    private static void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is TextBox box)
        {
            Apply(box, IndexUnder(box, e));
        }
    }

    private static void Apply(TextBox box, int? index)
    {
        var context = GetContext(box);
        var text = box.Text ?? "";

        // AcceptsReturn rather than a line count: it is what the field was DECLARED as, so a one-line
        // body box does not silently start behaving like a URL bar.
        var tip = context is null ? null : VariableHover.Describe(context, text, index, box.AcceptsReturn);

        if (tip is null)
        {
            ToolTip.SetTip(box, null);
            box.SetValue(ContentProperty, null);
            SetClass(box, "variable-undefined", false);
            SetClass(box, "variable-valid", false);
            return;
        }

        Show(box, tip);

        var undefined = VariableHover.AnyUndefined(context!, text);
        SetClass(box, "variable-undefined", undefined);
        SetClass(box, "variable-valid", !undefined);
    }

    /// <summary>
    /// Which character of the box the pointer is over, or null when it is past the end of the text.
    /// </summary>
    /// <remarks>
    /// Hit-tested against the TEXT PRESENTER's own layout rather than computed from the box's bounds:
    /// the presenter is inset by the box's padding and moves under a long value as it scrolls, so
    /// anything measured from the box would drift by exactly the amount that matters once a URL is
    /// longer than its field.
    /// </remarks>
    private static int? IndexUnder(TextBox box, PointerEventArgs e)
    {
        if (box.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault() is not { TextLayout: { } layout } presenter)
        {
            return null;
        }

        var hit = layout.HitTestPoint(e.GetPosition(presenter));
        return hit.IsInside ? hit.TextPosition : null;
    }

    private static void Show(TextBox box, string text)
    {
        if (box.GetValue(ContentProperty) is { } existing)
        {
            existing.Text = text;
            return;
        }

        var content = new TextBlock { Text = text };
        box.SetValue(ContentProperty, content);
        ToolTip.SetTip(box, content);
    }

    private static void SetClass(TextBox box, string className, bool value)
    {
        if (value)
        {
            if (!box.Classes.Contains(className))
            {
                box.Classes.Add(className);
            }
        }
        else
        {
            box.Classes.Remove(className);
        }
    }
}
