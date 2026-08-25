using System;
using Avalonia;
using Avalonia.Controls.Primitives;

namespace Fubar.Controls;

/// <summary>
/// A centered placeholder for empty regions - a large <see cref="Icon"/> glyph over a
/// <see cref="Title"/> and muted <see cref="Description"/>, with an optional <see cref="Action"/> slot
/// (e.g. an "Open Workspace" button) beneath. Used where "nothing is open / nothing matches / no
/// results yet" needs to read as intentional rather than broken.
/// </summary>
public class EmptyState : TemplatedControl
{
    public static readonly StyledProperty<string?> IconProperty =
        AvaloniaProperty.Register<EmptyState, string?>(nameof(Icon));

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<EmptyState, string?>(nameof(Title));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<EmptyState, string?>(nameof(Description));

    public static readonly StyledProperty<object?> ActionProperty =
        AvaloniaProperty.Register<EmptyState, object?>(nameof(Action));

    /// <summary>A glyph/emoji shown large above the title.</summary>
    public string? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>Optional call-to-action content shown below the description.</summary>
    public object? Action
    {
        get => GetValue(ActionProperty);
        set => SetValue(ActionProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(EmptyState);
}
