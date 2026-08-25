using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Fubar.Studio.UI.Converters;

/// <summary>Latency badge color thresholds (ResponsePane.md §3.2): normal under 1000ms, amber
/// (client-error token, reused for its warm "caution" tone) above that, red (server-error token)
/// above 5000ms.</summary>
public sealed class LatencyToBrushConverter : IValueConverter
{
    public static readonly LatencyToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            long ms and > 5000 => "StatusServerErrorFg",
            long ms and > 1000 => "StatusClientErrorFg",
            _ => "TextPrimary",
        };

        return Avalonia.Application.Current?.TryGetResource(key, Avalonia.Application.Current.ActualThemeVariant, out var brush) == true
            ? brush
            : Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
