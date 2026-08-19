using System;
using Avalonia.Controls;

namespace Fubar.Controls;

/// <summary>
/// A horizontal strip that lays its children out in a row with consistent spacing and a subtle
/// surface background - the container for address-bar buttons, view-switch pills, footer actions, ...
/// Being an <see cref="ItemsControl"/>, its children can be declared inline or bound from a
/// collection; child spacing is provided by the themed items panel.
/// </summary>
public class Toolbar : ItemsControl
{
    protected override Type StyleKeyOverride => typeof(Toolbar);
}
