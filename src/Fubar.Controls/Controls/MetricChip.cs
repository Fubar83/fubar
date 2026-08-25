using System;
using Avalonia;
using Avalonia.Controls.Primitives;

namespace Fubar.Controls;

/// <summary>
/// A compact "icon + value" readout for telemetry-style metrics - latency (⏱ 128 ms), size (📦 4.2 KB),
/// counts, etc. The <see cref="Icon"/> is a leading glyph; the <see cref="Text"/> value renders in a
/// monospace font and takes its colour from <c>Foreground</c>, so a host can tint it by threshold (e.g.
/// bind Foreground to a latency brush).
/// </summary>
public class MetricChip : TemplatedControl
{
    public static readonly StyledProperty<string?> IconProperty =
        AvaloniaProperty.Register<MetricChip, string?>(nameof(Icon));

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<MetricChip, string?>(nameof(Text));

    public string? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(MetricChip);
}
