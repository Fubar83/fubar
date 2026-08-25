using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Fubar.Studio.Core.Import;

namespace Fubar.Studio.UI.Converters;

/// <summary>Maps an <see cref="ImportChange"/> to a tag colour for the import diff (green add, amber
/// update, red remove, muted unchanged). Falls back to the secondary text brush.</summary>
public sealed class ImportChangeBrushConverter : IValueConverter
{
    public static readonly ImportChangeBrushConverter Instance = new();

    private static readonly SolidColorBrush Add = new(Color.Parse("#22C55E"));
    private static readonly SolidColorBrush Update = new(Color.Parse("#F59E0B"));
    private static readonly SolidColorBrush Remove = new(Color.Parse("#EF4444"));
    private static readonly SolidColorBrush Unchanged = new(Color.Parse("#94A3B8"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ImportChange.Add => Add,
        ImportChange.Update => Update,
        ImportChange.Remove => Remove,
        _ => Unchanged,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
