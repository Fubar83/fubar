using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Fubar.Controls;

/// <summary>
/// The vertical hairlines that make a tree read as a tree: one line per ancestor level, drawn down the
/// left of a row at the same <see cref="TreeLevelIndentConverter.StepPixels"/> rhythm the rows are
/// indented by.
/// </summary>
/// <remarks>
/// <para>
/// Put it in the row's DataTemplate as the first column, bound to the node's depth, INSTEAD of applying
/// <see cref="TreeLevelIndentConverter"/> to the row's Margin - it reserves exactly the same width, so
/// it indents the row as well as decorating it, and the two must not both be applied.
/// </para>
/// <para>
/// The lines are drawn per row and rely on rows being contiguous to join up into a continuous rail, so
/// the row must fill its container's full height - any vertical padding or margin between rows shows up
/// as a gap in the line. That is why <c>Themes/TreeView.axaml</c> gives TreeViewItem no vertical padding
/// and puts the row's breathing room in MinHeight instead.
/// </para>
/// </remarks>
public sealed class TreeIndentGuides : Control
{
    /// <summary>Nesting level of the row: 0 draws nothing and takes no width.</summary>
    public static readonly StyledProperty<int> DepthProperty =
        AvaloniaProperty.Register<TreeIndentGuides, int>(nameof(Depth));

    /// <summary>Pixels per level. Defaults to the same step the rows are indented by.</summary>
    public static readonly StyledProperty<double> StepProperty =
        AvaloniaProperty.Register<TreeIndentGuides, double>(
            nameof(Step),
            defaultValue: TreeLevelIndentConverter.StepPixels);

    public static readonly StyledProperty<IBrush?> LineBrushProperty =
        AvaloniaProperty.Register<TreeIndentGuides, IBrush?>(nameof(LineBrush));

    static TreeIndentGuides()
    {
        AffectsMeasure<TreeIndentGuides>(DepthProperty, StepProperty);
        AffectsRender<TreeIndentGuides>(DepthProperty, StepProperty, LineBrushProperty);
    }

    public int Depth
    {
        get => GetValue(DepthProperty);
        set => SetValue(DepthProperty, value);
    }

    public double Step
    {
        get => GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public IBrush? LineBrush
    {
        get => GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(System.Math.Max(Depth, 0) * Step, 0);

    public override void Render(DrawingContext context)
    {
        if (LineBrush is null || Depth <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        // A hairline that stays a hairline: 1 device pixel wherever the window is scaled to, and offset
        // by half of one so it lands ON a pixel column rather than straddling two and rendering as a
        // 2px smudge.
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var thickness = 1.0 / scale;

        // Centred in each level's indent block, which puts the line under the leading edge of the
        // parent row's content rather than hard against the pane edge.
        for (var level = 0; level < Depth; level++)
        {
            var x = ((level * Step) + (Step / 2)) - (thickness / 2);
            context.FillRectangle(LineBrush, new Rect(x, 0, thickness, Bounds.Height));
        }
    }
}
