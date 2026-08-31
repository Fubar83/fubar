using System;
using Avalonia;
using Avalonia.Controls.Primitives;

namespace Fubar.Controls;

/// <summary>
/// One line of a settings page: a plain-language <see cref="HeaderedContentControl.Header"/>, a muted
/// <see cref="Description"/> under it, and the control itself (<c>Content</c> - a switch, a combo, a
/// number box) on the right.
///
/// The description is the point. A settings window whose explanations all live in tooltips reads as a
/// wall of terse labels, and the one thing a user needs in order to answer "do I want this?" is the
/// thing they have to hover to find - if they suspect it is there at all. Written out, each row
/// answers its own question, and the tooltip goes back to being for detail nobody needs the first
/// time.
/// </summary>
public class SettingRow : HeaderedContentControl
{
    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<SettingRow, string?>(nameof(Description));

    /// <summary>
    /// One short sentence saying what turning this on does, in the user's words rather than the
    /// codebase's. Hidden when empty, for a row whose header is genuinely self-explanatory.
    /// </summary>
    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(SettingRow);
}
