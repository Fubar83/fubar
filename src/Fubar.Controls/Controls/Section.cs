using System;
using Avalonia;
using Avalonia.Controls;

namespace Fubar.Controls;

/// <summary>
/// A titled group container for pane/sidebar sections: a banded header row (an upper-cased
/// <see cref="Title"/> on the left plus an optional right-aligned <see cref="Action"/>, e.g. a "+"
/// button) over arbitrary <see cref="ContentControl.Content"/>. One component for the repeated
/// "group header + its list/tree" pattern (Environments / Auth Profiles / Requests in a left pane) -
/// every section shares the same header shape while each host plugs in different children.
///
/// Composes <see cref="SectionHeader"/> for the header row; the banded look (header fill, bold title,
/// padding) lives in the theme so callers set only <see cref="Title"/>, <see cref="Action"/>, and the
/// body. Draws a bottom divider by default (via <c>BorderThickness</c>) - set <c>BorderThickness="0"</c>
/// on the last section in a stack.
/// </summary>
public class Section : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<Section, string?>(nameof(Title));

    public static readonly StyledProperty<object?> ActionProperty =
        AvaloniaProperty.Register<Section, object?>(nameof(Action));

    /// <summary>The group heading, shown upper-cased in the banded header.</summary>
    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Right-aligned header content, usually an add/action button.</summary>
    public object? Action
    {
        get => GetValue(ActionProperty);
        set => SetValue(ActionProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(Section);
}
