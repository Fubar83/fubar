using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Fubar.Controls;

/// <summary>
/// The vertical hairlines that make a tree read as a tree: one line per ancestor level, drawn down the
/// left of a row.
/// </summary>
/// <remarks>
/// <para>
/// It IS the indent, not a decoration laid over one: it reserves <see cref="Depth"/> x
/// <see cref="Step"/> of width and draws the rails through the space it reserved. Nothing else may
/// indent the row as well, or the two stack up - which is exactly what went wrong when this sat in a
/// row's DataTemplate while the Fluent row template was still applying its own Level-sized margin.
/// <c>Themes/TreeView.axaml</c> now places it inside the TreeViewItem template, bound to
/// <c>Level</c>, so every tree gets it and no host has to remember.
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

    /// <summary>Pixels of indent per nesting level.</summary>
    public const double DefaultStep = 14;

    /// <summary>Pixels per level, and so the tree's indent step.</summary>
    public static readonly StyledProperty<double> StepProperty =
        AvaloniaProperty.Register<TreeIndentGuides, double>(nameof(Step), defaultValue: DefaultStep);

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
