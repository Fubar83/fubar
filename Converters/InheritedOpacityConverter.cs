using System.Globalization;
using Avalonia.Data.Converters;

namespace Fubar.Controls;

/// <summary>Maps a bool to an opacity: <c>true</c> -&gt; 0.6 (dimmed), otherwise 1.0. Handy for rendering
/// inherited / read-only rows muted. Reference via <c>{x:Static fc:InheritedOpacityConverter.Instance}</c>.</summary>
public sealed class InheritedOpacityConverter : IValueConverter
{
    public static readonly InheritedOpacityConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? 0.6 : 1.0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
