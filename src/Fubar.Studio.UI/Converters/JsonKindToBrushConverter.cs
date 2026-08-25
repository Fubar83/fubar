using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Fubar.Studio.UI.Converters;

/// <summary>Maps a <c>JsonTreeNodeViewModel.Kind</c> ("String"/"Number"/"Boolean"/"Null"/"Object"/
/// "Array") to its value color via the active theme's <c>Json*</c> token (ResponsePane.md §6).</summary>
public sealed class JsonKindToBrushConverter : IValueConverter
{
    public static readonly JsonKindToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = (value as string) switch
        {
            "String" => "JsonString",
            "Number" => "JsonNumber",
            "Boolean" => "JsonBoolean",
            "Null" => "JsonNull",
            _ => "TextPrimary",
        };

        return Avalonia.Application.Current?.TryGetResource(key, Avalonia.Application.Current.ActualThemeVariant, out var brush) == true
            ? brush
            : Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
