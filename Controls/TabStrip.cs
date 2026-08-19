using System;
using System.Collections;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Fubar.Controls;

/// <summary>
/// A horizontal, Chrome-style tab strip that owns its own drag-and-drop: reorder within the strip, a
/// floating ghost while a tab is dragged clear of any strip, and - via an <see cref="ITabDragHost"/> -
/// live move between strips in other windows and tear-off into a new window. It's a <see cref="ListBox"/>
/// underneath, so selection is <see cref="ListBox.SelectedItem"/> (bind it two-way) and tabs come from
/// <see cref="ItemsControl.ItemsSource"/> with a host-supplied <see cref="ItemsControl.ItemTemplate"/>
/// for the tab label. Everything domain-specific (what a tab represents, how windows are made) lives in
/// the host, so the strip stays app-agnostic.
/// </summary>
public class TabStrip : ListBox
{
    public static readonly StyledProperty<bool> ShowCloseButtonProperty =
        AvaloniaProperty.Register<TabStrip, bool>(nameof(ShowCloseButton), true);

    public static readonly StyledProperty<ICommand?> CloseCommandProperty =
        AvaloniaProperty.Register<TabStrip, ICommand?>(nameof(CloseCommand));

    public static readonly StyledProperty<ITabDragHost?> DragHostProperty =
        AvaloniaProperty.Register<TabStrip, ITabDragHost?>(nameof(DragHost));

    private const double DragThreshold = 6;

    // How far the cursor must leave a strip before the tab detaches (floats / becomes tear-off-able).
    // Keeps the tab in the bar while the cursor merely brushes just outside it, like Chrome - only a
    // deliberate pull spawns a floating ghost or a torn-off window.
    private const double DetachThreshold = 24;

    private object? _dragItem;
    private TabStrip _hostStrip = null!; // the strip currently holding the dragged item
    private IPointer? _pointer;          // the captured pointer, so a keyboard cancel can release it
    private TopLevel? _keyHost;          // drag window we listen to for Esc-to-cancel, cleared on reset
    private Point _dragStart;
    private bool _dragging;
    private Window? _ghost;
    private TabStrip? _dropTarget; // strip currently showing the drop caret + .drag-over highlight
    private Border? _dropCaret;    // this strip's PART_DropCaret insertion marker

    /// <summary>Whether each tab shows a trailing close button (also: middle-click closes).</summary>
    public bool ShowCloseButton
    {
        get => GetValue(ShowCloseButtonProperty);
        set => SetValue(ShowCloseButtonProperty, value);
    }

