using System;
using Avalonia.Controls;

namespace Fubar.Controls;

/// <summary>
/// A compact, borderless, square button meant to hold a single glyph or short symbol (close "&#x2715;",
/// "+", chevrons, ...). Transparent until hovered. Behaves exactly like <see cref="Button"/> (Command,
/// Click, Content) - only the chrome differs - so it composes into other controls such as
/// <see cref="Chip"/> and <see cref="SearchBox"/>.
/// </summary>
public class IconButton : Button
{
    protected override Type StyleKeyOverride => typeof(IconButton);
}
