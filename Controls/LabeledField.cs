using System;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace Fubar.Controls;

/// <summary>
/// Pairs a caption with an input. The <c>Header</c> is the label; the <c>Content</c> is the field
/// (a TextBox, ComboBox, ...). <see cref="Orientation"/> puts the label above the field
/// (<see cref="Orientation.Vertical"/>, the default) or to its left (<see cref="Orientation.Horizontal"/>)
/// for compact form rows.
/// </summary>
public class LabeledField : HeaderedContentControl
{
    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<LabeledField, Orientation>(nameof(Orientation), Orientation.Vertical);

    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(LabeledField);
}