    /// <summary>Invoked with the tab item when it is closed (close button or middle-click).</summary>
    public ICommand? CloseCommand
    {
        get => GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    /// <summary>The app bridge for cross-window move + tear-off. When null, the strip still reorders
    /// within itself and shows the ghost, but can't move tabs to other windows or tear off.</summary>
    public ITabDragHost? DragHost
    {
        get => GetValue(DragHostProperty);
        set => SetValue(DragHostProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(TabStrip);

    protected override void OnApplyTemplate(Avalonia.Controls.Primitives.TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _dropCaret = e.NameScope.Find<Border>("PART_DropCaret");
    }

    public TabStrip()
    {
        // Claim the press in the TUNNEL (preview) phase. By the time a press bubbles back up to the
        // strip it has already been handled AND the pointer captured by the tab's own content (a
        // TextBlock/ContentPresenter), which swallows the drag entirely - clicks still select but nothing
        // drags. Tunnelling lets the strip see the press first (still unhandled), arm the drag, capture
        // the pointer to itself, and mark it handled so no descendant can steal it.
        AddHandler(PointerPressedEvent, OnPreviewPointerPressed, RoutingStrategies.Tunnel);
    }

    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // A press on the close button (or any button in the tab) is that button's business - let it
        // through untouched (no select, no drag, no handled).
        if (e.Source is Visual visual && visual.FindAncestorOfType<Button>(includeSelf: true) is not null)
        {
            return;
        }

        var props = e.GetCurrentPoint(this).Properties;
        // Find the tab by pointer position, not e.Source: intermediary content presenters can report as
        // the source and have no ListBoxItem ancestor, so an e.Source-only lookup can miss the tab.
        var item = ItemFromSource(e.Source) ?? ItemAtPoint(e.GetPosition(this));
        if (item is null)
        {
            return; // empty strip area - leave it for normal handling (e.g. window drag)
        }

        if (props.IsMiddleButtonPressed)
        {
            InvokeClose(item);
            e.Handled = true;
            return;
        }

        if (!props.IsLeftButtonPressed)
        {
            return;
        }

        // We own this press. Select the tab ourselves (we're about to mark it handled, so the base
        // ListBox's own selection won't run), then arm the drag and capture the pointer to the strip.
        SelectedItem = item;

        _dragItem = item;
        _hostStrip = this;
        _pointer = e.Pointer;
        _dragStart = e.GetPosition(this);
        _dragging = false;
        e.Pointer.Capture(this);

        _keyHost = TopLevel.GetTopLevel(this);
        _keyHost?.AddHandler(KeyDownEvent, OnDragKeyDown, RoutingStrategies.Tunnel);

        // Handled so the tab's own content can't capture the pointer and eat the drag.
        e.Handled = true;
    }

    // Esc aborts an in-progress drag: drop the ghost/float and release capture, leaving the tab in the
    // strip the live drag last parked it in - so a mistaken pull never spawns a torn-off window. Handled
    // on the drag window's TopLevel (subscribed for the drag's lifetime) rather than via OnKeyDown, so it
    // fires no matter which element in that window currently holds keyboard focus.
    private void OnDragKeyDown(object? sender, KeyEventArgs e)
    {
        if (_dragItem is null || e.Key != Key.Escape)
        {
            return;
        }

        var pointer = _pointer;
        ResetDrag();
        pointer?.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_dragItem is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var pos = e.GetPosition(this);
        if (!_dragging && Math.Abs(pos.X - _dragStart.X) < DragThreshold && Math.Abs(pos.Y - _dragStart.Y) < DragThreshold)
        {
            return;
        }

        if (!_dragging)
        {
            _dragging = true;
            // A grab cursor makes it obvious a drag is in progress. It sticks across windows because the
            // pointer is captured to this strip, so this strip's cursor wins for the whole gesture.
            Cursor = new Cursor(StandardCursorType.SizeAll);
        }

        UpdateDrag(this.PointToScreen(pos));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_dragItem is null)
        {
            base.OnPointerReleased(e);
            return;
        }

        var item = _dragItem;
        var wasDragging = _dragging;
        var hostStrip = _hostStrip;
        var screenPoint = this.PointToScreen(e.GetPosition(this));

        // Reset (clears _dragItem) before releasing capture: releasing capture raises
        // OnPointerCaptureLost, and the null _dragItem makes that a no-op instead of a duplicate
        // cleanup/AfterDrop on this intentional release.
        ResetDrag();
        e.Pointer.Capture(null);

        base.OnPointerReleased(e);

        if (wasDragging)
        {
            FinalizeDrop(item, hostStrip, screenPoint);
        }
    }

    // A drag can end without a release: showing a new top-level window (even our own non-activating
    // ghost) or the OS reclaiming the pointer can yank the capture, and then OnPointerReleased never
    // runs. Left unhandled, the picked-up tab stays hidden (.tab-floating) and the ghost window leaks -
    // the drag appears to "eat" the tab. Treat capture loss as a cancel: drop the ghost and the drag
    // visuals and leave the tab wherever the live drag last parked it (it's always in a real strip).
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        if (_dragItem is null)
        {
            return;
        }

        ResetDrag();
        DragHost?.AfterDrop();
    }

    private void ResetDrag()
    {
        if (_dragItem is not null)
        {
            ClearDragVisual(_hostStrip, _dragItem);
        }

        HideDropIndicator();

        _keyHost?.RemoveHandler(KeyDownEvent, OnDragKeyDown);
        _keyHost = null;

        CloseGhost();
        Cursor = null; // back to the default arrow
        _dragItem = null;
        _pointer = null;
        _dragging = false;
    }

    private void UpdateDrag(PixelPoint screenPoint)
    {
        if (_dragItem is null)
        {
            return;
        }

        var host = DragHost;
        var peers = host?.PeerStrips ?? new[] { this };
        var target = peers.FirstOrDefault(s => s.ScreenContains(screenPoint));

        if (target is not null)
        {
            if (!ReferenceEquals(target, _hostStrip))
            {
                if (host is not null)
                {
                    host.MoveTab(_dragItem, _hostStrip, target, target.DropIndexAt(screenPoint));
                    _hostStrip = target;
                }
            }
            else
            {
                target.ReorderTo(_dragItem, screenPoint);
            }

            SetDragVisual(floating: false);
            CloseGhost();
            ShowDropIndicator(target); // accent caret + strip outline at the landing slot
        }
        else
        {
            HideDropIndicator();
            SetDragVisual(floating: true);
            ShowGhost(_dragItem);
            if (_ghost is not null)
            {
                _ghost.Position = new PixelPoint(screenPoint.X + 12, screenPoint.Y + 8);
            }
        }
    }

