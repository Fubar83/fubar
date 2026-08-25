using System;
using Avalonia;
using Avalonia.Controls.Primitives;

namespace Fubar.Controls;

/// <summary>
/// A group heading row: a small upper-cased <see cref="Title"/> on the left and an optional
/// <see cref="Action"/> slot on the right (typically an <see cref="IconButton"/> such as a "+" for
/// "add item to this section"). Matches the Left Pane's REQUESTS / ENVIRONMENTS / AUTH group headers.
/// </summary>
public class SectionHeader : TemplatedControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<SectionHeader, string?>(nameof(Title));

    public static readonly StyledProperty<object?> ActionProperty =
        AvaloniaProperty.Register<SectionHeader, object?>(nameof(Action));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Right-aligned content, usually an action button.</summary>
    public object? Action
    {
        get => GetValue(ActionProperty);
        set => SetValue(ActionProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(SectionHeader);
}
