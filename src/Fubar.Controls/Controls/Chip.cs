using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace Fubar.Controls;

/// <summary>
/// A rounded tag holding a piece of content plus an optional close (&#x2715;) button - filter tokens,
/// selected values, applied tags, ... The close button is an <see cref="IconButton"/>, shown only when
/// <see cref="ShowClose"/> is true, and fires <see cref="CloseCommand"/> (with
/// <see cref="CloseCommandParameter"/>) when clicked.
/// </summary>
public class Chip : ContentControl
{
    public static readonly StyledProperty<bool> ShowCloseProperty =
        AvaloniaProperty.Register<Chip, bool>(nameof(ShowClose), true);

    public static readonly StyledProperty<ICommand?> CloseCommandProperty =
        AvaloniaProperty.Register<Chip, ICommand?>(nameof(CloseCommand));

    public static readonly StyledProperty<object?> CloseCommandParameterProperty =
        AvaloniaProperty.Register<Chip, object?>(nameof(CloseCommandParameter));

    /// <summary>Whether the trailing close button is shown.</summary>
    public bool ShowClose
    {
        get => GetValue(ShowCloseProperty);
        set => SetValue(ShowCloseProperty, value);
    }

    /// <summary>Invoked when the close button is clicked.</summary>
    public ICommand? CloseCommand
    {
        get => GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    public object? CloseCommandParameter
    {
        get => GetValue(CloseCommandParameterProperty);
        set => SetValue(CloseCommandParameterProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(Chip);
}
