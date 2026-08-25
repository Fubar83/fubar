using System;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace Fubar.Controls;

/// <summary>
/// A one-pixel separator line. <see cref="Orientation"/> picks a horizontal rule (stretches wide,
/// separating stacked rows) or a vertical rule (stretches tall, separating toolbar groups). Colour
/// is <c>Background</c> (defaults to the subtle border token).
/// </summary>
public class Divider : TemplatedControl
{
    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<Divider, Orientation>(nameof(Orientation), Orientation.Horizontal);

    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(Divider);
}
