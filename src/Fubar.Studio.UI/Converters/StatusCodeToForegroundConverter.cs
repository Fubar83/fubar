using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Fubar.Studio.UI.Converters;

/// <summary>Foreground counterpart to <see cref="StatusCodeToBrushConverter"/> - the active theme's
/// <c>Status*Fg</c> token (ResponsePane.md §3.1).</summary>
public sealed class StatusCodeToForegroundConverter : IValueConverter
{
    public static readonly StatusCodeToForegroundConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            int code and >= 100 and < 200 => "StatusInfoFg",
            int code and >= 200 and < 300 => "StatusSuccessFg",
            int code and >= 300 and < 400 => "StatusRedirectFg",
            int code and >= 400 and < 500 => "StatusClientErrorFg",
            int code and >= 500 => "StatusServerErrorFg",
            int => "StatusConnFailedFg",
            _ => "StatusConnFailedFg",
        };

        return Avalonia.Application.Current?.TryGetResource(key, Avalonia.Application.Current.ActualThemeVariant, out var brush) == true
            ? brush
            : Brushes.White;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
