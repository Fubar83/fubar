using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Fubar.Controls;

/// <summary>
/// The connector rails that make a tree read as a tree, in the shape everyone already knows:
/// <code>
/// public/
/// ├─ request 1
/// ├─ request 2
/// └─ request 3
/// </code>
/// A tee into every row, an elbow into the last one, and an ancestor's rail carried down past its
/// descendants only for as long as that ancestor still has siblings below it.
/// </summary>
/// <remarks>
/// <para>
/// It IS the indent, not a decoration laid over one: it reserves (<see cref="Depth"/> + 1) x
/// <see cref="Step"/> of width - one column per ancestor plus one for the row's own connector - and
/// draws inside the space it reserved. Nothing else may indent the row as well, or the two stack up,
/// which is exactly what went wrong when this sat in a row's DataTemplate while the Fluent row template
/// was still applying its own Level-sized margin. <c>Themes/TreeView.axaml</c> places it inside the
/// TreeViewItem template bound to <c>Level</c>, so every tree gets it and no host has to remember.
/// </para>
/// <para>
/// The rails are drawn per row and rely on rows being contiguous to join up, so the row must fill its
/// container's full height - any vertical padding or margin between rows shows up as a break in the
/// line. That is why <c>Themes/TreeView.axaml</c> gives TreeViewItem no vertical padding and puts the
/// row's breathing room in MinHeight instead.
/// </para>
/// <para>
/// "Is this the last child?" is not something a row's data knows - it is a fact about the row's position
/// among its siblings, and about every ancestor's position among THEIRS. So it is read from the
/// container tree at render time rather than bound. Changing a folder's contents changes the answer for
/// rows that did not themselves change, which is why this subscribes to each owning ItemsControl's
/// ItemCount: without that, deleting the last request in a folder would leave the new last row still
/// drawing a tee into nothing.
/// </para>
/// </remarks>
public sealed class TreeIndentGuides : Control
{
    /// <summary>Number of ancestor levels above this row. 0 is a top-level row, which still gets a
    /// connector - it hangs off the tree's own root.</summary>
    public static readonly StyledProperty<int> DepthProperty =
        AvaloniaProperty.Register<TreeIndentGuides, int>(nameof(Depth));

    /// <summary>Pixels of indent per nesting level.</summary>
    public const double DefaultStep = 14;

    /// <summary>Pixels per level, and so the tree's indent step.</summary>
    public static readonly StyledProperty<double> StepProperty =
        AvaloniaProperty.Register<TreeIndentGuides, double>(nameof(Step), defaultValue: DefaultStep);

    public static readonly StyledProperty<IBrush?> LineBrushProperty =
        AvaloniaProperty.Register<TreeIndentGuides, IBrush?>(nameof(LineBrush));

    private readonly List<IDisposable> _siblingWatches = [];

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
        new((System.Math.Max(Depth, 0) + 1) * Step, 0);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        for (var owner = OwningItem(); owner is not null; owner = ParentItem(owner))
        {
            if (ContainerOwnerOf(owner) is { } items)
            {
                _siblingWatches.Add(items.GetObservable(ItemsControl.ItemCountProperty)
                    .Subscribe(new AnonymousObserver(InvalidateVisual)));
            }
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        foreach (var watch in _siblingWatches)
        {
            watch.Dispose();
        }

        _siblingWatches.Clear();
        base.OnDetachedFromVisualTree(e);
    }

    public override void Render(DrawingContext context)
    {
        if (LineBrush is null || Bounds.Height <= 0 || OwningItem() is not { } row)
        {
            return;
        }

        // A hairline that stays a hairline: 1 device pixel wherever the window is scaled to, and offset
        // by half of one so it lands ON a pixel column rather than straddling two and rendering as a
        // 2px smudge.
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var thickness = 1.0 / scale;
        var mid = System.Math.Round(Bounds.Height / 2);
        var depth = System.Math.Max(Depth, 0);

        // Ancestor rails, outermost first. An ancestor that was the last of its siblings has nothing
        // below it, so its rail stops there rather than running on past the end of its own branch.
        var ancestor = row;
        for (var level = depth - 1; level >= 0; level--)
        {
            ancestor = ParentItem(ancestor);
            if (ancestor is null)
            {
                break;
            }

            if (HasSiblingBelow(ancestor))
            {
                context.FillRectangle(LineBrush, new Rect(ColumnX(level, thickness), 0, thickness, Bounds.Height));
            }
        }

        // This row's own connector: down to the middle always, on to the bottom only if a sibling
        // follows - the difference between a tee and an elbow - plus the stub reaching the content.
        var x = ColumnX(depth, thickness);
        var down = HasSiblingBelow(row) ? Bounds.Height : mid;

        context.FillRectangle(LineBrush, new Rect(x, 0, thickness, down));
        context.FillRectangle(LineBrush, new Rect(x, mid, Step / 2, thickness));
    }

    /// <summary>Centre of the indent column for <paramref name="level"/>, snapped onto a pixel.</summary>
    private double ColumnX(int level, double thickness) =>
        ((level * Step) + (Step / 2)) - (thickness / 2);

    private TreeViewItem? OwningItem() => this.FindAncestorOfType<TreeViewItem>();

    private static TreeViewItem? ParentItem(TreeViewItem item) =>
        item.FindAncestorOfType<TreeViewItem>();

    /// <summary>The ItemsControl that generated <paramref name="item"/> - its parent row, or the tree.</summary>
    private static ItemsControl? ContainerOwnerOf(TreeViewItem item) =>
        item.FindAncestorOfType<ItemsControl>();

    private static bool HasSiblingBelow(TreeViewItem item)
    {
        if (ContainerOwnerOf(item) is not { } owner)
        {
            return false;
        }

        var index = owner.IndexFromContainer(item);
        return index >= 0 && index < owner.ItemCount - 1;
    }

    private sealed class AnonymousObserver(Action onNext) : IObserver<int>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(int value) => onNext();
    }
}
