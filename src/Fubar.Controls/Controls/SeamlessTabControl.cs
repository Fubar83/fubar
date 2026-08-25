using System;
using Avalonia.Controls;

namespace Fubar.Controls;

/// <summary>
/// A <see cref="TabControl"/> styled as classic "boxed" tabs: the selected tab merges seamlessly
/// into the bordered content area below it (no line under the active tab), while unselected tabs read
/// as closed boxes joining one continuous horizontal line. Drop it in anywhere the app needs that
/// look - a Params/Headers/Body/... strip, a view switcher, etc.
///
/// All appearance lives in <c>Themes/SeamlessTab.axaml</c>; this subclass exists only to give
/// those styles a dedicated type to target (so ordinary TabControls elsewhere are unaffected) and to
/// keep its own control-theme lookup off Fluent's default TabControl theme. The seam-merge is done
/// purely in XAML - the selected tab's own opaque Background (bound to this control's
/// <see cref="Avalonia.Controls.Primitives.TemplatedControl.Background"/>, the surface colour each host
/// sets) paints out the line under it - so no code-behind is needed. Each host sets Background to
/// whatever sits behind the tabs.
/// </summary>
public class SeamlessTabControl : TabControl
{
    protected override Type StyleKeyOverride => typeof(SeamlessTabControl);
}
