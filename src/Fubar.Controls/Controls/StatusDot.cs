using System;
using Avalonia;
using Avalonia.Controls.Primitives;

namespace Fubar.Controls;

/// <summary>
/// A tiny filled circle used as a status indicator (dirty dot, online/offline, unread, ...). Colour
/// comes from <c>Background</c>; size from <see cref="Diameter"/>. Pairs naturally with a text label
/// inside a <c>StackPanel</c> or with <see cref="Badge"/>.
/// </summary>
public class StatusDot : TemplatedControl
{
    public static readonly StyledProperty<double> DiameterProperty =
        AvaloniaProperty.Register<StatusDot, double>(nameof(Diameter), 8d);

    /// <summary>Width/height of the dot in device-independent pixels.</summary>
    public double Diameter
    {
        get => GetValue(DiameterProperty);
        set => SetValue(DiameterProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(StatusDot);
}
