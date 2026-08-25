using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Fubar.Studio.UI.Converters;

/// <summary>Muted background for an inherited Headers-tab row (RequestEditorPane.md §5), via the
/// active theme's <c>BgInheritedRow</c> token; transparent for a direct row.</summary>
public sealed class InheritedRowBackgroundConverter : IValueConverter
{
    public static readonly InheritedRowBackgroundConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true)
        {
            return Brushes.Transparent;
        }

        return Avalonia.Application.Current?.TryGetResource("BgInheritedRow", Avalonia.Application.Current.ActualThemeVariant, out var brush) == true
            ? brush
            : Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