    // --- drop indicator (accent caret in the target strip + a .drag-over outline on it) --------------

    private void ShowDropIndicator(TabStrip target)
    {
        if (!ReferenceEquals(_dropTarget, target))
        {
            _dropTarget?.ClearDropIndicator();
            _dropTarget = target;
        }

        target.MarkDropTarget(_dragItem);
    }

    private void HideDropIndicator()
    {
        _dropTarget?.ClearDropIndicator();
        _dropTarget = null;
    }

    // Place this strip's caret at the leading edge of where the dragged item now sits, and outline it.
    private void MarkDropTarget(object? item)
    {
        if (!Classes.Contains("drag-over"))
        {
            Classes.Add("drag-over");
        }

        if (_dropCaret is null || _dropCaret.Parent is not Visual host || ItemsSource is not IList list)
        {
            return;
        }

        var index = item is null ? -1 : list.IndexOf(item);
        double? x = null;
        if (index >= 0 && ContainerFromIndex(index) is Control container)
        {
            x = container.TranslatePoint(new Point(0, 0), host)?.X;
        }
        else if (list.Count > 0 && ContainerFromIndex(list.Count - 1) is Control last)
        {
            x = last.TranslatePoint(new Point(last.Bounds.Width, 0), host)?.X;
        }

        if (x is { } left)
        {
            _dropCaret.Margin = new Thickness(left - _dropCaret.Width / 2, 0, 0, 0);
            _dropCaret.IsVisible = true;
        }
    }

    private void ClearDropIndicator()
    {
        Classes.Remove("drag-over");
        if (_dropCaret is not null)
        {
            _dropCaret.IsVisible = false;
        }
    }

    private void FinalizeDrop(object item, TabStrip hostStrip, PixelPoint screenPoint)
    {
        var host = DragHost;
        if (host is null)
        {
            return;
        }

        try
        {
            if (!host.PeerStrips.Any(s => s.ScreenContains(screenPoint)))
            {
                host.TearOff(item, hostStrip, screenPoint);
            }
        }
        finally
        {
            host.AfterDrop();
        }
    }

    // --- geometry / item lookup helpers ------------------------------------------------------------

    private object? ItemFromSource(object? source) =>
        source is Visual v ? v.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext : null;

    // The tab whose realized container's box contains a client point, found geometrically rather than
    // by visual hit-testing: the strip's ScrollContentPresenter can report itself (not the tab) as the
    // pressed visual, so a hit-test-based lookup misses the tab and the drag never starts.
    private object? ItemAtPoint(Point clientPoint)
    {
        if (ItemsSource is not IList list)
        {
            return null;
        }

        for (var i = 0; i < list.Count; i++)
        {
            if (ContainerFromIndex(i) is Control container &&
                container.TranslatePoint(new Point(0, 0), this) is { } origin &&
                new Rect(origin, container.Bounds.Size).Contains(clientPoint))
            {
                return list[i];
            }
        }

        return null;
    }

    // Whether this strip should still "own" the dragged tab for a cursor at <paramref name="screenPoint"/>.
    // Horizontally that's the strip's exact span (so moving left/right hands the tab to a neighbouring
    // strip or lets it reorder to the ends); vertically it's inflated by <see cref="DetachThreshold"/>,
    // so a small nudge above/below the (typically short, title-bar-height) strip keeps the tab attached
    // and only a real vertical pull detaches it into the floating ghost / a tear-off.
    private bool ScreenContains(PixelPoint screenPoint)
    {
        if (TopLevel.GetTopLevel(this) is null || !IsEffectivelyVisible)
        {
            return false;
        }

        var topLeft = this.PointToScreen(new Point(0, 0));
        var bottomRight = this.PointToScreen(new Point(Bounds.Width, Bounds.Height));
        return screenPoint.X >= topLeft.X && screenPoint.X <= bottomRight.X &&
               screenPoint.Y >= topLeft.Y - DetachThreshold && screenPoint.Y <= bottomRight.Y + DetachThreshold;
    }

