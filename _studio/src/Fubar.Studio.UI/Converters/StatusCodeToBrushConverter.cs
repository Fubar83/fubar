using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Fubar.Studio.UI.Converters;

/// <summary>
/// Maps an HTTP status code to its badge background brush via the active theme's
/// <c>Status*Bg</c> token (ResponsePane.md §3.1) - foreground pairs live in the analogous
/// <c>Status*Fg</c> tokens, used directly by <c>ResponsePanelViewModel</c>'s badge properties.
/// </summary>
public sealed class StatusCodeToBrushConverter : IValueConverter
{
    public static readonly StatusCodeToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            int code and >= 100 and < 200 => "StatusInfoBg",
            int code and >= 200 and < 300 => "StatusSuccessBg",
            int code and >= 300 and < 400 => "StatusRedirectBg",
            int code and >= 400 and < 500 => "StatusClientErrorBg",
            int code and >= 500 => "StatusServerErrorBg",
            // 0 is HttpRequestExecutor's sentinel for a connection-level failure (no real status code).
            int => "StatusConnFailedBg",
            _ => "StatusConnFailedBg",
        };

        return Avalonia.Application.Current?.TryGetResource(key, Avalonia.Application.Current.ActualThemeVariant, out var brush) == true
            ? brush
            : Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
