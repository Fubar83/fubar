using System;
using Avalonia.Controls.Primitives;

namespace Fubar.Controls;

/// <summary>
/// A <see cref="ToggleButton"/> drawn as a rounded "pill" - the segmented view-switcher / filter-tab
/// look (Pretty | Tree | Raw, All | Enabled | Disabled, ...). Put several in a horizontal panel; give
/// them a shared <c>GroupName</c>-style behaviour via a RadioButton if single-select is required, or
/// use these directly for independent toggles.
/// </summary>
public class PillToggle : ToggleButton
{
    protected override Type StyleKeyOverride => typeof(PillToggle);
}
