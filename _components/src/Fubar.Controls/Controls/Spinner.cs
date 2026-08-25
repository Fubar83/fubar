using System;
using Avalonia;
using Avalonia.Controls.Primitives;

namespace Fubar.Controls;

/// <summary>
/// An indeterminate loading spinner - a rotating partial ring. Size via <see cref="Diameter"/>, colour
/// via <c>Foreground</c>. Show it while an async operation (a request send, a load) is in flight.
/// </summary>
public class Spinner : TemplatedControl
{
    public static readonly StyledProperty<double> DiameterProperty =
        AvaloniaProperty.Register<Spinner, double>(nameof(Diameter), 18d);

    public double Diameter
    {
        get => GetValue(DiameterProperty);
        set => SetValue(DiameterProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(Spinner);
}
