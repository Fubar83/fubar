using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Fubar.Studio.UI.Converters;

/// <summary>
/// Maps an HTTP method name to its badge brush via the active theme's <c>Method*Brush</c> token
/// (see Fubar.Controls' <c>Themes/Palette.axaml</c> <c>ThemeDictionaries</c>), so the badge color follows
/// Dark/Light/System theme switches rather than being fixed at compile time.
/// </summary>
public sealed class MethodToBrushConverter : IValueConverter
{
    public static readonly MethodToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = (value as string)?.ToUpperInvariant() switch
        {
            "GET" => "MethodGetBrush",
            "POST" => "MethodPostBrush",
            "PUT" or "PATCH" => "MethodPutBrush",
            "DELETE" => "MethodDeleteBrush",
            _ => "MethodOtherBrush",
        };

        return Avalonia.Application.Current?.TryGetResource(key, Avalonia.Application.Current.ActualThemeVariant, out var brush) == true
            ? brush
            : Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
