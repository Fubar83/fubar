using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace Fubar.Controls;

/// <summary>
/// Converts an integer nesting depth into a left-indent <see cref="Thickness"/> (left = depth *
/// <see cref="StepPixels"/>, top/bottom = 1px row gap). Apply directly to a tree row's DataTemplate
/// root to indent by level without relying on a specific TreeView template. Reference via
/// <c>{x:Static fc:TreeLevelIndentConverter.Instance}</c>.
/// </summary>
public sealed class TreeLevelIndentConverter : IValueConverter
{
    public static readonly TreeLevelIndentConverter Instance = new();

    /// <summary>Pixels of left margin per nesting level.</summary>
    public const double StepPixels = 14;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new Thickness(System.Math.Max(value as int? ?? 0, 0) * StepPixels, 1, 0, 1);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