    /// <summary>The insertion slot (0..Count) in this strip's <see cref="ItemsControl.ItemsSource"/>
    /// for a cursor at <paramref name="screenPoint"/>, chosen by comparing the cursor's X against each
    /// tab's horizontal midpoint. Returns <c>Count</c> to drop after the last tab (so dragging into the
    /// strip's empty tail appends / moves to the end), and -1 only when there's no bound list.</summary>
    private int DropIndexAt(PixelPoint screenPoint)
    {
        if (ItemsSource is not IList list)
        {
            return -1;
        }

        var x = this.PointToClient(screenPoint).X;
        for (var i = 0; i < list.Count; i++)
        {
            if (ContainerFromIndex(i) is not Control container)
            {
                continue;
            }

            var mid = container.TranslatePoint(new Point(container.Bounds.Width / 2, 0), this)?.X;
            if (mid is { } m && x < m)
            {
                return i;
            }
        }

        return list.Count;
    }

    private void ReorderTo(object item, PixelPoint screenPoint)
    {
        if (ItemsSource is not IList list)
        {
            return;
        }

        var from = list.IndexOf(item);
        if (from < 0)
        {
            return;
        }

        // DropIndexAt is an insert-before slot over the *current* list (which still contains the dragged
        // tab), so once the tab is pulled out of an earlier slot every later slot shifts left by one.
        var insert = DropIndexAt(screenPoint);
        var to = insert > from ? insert - 1 : insert;
        if (to < 0 || to >= list.Count || to == from)
        {
            return;
        }

        // Prefer ObservableCollection.Move (single, smooth CollectionChanged) when available.
        var move = list.GetType().GetMethod("Move", [typeof(int), typeof(int)]);
        if (move is not null)
        {
            move.Invoke(list, [from, to]);
        }
        else
        {
            var obj = list[from];
            list.RemoveAt(from);
            list.Insert(to, obj);
        }
    }

    private void InvokeClose(object item)
    {
        if (CloseCommand is { } cmd && cmd.CanExecute(item))
        {
            cmd.Execute(item);
        }
    }

    // --- drag visuals -----------------------------------------------------------------------------

    private void SetDragVisual(bool floating)
    {
        if (_dragItem is null)
        {
            return;
        }

        if (GetContainer(_hostStrip, _dragItem) is { } container)
        {
            ToggleClass(container, "tab-dragging", true);
            ToggleClass(container, "tab-floating", floating);
        }
    }

    private static void ClearDragVisual(TabStrip strip, object item)
    {
        if (GetContainer(strip, item) is { } container)
        {
            ToggleClass(container, "tab-dragging", false);
            ToggleClass(container, "tab-floating", false);
        }
    }

    private static Control? GetContainer(TabStrip strip, object item)
    {
        if (strip.ItemsSource is not IList list)
        {
            return null;
        }

        var index = list.IndexOf(item);
        return index >= 0 ? strip.ContainerFromIndex(index) as Control : null;
    }

    private static void ToggleClass(Control control, string name, bool on)
    {
        if (on)
        {
            if (!control.Classes.Contains(name))
            {
                control.Classes.Add(name);
            }
        }
        else
        {
            control.Classes.Remove(name);
        }
    }

    // --- floating ghost ---------------------------------------------------------------------------

    private void ShowGhost(object item)
    {
        if (_ghost is not null)
        {
            return;
        }

        var sidebar = this.TryFindResource("BgSidebar", out var sv) && sv is IBrush s ? s : Brushes.DimGray;
        var accent = this.TryFindResource("PostmanOrangeBrush", out var av) && av is IBrush a ? a : Brushes.OrangeRed;

        Control inner;
        if (ItemTemplate?.Build(item) is { } built)
        {
            built.DataContext = item;
            inner = built;
        }
        else
        {
            var fg = this.TryFindResource("TextPrimary", out var fv) && fv is IBrush f ? f : Brushes.White;
            inner = new TextBlock
            {
                Text = item.ToString(),
                Foreground = fg,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
            };
        }

        _ghost = new Window
        {
            CanResize = false,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            SizeToContent = SizeToContent.WidthAndHeight,
            Background = Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            Focusable = false,
            IsHitTestVisible = false,
            WindowDecorations = WindowDecorations.None,
            // A lifted-tab look: near-opaque, an accent border, and a deeper shadow so it reads clearly
            // as the tab being carried (vs. the dimmed placeholder left behind in the strip).
            Content = new Border
            {
                Height = 32,
                MinWidth = 140,
                Padding = new Thickness(12, 0),
                Opacity = 0.92,
                CornerRadius = new CornerRadius(8, 8, 0, 0),
                Background = sidebar,
                BorderBrush = accent,
                BorderThickness = new Thickness(1.5),
                BoxShadow = BoxShadows.Parse("0 8 22 0 #73000000"),
                Child = inner,
            },
        };

        _ghost.Show();
    }

    private void CloseGhost()
    {
        _ghost?.Close();
        _ghost = null;
    }
}
