using System;
using Avalonia.Controls;

namespace Fubar.Controls;

/// <summary>
/// A single-select "segmented" control - a joined row of pill segments where exactly one is active
/// (iOS-style). It's a <see cref="ListBox"/> underneath, so bind <see cref="ItemsControl.ItemsSource"/>
/// and two-way <see cref="SelectingItemsControl.SelectedItem"/>/<c>SelectedIndex</c>, with an
/// <see cref="ItemsControl.ItemTemplate"/> for each segment's label. A lightweight alternative to
/// <see cref="SeamlessTabControl"/> for compact view/filter switches.
/// </summary>
public class SegmentedControl : ListBox
{
    protected override Type StyleKeyOverride => typeof(SegmentedControl);
}
